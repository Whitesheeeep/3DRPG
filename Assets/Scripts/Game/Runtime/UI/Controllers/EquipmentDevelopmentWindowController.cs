using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using RPG.CurrencySystem;
using RPG.Game.UI.Bag;
using RPG.Game.UI.EquipmentDevelopment;
using RPG.Game.UI.Escape;
using RPG.Game.UI.Services;
using RPG.Game.UI.Views.Common;
using RPG.Game.UI.Views.WeaponDevelopment;
using RPG.Game.UI.WeaponDevelopment;
using RPG.Game.Runtime.ArtifactDevelopment;
using RPG.Game.Runtime.EquipmentDevelopment;
using RPG.Game.Runtime.WeaponDevelopment;
using RPG.ItemSystem;
using UnityEngine;
using WS_Modules.BusinessArchitecture;
using WS_Modules.CustomEventSystem;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.GameplayEffect;
using WS_Modules.UIModule;

namespace RPG.Game.UI.Controllers
{
    /// <summary>协调武器与圣遗物统一培养窗口的库存查询、页面状态、图集租约和用户意图。</summary>
    [DisallowMultipleComponent]
    public sealed class EquipmentDevelopmentWindowController : MonoBehaviour
    {
        #region 依赖字段

        private EquipmentDevelopmentWindowDataComponent data;
        private EquipmentDevelopmentView view;
        private ItemSelectionPanelView selectionPanel;
        private WindowSpriteAtlasLeaseService spriteAtlasLeaseService;
        private WeaponDevelopmentUIStateModel stateModel;
        private IArchitecture architecture;
        private WeaponInventoryManager weaponInventoryManager;
        private ArtifactInventoryManager artifactInventoryManager;
        private StackableInventoryManager stackableInventoryManager;
        private IUnRegister weaponChangedUnregister;
        private IUnRegister currencyChangedUnregister;
        private IUnRegister currencyRestoredUnregister;
        private IUnRegister artifactChangedUnregister;
        private IUnRegister artifactRestoredUnregister;
        private IUnRegister stackableChangedUnregister;
        private IUnRegister stackableRestoredUnregister;
        private WeaponDevelopmentService developmentService;
        private ArtifactDevelopmentService artifactDevelopmentService;
        private WeaponPreviewRuntime weaponPreviewRuntime;

        #endregion

        #region 状态字段

        private EquipmentInstanceId targetInstanceId;
        private EquipmentDevelopmentTargetKind targetKind;
        private bool hasTarget;
        private bool initialized;
        private bool disposed;
        private bool windowShown;
        private bool developmentOperationRunning;
        private EscCommandRegistration selectionEscRegistration;
        private EquipmentGrowthMode currentGrowthMode = EquipmentGrowthMode.ConfigurationUnavailable;
        private const int MaxSelectedExperienceMaterialCount = 99;
        private bool selectionLimitLogIssued;

        #endregion

        #region 初始化与释放

        /// <summary>绑定窗口序列化依赖并订阅武器实例变化。</summary>
        /// <param name="windowData">窗口序列化数据。</param>
        public void Initialize(EquipmentDevelopmentWindowDataComponent windowData)
        {
            if (initialized)
            {
                if (!ReferenceEquals(data, windowData)) throw new InvalidOperationException("[WeaponDevelopment] 不允许替换窗口数据组件。");
                return;
            }

            data = windowData ?? throw new ArgumentNullException(nameof(windowData));
            data.ValidateConfiguration();
            architecture = GameArchitecture.Interface;
            weaponInventoryManager = architecture.GetManager<WeaponInventoryManager>();
            artifactInventoryManager = architecture.GetManager<ArtifactInventoryManager>();
            stackableInventoryManager = architecture.GetManager<StackableInventoryManager>();
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
            view.EnhancementRequested += HandleEnhancementRequested;
            view.AscensionRequested += HandleAscensionRequested;
            view.RefinementRequested += HandleRefinementRequested;
            if (selectionPanel != null)
            {
                selectionPanel.EntryClicked += HandleRefinementMaterialClicked;
                selectionPanel.QuantityChangeRequested += HandleEnhancementQuantityChanged;
                selectionPanel.ReturnRequested += CloseSelectionPanel;
            }

            weaponChangedUnregister = EventSystem.Register_Type<WeaponInstanceChangedEvent>(
                typeof(WeaponInstanceChangedEvent), HandleWeaponChanged);
            currencyChangedUnregister = EventSystem.Register_Type<CurrencyBalanceChangedEvent>(
                typeof(CurrencyBalanceChangedEvent), HandleCurrencyBalanceChanged);
            currencyRestoredUnregister = EventSystem.Register_Type<CurrencyWalletRestoredEvent>(
                typeof(CurrencyWalletRestoredEvent), HandleCurrencyWalletRestored);
            artifactChangedUnregister = EventSystem.Register_Type<ArtifactInstanceChangedEvent>(
                typeof(ArtifactInstanceChangedEvent), HandleArtifactChanged);
            artifactRestoredUnregister = EventSystem.Register_Type<ArtifactInventoryRestoredEvent>(
                typeof(ArtifactInventoryRestoredEvent), HandleArtifactRestored);
            stackableChangedUnregister = EventSystem.Register_Type<StackableItemChangedEvent>(
                typeof(StackableItemChangedEvent), HandleStackableChanged);
            stackableRestoredUnregister = EventSystem.Register_Type<StackableInventoryRestoredEvent>(
                typeof(StackableInventoryRestoredEvent), HandleStackableRestored);
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
            view.EnhancementRequested -= HandleEnhancementRequested;
            view.AscensionRequested -= HandleAscensionRequested;
            view.RefinementRequested -= HandleRefinementRequested;
            if (selectionPanel != null)
            {
                selectionPanel.EntryClicked -= HandleRefinementMaterialClicked;
                selectionPanel.QuantityChangeRequested -= HandleEnhancementQuantityChanged;
                selectionPanel.ReturnRequested -= CloseSelectionPanel;
            }

            weaponChangedUnregister?.UnRegister();
            weaponChangedUnregister = null;
            currencyChangedUnregister?.UnRegister();
            currencyChangedUnregister = null;
            currencyRestoredUnregister?.UnRegister();
            currencyRestoredUnregister = null;
            artifactChangedUnregister?.UnRegister();
            artifactChangedUnregister = null;
            artifactRestoredUnregister?.UnRegister();
            artifactRestoredUnregister = null;
            stackableChangedUnregister?.UnRegister();
            stackableChangedUnregister = null;
            stackableRestoredUnregister?.UnRegister();
            stackableRestoredUnregister = null;
            selectionPanel?.HideImmediateAndReset();
            UnregisterSelectionEscCommand();
            spriteAtlasLeaseService?.Dispose();
            spriteAtlasLeaseService = null;
            weaponPreviewRuntime?.Dispose();
            weaponPreviewRuntime = null;
            developmentService = null;
            artifactDevelopmentService = null;
            windowShown = false;
            weaponInventoryManager = null;
            artifactInventoryManager = null;
            stackableInventoryManager = null;
        }

        #endregion

        #region 窗口生命周期

        /// <summary>
        /// 驱动当前可见窗口的预览尺寸检测；模型旋转由 Rig 自身按非缩放时间处理。
        /// </summary>
        private void Update()
        {
            if (windowShown)
                weaponPreviewRuntime?.Tick();
        }

