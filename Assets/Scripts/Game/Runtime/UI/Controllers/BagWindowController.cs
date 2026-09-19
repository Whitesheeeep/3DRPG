using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using RPG.Game.UI.Bag;
using RPG.Game.UI.Escape;
using RPG.Game.UI.Services;
using RPG.Game.UI.Views.Bag;
using RPG.Game.UI.WeaponDevelopment;
using RPG.ItemSystem;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using WS_Modules.BusinessArchitecture;
using WS_Modules.CustomEventSystem;
using WS_Modules.UIModule;

namespace RPG.Game.UI.Controllers
{
    /// <summary>
    /// 背包窗口根节点控制器，负责五类数据源、分类状态、虚拟网格和详情请求。
    /// 它不轮询输入，也不直接修改武器库存。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BagWindowController : MonoBehaviour
    {
        #region 依赖字段
        private BagWindowDataComponent data;
        private BagBrowseStateModel stateModel;
        private WindowSpriteAtlasLeaseService spriteAtlasLeaseService;
        private WeaponInventoryManager weaponInventoryManager;
        private ArtifactInventoryManager artifactInventoryManager;
        private StackableInventoryManager stackableInventoryManager;
        private readonly Dictionary<ItemCategory, IBagCategoryDataSource> dataSourceByCategoryMap = new();
        // 每个分类按钮的对应回调，便于 Dispose 时注销。
        private readonly List<UnityAction> categoryButtonActions = new();
        private IUnRegister weaponChangedUnregister;
        private IUnRegister weaponRestoredUnregister;
        private IUnRegister weaponDefinitionNewChangedUnregister;
        private IUnRegister artifactChangedUnregister;
        private IUnRegister artifactRestoredUnregister;
        private IUnRegister artifactDefinitionNewChangedUnregister;
        private IUnRegister stackableChangedUnregister;
        private IUnRegister stackableRestoredUnregister;
        #endregion

        #region 状态字段
        private IReadOnlyList<BagItemViewData> currentEntries = Array.Empty<BagItemViewData>();
        private bool initialized;
        private bool disposed;
        private bool windowShown;
        private bool atlasPreparationRunning;
        private UniTaskCompletionSource atlasPreparationCompletionSource;
        #endregion

        #region 初始化与释放
        /// <summary>绑定窗口序列化组件并建立一次性的分类、按钮和库存事件索引。</summary>
        /// <param name="windowData">窗口序列化数据。</param>
        public void Initialize(BagWindowDataComponent windowData)
        {
            if (initialized)
            {
                if (!ReferenceEquals(data, windowData))
                    throw new InvalidOperationException("[BagWindowController] 不允许替换窗口数据组件。");
                return;
            }

            data = windowData ?? throw new ArgumentNullException(nameof(windowData));
            data.ValidateConfiguration();
            data.BagIcon.raycastTarget = false;
            IArchitecture architecture = GameArchitecture.Interface;
            weaponInventoryManager = architecture.GetManager<WeaponInventoryManager>();
            artifactInventoryManager = architecture.GetManager<ArtifactInventoryManager>();
            stackableInventoryManager = architecture.GetManager<StackableInventoryManager>();
            spriteAtlasLeaseService = new WindowSpriteAtlasLeaseService(
                data.DynamicAtlasAddresses,
                data.AtlasReleaseDelaySeconds);
            stateModel = new BagBrowseStateModel();
            stateModel.CategoryChanged += HandleCategoryChanged;
            stateModel.SortChanged += HandleSortChanged;
            stateModel.SelectionChanged += HandleSelectionChanged;
            BuildDataSources();
            RegisterButtons();
            spriteAtlasLeaseService.Released += HandleAtlasReleased;
            weaponChangedUnregister = EventSystem.Register_Type<WeaponInstanceChangedEvent>(
                typeof(WeaponInstanceChangedEvent), HandleWeaponChanged);
            weaponRestoredUnregister = EventSystem.Register_Type<WeaponInventoryRestoredEvent>(
                typeof(WeaponInventoryRestoredEvent), HandleWeaponRestored);
            weaponDefinitionNewChangedUnregister = EventSystem.Register_Type<WeaponDefinitionNewStateChangedEvent>(
                typeof(WeaponDefinitionNewStateChangedEvent), HandleWeaponDefinitionNewStateChanged);
            artifactChangedUnregister = EventSystem.Register_Type<ArtifactInstanceChangedEvent>(
                typeof(ArtifactInstanceChangedEvent), HandleArtifactChanged);
            artifactRestoredUnregister = EventSystem.Register_Type<ArtifactInventoryRestoredEvent>(
                typeof(ArtifactInventoryRestoredEvent), HandleArtifactRestored);
            artifactDefinitionNewChangedUnregister = EventSystem.Register_Type<ArtifactDefinitionNewStateChangedEvent>(
                typeof(ArtifactDefinitionNewStateChangedEvent), HandleArtifactDefinitionNewStateChanged);
            stackableChangedUnregister = EventSystem.Register_Type<StackableItemChangedEvent>(
                typeof(StackableItemChangedEvent), HandleStackableChanged);
            stackableRestoredUnregister = EventSystem.Register_Type<StackableInventoryRestoredEvent>(
                typeof(StackableInventoryRestoredEvent), HandleStackableRestored);
            initialized = true;
        }

        /// <summary>窗口销毁时幂等注销事件、回收格子并立即释放动态图集。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (stateModel != null)
            {
                stateModel.CategoryChanged -= HandleCategoryChanged;
                stateModel.SortChanged -= HandleSortChanged;
                stateModel.SelectionChanged -= HandleSelectionChanged;
            }

            if (spriteAtlasLeaseService != null)
                spriteAtlasLeaseService.Released -= HandleAtlasReleased;
            weaponChangedUnregister?.UnRegister();
            weaponRestoredUnregister?.UnRegister();
            weaponDefinitionNewChangedUnregister?.UnRegister();
            artifactChangedUnregister?.UnRegister();
            artifactRestoredUnregister?.UnRegister();
            artifactDefinitionNewChangedUnregister?.UnRegister();
            stackableChangedUnregister?.UnRegister();
            stackableRestoredUnregister?.UnRegister();
            for (int index = 0; data != null && index < categoryButtonActions.Count; index++)
            {
                if (index < data.CategoryButtons.Count && data.CategoryButtons[index] != null)
                    data.CategoryButtons[index].onClick.RemoveListener(categoryButtonActions[index]);
            }

            data?.PreviousCategoryButton?.onClick.RemoveListener(SelectPreviousCategory);
            data?.NextCategoryButton?.onClick.RemoveListener(SelectNextCategory);
            data?.CloseButton?.onClick.RemoveListener(SubmitCloseRequest);
            data?.SortDirectionButton?.onClick.RemoveListener(ToggleSortDirection);
            data?.DeleteButton?.onClick.RemoveListener(SubmitDeleteRequest);
            data?.DetailsButton?.onClick.RemoveListener(SubmitDetailsRequest);
            data?.SortDropdown?.onValueChanged.RemoveListener(HandleSortDropdownChanged);
            categoryButtonActions.Clear();
            weaponInventoryManager = null;
            artifactInventoryManager = null;
            stackableInventoryManager = null;
            data?.GridView?.SetInteractable(false);
            data?.GridView?.Bind(Array.Empty<BagItemViewData>(), null);
            spriteAtlasLeaseService?.Dispose();
            spriteAtlasLeaseService = null;
            atlasPreparationCompletionSource?.TrySetCanceled();
            atlasPreparationCompletionSource = null;
        }
        #endregion

