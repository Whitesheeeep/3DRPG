using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using RPG.CurrencySystem;
using RPG.Game.UI.Bag;
using RPG.Game.UI.Escape;
using RPG.Game.UI.Services;
using RPG.Game.UI.Views.Common;
using RPG.Game.UI.Views.WeaponDevelopment;
using RPG.Game.UI.WeaponDevelopment;
using RPG.ItemSystem;
using UnityEngine;
using WS_Modules.BusinessArchitecture;
using WS_Modules.CustomEventSystem;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.GameplayEffect;
using WS_Modules.UIModule;

namespace RPG.Game.UI.Controllers
{
    /// <summary>协调武器培养窗口的真实库存查询、页面状态、图集租约和用户意图。</summary>
    [DisallowMultipleComponent]
    public sealed class WeaponDevelopmentWindowController : MonoBehaviour
    {
        #region 依赖字段

        private WeaponDevelopmentWindowDataComponent data;
        private WeaponDevelopmentView view;
        private ItemSelectionPanelView selectionPanel;
        private WindowSpriteAtlasLeaseService spriteAtlasLeaseService;
        private WeaponDevelopmentUIStateModel stateModel;
        private IArchitecture architecture;
        private IUnRegister weaponChangedUnregister;

        #endregion

        #region 状态字段

        private EquipmentInstanceId targetInstanceId;
        private bool hasTarget;
        private bool initialized;
        private bool disposed;
        private EscCommandRegistration selectionEscRegistration;
        private WeaponGrowthMode currentGrowthMode = WeaponGrowthMode.ConfigurationUnavailable;

        #endregion

        #region 初始化与释放

        /// <summary>绑定窗口序列化依赖并订阅武器实例变化。</summary>
        /// <param name="windowData">窗口序列化数据。</param>
        public void Initialize(WeaponDevelopmentWindowDataComponent windowData)
        {
            if (initialized)
            {
                if (!ReferenceEquals(data, windowData)) throw new InvalidOperationException("[WeaponDevelopment] 不允许替换窗口数据组件。");
                return;
            }

            data = windowData ?? throw new ArgumentNullException(nameof(windowData));
            data.ValidateConfiguration();
            view = data.View;
            selectionPanel = data.SelectionPanel;
            stateModel = new WeaponDevelopmentUIStateModel();
            stateModel.Changed += Refresh;
            spriteAtlasLeaseService = new WindowSpriteAtlasLeaseService(
                data.DynamicAtlasAddresses, data.AtlasReleaseDelaySeconds);
            view.PageRequested += HandlePageRequested;
            view.CloseRequested += CloseWindow;
            view.EnhancementMaterialRequested += OpenEnhancementSelectionPanel;
            view.AutoAddRequested += AutoAddEnhancementMaterials;
            view.AddMaterialRequested += OpenRefinementSelectionPanel;
            if (selectionPanel != null)
            {
                selectionPanel.EntryClicked += HandleRefinementMaterialClicked;
                selectionPanel.QuantityChangeRequested += HandleEnhancementQuantityChanged;
                selectionPanel.ReturnRequested += CloseSelectionPanel;
            }

            weaponChangedUnregister = EventSystem.Register_Type<WeaponInstanceChangedEvent>(
                typeof(WeaponInstanceChangedEvent), HandleWeaponChanged);
            initialized = true;
        }

        /// <summary>窗口销毁时注销事件、按钮和图集租约。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            stateModel.Changed -= Refresh;
            view.PageRequested -= HandlePageRequested;
            view.CloseRequested -= CloseWindow;
            view.EnhancementMaterialRequested -= OpenEnhancementSelectionPanel;
            view.AutoAddRequested -= AutoAddEnhancementMaterials;
            view.AddMaterialRequested -= OpenRefinementSelectionPanel;
            if (selectionPanel != null)
            {
                selectionPanel.EntryClicked -= HandleRefinementMaterialClicked;
                selectionPanel.QuantityChangeRequested -= HandleEnhancementQuantityChanged;
                selectionPanel.ReturnRequested -= CloseSelectionPanel;
            }

            weaponChangedUnregister?.UnRegister();
            weaponChangedUnregister = null;
            UnregisterSelectionEscCommand();
            spriteAtlasLeaseService?.Dispose();
            spriteAtlasLeaseService = null;
        }

        #endregion

        #region 窗口生命周期

        /// <summary>应用新的目标实例并恢复成长页初始状态。</summary>
        /// <param name="instanceId">武器实例标识。</param>
        public void SetTarget(EquipmentInstanceId instanceId)
        {
            targetInstanceId = instanceId;
            hasTarget = true;
            // OpenContext 可能在窗口仍可见时切换目标；先同步隐藏旧目标的材料面板并移除 Esc 层级。
            view?.SetSelectionPanelVisible(false);
            selectionPanel?.SetInteractable(false);
            UnregisterSelectionEscCommand();
            stateModel.Reset();
            Debug.Log($"[WeaponDevelopment] 切换培养目标：Instance={instanceId}，默认页面=Growth。", this);
        }

        /// <summary>窗口稳定显示时刷新目标数据并开始动态图集准备。</summary>
        public void OnWindowShown()
        {
            if (disposed) return;
            spriteAtlasLeaseService.CancelRelease();
            view.SetSelectionPanelVisible(false);
            selectionPanel?.SetInteractable(false);
            Refresh();
            PrepareAtlasAsync().Forget(HandleAsyncException);
        }

        /// <summary>窗口稳定隐藏时收起选择面板并启动图集延迟释放。</summary>
        public void OnWindowHidden()
        {
            if (disposed) return;
            CloseSelectionPanel();
            spriteAtlasLeaseService.ScheduleRelease();
        }

        /// <summary>供 Esc Command 收起当前培养材料选择面板。</summary>
        public void CloseSelectionPanelFromCommand() => CloseSelectionPanel();

        #endregion

        #region 页面与选择

        /// <summary>切换成长或精炼页面，并在离开当前材料选择页时收起面板。</summary>
        /// <param name="page">目标页面。</param>
        private void HandlePageRequested(WeaponDevelopmentPage page)
        {
            // 页签切换属于更高层导航；无论离开升级还是精炼页，都先撤销面板的 Esc 注册，避免 Esc 栈残留。
            if (stateModel.SelectionPanelVisible)
            {
                stateModel.SetSelectionPanelVisible(false);
                view.SetSelectionPanelVisible(false);
                selectionPanel?.SetInteractable(false);
                UnregisterSelectionEscCommand();
            }

            stateModel.SetPage(page);
        }