        /// <summary>应用新的目标实例并恢复成长页初始状态。</summary>
        /// <param name="context">包含装备类型和实例标识的打开上下文。</param>
        public void SetTarget(EquipmentDevelopmentOpenContext context)
        {
            bool targetChanged = !hasTarget || targetKind != context.TargetKind || targetInstanceId != context.InstanceId;
            if (targetChanged)
            {
                // 同一个预加载窗口可以在可见期间切换武器与圣遗物；先释放旧目标专属服务，避免事件刷新旧目标。
                developmentService = null;
                artifactDevelopmentService = null;
                weaponPreviewRuntime?.Clear();
            }
            targetKind = context.TargetKind;
            targetInstanceId = context.InstanceId;
            hasTarget = true;
            // OpenContext 可能在窗口仍可见时切换目标；先同步隐藏旧目标的材料面板并移除 Esc 层级。
            selectionPanel?.HideImmediateAndReset();
            UnregisterSelectionEscCommand();
            stateModel.Reset();
            selectionLimitLogIssued = false;
            if (windowShown)
            {
                // UIManager 复用已可见窗口时不会再次触发完整 Show 生命周期，因此这里主动重建服务并刷新新目标。
                EnsureCurrentDevelopmentService();
                Refresh();
                StartWeaponPreviewLoading();
            }
            Debug.Log($"[EquipmentDevelopment] 切换培养目标：Kind={targetKind}，Instance={targetInstanceId}，默认页面=Growth。", this);
        }

        /// <summary>窗口稳定显示时刷新目标数据并开始动态图集准备。</summary>
        public void OnWindowShown()
        {
            if (disposed) return;
            windowShown = true;
            EnsureCurrentDevelopmentService();
            spriteAtlasLeaseService.CancelRelease();
            selectionPanel.HideImmediateAndReset();
            Refresh();
            StartWeaponPreviewLoading();
            PrepareAtlasAsync().Forget(HandleAsyncException);
        }

        /// <summary>窗口稳定隐藏时收起选择面板并启动图集延迟释放。</summary>
        public void OnWindowHidden()
        {
            if (disposed) return;
            ResetSelectionPanelImmediately();
            windowShown = false;
            weaponPreviewRuntime?.Dispose();
            weaponPreviewRuntime = null;
            developmentService = null;
            artifactDevelopmentService = null;
            spriteAtlasLeaseService.ScheduleRelease();
        }

        /// <summary>按窗口显示生命周期创建当前窗口专属的培养服务。</summary>
        private void EnsureDevelopmentService()
        {
            if (developmentService != null) return;
            if (weaponInventoryManager == null || stackableInventoryManager == null)
                throw new InvalidOperationException("[WeaponDevelopment] 创建培养服务前必须完成库存 Manager 注入。");

            developmentService = new WeaponDevelopmentService(
                weaponInventoryManager, stackableInventoryManager, CurrencyManager.Instance);
            Debug.Log("[WeaponDevelopment] 已创建窗口级 WeaponDevelopmentService。", this);
        }

        /// <summary>按当前上下文创建对应的窗口级培养服务。</summary>
        private void EnsureCurrentDevelopmentService()
        {
            if (targetKind == EquipmentDevelopmentTargetKind.Weapon)
            {
                EnsureDevelopmentService();
                return;
            }

            if (artifactDevelopmentService == null)
            {
                if (artifactInventoryManager == null || stackableInventoryManager == null)
                    throw new InvalidOperationException("[EquipmentDevelopment] 创建圣遗物培养服务前必须完成 Manager 注入。");
                artifactDevelopmentService = new ArtifactDevelopmentService(
                    artifactInventoryManager, stackableInventoryManager, CurrencyManager.Instance);
                Debug.Log("[EquipmentDevelopment] 已创建窗口级 ArtifactDevelopmentService。", this);
            }
        }

        /// <summary>供 Esc Command 收起当前培养材料选择面板。</summary>
        public void CloseSelectionPanelFromCommand() => CloseSelectionPanel();

        #endregion

        #region 武器预览

        /// <summary>
        /// 创建当前窗口可见期间使用的武器预览运行时。
        /// </summary>
        private void EnsureWeaponPreviewRuntime()
        {
            if (weaponPreviewRuntime != null) return;
            weaponPreviewRuntime = new WeaponPreviewRuntime(data.WeaponPreviewRig, view.WeaponPreviewViewport);
        }

        /// <summary>
        /// 根据当前目标异步加载武器模型；圣遗物目标保持预览区空白。
        /// </summary>
        private void StartWeaponPreviewLoading()
        {
            if (!windowShown || targetKind != EquipmentDevelopmentTargetKind.Weapon)
            {
                view?.WeaponPreviewViewport?.Clear();
                return;
            }

            if (!TryGetTarget(out WeaponDefinition definition, out _))
            {
                view.WeaponPreviewViewport.Clear();
                return;
            }

            EnsureWeaponPreviewRuntime();
            weaponPreviewRuntime.ShowWeaponAsync(definition).Forget(HandleAsyncException);
        }

        #endregion

        #region 页面与选择

        /// <summary>切换成长或精炼页面，并在离开当前材料选择页时收起面板。</summary>
        /// <param name="page">目标页面。</param>
        private void HandlePageRequested(EquipmentDevelopmentPage page)
        {
            if (targetKind == EquipmentDevelopmentTargetKind.Artifact && page == EquipmentDevelopmentPage.Refinement)
                throw new ArgumentOutOfRangeException(nameof(page), page, "圣遗物培养窗口不支持精炼页面。");
            // 页签切换属于更高层导航；无论离开升级还是精炼页，都先撤销面板的 Esc 注册，避免 Esc 栈残留。
            if (stateModel.SelectionPanelVisible)
            {
                stateModel.SetSelectionPanelVisible(false);
                selectionPanel?.HideAnimated();
                UnregisterSelectionEscCommand();
            }

            stateModel.SetPage(page);
        }

        /// <summary>展开精炼材料选择面板并注册更高层 Esc Command。</summary>
        private void OpenRefinementSelectionPanel()
        {
            if (stateModel.CurrentPage != EquipmentDevelopmentPage.Refinement) return;

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
            BindSelectionPanel();
            RegisterSelectionEscCommand();
            selectionPanel?.ShowAnimated();
            Debug.Log(
                $"[WeaponDevelopment] 打开精炼材料面板：Definition={instance.DefinitionId}，" +
                $"RequiredDuplicateCount={stage.RequiredDuplicateCount}。", this);
        }

        /// <summary>展开武器强化素材选择面板并注册更高层 Esc Command。</summary>
        private void OpenEnhancementSelectionPanel()
        {
            if (stateModel.CurrentPage != EquipmentDevelopmentPage.Growth || currentGrowthMode != EquipmentGrowthMode.Enhancement)
                return;
            bool hasCandidates = targetKind == EquipmentDevelopmentTargetKind.Artifact
                ? HasArtifactEnhancementCandidates()
                : HasEnhancementCandidates();
            if (!hasCandidates && !HasSelectedEnhancementMaterials())
            {
                Debug.Log("[EquipmentDevelopment] 没有可选择的强化素材，忽略打开素材面板。", this);
                return;
            }
            stateModel.SetSelectionPanelVisible(true);
            BindSelectionPanel();
            RegisterSelectionEscCommand();
            selectionPanel?.ShowAnimated();
            Debug.Log("[WeaponDevelopment] 打开武器强化素材面板。", this);
        }

        /// <summary>收起当前培养材料选择面板并注销其 Esc Command。</summary>
        private void CloseSelectionPanel()
        {
            if (!stateModel.SelectionPanelVisible)
            {
                UnregisterSelectionEscCommand();
                return;
            }

            stateModel.SetSelectionPanelVisible(false);
            selectionPanel?.HideAnimated();
            UnregisterSelectionEscCommand();
            Debug.Log("[WeaponDevelopment] 关闭培养材料面板。", this);
        }