        #region 窗口生命周期
        /// <summary>窗口显示时取消释放倒计时、立即刷新动态区域并准备动态图集。</summary>
        public void OnWindowShown()
        {
            if (disposed) return;
            windowShown = true;
            spriteAtlasLeaseService.CancelRelease();
            // 这里再次启动准备是避免不走 OnWindowShown 的打开请求直接显示窗口，导致首次绑定时 Atlas 尚未完成。
            PrepareOpen();
            // 隐藏窗口时会关闭格子交互；重新显示必须在首次绑定前恢复按钮状态。
            data.GridView.SetInteractable(true);
            // Atlas 尚未完成时直接绑定 null Sprite；资源完成后再刷新当前分类。
            RefreshCurrentCategory(false);
        }

        /// <summary>窗口稳定隐藏时停止交互并启动 30 秒动态图集缓存释放倒计时。</summary>
        public void OnWindowHidden()
        {
            if (disposed) return;
            windowShown = false;
            data.GridView.SetInteractable(false);
            spriteAtlasLeaseService.ScheduleRelease();
        }

        /// <summary>
        /// 在窗口显示前启动动态图集准备；该方法不改变窗口可见性，也不等待资源完成。
        /// </summary>
        public void PrepareOpen()
        {
            PrepareOpenAsync().Forget(HandleAsyncException);
        }