        /// <summary>展开精炼材料选择面板并注册更高层 Esc Command。</summary>
        private void OpenRefinementSelectionPanel()
        {
            if (stateModel.CurrentPage != WeaponDevelopmentPage.Refinement) return;

            if (!TryGetTarget(out WeaponDefinition definition, out WeaponInstance instance)) return;
            WeaponRefinementStage stage = FindNextRefinementStage(definition, instance.RefinementRank);
            if (stage == null || stage.RequiredDuplicateCount <= 0)
            {
                Debug.LogWarning(
                    $"[WeaponDevelopment] 无法打开精炼材料面板：Definition={instance.DefinitionId}，" +
                    $"CurrentRank={instance.RefinementRank}，未配置有效的下一精炼阶段。", this);
                return;
            }

            stateModel.SetSelectionPanelVisible(true);
            view.SetSelectionPanelVisible(true);
            selectionPanel?.SetInteractable(true);
            RegisterSelectionEscCommand();
            Debug.Log(
                $"[WeaponDevelopment] 打开精炼材料面板：Definition={instance.DefinitionId}，" +
                $"RequiredDuplicateCount={stage.RequiredDuplicateCount}。", this);
        }

        /// <summary>展开武器强化素材选择面板并注册更高层 Esc Command。</summary>
        private void OpenEnhancementSelectionPanel()
        {
            if (stateModel.CurrentPage != WeaponDevelopmentPage.Growth || currentGrowthMode != WeaponGrowthMode.Enhancement)
                return;
            stateModel.SetSelectionPanelVisible(true);
            view.SetSelectionPanelVisible(true);
            selectionPanel?.SetInteractable(true);
            RegisterSelectionEscCommand();
            BindSelectionPanel();
            Debug.Log("[WeaponDevelopment] 打开武器强化素材面板。", this);
        }

        /// <summary>收起当前培养材料选择面板并注销其 Esc Command。</summary>
        private void CloseSelectionPanel()
        {
            if (!stateModel.SelectionPanelVisible)
            {
                selectionPanel?.SetInteractable(false);
                UnregisterSelectionEscCommand();
                return;
            }

            stateModel.SetSelectionPanelVisible(false);
            view.SetSelectionPanelVisible(false);
            selectionPanel?.SetInteractable(false);
            UnregisterSelectionEscCommand();
            Debug.Log("[WeaponDevelopment] 关闭培养材料面板。", this);
        }

        /// <summary>切换一个候选武器的精炼材料临时选择状态。</summary>
        /// <param name="entryKey">候选条目标识。</param>
        private void HandleRefinementMaterialClicked(BagEntryKey entryKey)
        {
            EquipmentInstanceId instanceId = new EquipmentInstanceId(entryKey.Value);
            if (!TryGetTarget(out WeaponDefinition definition, out WeaponInstance target)) return;
            WeaponRefinementStage stage = FindNextRefinementStage(definition, target.RefinementRank);
            if (stage == null || stage.RequiredDuplicateCount <= 0)
            {
                Debug.LogWarning(
                    $"[WeaponDevelopment] 忽略精炼材料点击：Definition={target.DefinitionId}，" +
                    $"CurrentRank={target.RefinementRank}，未配置有效的下一精炼阶段。", this);
                return;
            }

            int previousCount = stateModel.SelectedMaterialIds.Count;
            bool wasSelected = IsMaterialSelected(instanceId);
            // ToggleMaterial 会通过 Changed 同时刷新数量文本和网格多选视觉，避免同一次点击重复绑定候选列表。
            stateModel.ToggleMaterial(instanceId, stage.RequiredDuplicateCount);
            int currentCount = stateModel.SelectedMaterialIds.Count;
            if (currentCount == previousCount && !wasSelected)
            {
                Debug.LogWarning(
                    $"[WeaponDevelopment] 精炼材料数量已达上限：Definition={target.DefinitionId}，" +
                    $"Selected={currentCount}/{stage.RequiredDuplicateCount}。", this);
                return;
            }

            Debug.Log(
                $"[WeaponDevelopment] 更新精炼材料选择：Definition={target.DefinitionId}，" +
                $"MaterialInstance={instanceId}，Selected={currentCount}/{stage.RequiredDuplicateCount}。", this);
        }

        /// <summary>处理强化素材的左右键和长按数量调整意图。</summary>
        /// <param name="intent">条目数量调整意图。</param>
        private void HandleEnhancementQuantityChanged(BagItemQuantityIntent intent)
        {
            if (stateModel.CurrentPage != WeaponDevelopmentPage.Growth || currentGrowthMode != WeaponGrowthMode.Enhancement)
                return;
            if (!ItemId.TryCreate(intent.EntryKey.Value, out ItemId itemId) ||
                !ItemManager.Instance.TryGetDefinition(itemId, out ItemDefinition item) ||
                !(item is DevelopmentExperienceItemDefinition definition) ||
                !definition.SupportsExperienceType(DevelopmentExperienceItemType.Weapon))
                return;

            int ownedQuantity = StackableInventoryManager.Instance.GetQuantity(itemId);
            int delta = intent.Direction == BagItemQuantityDirection.Increase
                ? intent.Step
                : -intent.Step;
            stateModel.AdjustEnhancementMaterial(itemId, delta, ownedQuantity);
            // 长按会以更大步长重复回调；只记录单步意图，避免高频输入淹没 Console。
            if (intent.Step == 1)
                Debug.Log(
                    $"[WeaponDevelopment] 调整强化素材：Item={itemId}，Direction={intent.Direction}，Step={intent.Step}。", this);
        }

        /// <summary>按当前阶段上限自动选择武器强化素材。</summary>
        private void AutoAddEnhancementMaterials()
        {
            if (stateModel.CurrentPage != WeaponDevelopmentPage.Growth || currentGrowthMode != WeaponGrowthMode.Enhancement)
                return;
            if (!TryGetTarget(out WeaponDefinition definition, out WeaponInstance instance)) return;
            long requiredExperience = GetRequiredExperienceToCap(definition, instance, out _);
            if (requiredExperience <= 0) return;

            IReadOnlyList<StackableInventoryEntry> inventory =
                StackableInventoryManager.Instance.GetDevelopmentExperienceItems(DevelopmentExperienceItemType.Weapon);
            var candidates = new List<EnhancementMaterialCandidate>();
            for (int index = 0; index < inventory.Count; index++)
            {
                StackableInventoryEntry entry = inventory[index];
                if (entry == null || entry.Quantity <= 0 ||
                    !ItemManager.Instance.TryGetDefinition(entry.ItemId, out ItemDefinition item) ||
                    !(item is DevelopmentExperienceItemDefinition material) || material.ExperienceValue <= 0) continue;
                candidates.Add(new EnhancementMaterialCandidate(entry.ItemId, entry.Quantity, material.ExperienceValue));
            }

            candidates.Sort((left, right) =>
            {
                int result = left.ExperienceValue.CompareTo(right.ExperienceValue);
                return result != 0 ? result : left.ItemId.CompareTo(right.ItemId);
            });

            // key：素材 ItemId；value：本次升级会话准备消耗的数量。
            var selectedQuantityByItemIdMap = new Dictionary<ItemId, int>();
            long remaining = requiredExperience;
            for (int index = 0; index < candidates.Count; index++)
            {
                EnhancementMaterialCandidate candidate = candidates[index];
                long count = Math.Min((long)candidate.Quantity, remaining / candidate.ExperienceValue);
                if (count <= 0) continue;
                selectedQuantityByItemIdMap[candidate.ItemId] = (int)count;
                remaining -= count * candidate.ExperienceValue;
            }

            if (remaining > 0)
            {
                for (int index = 0; index < candidates.Count; index++)
                {
                    EnhancementMaterialCandidate candidate = candidates[index];
                    int selected = selectedQuantityByItemIdMap.TryGetValue(candidate.ItemId, out int current) ? current : 0;
                    if (selected >= candidate.Quantity) continue;
                    selectedQuantityByItemIdMap[candidate.ItemId] = selected + 1;
                    break;
                }
            }

            stateModel.ReplaceEnhancementMaterials(selectedQuantityByItemIdMap);
            Debug.Log($"[WeaponDevelopment] 自动添加强化素材：种类={selectedQuantityByItemIdMap.Count}，目标经验={requiredExperience}。", this);
        }