        /// <summary>在窗口生命周期变化时立即清理材料面板，不播放退场动画。</summary>
        private void ResetSelectionPanelImmediately()
        {
            stateModel?.SetSelectionPanelVisible(false);
            selectionPanel?.HideImmediateAndReset();
            UnregisterSelectionEscCommand();
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
            if (stateModel.CurrentPage != EquipmentDevelopmentPage.Growth || currentGrowthMode != EquipmentGrowthMode.Enhancement)
                return;
            if (targetKind == EquipmentDevelopmentTargetKind.Artifact)
            {
                HandleArtifactEnhancementQuantityChanged(intent);
                return;
            }
            if (!ItemId.TryCreate(intent.EntryKey.Value, out ItemId itemId) ||
                !ItemManager.Instance.TryGetDefinition(itemId, out ItemDefinition item) ||
                !(item is DevelopmentExperienceItemDefinition definition) ||
                !definition.SupportsExperienceType(DevelopmentExperienceItemType.Weapon))
                return;

            int ownedQuantity = stackableInventoryManager.GetQuantity(itemId);
            int previousQuantity = stateModel.SelectedEnhancementQuantities.TryGetValue(itemId, out int selectedQuantity)
                ? selectedQuantity
                : 0;
            int delta;
            if (intent.Direction == BagItemQuantityDirection.Increase)
            {
                int selectedTotal = GetSelectedEnhancementMaterialCount();
                int availableSlots = MaxSelectedExperienceMaterialCount - selectedTotal;
                if (availableSlots <= 0)
                {
                    LogSelectionLimitReachedOnce();
                    return;
                }

                int availableQuantity = Math.Max(0, ownedQuantity - previousQuantity);
                delta = Math.Min(Math.Min(Math.Max(1, intent.Step), availableQuantity), availableSlots);
                if (delta <= 0) return;
            }
            else
            {
                delta = -Math.Max(1, intent.Step);
            }

            stateModel.AdjustEnhancementMaterial(itemId, delta, ownedQuantity);
            selectionLimitLogIssued = false;
            // 长按会以更大步长重复回调；只记录单步意图，避免高频输入淹没 Console。
            if (intent.Step == 1)
                Debug.Log(
                    $"[WeaponDevelopment] 调整强化素材：Item={itemId}，Direction={intent.Direction}，Step={intent.Step}。", this);
        }

        /// <summary>处理圣遗物经验素材数量调整意图，并把选择限制在最大等级内。</summary>
        /// <param name="intent">条目数量调整意图。</param>
        private void HandleArtifactEnhancementQuantityChanged(BagItemQuantityIntent intent)
        {
            if (!ItemId.TryCreate(intent.EntryKey.Value, out ItemId itemId) ||
                !ItemManager.Instance.TryGetDefinition(itemId, out ItemDefinition item) ||
                !(item is DevelopmentExperienceItemDefinition definition) ||
                !definition.SupportsExperienceType(DevelopmentExperienceItemType.Artifact)) return;

            int ownedQuantity = stackableInventoryManager.GetQuantity(itemId);
            int previousQuantity = stateModel.SelectedEnhancementQuantities.TryGetValue(itemId,
                out int selectedQuantity) ? selectedQuantity : 0;
            int nextQuantity = previousQuantity;
            if (intent.Direction == BagItemQuantityDirection.Decrease)
            {
                nextQuantity = Math.Max(0, previousQuantity - Math.Max(1, intent.Step));
            }
            else
            {
                int availableSlots = MaxSelectedExperienceMaterialCount - GetSelectedEnhancementMaterialCount();
                if (availableSlots <= 0)
                {
                    LogSelectionLimitReachedOnce();
                    return;
                }

                int availableQuantity = Math.Max(0, ownedQuantity - previousQuantity);
                int delta = Math.Min(Math.Min(Math.Max(1, intent.Step), availableQuantity), availableSlots);
                nextQuantity = previousQuantity + delta;
            }

            stateModel.AdjustEnhancementMaterial(itemId, nextQuantity - previousQuantity, ownedQuantity);
            selectionLimitLogIssued = false;
            Debug.Log($"[EquipmentDevelopment] 调整圣遗物经验素材：Item={itemId}，Quantity={nextQuantity}。", this);
        }

        /// <summary>只在一次连续输入首次触及 99 本上限时记录日志。</summary>
        private void LogSelectionLimitReachedOnce()
        {
            if (selectionLimitLogIssued) return;
            selectionLimitLogIssued = true;
            Debug.Log(
                $"[EquipmentDevelopment] 已达到单次素材选择上限：{MaxSelectedExperienceMaterialCount}本，仍可减少已选素材。",
                this);
        }

        /// <summary>按圣遗物当前等级上限自动选择经验素材。</summary>
        private void AutoAddArtifactEnhancementMaterials()
        {
            if (!artifactInventoryManager.TryGetInstance(targetInstanceId, out ArtifactInstance instance) ||
                !ItemManager.Instance.TryGetDefinition(instance.DefinitionId, out ItemDefinition item) ||
                !(item is ArtifactDefinition definition)) return;
            long requiredExperience = GetArtifactRequiredExperienceToCap(definition, instance);

            IReadOnlyList<StackableInventoryEntry> inventory =
                stackableInventoryManager.GetDevelopmentExperienceItems(DevelopmentExperienceItemType.Artifact);
            var selections = new List<EnhancementMaterialSelection>(inventory.Count);
            for (int index = 0; index < inventory.Count; index++)
            {
                StackableInventoryEntry entry = inventory[index];
                if (entry == null || entry.Quantity <= 0 ||
                    !ItemManager.Instance.TryGetDefinition(entry.ItemId, out ItemDefinition materialItem) ||
                    !(materialItem is DevelopmentExperienceItemDefinition material) ||
                    !material.SupportsExperienceType(DevelopmentExperienceItemType.Artifact) ||
                    material.ExperienceValue <= 0) continue;
                selections.Add(new EnhancementMaterialSelection(entry.ItemId,
                    Math.Min(entry.Quantity, MaxSelectedExperienceMaterialCount), material.ExperienceValue));
            }

            var selectedQuantityByItemIdMap = new Dictionary<ItemId, int>();
            EnhancementMaterialConsumptionPlan plan =
                EnhancementMaterialConsumptionPlanner.Build(
                    requiredExperience, selections, MaxSelectedExperienceMaterialCount);
            foreach (KeyValuePair<ItemId, int> pair in plan.ConsumedQuantityByItemIdMap)
                selectedQuantityByItemIdMap[pair.Key] = pair.Value;
            stateModel.ReplaceEnhancementMaterials(selectedQuantityByItemIdMap);
            selectionLimitLogIssued = false;
            Debug.Log($"[EquipmentDevelopment] 自动添加圣遗物经验素材：种类={selectedQuantityByItemIdMap.Count}，" +
                      $"经验={plan.ConsumedExperience}，Overflow={plan.OverflowExperience}。", this);
        }

        /// <summary>提交圣遗物升级并在成功后清空当前临时素材。</summary>
        private void HandleArtifactEnhancementRequested()
        {
            if (developmentOperationRunning || targetKind != EquipmentDevelopmentTargetKind.Artifact) return;
            EnsureCurrentDevelopmentService();
            developmentOperationRunning = true;
            try
            {
                ArtifactDevelopmentOperationResult result = artifactDevelopmentService.Enhance(
                    targetInstanceId, stateModel.SelectedEnhancementQuantities);
                if (!result.Succeeded)
                {
                    Debug.LogWarning($"[EquipmentDevelopment] 圣遗物升级未执行：Status={result.Status}，Message={result.Message}。", this);
                    return;
                }

                stateModel.ClearEnhancementMaterials();
                CloseSelectionPanel();
                Refresh();
            }
            finally
            {
                developmentOperationRunning = false;
            }
        }

