using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using RPG.Game.UI.Bag;
using RPG.Game.UI.Escape;
using RPG.Game.UI.Flow;
using RPG.Game.UI.Services;
using RPG.Game.UI.Views.Bag;
using RPG.Game.UI.WeaponDevelopment;
using RPG.ItemSystem;
using UnityEngine;
using UnityEngine.Events;
using WS_Modules.CustomEventSystem;
using WS_Modules.UIModule;

namespace RPG.Game.UI.Controllers
{
    /// <summary>
    /// 背包窗口根节点控制器，负责分类状态、武器数据源、虚拟网格和详情请求。
    /// 它不轮询输入，也不直接修改武器库存。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BagWindowController : MonoBehaviour
    {
        #region 依赖字段

        private BagWindowDataComponent data;
        private BagBrowseStateModel stateModel;
        private WindowSpriteAtlasLeaseService spriteAtlasLeaseService;
        private readonly Dictionary<ItemCategory, IBagCategoryDataSource> dataSourceByCategoryMap = new();
        // 每个分类按钮的对应回调，便于 Dispose 时注销。
        private readonly List<UnityAction> categoryButtonActions = new();
        private IUnRegister weaponChangedUnregister;
        private IUnRegister weaponRestoredUnregister;
        private IUnRegister weaponDefinitionNewChangedUnregister;
        private IUnRegister hudVisibilityRejectedUnregister;
        private IUnRegister bagOpenRequestedUnregister;

        #endregion

        #region 状态字段

        private IReadOnlyList<BagItemViewData> currentEntries = Array.Empty<BagItemViewData>();
        private bool initialized;
        private bool disposed;
        private bool windowShown;
        private bool atlasPreparationRunning;
        private UniTaskCompletionSource atlasPreparationCompletionSource;
        private bool bagOpenRequestRunning;
        private bool waitingForHudHidden;
        private bool bagOpenedByHudReplacement;
        private bool hudWasVisibleBeforeBagOpen;
        private bool hudRestorePending;
        private GameWindowTransitionRequestId hudVisibilityRequestId;

        #endregion

        #region 初始化与释放

        /// <summary>绑定窗口序列化组件并建立一次性的分类、按钮和库存事件索引。</summary>
        /// <param name="windowData">窗口序列化数据。</param>
        public void Initialize(BagWindowDataComponent windowData)
        {
            if (initialized)
            {
                if (!ReferenceEquals(data, windowData)) throw new InvalidOperationException("[BagWindowController] 不允许替换窗口数据组件。");
                return;
            }

            data = windowData ?? throw new ArgumentNullException(nameof(windowData));
            data.ValidateConfiguration();
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
            bagOpenRequestedUnregister = EventSystem.Register_Type<BagWindowOpenRequestedEventArgs>(
                typeof(BagWindowOpenRequestedEventArgs), HandleBagWindowOpenRequested);
            hudVisibilityRejectedUnregister = EventSystem.Register_Type<HudVisibilityChangeRejectedEventArgs>(
                typeof(HudVisibilityChangeRejectedEventArgs), HandleHudVisibilityRejected);
            UIManager.Instance.WindowHidden += HandleWindowHidden;
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
            hudVisibilityRejectedUnregister?.UnRegister();
            bagOpenRequestedUnregister?.UnRegister();
            UIManager.Instance.WindowHidden -= HandleWindowHidden;
            waitingForHudHidden = false;
            bagOpenRequestRunning = false;
            hudRestorePending = false;
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

        /// <summary>启动或复用动态图集准备任务，供打开请求等待资源尝试完成。</summary>
        /// <returns>当前动态图集准备任务。</returns>
        public UniTask PrepareOpenAsync()
        {
            if (disposed) return UniTask.CompletedTask;
            if (atlasPreparationRunning) return atlasPreparationCompletionSource.Task;
            atlasPreparationRunning = true;
            UniTaskCompletionSource completionSource = new UniTaskCompletionSource();
            atlasPreparationCompletionSource = completionSource;
            PrepareAtlasesAsync(completionSource).Forget(HandleAsyncException);
            return completionSource.Task;
        }

        #endregion

        #region 分类与排序

        /// <summary>建立五个分类的数据源，武器使用真实库存，其余分类保留未开放占位。</summary>
        private void BuildDataSources()
        {
            dataSourceByCategoryMap.Clear();
            dataSourceByCategoryMap.Add(ItemCategory.Weapon,
                new WeaponBagCategoryDataSource(ResolveSprite));
            ItemCategory[] categories =
            {
                ItemCategory.DevelopmentItem,
                ItemCategory.Ingredient,
                ItemCategory.Food,
                ItemCategory.Artifact
            };
            for (int index = 0; index < categories.Length; index++)
                dataSourceByCategoryMap[categories[index]] = new UnavailableBagCategoryDataSource(categories[index]);
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
                data.SortDropdown.AddOptions(new List<string> { "品质", "等级", "获得顺序" });
                data.SortDropdown.onValueChanged.AddListener(HandleSortDropdownChanged);
            }
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

        /// <summary>按作者配置的分类顺序移动，不在首尾循环。</summary>
        private void SelectCategoryOffset(int offset)
        {
            int index = IndexOfCategory(stateModel.CurrentCategory);
            if (index >= 0) SelectCategoryAt(index + offset);
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
                if (currentEntries[index].EntryKey == key) return true;
            return false;
        }

        /// <summary>刷新左右分类箭头的禁用状态。</summary>
        private void UpdateArrowState()
        {
            int index = IndexOfCategory(stateModel.CurrentCategory);
            if (data.PreviousCategoryButton != null) data.PreviousCategoryButton.interactable = index > 0;
            if (data.NextCategoryButton != null) data.NextCategoryButton.interactable = index >= 0 && index < data.CategoryOrder.Count - 1;
        }

        /// <summary>查找当前分类在作者顺序中的位置。</summary>
        private int IndexOfCategory(ItemCategory category)
        {
            for (int index = 0; index < data.CategoryOrder.Count; index++)
                if (data.CategoryOrder[index] == category) return index;
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
                WeaponInventoryManager.Instance.AcknowledgeNew(instanceId);
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
                !TryParseInstanceId(stateModel.SelectedEntryKey.Value.Value, out EquipmentInstanceId instanceId)) return;
            EventSystem.EventTrigger_Type(typeof(BagWeaponDeleteRequestedEventArgs),
                new BagWeaponDeleteRequestedEventArgs(instanceId));
        }

        /// <summary>提交详情请求，不在背包 View 中直接打开其他窗口。</summary>
        private void SubmitDetailsRequest()
        {
            if (!stateModel.SelectedEntryKey.HasValue || stateModel.CurrentCategory != ItemCategory.Weapon ||
                !TryParseInstanceId(stateModel.SelectedEntryKey.Value.Value, out EquipmentInstanceId instanceId)) return;
            OpenWeaponDevelopmentAsync(instanceId).Forget(HandleAsyncException);
        }

        /// <summary>处理 Bag 专用打开请求，先完成资源准备再请求隐藏 HUD。</summary>
        /// <param name="_">Bag 打开请求来源。</param>
        private void HandleBagWindowOpenRequested(BagWindowOpenRequestedEventArgs _)
        {
            if (disposed || bagOpenRequestRunning ||
                (UIManager.Instance.TryGetWindow<BagWindow>(out BagWindow currentBag) && currentBag.Visible)) return;
            bagOpenRequestRunning = true;
            hudRestorePending = false;
            hudVisibilityRequestId = GameWindowTransitionRequestId.Create();
            StartBagOpenAsync().Forget(HandleAsyncException);
        }

        /// <summary>执行 Bag 的资源准备和 HUD 隐藏请求；HUD 隐藏完成后由 WindowHidden 回调继续。</summary>
        private async UniTask StartBagOpenAsync()
        {
            try
            {
                if (!UIManager.Instance.TryGetWindow<BagWindow>(out BagWindow bagWindow))
                    throw new InvalidOperationException("BagWindow 尚未完成预加载。");

                // 先等待图集加载尝试完成，确保 HUD 消失后目标窗口可以立即建立列表。
                await bagWindow.PrepareOpenAsync();
                hudWasVisibleBeforeBagOpen = UIManager.Instance.TryGetWindow<HUDWindow>(out HUDWindow hudWindow) &&
                                              hudWindow.Visible;
                if (hudWasVisibleBeforeBagOpen)
                {
                    waitingForHudHidden = true;
                    EventSystem.EventTrigger_Type(
                        typeof(HudVisibilityChangeRequestedEventArgs),
                        new HudVisibilityChangeRequestedEventArgs(hudVisibilityRequestId, false));
                    return;
                }

                await OpenBagWindowAsync();
            }
            catch
            {
                bagOpenRequestRunning = false;
                waitingForHudHidden = false;
                throw;
            }
        }

        /// <summary>在 HUD 已稳定隐藏后直接打开 BagWindow。</summary>
        private async UniTask OpenBagWindowAsync()
        {
            waitingForHudHidden = false;
            try
            {
                BagWindow openedWindow = await UIManager.Instance.PopUpWindowAsync<BagWindow>();
                if (openedWindow == null || !openedWindow.Visible)
                    throw new InvalidOperationException("BagWindow 打开请求未返回可见窗口。");
                bagOpenedByHudReplacement = hudWasVisibleBeforeBagOpen;
                bagOpenRequestRunning = false;
            }
            catch
            {
                bagOpenRequestRunning = false;
                if (hudWasVisibleBeforeBagOpen) RestoreHudAfterBagFailure();
                throw;
            }
        }

        /// <summary>只响应属于当前 Bag 请求的 HUD 稳定隐藏通知。</summary>
        /// <param name="snapshot">UIManager 发布的稳定窗口快照。</param>
        private void HandleWindowHidden(UIWindowSnapshot snapshot)
        {
            if (disposed) return;
            if (waitingForHudHidden && snapshot.WindowName == nameof(HUDWindow))
            {
                // 清除等待标志后再启动异步打开，防止同一生命周期通知重复消费。
                waitingForHudHidden = false;
                OpenBagWindowAsync().Forget(HandleAsyncException);
                return;
            }

            if (snapshot.WindowName == nameof(BagWindow) && bagOpenedByHudReplacement)
            {
                bagOpenedByHudReplacement = false;
                // Bag 关闭后若还有其它全屏窗口，保留待恢复状态，等待它们各自的 WindowHidden 通知。
                hudRestorePending = hudWasVisibleBeforeBagOpen;
            }

            if (hudRestorePending && !HasVisibleFullScreenBusinessWindow())
            {
                hudRestorePending = false;
                RestoreHudAfterBagClose();
            }
        }

        /// <summary>处理 HUD Controller 对当前 Bag 隐藏请求的明确拒绝。</summary>
        /// <param name="eventArgs">HUD 拒绝通知。</param>
        private void HandleHudVisibilityRejected(HudVisibilityChangeRejectedEventArgs eventArgs)
        {
            if (!waitingForHudHidden || eventArgs.RequestId != hudVisibilityRequestId) return;
            waitingForHudHidden = false;
            bagOpenRequestRunning = false;
            Debug.LogWarning($"[BagWindow] HUD 隐藏请求被拒绝：{eventArgs.Reason}", this);
        }

        /// <summary>直接打开武器培养窗口并传入当前实例上下文。</summary>
        /// <param name="instanceId">目标武器实例。</param>
        private async UniTask OpenWeaponDevelopmentAsync(EquipmentInstanceId instanceId)
        {
            if (!UIManager.Instance.IsInitialized)
                throw new InvalidOperationException("UIManager 尚未初始化，无法打开武器培养窗口。");
            WeaponDevelopmentWindow window = await UIManager.Instance.PopUpWindowAsync<WeaponDevelopmentWindow,
                WeaponDevelopmentOpenContext>(new WeaponDevelopmentOpenContext(instanceId));
            if (window == null || !window.Visible)
                throw new InvalidOperationException("武器培养窗口打开请求未返回可见窗口。");
        }

        /// <summary>判断 Bag 隐藏后是否仍有其它可见全屏业务窗口。</summary>
        /// <returns>存在其它可见全屏业务窗口时返回 true。</returns>
        private static bool HasVisibleFullScreenBusinessWindow()
        {
            IReadOnlyList<UIWindowSnapshot> snapshots = UIManager.Instance.GetWindowSnapshots();
            for (int index = 0; index < snapshots.Count; index++)
            {
                UIWindowSnapshot snapshot = snapshots[index];
                if (snapshot.Visible && snapshot.FullScreenWindow && snapshot.WindowName != nameof(HUDWindow) &&
                    snapshot.WindowName != nameof(BagWindow)) return true;
            }

            return false;
        }

        /// <summary>Bag 打开失败时恢复此前可见的 HUD。</summary>
        private static void RestoreHudAfterBagFailure()
        {
            if (UIManager.Instance.TryGetWindow<HUDWindow>(out HUDWindow hudWindow) && !hudWindow.Visible)
                UIManager.Instance.PopUpWindowAsync<HUDWindow>().Forget();
        }

        /// <summary>Bag 关闭且没有其它全屏业务窗口时直接恢复 HUD。</summary>
        private static void RestoreHudAfterBagClose()
        {
            if (UIManager.Instance.TryGetWindow<HUDWindow>(out HUDWindow hudWindow) && !hudWindow.Visible)
                UIManager.Instance.PopUpWindowAsync<HUDWindow>().Forget();
        }

        /// <summary>处理选择变化并刷新右侧共用详情外壳与武器内容。</summary>
        private void HandleSelectionChanged(BagEntryKey? key)
        {
            // 状态模型是选择的唯一来源；每次详情刷新同时把选择投影到当前可见格子。
            data.GridView?.SetSelection(key);
            if (!key.HasValue || !dataSourceByCategoryMap.TryGetValue(key.Value.Category,
                    out IBagCategoryDataSource source) || !source.TryBuildDetails(key.Value, out BagDetailViewData details))
            {
                data.DetailShellView?.Clear();
                data.WeaponDetailView?.Clear();
                if (data.DeleteButton != null) data.DeleteButton.interactable = false;
                if (data.DetailsButton != null) data.DetailsButton.interactable = false;
                return;
            }

            data.DetailShellView?.Bind(details);
            if (key.Value.Category == ItemCategory.Weapon) data.WeaponDetailView?.Bind(details);
            if (data.DeleteButton != null) data.DeleteButton.interactable = true;
            if (data.DetailsButton != null) data.DetailsButton.interactable = true;
        }

        #endregion

        #region 资源与动态刷新

        /// <summary>动态图集真正释放后清空动态网格和详情，避免 Image 继续持有失效 Sprite。</summary>
        private void HandleAtlasReleased()
        {
            if (disposed) return;
            currentEntries = Array.Empty<BagItemViewData>();
            data.GridView?.Bind(Array.Empty<BagItemViewData>(), null);
            data.DetailShellView?.Clear();
            data.WeaponDetailView?.Clear();
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

        /// <summary>按当前租约解析动态图集 Sprite，未加载时返回 null。</summary>
        private Sprite ResolveSprite(string address, string spriteName)
        {
            return spriteAtlasLeaseService.TryGetSprite(address, spriteName, out Sprite sprite)
                ? sprite
                : null;
        }

        /// <summary>记录窗口异步流程中的非取消异常。</summary>
        private void HandleAsyncException(Exception exception)
        {
            if (!(exception is OperationCanceledException)) Debug.LogException(exception, this);
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