        #endregion

        #region Esc 注册

        /// <summary>注册选择面板关闭 Command。</summary>
        private void RegisterSelectionEscCommand()
        {
            if (selectionEscRegistration.IsValid) return;
            architecture ??= GameArchitecture.Interface;
            selectionEscRegistration = architecture.SendCommand(
                new RegisterEscCommand(new CloseWeaponSelectionPanelCommand()));
        }

        /// <summary>注销选择面板关闭 Command。</summary>
        private void UnregisterSelectionEscCommand()
        {
            if (!selectionEscRegistration.IsValid) return;
            architecture?.SendCommand(new UnregisterEscCommand(selectionEscRegistration));
            selectionEscRegistration = default;
        }

        /// <summary>请求通过 UIManager 关闭当前培养窗口。</summary>
        private void CloseWindow() => UIManager.Instance.HideWindowAsync<WeaponDevelopmentWindow>().Forget(HandleAsyncException);

        #endregion

        #region 数据刷新

        /// <summary>从 Manager 构建当前页面展示数据。</summary>
        private void Refresh()
        {
            if (disposed || !hasTarget || !WeaponInventoryManager.IsConfigured || !ItemManager.Instance.IsConfigured)
                return;
            if (!WeaponInventoryManager.Instance.TryGetInstance(targetInstanceId, out WeaponInstance instance)) return;
            if (!ItemManager.Instance.TryGetDefinition(instance.DefinitionId, out ItemDefinition item) ||
                !(item is WeaponDefinition definition)) return;

            WeaponDetails details = WeaponDetailsQuery.Create(definition, instance);
            IReadOnlyList<string> lines = BuildDetailsLines(details);
            WeaponDevelopmentViewData pageData = BuildPageData(definition, instance, lines);
            string mora = CurrencyManager.Instance.GetBalance(CurrencyId.Mola).ToString();
            view.Bind(definition.DisplayName, definition.WeaponType.ToString(), mora, pageData);
            if (stateModel.SelectionPanelVisible)
            {
                if (!IsSelectionPanelValidForCurrentPage())
                {
                    CloseSelectionPanel();
                    return;
                }

                BindSelectionPanel();
            }
        }

        /// <summary>根据当前页生成升级、突破或精炼页面的专用展示数据。</summary>
        /// <param name="definition">武器定义。</param>
        /// <param name="instance">武器实例。</param>
        /// <param name="detailsLines">当前属性文本。</param>
        /// <returns>页面数据。</returns>
        private WeaponDevelopmentViewData BuildPageData(WeaponDefinition definition, WeaponInstance instance,
            IReadOnlyList<string> detailsLines)
        {
            switch (stateModel.CurrentPage)
            {
                case WeaponDevelopmentPage.Growth:
                    return BuildGrowthData(definition, instance, detailsLines);
                case WeaponDevelopmentPage.Refinement:
                    return BuildRefinementData(definition, instance);
                default:
                    throw new ArgumentOutOfRangeException(nameof(stateModel.CurrentPage),
                        stateModel.CurrentPage, "未知的武器培养页面。");
            }
        }

        /// <summary>根据当前等级构建升级、突破、满级或配置不可用状态的数据。</summary>
        private WeaponDevelopmentViewData BuildGrowthData(WeaponDefinition definition, WeaponInstance instance,
            IReadOnlyList<string> detailsLines)
        {
            WeaponGrowthMode mode = ResolveGrowthMode(definition, instance, out int currentCap,
                out WeaponAscensionStage nextStage);
            currentGrowthMode = mode;
            switch (mode)
            {
                case WeaponGrowthMode.Enhancement:
                    return BuildEnhancementData(definition, instance, detailsLines, currentCap);
                case WeaponGrowthMode.Ascension:
                    return BuildAscensionData(definition, instance, detailsLines, currentCap, nextStage);
                case WeaponGrowthMode.MaxLevel:
                    return BuildMaxLevelData(instance, detailsLines);
                default:
                    return BuildUnavailableGrowthData(instance, detailsLines, currentCap);
            }
        }

        /// <summary>构建可使用经验素材升级时的等级、经验和素材预览数据。</summary>
        private WeaponDevelopmentViewData BuildEnhancementData(WeaponDefinition definition, WeaponInstance instance,
            IReadOnlyList<string> detailsLines, int currentCap)
        {
            long requiredExperience = GetRequiredExperienceToCap(definition, instance, out _);
            long selectedExperience = GetSelectedEnhancementExperience();
            BuildProjectedProgress(definition.GrowthProfile, instance, currentCap, selectedExperience,
                out int projectedLevel, out int projectedExperience, out int projectedNextExperience,
                out long projectedCost);
            IReadOnlyList<string> previewLines = BuildComparisonLines(detailsLines,
                BuildDetailsLines(WeaponDetailsQuery.CreateProjected(definition, instance, projectedLevel,
                    instance.AscensionRank, instance.RefinementRank)));
            IReadOnlyList<BagItemViewData> selectedMaterials = BuildSelectedEnhancementEntries();
            bool hasCandidates = HasEnhancementCandidates();
            long currencyOwned = CurrencyManager.Instance.GetBalance(CurrencyId.Mola);
            float progress = projectedNextExperience <= 0
                ? (projectedLevel >= currentCap ? 1f : 0f)
                : Mathf.Clamp01(projectedExperience / (float)projectedNextExperience);
            var enhancement = new WeaponEnhancementViewData(
                "武器升级", "使用武器强化素材预览成长", instance.Level, projectedLevel,
                selectedExperience, projectedExperience, projectedNextExperience, progress,
                previewLines, selectedMaterials, currencyOwned, projectedCost, false, "升级（预览）",
                hasCandidates, hasCandidates);
            return new WeaponDevelopmentViewData(WeaponDevelopmentPage.Growth, "升级",
                WeaponGrowthMode.Enhancement, enhancement, null, null);
        }