        /// <summary>按当前阶段上限自动选择武器强化素材。</summary>
        private void AutoAddEnhancementMaterials()
        {
            if (stateModel.CurrentPage != EquipmentDevelopmentPage.Growth || currentGrowthMode != EquipmentGrowthMode.Enhancement)
                return;
            if (targetKind == EquipmentDevelopmentTargetKind.Artifact)
            {
                AutoAddArtifactEnhancementMaterials();
                return;
            }
            if (!TryGetTarget(out WeaponDefinition definition, out WeaponInstance instance)) return;
            long requiredExperience = GetRequiredExperienceToCap(definition, instance, out _);

            IReadOnlyList<StackableInventoryEntry> inventory =
                stackableInventoryManager.GetDevelopmentExperienceItems(DevelopmentExperienceItemType.Weapon);
            var selections = new List<EnhancementMaterialSelection>(inventory.Count);
            for (int index = 0; index < inventory.Count; index++)
            {
                StackableInventoryEntry entry = inventory[index];
                if (entry == null || entry.Quantity <= 0 ||
                    !ItemManager.Instance.TryGetDefinition(entry.ItemId, out ItemDefinition item) ||
                    !(item is DevelopmentExperienceItemDefinition material) || material.ExperienceValue <= 0) continue;
                selections.Add(new EnhancementMaterialSelection(entry.ItemId,
                    Math.Min(entry.Quantity, MaxSelectedExperienceMaterialCount), material.ExperienceValue));
            }

            var selectedQuantityByItemIdMap = new Dictionary<ItemId, int>();
            EnhancementMaterialConsumptionPlan plan =
                EnhancementMaterialConsumptionPlanner.Build(
                    requiredExperience, selections, MaxSelectedExperienceMaterialCount);
            foreach (KeyValuePair<ItemId, int> pair in plan.ConsumedQuantityByItemIdMap)
                selectedQuantityByItemIdMap[pair.Key] = pair.Value;
            stateModel.ReplaceEnhancementMaterials(selectedQuantityByItemIdMap);
            selectionLimitLogIssued = false;
            Debug.Log($"[EquipmentDevelopment] 自动添加武器经验素材：种类={selectedQuantityByItemIdMap.Count}，" +
                      $"经验={plan.ConsumedExperience}，Overflow={plan.OverflowExperience}。", this);
        }

        /// <summary>处理升级页的确认操作，并在成功后清理本次临时选择。</summary>
        private void HandleEnhancementRequested()
        {
            if (targetKind == EquipmentDevelopmentTargetKind.Artifact)
            {
                HandleArtifactEnhancementRequested();
                return;
            }
            if (developmentOperationRunning || !TryGetTarget(out _, out WeaponInstance instance)) return;
            EnsureDevelopmentService();
            developmentOperationRunning = true;
            try
            {
                WeaponDevelopmentOperationResult result = developmentService.Enhance(
                    instance.InstanceId, stateModel.SelectedEnhancementQuantities);
                if (!result.Succeeded)
                {
                    Debug.LogWarning($"[WeaponDevelopment] 升级预览操作未执行：Status={result.Status}，Message={result.Message}。", this);
                    return;
                }

                stateModel.ClearEnhancementMaterials();
                CloseSelectionPanel();
                Debug.Log($"[WeaponDevelopment] 完成武器升级：Instance={instance.InstanceId}。", this);
                Refresh();
            }
            finally
            {
                developmentOperationRunning = false;
            }
        }

        /// <summary>处理突破页的确认操作，并在成功后关闭材料选择面板。</summary>
        private void HandleAscensionRequested()
        {
            if (developmentOperationRunning || !TryGetTarget(out _, out WeaponInstance instance)) return;
            EnsureDevelopmentService();
            developmentOperationRunning = true;
            try
            {
                WeaponDevelopmentOperationResult result = developmentService.Ascend(instance.InstanceId);
                if (!result.Succeeded)
                {
                    Debug.LogWarning($"[WeaponDevelopment] 突破操作未执行：Status={result.Status}，Message={result.Message}。", this);
                    return;
                }

                CloseSelectionPanel();
                Debug.Log($"[WeaponDevelopment] 完成武器突破：Instance={instance.InstanceId}。", this);
                Refresh();
            }
            finally
            {
                developmentOperationRunning = false;
            }
        }

        /// <summary>处理精炼页的确认操作，并在成功后清理已选同名武器。</summary>
        private void HandleRefinementRequested()
        {
            if (developmentOperationRunning || !TryGetTarget(out _, out WeaponInstance instance)) return;
            EnsureDevelopmentService();
            developmentOperationRunning = true;
            try
            {
                WeaponDevelopmentOperationResult result = developmentService.Refine(
                    instance.InstanceId, stateModel.SelectedMaterialIds);
                if (!result.Succeeded)
                {
                    Debug.LogWarning($"[WeaponDevelopment] 精炼操作未执行：Status={result.Status}，Message={result.Message}。", this);
                    return;
                }

                stateModel.ClearRefinementMaterials();
                CloseSelectionPanel();
                Debug.Log($"[WeaponDevelopment] 完成武器精炼：Instance={instance.InstanceId}。", this);
                Refresh();
            }
            finally
            {
                developmentOperationRunning = false;
            }
        }

        #endregion

        #region Esc 注册

        /// <summary>注册选择面板关闭 Command。</summary>
        private void RegisterSelectionEscCommand()
        {
            if (selectionEscRegistration.IsValid) return;
            architecture ??= GameArchitecture.Interface;
            selectionEscRegistration = architecture.SendCommand(
                new RegisterEscCommand(new CloseEquipmentSelectionPanelCommand()));
        }

        /// <summary>注销选择面板关闭 Command。</summary>
        private void UnregisterSelectionEscCommand()
        {
            if (!selectionEscRegistration.IsValid) return;
            architecture?.SendCommand(new UnregisterEscCommand(selectionEscRegistration));
            selectionEscRegistration = default;
        }

        /// <summary>请求通过 UIManager 关闭当前培养窗口。</summary>
        private void CloseWindow() => UIManager.Instance.HideWindowAsync<EquipmentDevelopmentWindow>().Forget(HandleAsyncException);

        #endregion

        #region 数据刷新