        /// <summary>启动或复用动态图集准备任务，供需要显式等待资源尝试完成的调用方使用。</summary>
        /// <returns>当前动态图集准备任务。</returns>
        public UniTask PrepareOpenAsync()
        {
            if (disposed) return UniTask.CompletedTask;
            if (atlasPreparationRunning) return atlasPreparationCompletionSource.Task;
            atlasPreparationRunning = true;
            UniTaskCompletionSource completionSource = new UniTaskCompletionSource();
            atlasPreparationCompletionSource = completionSource;
            PrepareAtlasesAsync(completionSource).Forget();
            return completionSource.Task;
        }
        #endregion

        #region 分类与排序

        /// <summary>建立五个分类的真实数据源，并让所有分类共享同一网格和详情 View。</summary>
        private void BuildDataSources()
        {
            dataSourceByCategoryMap.Clear();
            dataSourceByCategoryMap.Add(ItemCategory.Weapon,
                new WeaponBagCategoryDataSource(weaponInventoryManager, ResolveSprite));
            dataSourceByCategoryMap.Add(ItemCategory.Artifact,
                new ArtifactBagCategoryDataSource(artifactInventoryManager, ResolveSprite));
            dataSourceByCategoryMap.Add(ItemCategory.DevelopmentExperienceItem,
                new StackableBagCategoryDataSource(
                    ItemCategory.DevelopmentExperienceItem,
                    stackableInventoryManager,
                    ResolveSprite));
            dataSourceByCategoryMap.Add(ItemCategory.Food,
                new StackableBagCategoryDataSource(
                    ItemCategory.Food,
                    stackableInventoryManager,
                    ResolveSprite));
            dataSourceByCategoryMap.Add(ItemCategory.DevelopmentItem,
                new StackableBagCategoryDataSource(
                    ItemCategory.DevelopmentItem,
                    stackableInventoryManager,
                    ResolveSprite));
            Debug.Log("[BagWindowController] 已注册武器、圣遗物、养成经验道具、食物和养成道具五个分类数据源。", this);
        }

        /// <summary>注册分类、箭头、排序、详情、删除和关闭按钮的纯请求回调。</summary>
        private void RegisterButtons()
        {
            IReadOnlyList<UnityEngine.UI.Button> buttons = data.CategoryButtons;
            for (int index = 0; index < buttons.Count; index++)
            {
                int capturedIndex = index;
                UnityAction action = () => SelectCategoryAt(capturedIndex);
                categoryButtonActions.Add(action);
                buttons[index]?.onClick.AddListener(action);
            }

            data.PreviousCategoryButton?.onClick.AddListener(SelectPreviousCategory);
            data.NextCategoryButton?.onClick.AddListener(SelectNextCategory);
            data.CloseButton?.onClick.AddListener(SubmitCloseRequest);
            data.SortDirectionButton?.onClick.AddListener(ToggleSortDirection);
            data.DeleteButton?.onClick.AddListener(SubmitDeleteRequest);
            data.DetailsButton?.onClick.AddListener(SubmitDetailsRequest);
            if (data.SortDropdown != null)
            {
                data.SortDropdown.ClearOptions();
                RefreshSortOptions();
                data.SortDropdown.onValueChanged.AddListener(HandleSortDropdownChanged);
            }
        }