        /// <summary>构建达到阶段上限后显示的突破星级、等级上限和材料数据。</summary>
        private WeaponDevelopmentViewData BuildAscensionData(WeaponDefinition definition, WeaponInstance instance,
            IReadOnlyList<string> detailsLines, int currentCap, WeaponAscensionStage nextStage)
        {
            var lines = new List<string>(detailsLines) { "突破属性增量暂未配置" };
            IReadOnlyList<BagItemViewData> requiredMaterials = BuildRequiredAscensionEntries(nextStage.Cost);
            string status = BuildCostSummary(nextStage.Cost);
            var ascension = new WeaponAscensionViewData(WeaponGrowthMode.Ascension, "武器突破",
                "达到当前等级上限后解锁下一阶段", instance.AscensionRank, instance.AscensionRank + 1,
                instance.Level, currentCap, nextStage.MaxLevelAfter, true, lines, requiredMaterials,
                status, false, "突破（预览）");
            return new WeaponDevelopmentViewData(WeaponDevelopmentPage.Growth, "突破",
                WeaponGrowthMode.Ascension, null, ascension, null);
        }

        /// <summary>构建已经达到 Definition 全局等级上限时的突破布局终态。</summary>
        private static WeaponDevelopmentViewData BuildMaxLevelData(WeaponInstance instance,
            IReadOnlyList<string> detailsLines)
        {
            var ascension = new WeaponAscensionViewData(WeaponGrowthMode.MaxLevel, "武器已满级",
                "当前 Definition 没有更高等级", instance.AscensionRank, 0, instance.Level, instance.Level,
                instance.Level, false, detailsLines, Array.Empty<BagItemViewData>(), "无需继续升级", false, "已满级");
            return new WeaponDevelopmentViewData(WeaponDevelopmentPage.Growth, "已满级",
                WeaponGrowthMode.MaxLevel, null, ascension, null);
        }

        /// <summary>构建突破配置不完整时的明确错误展示。</summary>
        private static WeaponDevelopmentViewData BuildUnavailableGrowthData(WeaponInstance instance,
            IReadOnlyList<string> detailsLines, int currentCap)
        {
            string capText = currentCap > 0 ? $"当前阶段上限：{currentCap}" : "无法推导当前阶段上限";
            var ascension = new WeaponAscensionViewData(WeaponGrowthMode.ConfigurationUnavailable, "成长配置不可用",
                "请补齐突破阶段配置", instance.AscensionRank, 0, instance.Level, currentCap, currentCap,
                false, detailsLines, Array.Empty<BagItemViewData>(), $"无法安全预览升级或突破；{capText}", false,
                "配置不可用");
            return new WeaponDevelopmentViewData(WeaponDevelopmentPage.Growth, "培养",
                WeaponGrowthMode.ConfigurationUnavailable, null, ascension, null);
        }

        /// <summary>构建精炼页的阶数、效果、已选武器和费用数据。</summary>
        private WeaponDevelopmentViewData BuildRefinementData(WeaponDefinition definition, WeaponInstance instance)
        {
            WeaponGrowthMode growthMode = ResolveGrowthMode(definition, instance, out _, out _);
            currentGrowthMode = growthMode;
            string growthTabLabel = GetGrowthTabLabel(growthMode);
            WeaponRefinementStage nextStage = FindNextRefinementStage(definition, instance.RefinementRank);
            if (nextStage == null || nextStage.RequiredDuplicateCount <= 0)
            {
                return new WeaponDevelopmentViewData(WeaponDevelopmentPage.Refinement, growthTabLabel,
                    growthMode, null, null,
                    new WeaponRefinementViewData("武器精炼", "不可继续精炼", instance.RefinementRank, 0,
                        false, BuildRefinementEffectLines(WeaponDetailsQuery.CreateProjected(definition, instance,
                            instance.Level, instance.AscensionRank, instance.RefinementRank)),
                        Array.Empty<BagItemViewData>(), 0, 0,
                        CurrencyManager.Instance.GetBalance(CurrencyId.Mola), 0L, false,
                        "已达上限或未配置", false));
            }

            WeaponDetails currentDetails = WeaponDetailsQuery.CreateProjected(definition, instance,
                instance.Level, instance.AscensionRank, instance.RefinementRank);
            WeaponDetails projectedDetails = WeaponDetailsQuery.CreateProjected(definition, instance,
                instance.Level, instance.AscensionRank, nextStage.Rank);
            IReadOnlyList<string> currentEffects = BuildRefinementEffectLines(currentDetails);
            IReadOnlyList<string> projectedEffects = BuildRefinementEffectLines(projectedDetails);
            IReadOnlyList<BagItemViewData> selectedMaterials = BuildSelectedRefinementEntries(definition, instance);
            long currencyOwned = CurrencyManager.Instance.GetBalance(CurrencyId.Mola);
            long currencyCost = GetCurrencyCost(nextStage.Cost, CurrencyId.Mola);
            return new WeaponDevelopmentViewData(WeaponDevelopmentPage.Refinement, growthTabLabel,
                growthMode, null, null,
                new WeaponRefinementViewData("武器精炼", "精炼效果与材料预览",
                    instance.RefinementRank, nextStage.Rank, true,
                    BuildComparisonLines(currentEffects, projectedEffects), selectedMaterials,
                    stateModel.SelectedMaterialIds.Count, nextStage.RequiredDuplicateCount,
                    currencyOwned, currencyCost, false, "精炼（预览）", true));
        }

        /// <summary>解析成长入口当前应该显示的升级、突破或终态。</summary>
        /// <param name="definition">武器定义。</param>
        /// <param name="instance">武器实例。</param>
        /// <param name="currentCap">推导出的当前等级上限。</param>
        /// <param name="nextStage">下一突破阶段。</param>
        /// <returns>成长展示模式。</returns>
        private static WeaponGrowthMode ResolveGrowthMode(WeaponDefinition definition, WeaponInstance instance,
            out int currentCap, out WeaponAscensionStage nextStage)
        {
            currentCap = 0;
            nextStage = FindNextAscensionStage(definition, instance.AscensionRank);
            if (nextStage != null && nextStage.RequiredLevel > 0 && nextStage.MaxLevelAfter > 0)
            {
                currentCap = nextStage.RequiredLevel;
                return instance.Level >= currentCap ? WeaponGrowthMode.Ascension : WeaponGrowthMode.Enhancement;
            }

            int completedIndex = instance.AscensionRank - 1;
            if (completedIndex >= 0 && completedIndex < definition.AscensionStages.Count)
                currentCap = definition.AscensionStages[completedIndex].MaxLevelAfter;

            if (instance.Level >= definition.MaxLevel && instance.AscensionRank >= definition.MaxAscensionRank)
                return WeaponGrowthMode.MaxLevel;
            return currentCap > instance.Level ? WeaponGrowthMode.Enhancement : WeaponGrowthMode.ConfigurationUnavailable;
        }