        /// <summary>从 Manager 构建当前页面展示数据。</summary>
        private void Refresh()
        {
            if (disposed || !windowShown || !hasTarget || !ItemManager.Instance.IsConfigured)
                return;
            if (targetKind == EquipmentDevelopmentTargetKind.Artifact)
            {
                RefreshArtifact();
                return;
            }
            if (!WeaponInventoryManager.IsConfigured) return;
            if (!weaponInventoryManager.TryGetInstance(targetInstanceId, out WeaponInstance instance)) return;
            if (!ItemManager.Instance.TryGetDefinition(instance.DefinitionId, out ItemDefinition item) ||
                !(item is WeaponDefinition definition)) return;

            WeaponDetails details = WeaponDetailsQuery.Create(definition, instance);
            IReadOnlyList<string> lines = BuildDetailsLines(details);
            EquipmentDevelopmentViewData pageData = BuildPageData(definition, instance, lines);
            string mora = CurrencyManager.Instance.GetBalance(CurrencyId.Mola).ToString();
            view.Bind(definition.DisplayName, definition.WeaponType.ToString(), mora, null,
                EquipmentDevelopmentTargetKind.Weapon, pageData);
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

        /// <summary>刷新圣遗物目标的统一升级页面。</summary>
        private void RefreshArtifact()
        {
            if (!ArtifactInventoryManager.IsConfigured || artifactInventoryManager == null ||
                !artifactInventoryManager.TryGetInstance(targetInstanceId, out ArtifactInstance instance) ||
                !ItemManager.Instance.TryGetDefinition(instance.DefinitionId, out ItemDefinition item) ||
                !(item is ArtifactDefinition definition)) return;

            EquipmentDevelopmentViewData pageData = BuildArtifactPageData(definition, instance);
            Sprite icon = spriteAtlasLeaseService.TryGetSprite(definition.IconAddress, definition.IconSpriteName,
                out Sprite resolved) ? resolved : null;
            string slotText = $"圣遗物 · {GetArtifactSlotLabel(definition.Slot)}";
            string mora = CurrencyManager.Instance.GetBalance(CurrencyId.Mola).ToString();
            view.Bind(definition.DisplayName, slotText, mora, icon,
                EquipmentDevelopmentTargetKind.Artifact, pageData);
            if (!stateModel.SelectionPanelVisible) return;
            if (!IsSelectionPanelValidForCurrentPage())
            {
                CloseSelectionPanel();
                return;
            }

            BindSelectionPanel();
        }

        /// <summary>构建圣遗物升级页面的当前、预计属性和素材投影。</summary>
        /// <param name="definition">圣遗物 Definition。</param>
        /// <param name="instance">目标实例。</param>
        /// <returns>统一装备培养页面数据。</returns>
        private EquipmentDevelopmentViewData BuildArtifactPageData(ArtifactDefinition definition, ArtifactInstance instance)
        {
            EnsureCurrentDevelopmentService();
            bool validProjection = artifactDevelopmentService.TryProject(instance.InstanceId,
                stateModel.SelectedEnhancementQuantities, out ArtifactDevelopmentProjection projection);
            EquipmentGrowthMode mode = validProjection
                ? (instance.Level >= definition.MaxLevel ? EquipmentGrowthMode.MaxLevel : EquipmentGrowthMode.Enhancement)
                : EquipmentGrowthMode.ConfigurationUnavailable;
            currentGrowthMode = mode;

            IReadOnlyList<string> currentLines = BagGameplayEffectPresentationBuilder.BuildStaticAttributeLines(
                definition.LevelEffects, instance.Level, $"圣遗物 {definition.DisplayName}");
            IReadOnlyList<string> projectedLines = validProjection
                ? BagGameplayEffectPresentationBuilder.BuildStaticAttributeLines(
                    definition.LevelEffects, projection.Level, $"圣遗物 {definition.DisplayName} 预计")
                : currentLines;
            IReadOnlyList<string> previewLines = BuildComparisonLines(currentLines, projectedLines);
            IReadOnlyList<BagItemViewData> selectedMaterials = BuildSelectedArtifactEnhancementEntries();
            long currencyOwned = CurrencyManager.Instance.GetBalance(CurrencyId.Mola);
            long selectedExperience = GetSelectedArtifactExperience();
            int projectedLevel = validProjection ? projection.Level : instance.Level;
            int projectedExperience = validProjection ? projection.CurrentExperience : instance.CurrentExperience;
            int projectedNextExperience = validProjection ? projection.NextExperience : 0;
            float progress = validProjection ? projection.Progress : 0f;
            bool hasCandidates = HasArtifactEnhancementCandidates();
            bool hasSelectedMaterials = HasSelectedEnhancementMaterials();
            bool canEnhance = mode == EquipmentGrowthMode.Enhancement && selectedExperience > 0L &&
                              validProjection &&
                              (projectedLevel != instance.Level || projectedExperience != instance.CurrentExperience) &&
                              projection.CurrencyCost <= currencyOwned;
            string title = mode == EquipmentGrowthMode.ConfigurationUnavailable ? "培养配置不可用" :
                mode == EquipmentGrowthMode.MaxLevel ? "圣遗物已满级" : "圣遗物升级";
            string subtitle = mode == EquipmentGrowthMode.ConfigurationUnavailable
                ? "请补齐圣遗物成长配置"
                : "使用养成经验道具提升等级";
            var enhancement = new EquipmentEnhancementViewData(
                title, subtitle, instance.Level, projectedLevel, selectedExperience,
                projectedExperience, projectedNextExperience, progress, previewLines, selectedMaterials,
                currencyOwned, validProjection ? projection.CurrencyCost : 0L,
                canEnhance, mode == EquipmentGrowthMode.MaxLevel ? "已满级" : "升级",
                mode == EquipmentGrowthMode.Enhancement && (hasCandidates || hasSelectedMaterials),
                mode == EquipmentGrowthMode.Enhancement && hasCandidates);
            return new EquipmentDevelopmentViewData(EquipmentDevelopmentPage.Growth,
                mode == EquipmentGrowthMode.Enhancement ? "升级" :
                mode == EquipmentGrowthMode.MaxLevel ? "已满级" : "培养",
                mode, enhancement, null, null);
        }

        /// <summary>按圣遗物经验候选顺序构建已选择的经验素材卡片。</summary>
        /// <returns>已选数量大于零的素材条目。</returns>
        private IReadOnlyList<BagItemViewData> BuildSelectedArtifactEnhancementEntries()
        {
            var result = new List<BagItemViewData>();
            IReadOnlyList<StackableInventoryEntry> inventory =
                stackableInventoryManager.GetDevelopmentExperienceItems(DevelopmentExperienceItemType.Artifact);
            for (int index = 0; index < inventory.Count; index++)
            {
                StackableInventoryEntry entry = inventory[index];
                if (entry == null || entry.Quantity <= 0 ||
                    !ItemManager.Instance.TryGetDefinition(entry.ItemId, out ItemDefinition item) ||
                    !(item is DevelopmentExperienceItemDefinition definition) ||
                    !definition.SupportsExperienceType(DevelopmentExperienceItemType.Artifact) ||
                    !stateModel.SelectedEnhancementQuantities.TryGetValue(entry.ItemId, out int selected) || selected <= 0) continue;
                Sprite icon = spriteAtlasLeaseService.TryGetSprite(definition.IconAddress, definition.IconSpriteName,
                    out Sprite resolved) ? resolved : null;
                result.Add(new BagItemViewData(
                    new BagEntryKey(ItemCategory.DevelopmentExperienceItem, entry.ItemId.ToString()),
                    definition.DisplayName, (int)definition.Rarity, FormatSelectedQuantity(selected, entry.Quantity),
                    icon, null, string.Empty, false, false, false));
            }

            return result;
        }

        /// <summary>汇总当前圣遗物升级会话选择的经验。</summary>
        /// <returns>所选经验总量。</returns>
        private long GetSelectedArtifactExperience()
        {
            if (!artifactInventoryManager.TryGetInstance(targetInstanceId, out ArtifactInstance instance) ||
                !ItemManager.Instance.TryGetDefinition(instance.DefinitionId, out ItemDefinition item) ||
                !(item is ArtifactDefinition definition)) return 0L;
            return BuildSelectedConsumptionPlan(DevelopmentExperienceItemType.Artifact,
                GetArtifactRequiredExperienceToCap(definition, instance)).ConsumedExperience;
        }

        /// <summary>判断是否存在可用于圣遗物升级的经验道具。</summary>
        /// <returns>存在候选时返回 true。</returns>
        private bool HasArtifactEnhancementCandidates()
        {
            IReadOnlyList<StackableInventoryEntry> entries =
                stackableInventoryManager.GetDevelopmentExperienceItems(DevelopmentExperienceItemType.Artifact);
            for (int index = 0; index < entries.Count; index++)
            {
                StackableInventoryEntry entry = entries[index];
                if (entry != null && entry.Quantity > 0 &&
                    ItemManager.Instance.TryGetDefinition(entry.ItemId, out ItemDefinition item) &&
                    item is DevelopmentExperienceItemDefinition definition && definition.ExperienceValue > 0 &&
                    definition.SupportsExperienceType(DevelopmentExperienceItemType.Artifact)) return true;
            }

            return false;
        }

        /// <summary>计算圣遗物从当前进度到最大等级还需要的经验。</summary>
        /// <param name="definition">圣遗物 Definition。</param>
        /// <param name="instance">目标实例。</param>
        /// <returns>还需经验；配置无效时返回零。</returns>
        private static long GetArtifactRequiredExperienceToCap(ArtifactDefinition definition, ArtifactInstance instance)
        {
            if (definition == null || definition.GrowthProfile == null ||
                definition.GrowthProfile.BakedProgressions == null ||
                instance.Level < 0 || instance.Level > definition.MaxLevel ||
                instance.Level >= definition.GrowthProfile.BakedProgressions.Count ||
                definition.MaxLevel >= definition.GrowthProfile.BakedProgressions.Count) return 0L;
            BakedArtifactLevelProgression current = definition.GrowthProfile.BakedProgressions[instance.Level];
            BakedArtifactLevelProgression cap = definition.GrowthProfile.BakedProgressions[definition.MaxLevel];
            return current == null || cap == null
                ? 0L
                : Math.Max(0L, (long)cap.CumulativeExperience - current.CumulativeExperience - instance.CurrentExperience);
        }

        /// <summary>获取圣遗物部位的中文展示名称。</summary>
        /// <param name="slot">圣遗物部位。</param>
        /// <returns>部位名称。</returns>
        private static string GetArtifactSlotLabel(ArtifactSlot slot) => slot switch
        {
            ArtifactSlot.FlowerOfLife => "生之花",
            ArtifactSlot.PlumeOfDeath => "死之羽",
            ArtifactSlot.SandsOfEon => "时之沙",
            ArtifactSlot.GobletOfEonothem => "空之杯",
            ArtifactSlot.CircletOfLogos => "理之冠",
            _ => "未知部位"
        };

        /// <summary>根据当前页生成升级、突破或精炼页面的专用展示数据。</summary>
        /// <param name="definition">武器定义。</param>
        /// <param name="instance">武器实例。</param>
        /// <param name="detailsLines">当前属性文本。</param>
        /// <returns>页面数据。</returns>
        private EquipmentDevelopmentViewData BuildPageData(WeaponDefinition definition, WeaponInstance instance,
            IReadOnlyList<string> detailsLines)
        {
            switch (stateModel.CurrentPage)
            {
                case EquipmentDevelopmentPage.Growth:
                    return BuildGrowthData(definition, instance, detailsLines);
                case EquipmentDevelopmentPage.Refinement:
                    return BuildRefinementData(definition, instance);
                default:
                    throw new ArgumentOutOfRangeException(nameof(stateModel.CurrentPage),
                        stateModel.CurrentPage, "未知的武器培养页面。");
            }
        }

        /// <summary>根据当前等级构建升级、突破、满级或配置不可用状态的数据。</summary>
        private EquipmentDevelopmentViewData BuildGrowthData(WeaponDefinition definition, WeaponInstance instance,
            IReadOnlyList<string> detailsLines)
        {
            EquipmentGrowthMode mode = ResolveGrowthMode(definition, instance, out int currentCap,
                out WeaponAscensionStage nextStage);
            currentGrowthMode = mode;
            switch (mode)
            {
                case EquipmentGrowthMode.Enhancement:
                    return BuildEnhancementData(definition, instance, detailsLines, currentCap);
                case EquipmentGrowthMode.Ascension:
                    return BuildAscensionData(definition, instance, detailsLines, currentCap, nextStage);
                case EquipmentGrowthMode.MaxLevel:
                    return BuildMaxLevelData(instance, detailsLines);
                default:
                    return BuildUnavailableGrowthData(instance, detailsLines, currentCap);
            }
        }

        /// <summary>构建可使用经验素材升级时的等级、经验和素材预览数据。</summary>
        private EquipmentDevelopmentViewData BuildEnhancementData(WeaponDefinition definition, WeaponInstance instance,
            IReadOnlyList<string> detailsLines, int currentCap)
        {
            long selectedExperience = GetSelectedEnhancementExperience();
            BuildProjectedProgress(definition.GrowthProfile, instance, currentCap, selectedExperience,
                out int projectedLevel, out int projectedExperience, out int projectedNextExperience,
                out long projectedCost);
            IReadOnlyList<string> previewLines = BuildComparisonLines(detailsLines,
                BuildDetailsLines(WeaponDetailsQuery.CreateProjected(definition, instance, projectedLevel,
                    instance.AscensionRank, instance.RefinementRank)));
            IReadOnlyList<BagItemViewData> selectedMaterials = BuildSelectedEnhancementEntries();
            bool hasCandidates = HasEnhancementCandidates();
            bool hasSelectedMaterials = HasSelectedEnhancementMaterials();
            long currencyOwned = CurrencyManager.Instance.GetBalance(CurrencyId.Mola);
            float progress = projectedNextExperience <= 0
                ? (projectedLevel >= currentCap ? 1f : 0f)
                : Mathf.Clamp01(projectedExperience / (float)projectedNextExperience);
            bool canEnhance = selectedExperience > 0L &&
                              (projectedLevel != instance.Level || projectedExperience != instance.CurrentExperience) &&
                              projectedCost <= currencyOwned;
            var enhancement = new EquipmentEnhancementViewData(
                "武器升级", "使用武器经验素材提升等级", instance.Level, projectedLevel,
                selectedExperience, projectedExperience, projectedNextExperience, progress,
                previewLines, selectedMaterials, currencyOwned, projectedCost, canEnhance, "升级",
                hasCandidates || hasSelectedMaterials, hasCandidates);
            return new EquipmentDevelopmentViewData(EquipmentDevelopmentPage.Growth, "升级",
                EquipmentGrowthMode.Enhancement, enhancement, null, null);
        }

        /// <summary>构建达到阶段上限后显示的突破星级、等级上限和材料数据。</summary>
        private EquipmentDevelopmentViewData BuildAscensionData(WeaponDefinition definition, WeaponInstance instance,
            IReadOnlyList<string> detailsLines, int currentCap, WeaponAscensionStage nextStage)
        {
            IReadOnlyList<string> lines = detailsLines;
            IReadOnlyList<BagItemViewData> requiredMaterials = BuildRequiredAscensionEntries(nextStage.Cost);
            long currencyOwned = CurrencyManager.Instance.GetBalance(CurrencyId.Mola);
            long currencyCost = GetCurrencyCost(nextStage.Cost, CurrencyId.Mola);
            bool canAscend = instance.Level >= nextStage.RequiredLevel &&
                             CanAffordGrowthCost(nextStage.Cost, DevelopmentItemType.WeaponAscension);
            var ascension = new WeaponAscensionViewData(EquipmentGrowthMode.Ascension,
                "达到当前等级上限后解锁下一阶段", instance.AscensionRank, instance.AscensionRank + 1,
                instance.Level, currentCap, nextStage.MaxLevelAfter, true, lines, requiredMaterials,
                currencyOwned, currencyCost, canAscend, "突破");
            return new EquipmentDevelopmentViewData(EquipmentDevelopmentPage.Growth, "突破",
                EquipmentGrowthMode.Ascension, null, ascension, null);
        }

        /// <summary>构建已经达到 Definition 全局等级上限时的突破布局终态。</summary>
        private static EquipmentDevelopmentViewData BuildMaxLevelData(WeaponInstance instance,
            IReadOnlyList<string> detailsLines)
        {
            var ascension = new WeaponAscensionViewData(EquipmentGrowthMode.MaxLevel,
                "当前 Definition 没有更高等级", instance.AscensionRank, 0, instance.Level, instance.Level,
                instance.Level, false, detailsLines, Array.Empty<BagItemViewData>(), 0L, 0L, false, "已满级");
            return new EquipmentDevelopmentViewData(EquipmentDevelopmentPage.Growth, "已满级",
                EquipmentGrowthMode.MaxLevel, null, ascension, null);
        }

        /// <summary>构建突破配置不完整时的明确错误展示。</summary>
        private static EquipmentDevelopmentViewData BuildUnavailableGrowthData(WeaponInstance instance,
            IReadOnlyList<string> detailsLines, int currentCap)
        {
            var ascension = new WeaponAscensionViewData(EquipmentGrowthMode.ConfigurationUnavailable,
                "请补齐突破阶段配置", instance.AscensionRank, 0, instance.Level, currentCap, currentCap,
                false, detailsLines, Array.Empty<BagItemViewData>(), 0L, 0L, false,
                    "配置不可用");
            return new EquipmentDevelopmentViewData(EquipmentDevelopmentPage.Growth, "培养",
                EquipmentGrowthMode.ConfigurationUnavailable, null, ascension, null);
        }

        /// <summary>构建精炼页的阶数、效果、已选武器和费用数据。</summary>
        private EquipmentDevelopmentViewData BuildRefinementData(WeaponDefinition definition, WeaponInstance instance)
        {
            EquipmentGrowthMode growthMode = ResolveGrowthMode(definition, instance, out _, out _);
            currentGrowthMode = growthMode;
            string growthTabLabel = GetGrowthTabLabel(growthMode);
            WeaponRefinementStage nextStage = FindNextRefinementStage(definition, instance.RefinementRank);
            if (nextStage == null || nextStage.RequiredDuplicateCount <= 0)
            {
                return new EquipmentDevelopmentViewData(EquipmentDevelopmentPage.Refinement, growthTabLabel,
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
            bool canRefine = stateModel.SelectedMaterialIds.Count == nextStage.RequiredDuplicateCount &&
                             currencyCost <= currencyOwned;
            return new EquipmentDevelopmentViewData(EquipmentDevelopmentPage.Refinement, growthTabLabel,
                growthMode, null, null,
                new WeaponRefinementViewData("武器精炼", "精炼效果与材料预览",
                    instance.RefinementRank, nextStage.Rank, true,
                    BuildComparisonLines(currentEffects, projectedEffects), selectedMaterials,
                    stateModel.SelectedMaterialIds.Count, nextStage.RequiredDuplicateCount,
                    currencyOwned, currencyCost, canRefine, "精炼", true));
        }

        /// <summary>解析成长入口当前应该显示的升级、突破或终态。</summary>
        /// <param name="definition">武器定义。</param>
        /// <param name="instance">武器实例。</param>
        /// <param name="currentCap">推导出的当前等级上限。</param>
        /// <param name="nextStage">下一突破阶段。</param>
        /// <returns>成长展示模式。</returns>
        private static EquipmentGrowthMode ResolveGrowthMode(WeaponDefinition definition, WeaponInstance instance,
            out int currentCap, out WeaponAscensionStage nextStage)
        {
            currentCap = 0;
            nextStage = FindNextAscensionStage(definition, instance.AscensionRank);
            if (nextStage != null && nextStage.RequiredLevel > 0 && nextStage.MaxLevelAfter > 0)
            {
                currentCap = nextStage.RequiredLevel;
                return instance.Level >= currentCap ? EquipmentGrowthMode.Ascension : EquipmentGrowthMode.Enhancement;
            }

            int completedIndex = instance.AscensionRank - 1;
            if (completedIndex >= 0 && completedIndex < definition.AscensionStages.Count)
                currentCap = definition.AscensionStages[completedIndex].MaxLevelAfter;

            if (instance.Level >= definition.MaxLevel && instance.AscensionRank >= definition.MaxAscensionRank)
                return EquipmentGrowthMode.MaxLevel;
            return currentCap > instance.Level ? EquipmentGrowthMode.Enhancement : EquipmentGrowthMode.ConfigurationUnavailable;
        }

        /// <summary>将成长模式转换为左侧成长 Tab 的稳定文案。</summary>
        /// <param name="growthMode">目标武器当前成长模式。</param>
        /// <returns>成长入口文案。</returns>
        private static string GetGrowthTabLabel(EquipmentGrowthMode growthMode)
        {
            switch (growthMode)
            {
                case EquipmentGrowthMode.Enhancement:
                    return "升级";
                case EquipmentGrowthMode.Ascension:
                    return "突破";
                case EquipmentGrowthMode.MaxLevel:
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
                lines.Add(current == preview ? current : BuildComparisonLine(current, preview));
            }

            return lines;
        }

        /// <summary>构建属性前后对比行，并仅将提升后的数值标记为金色。</summary>
        /// <param name="current">当前属性文本。</param>
        /// <param name="preview">预计属性文本。</param>
        /// <returns>带富文本颜色标记的属性对比行。</returns>
        private static string BuildComparisonLine(string current, string preview)
        {
            int currentSeparator = FindAttributeSeparator(current);
            int previewSeparator = FindAttributeSeparator(preview);
            if (currentSeparator >= 0 && previewSeparator >= 0)
            {
                string currentLabel = current.Substring(0, currentSeparator).Trim();
                string previewLabel = preview.Substring(0, previewSeparator).Trim();
                if (string.Equals(currentLabel, previewLabel, StringComparison.Ordinal))
                {
                    string currentValue = current.Substring(currentSeparator + 1).Trim();
                    string previewValue = preview.Substring(previewSeparator + 1).Trim();
                    return $"{currentLabel}: {currentValue} → <color=#FBB000>{previewValue}</color>";
                }
            }

            return $"{current} → <color=#FBB000>{preview}</color>";
        }

        /// <summary>查找属性名称与数值之间的中英文冒号分隔符。</summary>
        /// <param name="value">待解析的属性文本。</param>
        /// <returns>分隔符索引；不存在时返回负数。</returns>
        private static int FindAttributeSeparator(string value)
        {
            if (string.IsNullOrEmpty(value)) return -1;
            int asciiSeparator = value.IndexOf(':');
            int fullWidthSeparator = value.IndexOf('：');
            if (asciiSeparator < 0) return fullWidthSeparator;
            if (fullWidthSeparator < 0) return asciiSeparator;
            return Math.Min(asciiSeparator, fullWidthSeparator);
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
                stackableInventoryManager.GetDevelopmentExperienceItems(DevelopmentExperienceItemType.Weapon);
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
            IReadOnlyList<WeaponInstance> instances = weaponInventoryManager.GetInstances();
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
            int ownedQuantity = stackableInventoryManager.GetQuantity(itemCost.ItemId);
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
            if (!TryGetTarget(out WeaponDefinition definition, out WeaponInstance instance)) return 0L;
            return BuildSelectedConsumptionPlan(DevelopmentExperienceItemType.Weapon,
                GetRequiredExperienceToCap(definition, instance, out _)).ConsumedExperience;
        }

        /// <summary>判断当前背包是否存在可选择的武器强化素材。</summary>
        /// <returns>存在有效候选时返回 true。</returns>
        private bool HasEnhancementCandidates()
        {
            IReadOnlyList<StackableInventoryEntry> entries =
                stackableInventoryManager.GetDevelopmentExperienceItems(DevelopmentExperienceItemType.Weapon);
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

        /// <summary>判断当前培养会话是否已有至少一本经验素材选择。</summary>
        /// <returns>存在正数量选择时返回 true。</returns>
        private bool HasSelectedEnhancementMaterials()
        {
            return GetSelectedEnhancementMaterialCount() > 0;
        }

        /// <summary>汇总当前培养会话已选择的经验素材本数。</summary>
        /// <returns>已选素材总本数。</returns>
        private int GetSelectedEnhancementMaterialCount()
        {
            int total = 0;
            foreach (KeyValuePair<ItemId, int> pair in stateModel.SelectedEnhancementQuantities)
                total = Math.Min(MaxSelectedExperienceMaterialCount, total + Math.Max(0, pair.Value));
            return total;
        }

        /// <summary>按培养对象为当前选择构建实际消耗规划。</summary>
        /// <param name="requestedType">经验素材支持的培养对象。</param>
        /// <param name="requiredExperience">目标还需经验。</param>
        /// <returns>当前选择的有界背包规划。</returns>
        private EnhancementMaterialConsumptionPlan BuildSelectedConsumptionPlan(
            DevelopmentExperienceItemType requestedType, long requiredExperience)
        {
            var selections = new List<EnhancementMaterialSelection>(stateModel.SelectedEnhancementQuantities.Count);
            foreach (KeyValuePair<ItemId, int> pair in stateModel.SelectedEnhancementQuantities)
            {
                if (pair.Value <= 0 || !ItemManager.Instance.TryGetDefinition(pair.Key, out ItemDefinition item) ||
                    !(item is DevelopmentExperienceItemDefinition definition) ||
                    !definition.SupportsExperienceType(requestedType)) continue;
                selections.Add(new EnhancementMaterialSelection(pair.Key, pair.Value, definition.ExperienceValue));
            }

            return EnhancementMaterialConsumptionPlanner.Build(
                requiredExperience, selections, MaxSelectedExperienceMaterialCount);
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

        /// <summary>按正式养成道具用途检查突破成本是否已满足。</summary>
        /// <param name="cost">待支付的阶段成本。</param>
        /// <param name="requestedType">突破用途。</param>
        /// <returns>材料和摩拉都足够时返回 true。</returns>
        private bool CanAffordGrowthCost(GrowthCost cost, DevelopmentItemType requestedType)
        {
            if (cost == null || cost.ItemCosts == null || cost.CurrencyCosts == null ||
                (cost.ItemCosts.Count == 0 && cost.CurrencyCosts.Count == 0)) return false;
            var quantityByItemIdMap = new Dictionary<ItemId, int>();
            for (int index = 0; index < cost.ItemCosts.Count; index++)
            {
                ItemCostEntry entry = cost.ItemCosts[index];
                if (entry == null || entry.Quantity <= 0 ||
                    !ItemManager.Instance.TryGetDefinition(entry.ItemId, out ItemDefinition item) ||
                    !(item is DevelopmentItemDefinition material) ||
                    !material.SupportsDevelopmentType(requestedType)) return false;
                quantityByItemIdMap[entry.ItemId] = quantityByItemIdMap.TryGetValue(entry.ItemId, out int current)
                    ? current + entry.Quantity
                    : entry.Quantity;
            }

            foreach (KeyValuePair<ItemId, int> pair in quantityByItemIdMap)
                if (stackableInventoryManager.GetQuantity(pair.Key) < pair.Value) return false;
            var amountByCurrencyIdMap = new Dictionary<CurrencyId, long>();
            for (int index = 0; index < cost.CurrencyCosts.Count; index++)
            {
                CurrencyCostEntry entry = cost.CurrencyCosts[index];
                if (entry == null || entry.CurrencyId == CurrencyId.None || entry.Amount <= 0) return false;
                amountByCurrencyIdMap[entry.CurrencyId] = amountByCurrencyIdMap.TryGetValue(entry.CurrencyId, out long current)
                    ? current + entry.Amount
                    : entry.Amount;
            }

            foreach (KeyValuePair<CurrencyId, long> pair in amountByCurrencyIdMap)
                if (pair.Value > CurrencyManager.Instance.GetBalance(pair.Key)) return false;
            return true;
        }

        #endregion

        #region 候选列表与事件

        /// <summary>按当前成长模式或精炼页绑定对应的候选材料列表。</summary>
        private void BindSelectionPanel()
        {
            if (selectionPanel == null) return;
            if (stateModel.CurrentPage == EquipmentDevelopmentPage.Growth && currentGrowthMode == EquipmentGrowthMode.Enhancement)
            {
                if (targetKind == EquipmentDevelopmentTargetKind.Artifact)
                    BindArtifactEnhancementSelectionPanel();
                else
                    BindEnhancementSelectionPanel();
                return;
            }

            if (stateModel.CurrentPage != EquipmentDevelopmentPage.Refinement ||
                !weaponInventoryManager.TryGetInstance(targetInstanceId, out WeaponInstance target)) return;
            var entries = new List<BagItemViewData>();
            var selectedEntryKeys = new List<BagEntryKey>();
            IReadOnlyList<WeaponInstance> instances = weaponInventoryManager.GetInstances();
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
                stackableInventoryManager.GetDevelopmentExperienceItems(DevelopmentExperienceItemType.Weapon);
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

        /// <summary>绑定圣遗物升级经验候选并投影当前会话数量。</summary>
        private void BindArtifactEnhancementSelectionPanel()
        {
            var entries = new List<BagItemViewData>();
            var selectedEntryKeys = new List<BagEntryKey>();
            IReadOnlyList<StackableInventoryEntry> inventory =
                stackableInventoryManager.GetDevelopmentExperienceItems(DevelopmentExperienceItemType.Artifact);
            for (int index = 0; index < inventory.Count; index++)
            {
                StackableInventoryEntry entry = inventory[index];
                if (entry == null || entry.Quantity <= 0 ||
                    !ItemManager.Instance.TryGetDefinition(entry.ItemId, out ItemDefinition item) ||
                    !(item is DevelopmentExperienceItemDefinition definition) ||
                    !definition.SupportsExperienceType(DevelopmentExperienceItemType.Artifact) ||
                    definition.ExperienceValue <= 0) continue;
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
            return (targetKind == EquipmentDevelopmentTargetKind.Weapon &&
                    stateModel.CurrentPage == EquipmentDevelopmentPage.Refinement) ||
                   (stateModel.CurrentPage == EquipmentDevelopmentPage.Growth &&
                    currentGrowthMode == EquipmentGrowthMode.Enhancement);
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
            if (targetKind != EquipmentDevelopmentTargetKind.Weapon) return;
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

        /// <summary>圣遗物实例变化时刷新目标详情或关闭已经删除的目标。</summary>
        /// <param name="eventArgs">圣遗物实例变化事件。</param>
        private void HandleArtifactChanged(ArtifactInstanceChangedEvent eventArgs)
        {
            if (targetKind != EquipmentDevelopmentTargetKind.Artifact || !hasTarget || eventArgs.Instance == null) return;
            if (eventArgs.Instance.InstanceId == targetInstanceId &&
                eventArgs.ChangeType == EquipmentInstanceChangeType.Removed)
            {
                CloseWindow();
                return;
            }

            if (eventArgs.Instance.InstanceId == targetInstanceId || stateModel.SelectionPanelVisible)
                Refresh();
        }

        /// <summary>圣遗物库存恢复后刷新统一培养窗口。</summary>
        /// <param name="_">恢复事件。</param>
        private void HandleArtifactRestored(ArtifactInventoryRestoredEvent _)
        {
            if (targetKind == EquipmentDevelopmentTargetKind.Artifact && windowShown) Refresh();
        }

        /// <summary>经验素材数量变化时刷新武器或圣遗物培养预览。</summary>
        /// <param name="eventArgs">可堆叠物品变化事件。</param>
        private void HandleStackableChanged(StackableItemChangedEvent eventArgs)
        {
            if (!windowShown || !hasTarget || !ItemManager.Instance.TryGetDefinition(eventArgs.ItemId,
                    out ItemDefinition item) || !(item is DevelopmentExperienceItemDefinition definition)) return;
            bool relevant = targetKind == EquipmentDevelopmentTargetKind.Weapon
                ? definition.SupportsExperienceType(DevelopmentExperienceItemType.Weapon)
                : definition.SupportsExperienceType(DevelopmentExperienceItemType.Artifact);
            if (relevant) Refresh();
        }

        /// <summary>可堆叠库存恢复后刷新当前培养页面。</summary>
        /// <param name="_">恢复事件。</param>
        private void HandleStackableRestored(StackableInventoryRestoredEvent _)
        {
            if (windowShown) Refresh();
        }

        /// <summary>摩拉余额变化时刷新当前培养窗口的余额和费用展示。</summary>
        /// <param name="eventArgs">货币余额变化事件。</param>
        private void HandleCurrencyBalanceChanged(CurrencyBalanceChangedEvent eventArgs)
        {
            if (!windowShown || eventArgs.CurrencyId != CurrencyId.Mola) return;
            Refresh();
        }

        /// <summary>货币钱包恢复后刷新当前培养窗口。</summary>
        /// <param name="_">货币钱包恢复事件。</param>
        private void HandleCurrencyWalletRestored(CurrencyWalletRestoredEvent _)
        {
            if (windowShown) Refresh();
        }

        /// <summary>刷新图集并在完成后重新绑定候选列表。</summary>
        private async UniTask PrepareAtlasAsync()
        {
            await spriteAtlasLeaseService.BeginLoadConfiguredAtlasesAsync();
            if (disposed || !windowShown) return;

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
                if (definition.RefinementStages[index].Rank == refinementRank + 1)
                    return definition.RefinementStages[index];
            return null;
        }

        /// <summary>查询当前目标 Definition 和实例。</summary>
        private bool TryGetTarget(out WeaponDefinition definition, out WeaponInstance instance)
        {
            definition = null;
            instance = null;
            return hasTarget && WeaponInventoryManager.IsConfigured && ItemManager.Instance.IsConfigured &&
                   weaponInventoryManager.TryGetInstance(targetInstanceId, out instance) &&
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