        /// <summary>根据当前分类刷新排序下拉框文案并保留当前选项下标。</summary>
        private void RefreshSortOptions()
        {
            if (data.SortDropdown == null || !dataSourceByCategoryMap.TryGetValue(
                    stateModel.CurrentCategory, out IBagCategoryDataSource source)) return;
            int selectedValue = Mathf.Clamp((int)stateModel.SortMode, 0, 2);
            data.SortDropdown.ClearOptions();
            data.SortDropdown.AddOptions(new List<string> { "品质", source.PrimarySortLabel, "获得顺序" });
            data.SortDropdown.SetValueWithoutNotify(selectedValue);
        }

        /// <summary>切换到序列化分类顺序中的指定下标。</summary>
        private void SelectCategoryAt(int index)
        {
            if (index < 0 || index >= data.CategoryOrder.Count) return;
            stateModel.SetCategory(data.CategoryOrder[index]);
        }

        /// <summary>切换到当前分类前一个分类。</summary>
        private void SelectPreviousCategory() => SelectCategoryOffset(-1);

        /// <summary>切换到当前分类后一个分类。</summary>
        private void SelectNextCategory() => SelectCategoryOffset(1);

        /// <summary>按作者配置的分类顺序循环移动；越过首尾时回到另一端。</summary>
        private void SelectCategoryOffset(int offset)
        {
            int index = IndexOfCategory(stateModel.CurrentCategory);
            int categoryCount = data.CategoryOrder.Count;
            if (index < 0 || categoryCount <= 1) return;

            // 通过归一化取模处理负数偏移，保证上一页从第一个分类回到最后一个分类。
            int nextIndex = (index + offset) % categoryCount;
            if (nextIndex < 0) nextIndex += categoryCount;
            Debug.Log($"[BagWindowController] 循环切换背包分类，from={stateModel.CurrentCategory}，to={data.CategoryOrder[nextIndex]}。", this);
            SelectCategoryAt(nextIndex);
        }

        /// <summary>处理排序下拉框改变并保留稳定条目选择。</summary>
        private void HandleSortDropdownChanged(int value)
        {
            stateModel.SetSortMode((BagSortMode)Mathf.Clamp(value, 0, 2));
        }

        /// <summary>切换独立升降序按钮。</summary>
        private void ToggleSortDirection() => stateModel.ToggleSortDirection();

        /// <summary>刷新分类列表、滚动位置和箭头状态。</summary>
        private void RefreshCurrentCategory(bool resetScroll)
        {
            if (disposed || !dataSourceByCategoryMap.TryGetValue(stateModel.CurrentCategory,
                    out IBagCategoryDataSource source))
                return;
            currentEntries = source.BuildEntries(stateModel.SortMode, stateModel.SortDirection);
            RefreshCapacityDisplay();
            if (resetScroll) data.GridView?.ScrollToTop();
            data.GridView?.Bind(currentEntries, HandleEntrySelected);
            if (currentEntries.Count == 0)
            {
                stateModel.SetSelection(null);
                // 空分类不会触发 SelectionChanged（本来就没有选择）时，仍要主动清除上一分类的详情。
                HandleSelectionChanged(null);
                UpdateArrowState();
                return;
            }

            BagEntryKey? selected = stateModel.SelectedEntryKey;
            bool selectionStillExists = selected.HasValue && ContainsEntry(selected.Value);
            BagEntryKey? nextSelection = selectionStillExists ? selected : currentEntries[0].EntryKey;
            bool selectionChanged = !Nullable.Equals(selected, nextSelection);
            stateModel.SetSelection(nextSelection);
            // 数据事件可能只更新当前实例的等级、精炼或锁定状态，稳定键不变时不会触发 SelectionChanged；
            // 此处主动重建当前详情，保证列表快照和详情快照来自同一次库存读取。
            if (!selectionChanged) HandleSelectionChanged(nextSelection);
            UpdateArrowState();
        }

        /// <summary>分类变化时回到顶部并选择新分类排序后的第一项。</summary>
        /// <param name="category">新的分类。</param>
        private void HandleCategoryChanged(ItemCategory category)
        {
            RefreshSortOptions();
            RefreshCurrentCategory(true);
        }