        /// <summary>将成长模式转换为左侧成长 Tab 的稳定文案。</summary>
        /// <param name="growthMode">目标武器当前成长模式。</param>
        /// <returns>成长入口文案。</returns>
        private static string GetGrowthTabLabel(WeaponGrowthMode growthMode)
        {
            switch (growthMode)
            {
                case WeaponGrowthMode.Enhancement:
                    return "升级";
                case WeaponGrowthMode.Ascension:
                    return "突破";
                case WeaponGrowthMode.MaxLevel:
                    return "已满级";
                default:
                    return "培养";
            }
        }

        /// <summary>将当前和预计属性行按相同顺序合并为前后对比文本。</summary>
        /// <param name="currentLines">当前属性行。</param>
        /// <param name="previewLines">预计属性行。</param>
        /// <returns>属性对比行。</returns>
        private static IReadOnlyList<string> BuildComparisonLines(IReadOnlyList<string> currentLines,
            IReadOnlyList<string> previewLines)
        {
            int count = Math.Max(currentLines?.Count ?? 0, previewLines?.Count ?? 0);
            var lines = new List<string>(count);
            for (int index = 0; index < count; index++)
            {
                string current = currentLines != null && index < currentLines.Count ? currentLines[index] : "—";
                string preview = previewLines != null && index < previewLines.Count ? previewLines[index] : "—";
                lines.Add(current == preview ? current : $"{current} → {preview}");
            }

            return lines;
        }

        /// <summary>获取当前等级下一等级所需的烘焙经验。</summary>
        /// <param name="profile">武器成长配置。</param>
        /// <param name="level">当前等级。</param>
        /// <returns>下一等级经验；无法查询时返回零。</returns>
        private static int GetCurrentNextExperience(WeaponGrowthProfile profile, int level)
        {
            BakedWeaponLevelProgression progression = GetProgression(profile, level);
            return progression?.NextExperience ?? 0;
        }

        /// <summary>计算到当前阶段等级上限还缺少的绝对经验。</summary>
        /// <param name="definition">武器定义。</param>
        /// <param name="instance">武器实例。</param>
        /// <param name="currentCap">当前阶段上限。</param>
        /// <returns>还需经验。</returns>
        private static long GetRequiredExperienceToCap(WeaponDefinition definition, WeaponInstance instance,
            out int currentCap)
        {
            ResolveGrowthMode(definition, instance, out currentCap, out _);
            BakedWeaponLevelProgression current = GetProgression(definition.GrowthProfile, instance.Level);
            BakedWeaponLevelProgression cap = GetProgression(definition.GrowthProfile, currentCap);
            if (current == null || cap == null) return 0;
            long absoluteCurrent = (long)current.CumulativeExperience + instance.CurrentExperience;
            return Math.Max(0L, (long)cap.CumulativeExperience - absoluteCurrent);
        }

        /// <summary>按库存顺序构建当前已经选择的升级素材条目。</summary>
        /// <returns>已选择数量大于零的素材列表。</returns>
        private IReadOnlyList<BagItemViewData> BuildSelectedEnhancementEntries()
        {
            var result = new List<BagItemViewData>();
            IReadOnlyList<StackableInventoryEntry> inventory =
                StackableInventoryManager.Instance.GetDevelopmentExperienceItems(DevelopmentExperienceItemType.Weapon);
            for (int index = 0; index < inventory.Count; index++)
            {
                StackableInventoryEntry entry = inventory[index];
                if (entry == null || entry.Quantity <= 0 ||
                    !ItemManager.Instance.TryGetDefinition(entry.ItemId, out ItemDefinition item) ||
                    !(item is DevelopmentExperienceItemDefinition definition) || definition.ExperienceValue <= 0 ||
                    !stateModel.SelectedEnhancementQuantities.TryGetValue(entry.ItemId, out int selectedQuantity) ||
                    selectedQuantity <= 0) continue;

                Sprite icon = spriteAtlasLeaseService.TryGetSprite(definition.IconAddress, definition.IconSpriteName,
                    out Sprite resolved) ? resolved : null;
                var entryKey = new BagEntryKey(ItemCategory.DevelopmentExperienceItem, entry.ItemId.ToString());
                result.Add(new BagItemViewData(entryKey, definition.DisplayName, (int)definition.Rarity,
                    FormatSelectedQuantity(selectedQuantity, entry.Quantity), icon, null, string.Empty, false, false, false));
            }

            return result;
        }

        /// <summary>按当前选择顺序构建已选同名武器的精炼材料卡片。</summary>
        /// <param name="definition">目标武器定义。</param>
        /// <param name="target">当前目标武器实例。</param>
        /// <returns>仍存在且属于候选范围的已选武器卡片。</returns>
        private IReadOnlyList<BagItemViewData> BuildSelectedRefinementEntries(WeaponDefinition definition,
            WeaponInstance target)
        {
            var result = new List<BagItemViewData>();
            IReadOnlyList<WeaponInstance> instances = WeaponInventoryManager.Instance.GetInstances();
            for (int index = 0; index < instances.Count; index++)
            {
                WeaponInstance material = instances[index];
                if (material == null || material.InstanceId == target.InstanceId ||
                    material.DefinitionId != definition.ItemId || !IsMaterialSelected(material.InstanceId) ||
                    material.IsLocked || material.IsEquipped) continue;
                if (!ItemManager.Instance.TryGetDefinition(material.DefinitionId, out ItemDefinition item) ||
                    !(item is WeaponDefinition materialDefinition)) continue;

                Sprite icon = spriteAtlasLeaseService.TryGetSprite(materialDefinition.IconAddress,
                    materialDefinition.IconSpriteName, out Sprite resolved) ? resolved : null;
                var entryKey = new BagEntryKey(ItemCategory.Weapon, material.InstanceId.ToString());
                result.Add(new BagItemViewData(entryKey, materialDefinition.DisplayName,
                    (int)materialDefinition.Rarity, $"Lv.{material.Level} · R{material.RefinementRank}", icon,
                    null, string.Empty, false, material.IsLocked, material.IsEquipped));
            }

            return result;
        }

        /// <summary>按突破配置顺序构建所需素材及其实际拥有数量。</summary>
        /// <param name="cost">突破消耗。</param>
        /// <returns>突破素材列表。</returns>
        private IReadOnlyList<BagItemViewData> BuildRequiredAscensionEntries(GrowthCost cost)
        {
            var result = new List<BagItemViewData>();
            if (cost == null || cost.ItemCosts == null) return result;
            for (int index = 0; index < cost.ItemCosts.Count; index++)
            {
                ItemCostEntry itemCost = cost.ItemCosts[index];
                if (itemCost == null || !ItemManager.Instance.TryGetDefinition(itemCost.ItemId,
                        out ItemDefinition definition)) continue;
                Sprite icon = spriteAtlasLeaseService.TryGetSprite(definition.IconAddress, definition.IconSpriteName,
                    out Sprite resolved) ? resolved : null;
                int ownedQuantity = StackableInventoryManager.Instance.GetQuantity(itemCost.ItemId);
                var entryKey = new BagEntryKey(ItemCategory.DevelopmentItem, itemCost.ItemId.ToString());
                result.Add(new BagItemViewData(entryKey, definition.DisplayName, (int)definition.Rarity,
                    FormatOwnedQuantity(ownedQuantity, itemCost.Quantity), icon, null, string.Empty, false, false, false));
            }

            return result;
        }

        /// <summary>格式化素材条目的已拥有数量和需求数量，不足数量使用红色富文本。</summary>
        /// <param name="ownedQuantity">当前拥有数量。</param>
        /// <param name="requiredQuantity">需求数量。</param>
        /// <returns>条目数量文本。</returns>
        private static string FormatOwnedQuantity(int ownedQuantity, int requiredQuantity)
        {
            return ownedQuantity < requiredQuantity
                ? $"<color=#E36B6B>{ownedQuantity}</color>/{requiredQuantity}"
                : $"{ownedQuantity}/{requiredQuantity}";
        }

        /// <summary>格式化升级素材的已选数量与背包总拥有量，不将正常未选数量标记为不足。</summary>
        /// <param name="selectedQuantity">本次已经选择的数量。</param>
        /// <param name="ownedQuantity">背包中的总拥有量。</param>
        /// <returns>已选数量/拥有数量文本。</returns>
        private static string FormatSelectedQuantity(int selectedQuantity, int ownedQuantity)
        {
            return $"{Math.Max(0, selectedQuantity)}/{Math.Max(0, ownedQuantity)}";
        }

        /// <summary>把所选素材经验投影为当前阶段上限内的等级和经验。</summary>
        /// <param name="profile">武器成长配置。</param>
        /// <param name="instance">当前武器实例。</param>
        /// <param name="currentCap">当前阶段等级上限。</param>
        /// <param name="selectedExperience">所选素材总经验。</param>
        /// <param name="projectedLevel">预计等级。</param>
        /// <param name="projectedExperience">预计等级内经验。</param>
        /// <param name="projectedNextExperience">预计下一等级经验。</param>
        /// <param name="projectedCost">预计跨级货币消耗。</param>
        private static void BuildProjectedProgress(WeaponGrowthProfile profile, WeaponInstance instance,
            int currentCap, long selectedExperience, out int projectedLevel, out int projectedExperience,
            out int projectedNextExperience, out long projectedCost)
        {
            BakedWeaponLevelProgression current = GetProgression(profile, instance.Level);
            BakedWeaponLevelProgression cap = GetProgression(profile, currentCap);
            if (current == null || cap == null)
            {
                projectedLevel = instance.Level;
                projectedExperience = instance.CurrentExperience;
                projectedNextExperience = 0;
                projectedCost = 0;
                return;
            }

            long totalExperience = Math.Min((long)cap.CumulativeExperience,
                (long)current.CumulativeExperience + instance.CurrentExperience + Math.Max(0L, selectedExperience));
            projectedLevel = instance.Level;
            while (projectedLevel < currentCap)
            {
                BakedWeaponLevelProgression progression = GetProgression(profile, projectedLevel);
                if (progression == null || progression.NextExperience <= 0 ||
                    totalExperience < (long)progression.CumulativeExperience + progression.NextExperience) break;
                projectedLevel++;
            }

            BakedWeaponLevelProgression projected = GetProgression(profile, projectedLevel);
            projectedExperience = projected == null
                ? instance.CurrentExperience
                : (int)Math.Max(0L, totalExperience - projected.CumulativeExperience);
            projectedNextExperience = projected?.NextExperience ?? 0;
            projectedCost = 0L;
            for (int level = instance.Level; level < projectedLevel; level++)
            {
                BakedWeaponLevelProgression progression = GetProgression(profile, level);
                if (progression != null) projectedCost += progression.CurrencyCost;
            }
        }

        /// <summary>按等级从成长表读取烘焙进度。</summary>
        /// <param name="profile">成长配置。</param>
        /// <param name="level">目标等级。</param>
        /// <returns>匹配进度；超出表范围时返回空。</returns>
        private static BakedWeaponLevelProgression GetProgression(WeaponGrowthProfile profile, int level)
        {
            if (profile == null || profile.BakedProgressions == null || level < 1 ||
                level > profile.BakedProgressions.Count) return null;
            return profile.BakedProgressions[level - 1];
        }

        /// <summary>汇总当前会话选中的武器强化素材经验。</summary>
        /// <returns>所选经验总量。</returns>
        private long GetSelectedEnhancementExperience()
        {
            long total = 0L;
            foreach (KeyValuePair<ItemId, int> pair in stateModel.SelectedEnhancementQuantities)
            {
                if (!ItemManager.Instance.TryGetDefinition(pair.Key, out ItemDefinition item) ||
                    !(item is DevelopmentExperienceItemDefinition definition)) continue;
                total += (long)pair.Value * definition.ExperienceValue;
            }

            return total;
        }

        /// <summary>判断当前背包是否存在可选择的武器强化素材。</summary>
        /// <returns>存在有效候选时返回 true。</returns>
        private bool HasEnhancementCandidates()
        {
            IReadOnlyList<StackableInventoryEntry> entries =
                StackableInventoryManager.Instance.GetDevelopmentExperienceItems(DevelopmentExperienceItemType.Weapon);
            for (int index = 0; index < entries.Count; index++)
            {
                StackableInventoryEntry entry = entries[index];
                if (entry == null || entry.Quantity <= 0 ||
                    !ItemManager.Instance.TryGetDefinition(entry.ItemId, out ItemDefinition item) ||
                    !(item is DevelopmentExperienceItemDefinition definition) || definition.ExperienceValue <= 0) continue;
                return true;
            }

            return false;
        }

        /// <summary>从静态 GE 贡献聚合同一属性并生成最多两条属性文本。</summary>
        private static IReadOnlyList<string> BuildDetailsLines(WeaponDetails details)
        {
            var values = new List<AttributeDisplayValue>();
            AppendAttributes(details.LevelEffects, values);
            AppendAttributes(details.RefinementEffects, values);
            var lines = new List<string>(2);
            for (int index = 0; index < values.Count && lines.Count < 2; index++)
            {
                AttributeDisplayValue value = values[index];
                lines.Add($"{value.Attribute.DisplayName}: {WeaponAttributeTextFormatter.Format(value.Type, value.Value)}");
            }

            return lines;
        }