        /// <summary>排序字段或方向变化时重建列表但保留仍存在的稳定选择。</summary>
        private void HandleSortChanged()
        {
            RefreshCurrentCategory(false);
        }

        /// <summary>判断当前稳定选择是否仍存在于新排序结果。</summary>
        private bool ContainsEntry(BagEntryKey key)
        {
            for (int index = 0; index < currentEntries.Count; index++)
                if (currentEntries[index].EntryKey == key)
                    return true;
            return false;
        }

        /// <summary>刷新左右分类箭头的状态；存在多个分类时两侧都允许循环翻页。</summary>
        private void UpdateArrowState()
        {
            int index = IndexOfCategory(stateModel.CurrentCategory);
            bool canCycle = index >= 0 && data.CategoryOrder.Count > 1;
            if (data.PreviousCategoryButton != null) data.PreviousCategoryButton.interactable = canCycle;
            if (data.NextCategoryButton != null) data.NextCategoryButton.interactable = canCycle;
        }

        /// <summary>刷新武器和圣遗物的容纳区容量；可堆叠分类没有统一容量时隐藏文本。</summary>
        private void RefreshCapacityDisplay()
        {
            TMP_Text upperLimitCountText = data.UpperLimitCountText;
            if (upperLimitCountText == null) return;

            switch (stateModel.CurrentCategory)
            {
                case ItemCategory.Weapon:
                    upperLimitCountText.gameObject.SetActive(true);
                    upperLimitCountText.text = $"上限：{weaponInventoryManager.StoredCount:N0} / {weaponInventoryManager.Capacity:N0}";
                    break;
                case ItemCategory.Artifact:
                    upperLimitCountText.gameObject.SetActive(true);
                    upperLimitCountText.text = $"上限：{artifactInventoryManager.Count:N0} / {artifactInventoryManager.Capacity:N0}";
                    break;
                default:
                    upperLimitCountText.gameObject.SetActive(false);
                    break;
            }
        }

        /// <summary>查找当前分类在作者顺序中的位置。</summary>
        private int IndexOfCategory(ItemCategory category)
        {
            for (int index = 0; index < data.CategoryOrder.Count; index++)
                if (data.CategoryOrder[index] == category)
                    return index;
            return -1;
        }
        #endregion

        #region 选择与业务请求
        /// <summary>选择网格条目并确认新获得状态。</summary>
        private void HandleEntrySelected(BagEntryKey entryKey)
        {
            stateModel.SetSelection(entryKey);
            if (entryKey.Category == ItemCategory.Weapon &&
                TryParseInstanceId(entryKey.Value, out EquipmentInstanceId instanceId))
                weaponInventoryManager.AcknowledgeNew(instanceId);
            else if (entryKey.Category == ItemCategory.Artifact &&
                     TryParseInstanceId(entryKey.Value, out EquipmentInstanceId artifactInstanceId))
                artifactInventoryManager.AcknowledgeNew(artifactInstanceId);
            else if (entryKey.Category != ItemCategory.Artifact)
                AcknowledgeStackable(entryKey);
        }

        /// <summary>提交 BagWindow 关闭 Command，由统一 Esc 栈和按钮共用。</summary>
        private void SubmitCloseRequest()
        {
            GameArchitecture.Interface.SendCommand(new CloseBagWindowCommand());
        }

        /// <summary>提交删除请求，不在背包 View 中直接移除实例。</summary>
        private void SubmitDeleteRequest()
        {
            if (!stateModel.SelectedEntryKey.HasValue || stateModel.CurrentCategory != ItemCategory.Weapon ||
                !TryParseInstanceId(stateModel.SelectedEntryKey.Value.Value,
                    out EquipmentInstanceId instanceId)) return;
            EventSystem.EventTrigger_Type(typeof(BagWeaponDeleteRequestedEventArgs),
                new BagWeaponDeleteRequestedEventArgs(instanceId));
        }