        /// <summary>只构建精炼效果贡献文本，不混入等级效果或突破属性。</summary>
        /// <param name="details">指定精炼阶数的武器详情快照。</param>
        /// <returns>精炼效果文本；没有静态效果时返回明确的未配置提示。</returns>
        private static IReadOnlyList<string> BuildRefinementEffectLines(WeaponDetails details)
        {
            var values = new List<AttributeDisplayValue>();
            AppendAttributes(details.RefinementEffects, values);
            if (values.Count == 0) return new[] { "精炼效果暂未配置" };

            var lines = new List<string>(values.Count);
            for (int index = 0; index < values.Count; index++)
            {
                AttributeDisplayValue value = values[index];
                lines.Add($"{value.Attribute.DisplayName}: {WeaponAttributeTextFormatter.Format(value.Type, value.Value)}");
            }

            return lines;
        }

        /// <summary>按属性首次出现顺序聚合 Add 或 Multiply 贡献。</summary>
        /// <param name="evaluations">静态效果查询结果。</param>
        /// <param name="values">接收聚合后的属性值。</param>
        private static void AppendAttributes(IReadOnlyList<WeaponEffectEvaluation> evaluations,
            List<AttributeDisplayValue> values)
        {
            for (int effectIndex = 0; effectIndex < evaluations.Count; effectIndex++)
            {
                IReadOnlyList<WeaponEffectContribution> contributions = evaluations[effectIndex].Contributions;
                for (int index = 0; index < contributions.Count; index++)
                {
                    GameplayEffectStaticModifierResult result = contributions[index].Result;
                    if ((result.Type != AttributeModifierType.Add && result.Type != AttributeModifierType.Multiply) ||
                        !result.Attribute.IsValid) continue;

                    int existingIndex = values.FindIndex(value => value.Attribute.Id == result.Attribute.Id);
                    if (existingIndex < 0)
                    {
                        values.Add(new AttributeDisplayValue(result.Attribute, result.Type, result.Magnitude));
                        continue;
                    }

                    AttributeDisplayValue existing = values[existingIndex];
                    if (existing.Type != result.Type)
                    {
                        // 同一属性同时配置两种 Modifier 时沿用背包规则：Multiply 优先，避免伪造相加单位。
                        if (result.Type == AttributeModifierType.Multiply)
                            values[existingIndex] = new AttributeDisplayValue(existing.Attribute, result.Type, result.Magnitude);
                        continue;
                    }

                    float aggregatedValue = existing.Type == AttributeModifierType.Add
                        ? existing.Value + result.Magnitude
                        : existing.Value * result.Magnitude;
                    values[existingIndex] = new AttributeDisplayValue(existing.Attribute, existing.Type, aggregatedValue);
                }
            }
        }

        /// <summary>追加成本名称和真实拥有数量；无效配置显示为未配置。</summary>
        private static void AppendCostLines(List<string> lines, GrowthCost cost)
        {
            if (cost == null || (cost.ItemCosts.Count == 0 && cost.CurrencyCosts.Count == 0))
            {
                lines.Add("消耗：暂未配置");
                return;
            }

            for (int index = 0; index < cost.ItemCosts.Count; index++)
            {
                ItemCostEntry itemCost = cost.ItemCosts[index];
                lines.Add($"素材 {itemCost.ItemId}：{StackableInventoryManager.Instance.GetQuantity(itemCost.ItemId)}/{itemCost.Quantity}");
            }

            for (int index = 0; index < cost.CurrencyCosts.Count; index++)
            {
                CurrencyCostEntry currencyCost = cost.CurrencyCosts[index];
                lines.Add($"货币 {currencyCost.CurrencyId}：{CurrencyManager.Instance.GetBalance(currencyCost.CurrencyId)}/{currencyCost.Amount}");
            }
        }

        /// <summary>读取成长消耗中的指定货币金额；未配置时返回零。</summary>
        /// <param name="cost">成长消耗配置。</param>
        /// <param name="currencyId">要读取的货币标识。</param>
        /// <returns>指定货币的需求总量。</returns>
        private static long GetCurrencyCost(GrowthCost cost, CurrencyId currencyId)
        {
            if (cost?.CurrencyCosts == null) return 0L;
            long total = 0L;
            for (int index = 0; index < cost.CurrencyCosts.Count; index++)
            {
                CurrencyCostEntry entry = cost.CurrencyCosts[index];
                if (entry != null && entry.CurrencyId == currencyId) total += Math.Max(0, entry.Amount);
            }

            return total;
        }

        /// <summary>将成长成本格式化为独立状态文本，避免成本与页面摘要占用同一文本区域。</summary>
        /// <param name="cost">待格式化的成长成本。</param>
        /// <returns>成本摘要或未配置提示。</returns>
        private static string BuildCostSummary(GrowthCost cost)
        {
            if (cost == null || (cost.ItemCosts.Count == 0 && cost.CurrencyCosts.Count == 0))
                return "消耗：暂未配置";

            var lines = new List<string>();
            AppendCostLines(lines, cost);
            return string.Join("；", lines);
        }

        #endregion

        #region 候选列表与事件

        /// <summary>按当前成长模式或精炼页绑定对应的候选材料列表。</summary>
        private void BindSelectionPanel()
        {
            if (selectionPanel == null) return;
            if (stateModel.CurrentPage == WeaponDevelopmentPage.Growth && currentGrowthMode == WeaponGrowthMode.Enhancement)
            {
                BindEnhancementSelectionPanel();
                return;
            }

            if (stateModel.CurrentPage != WeaponDevelopmentPage.Refinement ||
                !WeaponInventoryManager.Instance.TryGetInstance(targetInstanceId, out WeaponInstance target)) return;
            var entries = new List<BagItemViewData>();
            var selectedEntryKeys = new List<BagEntryKey>();
            IReadOnlyList<WeaponInstance> instances = WeaponInventoryManager.Instance.GetInstances();
            for (int index = 0; index < instances.Count; index++)
            {
                WeaponInstance instance = instances[index];
                if (instance.InstanceId == target.InstanceId || instance.DefinitionId != target.DefinitionId ||
                    instance.IsLocked || instance.IsEquipped) continue;
                if (!ItemManager.Instance.TryGetDefinition(instance.DefinitionId, out ItemDefinition item) ||
                    !(item is WeaponDefinition definition)) continue;
                Sprite icon = spriteAtlasLeaseService.TryGetSprite(definition.IconAddress, definition.IconSpriteName, out Sprite resolved)
                    ? resolved
                    : null;
                var entryKey = new BagEntryKey(ItemCategory.Weapon, instance.InstanceId.ToString());
                entries.Add(new BagItemViewData(
                    entryKey, definition.DisplayName,
                    (int)definition.Rarity, $"Lv.{instance.Level}", icon, null, string.Empty,
                    false, instance.IsLocked, instance.IsEquipped));
                // 材料实例 ID 是状态模型的唯一来源；这里只投影仍属于当前候选列表的稳定网格键。
                if (IsMaterialSelected(instance.InstanceId)) selectedEntryKeys.Add(entryKey);
            }

            selectionPanel.Bind(entries, selectedEntryKeys, null);
        }