        /// <summary>提交详情请求，不在背包 View 中直接打开其他窗口。</summary>
        private void SubmitDetailsRequest()
        {
            if (!stateModel.SelectedEntryKey.HasValue ||
                (stateModel.CurrentCategory != ItemCategory.Weapon && stateModel.CurrentCategory != ItemCategory.Artifact) ||
                !TryParseInstanceId(stateModel.SelectedEntryKey.Value.Value, out EquipmentInstanceId instanceId)) return;

            EquipmentDevelopmentOpenContext context = stateModel.CurrentCategory == ItemCategory.Weapon
                ? EquipmentDevelopmentOpenContext.ForWeapon(instanceId)
                : EquipmentDevelopmentOpenContext.ForArtifact(instanceId);
            OpenEquipmentDevelopmentAsync(context).Forget(HandleAsyncException);
        }

        /// <summary>直接打开统一装备培养窗口并传入当前实例上下文。</summary>
        /// <param name="context">目标装备实例上下文。</param>
        private async UniTask OpenEquipmentDevelopmentAsync(EquipmentDevelopmentOpenContext context)
        {
            if (!UIManager.Instance.IsInitialized)
                throw new InvalidOperationException("UIManager 尚未初始化，无法打开装备培养窗口。");
            EquipmentDevelopmentWindow window = await UIManager.Instance.PopUpWindowAsync<EquipmentDevelopmentWindow,
                EquipmentDevelopmentOpenContext>(context);
            if (window == null || !window.Visible)
                throw new InvalidOperationException("装备培养窗口打开请求未返回可见窗口。");
        }

        /// <summary>处理选择变化并刷新右侧所有分类共用详情 View。</summary>
        private void HandleSelectionChanged(BagEntryKey? key)
        {
            // 状态模型是选择的唯一来源；每次详情刷新同时把选择投影到当前可见格子。
            data.GridView?.SetSelection(key);
            if (!key.HasValue || !dataSourceByCategoryMap.TryGetValue(key.Value.Category,
                    out IBagCategoryDataSource source) ||
                !source.TryBuildDetails(key.Value, out BagDetailViewData details))
            {
                data.DetailView?.Clear();
                if (data.DeleteButton != null) data.DeleteButton.interactable = false;
                if (data.DetailsButton != null) data.DetailsButton.interactable = false;
                return;
            }

            data.DetailView?.Bind(details);
            if (data.DeleteButton != null) data.DeleteButton.interactable = details.ShowDeleteAction;
            if (data.DetailsButton != null) data.DetailsButton.interactable = details.ShowDetailsAction;
        }
        #endregion

        #region 资源与动态刷新
        /// <summary>动态图集真正释放后清空动态网格和详情，避免 Image 继续持有失效 Sprite。</summary>
        private void HandleAtlasReleased()
        {
            if (disposed) return;
            currentEntries = Array.Empty<BagItemViewData>();
            data.GridView?.Bind(Array.Empty<BagItemViewData>(), null);
            data.DetailView?.Clear();
        }

        /// <summary>启动动态图集并在完成后刷新当前可见分类的 Sprite 引用。</summary>
        private async UniTask PrepareAtlasesAsync(UniTaskCompletionSource completionSource)
        {
            try
            {
                bool success = await spriteAtlasLeaseService.BeginLoadConfiguredAtlasesAsync();
                if (windowShown) RefreshCurrentCategory(false);
                if (!success)
                    Debug.LogWarning("[BagWindow][Atlas] 部分动态图集失败，对应 Sprite 保持为 null，失败地址将在下次打开时重试。", this);
                completionSource.TrySetResult();
            }
            catch (Exception exception)
            {
                completionSource.TrySetException(exception);
                throw;
            }
            finally
            {
                atlasPreparationRunning = false;
                if (ReferenceEquals(atlasPreparationCompletionSource, completionSource))
                    atlasPreparationCompletionSource = null;
            }
        }

        /// <summary>记录 Bag 图集准备中的非取消异常，避免非阻塞调用产生未观察任务异常。</summary>
        /// <param name="exception">图集准备异常。</param>
        private void HandleAsyncException(Exception exception)
        {
            if (!(exception is OperationCanceledException))
                Debug.LogException(exception, this);
        }

        /// <summary>按当前租约解析动态图集 Sprite，未加载时返回 null。</summary>
        private Sprite ResolveSprite(string address, string spriteName)
        {
            return spriteAtlasLeaseService.TryGetSprite(address, spriteName, out Sprite sprite)
                ? sprite
                : null;
        }
        #endregion

        #region 库存事件
        /// <summary>武器实例变化后按稳定选择刷新当前列表。</summary>
        private void HandleWeaponChanged(WeaponInstanceChangedEvent _)
        {
            if (!disposed && stateModel.CurrentCategory == ItemCategory.Weapon) RefreshCurrentCategory(false);
        }

        /// <summary>库存恢复后重新构建当前分类。</summary>
        private void HandleWeaponRestored(WeaponInventoryRestoredEvent _)
        {
            if (!disposed && stateModel.CurrentCategory == ItemCategory.Weapon) RefreshCurrentCategory(false);
        }

        /// <summary>武器 Definition New 状态变化后刷新当前武器列表。</summary>
        /// <param name="_">Definition New 状态变化事件。</param>
        private void HandleWeaponDefinitionNewStateChanged(WeaponDefinitionNewStateChangedEvent _)
        {
            if (!disposed && stateModel.CurrentCategory == ItemCategory.Weapon) RefreshCurrentCategory(false);
        }

        /// <summary>圣遗物实例变化后刷新当前圣遗物分类。</summary>
        /// <param name="change">圣遗物变化事件。</param>
        private void HandleArtifactChanged(ArtifactInstanceChangedEvent change)
        {
            if (!disposed && stateModel.CurrentCategory == ItemCategory.Artifact) RefreshCurrentCategory(false);
        }

        /// <summary>圣遗物存档恢复后刷新当前分类。</summary>
        /// <param name="_">圣遗物恢复事件。</param>
        private void HandleArtifactRestored(ArtifactInventoryRestoredEvent _)
        {
            if (!disposed && stateModel.CurrentCategory == ItemCategory.Artifact) RefreshCurrentCategory(false);
        }

        /// <summary>圣遗物 New 状态变化后刷新当前分类。</summary>
        /// <param name="_">圣遗物 New 变化事件。</param>
        private void HandleArtifactDefinitionNewStateChanged(ArtifactDefinitionNewStateChangedEvent _)
        {
            if (!disposed && stateModel.CurrentCategory == ItemCategory.Artifact) RefreshCurrentCategory(false);
        }

        /// <summary>可堆叠数量或 New 状态变化后按分类刷新。</summary>
        /// <param name="change">可堆叠变化事件。</param>
        private void HandleStackableChanged(StackableItemChangedEvent change)
        {
            if (disposed || !dataSourceByCategoryMap.TryGetValue(stateModel.CurrentCategory,
                    out IBagCategoryDataSource source) || source.Category == ItemCategory.Weapon || source.Category == ItemCategory.Artifact)
                return;
            if (ItemManager.Instance.TryGetDefinition(change.ItemId, out ItemDefinition definition) &&
                definition.Category == stateModel.CurrentCategory)
                RefreshCurrentCategory(false);
        }

        /// <summary>可堆叠库存恢复后刷新当前分类。</summary>
        /// <param name="_">可堆叠恢复事件。</param>
        private void HandleStackableRestored(StackableInventoryRestoredEvent _)
        {
            if (!disposed && stateModel.CurrentCategory != ItemCategory.Weapon &&
                stateModel.CurrentCategory != ItemCategory.Artifact)
                RefreshCurrentCategory(false);
        }

        /// <summary>确认当前可堆叠条目的 New 提示。</summary>
        /// <param name="entryKey">可堆叠条目标识。</param>
        private void AcknowledgeStackable(BagEntryKey entryKey)
        {
            if (!ItemId.TryCreate(entryKey.Value, out ItemId itemId)) return;
            stackableInventoryManager.AcknowledgeNew(itemId);
        }

        /// <summary>将稳定实例字符串解析为 EquipmentInstanceId。</summary>
        private static bool TryParseInstanceId(string value, out EquipmentInstanceId instanceId)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                instanceId = default;
                return false;
            }

            instanceId = new EquipmentInstanceId(value);
            return true;
        }
        #endregion
    }
}