        /// <summary>绑定武器强化素材候选并投影当前会话数量。</summary>
        private void BindEnhancementSelectionPanel()
        {
            var entries = new List<BagItemViewData>();
            var selectedEntryKeys = new List<BagEntryKey>();
            IReadOnlyList<StackableInventoryEntry> inventory =
                StackableInventoryManager.Instance.GetDevelopmentExperienceItems(DevelopmentExperienceItemType.Weapon);
            for (int index = 0; index < inventory.Count; index++)
            {
                StackableInventoryEntry entry = inventory[index];
                if (entry == null || entry.Quantity <= 0 ||
                    !ItemManager.Instance.TryGetDefinition(entry.ItemId, out ItemDefinition item) ||
                    !(item is DevelopmentExperienceItemDefinition definition) || definition.ExperienceValue <= 0) continue;

                Sprite icon = spriteAtlasLeaseService.TryGetSprite(definition.IconAddress, definition.IconSpriteName,
                    out Sprite resolved) ? resolved : null;
                var entryKey = new BagEntryKey(ItemCategory.DevelopmentExperienceItem, entry.ItemId.ToString());
                int selectedQuantity = stateModel.SelectedEnhancementQuantities.TryGetValue(entry.ItemId,
                    out int selected) ? selected : 0;
                entries.Add(new BagItemViewData(entryKey, definition.DisplayName, (int)definition.Rarity,
                    $"{selectedQuantity}/{entry.Quantity}", icon, null, string.Empty, false, false, false));
                if (selectedQuantity > 0) selectedEntryKeys.Add(entryKey);
            }

            selectionPanel.BindQuantitySelection(entries, selectedEntryKeys);
        }

        /// <summary>判断当前展开的材料面板是否仍匹配页面和成长模式。</summary>
        /// <returns>面板可以继续保留时返回 true。</returns>
        private bool IsSelectionPanelValidForCurrentPage()
        {
            return stateModel.CurrentPage == WeaponDevelopmentPage.Refinement ||
                   (stateModel.CurrentPage == WeaponDevelopmentPage.Growth &&
                    currentGrowthMode == WeaponGrowthMode.Enhancement);
        }

        /// <summary>判断一个候选武器实例是否处于当前培养材料临时选择中。</summary>
        /// <param name="instanceId">候选武器实例标识。</param>
        /// <returns>实例已被选为材料时返回 true。</returns>
        private bool IsMaterialSelected(EquipmentInstanceId instanceId)
        {
            foreach (EquipmentInstanceId selectedMaterialId in stateModel.SelectedMaterialIds)
                if (selectedMaterialId == instanceId) return true;
            return false;
        }

        /// <summary>目标武器实例变化时刷新页面或关闭已失效窗口。</summary>
        /// <param name="eventArgs">武器实例变化事件。</param>
        private void HandleWeaponChanged(WeaponInstanceChangedEvent eventArgs)
        {
            if (!hasTarget || eventArgs.Instance == null) return;
            if (eventArgs.Instance.InstanceId == targetInstanceId &&
                eventArgs.ChangeType == EquipmentInstanceChangeType.Removed)
            {
                CloseWindow();
                return;
            }

            if (eventArgs.Instance.InstanceId == targetInstanceId || stateModel.SelectionPanelVisible)
                Refresh();
        }

        /// <summary>刷新图集并在完成后重新绑定候选列表。</summary>
        private async UniTask PrepareAtlasAsync()
        {
            await spriteAtlasLeaseService.BeginLoadConfiguredAtlasesAsync();
            if (disposed) return;

            // 图集完成后重新构建当前页面数据，使升级已选素材和突破材料立即拿到真实 Sprite。
            Refresh();
        }

        /// <summary>查找当前突破阶数后的下一阶段。</summary>
        private static WeaponAscensionStage FindNextAscensionStage(WeaponDefinition definition, int ascensionRank)
        {
            int index = ascensionRank;
            return index >= 0 && index < definition.AscensionStages.Count ? definition.AscensionStages[index] : null;
        }

        /// <summary>查找当前精炼阶数后的下一阶段。</summary>
        private static WeaponRefinementStage FindNextRefinementStage(WeaponDefinition definition, int refinementRank)
        {
            for (int index = 0; index < definition.RefinementStages.Count; index++)
                if (definition.RefinementStages[index].Rank > refinementRank) return definition.RefinementStages[index];
            return null;
        }

        /// <summary>查询当前目标 Definition 和实例。</summary>
        private bool TryGetTarget(out WeaponDefinition definition, out WeaponInstance instance)
        {
            definition = null;
            instance = null;
            return hasTarget && WeaponInventoryManager.IsConfigured && ItemManager.Instance.IsConfigured &&
                   WeaponInventoryManager.Instance.TryGetInstance(targetInstanceId, out instance) &&
                   ItemManager.Instance.TryGetDefinition(instance.DefinitionId, out ItemDefinition item) &&
                   (definition = item as WeaponDefinition) != null;
        }

        /// <summary>记录资源异步流程中的非取消异常。</summary>
        private void HandleAsyncException(Exception exception)
        {
            if (!(exception is OperationCanceledException)) Debug.LogException(exception, this);
        }

        #endregion

        #region 内部展示类型

        /// <summary>保存自动添加强化素材所需的库存数量与单位经验。</summary>
        private readonly struct EnhancementMaterialCandidate
        {
            /// <summary>创建强化素材候选项。</summary>
            /// <param name="itemId">素材标识。</param>
            /// <param name="quantity">库存数量。</param>
            /// <param name="experienceValue">每个素材提供的经验值。</param>
            public EnhancementMaterialCandidate(ItemId itemId, int quantity, int experienceValue)
            {
                ItemId = itemId;
                Quantity = quantity;
                ExperienceValue = experienceValue;
            }

            /// <summary>素材标识。</summary>
            public ItemId ItemId { get; }

            /// <summary>可用库存数量。</summary>
            public int Quantity { get; }

            /// <summary>单个素材提供的经验值。</summary>
            public int ExperienceValue { get; }
        }

        /// <summary>保存同一 GameplayAttribute 聚合后的展示值。</summary>
        private readonly struct AttributeDisplayValue
        {
            /// <summary>创建属性展示值。</summary>
            /// <param name="attribute">属性定义。</param>
            /// <param name="type">聚合使用的 Modifier 类型。</param>
            /// <param name="value">聚合后的数值或倍率。</param>
            public AttributeDisplayValue(GameplayAttribute attribute, AttributeModifierType type, float value)
            {
                Attribute = attribute;
                Type = type;
                Value = value;
            }

            /// <summary>属性定义。</summary>
            public GameplayAttribute Attribute { get; }

            /// <summary>Modifier 类型。</summary>
            public AttributeModifierType Type { get; }

            /// <summary>聚合后的数值或倍率。</summary>
            public float Value { get; }
        }

        #endregion
    }
}
