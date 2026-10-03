using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using RPG.Game;
using RPG.Game.UI.Services;
using RPG.Game.UI.Task;
using RPG.Game.UI.Views.Task;
using RPG.ItemSystem;
using RPG.RewardSystemNS;
using RPG.SaveSystem;
using RPG.TaskSystemNS;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.CustomEventSystem;
using WS_Modules.UIModule;

namespace RPG.Game.UI.Controllers
{
    /// <summary>将任务窗口操作连接到 TaskSystem，并协调任务事实变化后的视图刷新。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖同根 TaskWindowDataComponent；任务状态只能经由 GameArchitecture 中的 TaskSystem 查询和命令更新。")]
    public sealed class TaskWindowController : MonoBehaviour
    {
        #region 依赖字段
        private TaskWindowDataComponent data;
        private TaskBrowseStateModel browseState;
        private TaskWindowView view;
        private TaskDetailsPanelView detailsView;
        private TaskSystem taskSystem;
        private SaveManager saveManager;
        private readonly List<IUnRegister> taskEventUnregisters = new List<IUnRegister>();
        private bool initialized;
        private bool visible;
        private bool refreshPending;
        private string claimFailureMessage = string.Empty;
        private WindowSpriteAtlasLeaseService rewardAtlasLeaseService;
        private string rewardAtlasAddressSignature = string.Empty;
        #endregion

        #region 生命周期
        /// <summary>校验窗口引用并连接任务列表、分类页签和详情 View。</summary>
        /// <param name="windowData">窗口 Prefab 的显式依赖。</param>
        public void Initialize(TaskWindowDataComponent windowData)
        {
            if (initialized) return;
            data = windowData ?? throw new ArgumentNullException(nameof(windowData));
            data.ValidateConfiguration();
            taskSystem = GameArchitecture.Interface.GetSystem<TaskSystem>();
            saveManager = GameArchitecture.Interface.GetManager<SaveManager>();
            browseState = new TaskBrowseStateModel();
            detailsView = data.DetailsPanelView;
            view = new TaskWindowView(data, browseState.CategoryFilter, HandleCategoryFilterRequested);
            detailsView.TrackRequested += HandlePrimaryAction;
            detailsView.ClaimRequested += HandleClaimRequested;
            detailsView.Render(null, null, false, string.Empty, ResolveRewardSprite);
            initialized = true;
            Debug.Log("[TaskWindowController] 已连接 TaskSystem、分类列表和详情面板操作。", this);
        }

        /// <summary>显示窗口时订阅事实变化并读取当前任务快照。</summary>
        public void OnWindowShown()
        {
            if (!initialized || visible) return;
            visible = true;
            RegisterTaskEvents();
            saveManager.OperationCompleted += HandleSaveOperationCompleted;
            RefreshNow();
            LoadRewardAtlasesIfNeeded().Forget(HandleRewardAtlasLoadException);
            Debug.Log("[TaskWindowController] 窗口显示，已绑定任务与存档加载通知。", this);
        }

        /// <summary>隐藏窗口时注销事件，避免不可见视图继续刷新。</summary>
        public void OnWindowHidden()
        {
            if (!visible) return;
            visible = false;
            UnregisterTaskEvents();
            saveManager.OperationCompleted -= HandleSaveOperationCompleted;
            rewardAtlasLeaseService?.ScheduleRelease();
            Debug.Log("[TaskWindowController] 窗口隐藏，已释放任务与存档通知。", this);
        }

        /// <summary>销毁时移除按钮意图连接、事件订阅和动态列表实例。</summary>
        public void Dispose()
        {
            OnWindowHidden();
            if (detailsView != null)
            {
                detailsView.TrackRequested -= HandlePrimaryAction;
                detailsView.ClaimRequested -= HandleClaimRequested;
            }
            view?.Dispose();
            view = null;
            detailsView = null;
            if (rewardAtlasLeaseService != null)
            {
                rewardAtlasLeaseService.Released -= HandleRewardAtlasesReleased;
                rewardAtlasLeaseService.Dispose();
                rewardAtlasLeaseService = null;
            }
            taskSystem = null;
            saveManager = null;
            data = null;
            initialized = false;
        }

        /// <summary>仅在事实变化后合并执行一次当前帧刷新。</summary>
        private void Update()
        {
            if (!visible || !refreshPending) return;
            RefreshNow();
        }
        #endregion

        #region 任务事件绑定
        /// <summary>订阅会影响当前任务面板投影的任务事实。</summary>
        private void RegisterTaskEvents()
        {
            taskEventUnregisters.Add(EventSystem.Register_Type<TaskAcceptedEventArgs>(typeof(TaskAcceptedEventArgs), HandleTaskAccepted));
            taskEventUnregisters.Add(EventSystem.Register_Type<TaskObjectiveProgressChangedEventArgs>(typeof(TaskObjectiveProgressChangedEventArgs), HandleTaskProgressChanged));
            taskEventUnregisters.Add(EventSystem.Register_Type<TaskStageChangedEventArgs>(typeof(TaskStageChangedEventArgs), HandleTaskStageChanged));
            taskEventUnregisters.Add(EventSystem.Register_Type<TaskStateChangedEventArgs>(typeof(TaskStateChangedEventArgs), HandleTaskStateChanged));
            taskEventUnregisters.Add(EventSystem.Register_Type<TaskRewardClaimableEventArgs>(typeof(TaskRewardClaimableEventArgs), HandleTaskClaimable));
            taskEventUnregisters.Add(EventSystem.Register_Type<TaskCompletedEventArgs>(typeof(TaskCompletedEventArgs), HandleTaskCompleted));
            taskEventUnregisters.Add(EventSystem.Register_Type<TaskTrackedChangedEventArgs>(typeof(TaskTrackedChangedEventArgs), HandleTaskTrackedChanged));
            taskEventUnregisters.Add(EventSystem.Register_Type<TaskAcknowledgedEventArgs>(typeof(TaskAcknowledgedEventArgs), HandleTaskAcknowledged));
        }

        /// <summary>注销本窗口显示期间的任务事实订阅。</summary>
        private void UnregisterTaskEvents()
        {
            for (int index = 0; index < taskEventUnregisters.Count; index++)
                taskEventUnregisters[index]?.UnRegister();
            taskEventUnregisters.Clear();
        }

        /// <summary>将任务事件和成功读档通知合并为一次 UI 刷新。</summary>
        private void MarkRefreshPending() => refreshPending = true;

        /// <summary>任务接取后安排列表与详情重读。</summary>
        /// <param name="eventArgs">接取成功的任务事实。</param>
        private void HandleTaskAccepted(TaskAcceptedEventArgs eventArgs) => MarkRefreshPending();

        /// <summary>目标进度变化后安排详情重读。</summary>
        /// <param name="eventArgs">已更新的目标进度事实。</param>
        private void HandleTaskProgressChanged(TaskObjectiveProgressChangedEventArgs eventArgs) => MarkRefreshPending();

        /// <summary>阶段切换后安排目标和奖励投影重读。</summary>
        /// <param name="eventArgs">新激活的阶段事实。</param>
        private void HandleTaskStageChanged(TaskStageChangedEventArgs eventArgs) => MarkRefreshPending();

        /// <summary>任务状态变化后安排详情重读。</summary>
        /// <param name="eventArgs">状态变更事实。</param>
        private void HandleTaskStateChanged(TaskStateChangedEventArgs eventArgs) => MarkRefreshPending();

        /// <summary>任务进入待领奖状态后安排按钮刷新。</summary>
        /// <param name="eventArgs">待领奖状态事实。</param>
        private void HandleTaskClaimable(TaskRewardClaimableEventArgs eventArgs) => MarkRefreshPending();

        /// <summary>任务完成后从活动列表移除对应条目。</summary>
        /// <param name="eventArgs">已完成任务事实。</param>
        private void HandleTaskCompleted(TaskCompletedEventArgs eventArgs) => MarkRefreshPending();

        /// <summary>追踪变化后安排选中条目标记刷新。</summary>
        /// <param name="eventArgs">追踪状态变化事实。</param>
        private void HandleTaskTrackedChanged(TaskTrackedChangedEventArgs eventArgs) => MarkRefreshPending();

        /// <summary>任务确认查看后刷新对应未读标记。</summary>
        /// <param name="eventArgs">已确认查看的任务事实。</param>
        private void HandleTaskAcknowledged(TaskAcknowledgedEventArgs eventArgs) => MarkRefreshPending();

        /// <summary>保存新的分类筛选并刷新匹配的活动任务集合。</summary>
        /// <param name="categoryFilter">用户选中的页签筛选条件。</param>
        private void HandleCategoryFilterRequested(E_TaskCategoryFilter categoryFilter)
        {
            if (browseState.CategoryFilter == categoryFilter) return;
            browseState.SelectCategoryFilter(categoryFilter);
            claimFailureMessage = string.Empty;
            RefreshNow();
        }

        /// <summary>仅在读档成功后重读完整事实，存档和列表操作不触发冗余刷新。</summary>
        /// <param name="completion">存档操作结束事实。</param>
        private void HandleSaveOperationCompleted(SaveOperationCompleted completion)
        {
            if (completion.Kind == SaveOperationKind.Load && completion.IsSuccess) MarkRefreshPending();
        }
        #endregion

        #region 用户操作与状态刷新
        /// <summary>在当前任务追踪与待领奖状态间执行对应的唯一主操作。</summary>
        private void HandlePrimaryAction()
        {
            if (!browseState.SelectedTaskId.IsValid) return;
            if (!taskSystem.TryGetActiveRecord(browseState.SelectedTaskId, out TaskRecord record)) return;
            bool succeeded = taskSystem.TrackedTaskId == record.TaskId
                ? taskSystem.ClearTrackedTask()
                : taskSystem.TrySetTrackedTask(record.TaskId);
            if (succeeded) claimFailureMessage = string.Empty;
            MarkRefreshPending();
        }

        /// <summary>通过独立领奖按钮提交当前选中且待领奖的任务。</summary>
        private void HandleClaimRequested()
        {
            if (!browseState.SelectedTaskId.IsValid ||
                !taskSystem.TryGetActiveRecord(browseState.SelectedTaskId, out TaskRecord record) ||
                record.State != E_TaskLifecycleState.Claimable)
            {
                MarkRefreshPending();
                return;
            }

            TaskClaimResult claimResult = taskSystem.TryClaimReward(record.TaskId);
            claimFailureMessage = claimResult.Succeeded
                ? string.Empty
                : $"领取失败：{claimResult.Failure}。调整库存或货币上限后可以重试。";
            MarkRefreshPending();
        }

        /// <summary>基于 TaskSystem 当前事实重建列表并显示稳定的默认选择。</summary>
        private void RefreshNow()
        {
            refreshPending = false;
            IReadOnlyList<TaskRecord> activeRecords = taskSystem.ActiveRecords;
            RefreshRewardAtlasLease(activeRecords);
            List<TaskRecord> visibleRecords = BuildVisibleRecords(activeRecords);
            TaskId previousSelection = browseState.SelectedTaskId;
            if (!ContainsActiveTask(visibleRecords, browseState.SelectedTaskId))
            {
                browseState.SelectedTaskId = taskSystem.TrackedTaskId.IsValid &&
                    ContainsActiveTask(visibleRecords, taskSystem.TrackedTaskId)
                    ? taskSystem.TrackedTaskId
                    : visibleRecords.Count > 0 ? visibleRecords[0].TaskId : default;
            }
            if (previousSelection != browseState.SelectedTaskId) claimFailureMessage = string.Empty;

            view.RenderTaskList(visibleRecords, TaskConfigManager.Instance.GetRequiredDefinition,
                browseState.CategoryFilter, browseState.SelectedTaskId, taskSystem.UnreadTaskIds,
                taskSystem.TrackedTaskId, HandleTaskSelected);
            if (browseState.SelectedTaskId.IsValid &&
                taskSystem.TryGetActiveRecord(browseState.SelectedTaskId, out TaskRecord selectedRecord))
            {
                TaskDefinition definition = TaskConfigManager.Instance.GetRequiredDefinition(selectedRecord.TaskId);
                detailsView.Render(definition, selectedRecord, taskSystem.TrackedTaskId == selectedRecord.TaskId,
                    claimFailureMessage, ResolveRewardSprite);
                // 先让面板实际呈现被选任务，再清除它自己的未读事实。
                taskSystem.AcknowledgeTask(selectedRecord.TaskId);
            }
            else
            {
                detailsView.Render(null, null, false, string.Empty, ResolveRewardSprite);
            }
        }

        /// <summary>选中活动任务后立即更新详情，并确认仅该任务已经被查看。</summary>
        /// <param name="taskId">选中的活动任务。</param>
        private void HandleTaskSelected(TaskId taskId)
        {
            browseState.SelectedTaskId = taskId;
            claimFailureMessage = string.Empty;
            RefreshNow();
        }

        /// <summary>判断任务快照是否仍包含指定活动任务。</summary>
        /// <param name="records">当前活动任务记录。</param>
        /// <param name="taskId">待检查任务。</param>
        /// <returns>任务存在时返回 true。</returns>
        private static bool ContainsActiveTask(IReadOnlyList<TaskRecord> records, TaskId taskId)
        {
            if (!taskId.IsValid) return false;
            for (int index = 0; index < records.Count; index++)
                if (records[index].TaskId == taskId) return true;
            return false;
        }

        /// <summary>按窗口分类状态从 TaskSystem 活动事实中生成列表读取结果。</summary>
        /// <param name="activeRecords">TaskSystem 当前全部活动任务。</param>
        /// <returns>按活动记录原始稳定顺序筛选后的任务。</returns>
        private List<TaskRecord> BuildVisibleRecords(IReadOnlyList<TaskRecord> activeRecords)
        {
            var visibleRecords = new List<TaskRecord>(activeRecords.Count);
            for (int index = 0; index < activeRecords.Count; index++)
            {
                TaskRecord record = activeRecords[index];
                if (browseState.CategoryFilter == E_TaskCategoryFilter.All)
                {
                    visibleRecords.Add(record);
                    continue;
                }

                TaskDefinition definition = TaskConfigManager.Instance.GetRequiredDefinition(record.TaskId);
                bool isMainTask = definition.CategoryId.Value == "main";
                if ((browseState.CategoryFilter == E_TaskCategoryFilter.Main && isMainTask) ||
                    (browseState.CategoryFilter == E_TaskCategoryFilter.Side && !isMainTask))
                    visibleRecords.Add(record);
            }
            return visibleRecords;
        }

        /// <summary>按当前活动任务奖励引用同步动态图集租约；仅地址集合变化时重建租约。</summary>
        /// <param name="records">当前活动任务记录。</param>
        private void RefreshRewardAtlasLease(IReadOnlyList<TaskRecord> records)
        {
            var atlasAddresses = new List<string>();
            if (ItemManager.Instance.IsConfigured)
            {
                for (int recordIndex = 0; recordIndex < records.Count; recordIndex++)
                {
                    TaskDefinition definition = TaskConfigManager.Instance.GetRequiredDefinition(records[recordIndex].TaskId);
                    for (int rewardIndex = 0; rewardIndex < definition.Rewards.Count; rewardIndex++)
                    {
                        if (!(definition.Rewards[rewardIndex] is ItemRewardDefinition itemReward)) continue;
                        for (int itemIndex = 0; itemIndex < itemReward.Items.Count; itemIndex++)
                        {
                            if (!ItemManager.Instance.TryGetDefinition(itemReward.Items[itemIndex].ItemId,
                                    out ItemDefinition itemDefinition) ||
                                string.IsNullOrWhiteSpace(itemDefinition.IconAddress) ||
                                atlasAddresses.Contains(itemDefinition.IconAddress)) continue;
                            atlasAddresses.Add(itemDefinition.IconAddress);
                        }
                    }
                }
            }

            string nextSignature = string.Join("\n", atlasAddresses);
            if (nextSignature == rewardAtlasAddressSignature) return;
            rewardAtlasAddressSignature = nextSignature;
            if (rewardAtlasLeaseService != null)
            {
                rewardAtlasLeaseService.Released -= HandleRewardAtlasesReleased;
                rewardAtlasLeaseService.Dispose();
                rewardAtlasLeaseService = null;
            }
            if (atlasAddresses.Count == 0) return;

            rewardAtlasLeaseService = new WindowSpriteAtlasLeaseService(atlasAddresses, 0.5f, "TaskWindow");
            rewardAtlasLeaseService.Released += HandleRewardAtlasesReleased;
            if (visible) LoadRewardAtlasesIfNeeded().Forget(HandleRewardAtlasLoadException);
        }

        /// <summary>加载当前窗口奖励使用的图集，并忽略租约被重建后的旧异步结果。</summary>
        private async UniTask LoadRewardAtlasesIfNeeded()
        {
            WindowSpriteAtlasLeaseService currentLease = rewardAtlasLeaseService;
            if (!visible || currentLease == null) return;
            bool succeeded = await currentLease.BeginLoadConfiguredAtlasesAsync();
            if (!visible || !ReferenceEquals(currentLease, rewardAtlasLeaseService)) return;
            if (!succeeded)
                Debug.LogWarning("[TaskWindowController] 部分任务奖励图标加载失败，奖励名称与数量仍会显示。", this);
            MarkRefreshPending();
        }

        /// <summary>查询当前成功加载的任务奖励 Sprite。</summary>
        /// <param name="address">图集 Addressable 地址。</param>
        /// <param name="spriteName">图集内 Sprite 名称。</param>
        /// <returns>成功加载时返回图标，否则返回 null。</returns>
        private Sprite ResolveRewardSprite(string address, string spriteName)
        {
            return rewardAtlasLeaseService != null &&
                   rewardAtlasLeaseService.TryGetSprite(address, spriteName, out Sprite sprite)
                ? sprite
                : null;
        }

        /// <summary>图集释放后请求清除旧 Sprite 引用并刷新当前详情。</summary>
        private void HandleRewardAtlasesReleased()
        {
            if (visible) MarkRefreshPending();
        }

        /// <summary>记录奖励图集异步加载期间未能恢复的异常。</summary>
        /// <param name="exception">动态图集加载或释放中的异常。</param>
        private void HandleRewardAtlasLoadException(Exception exception)
        {
            if (!(exception is OperationCanceledException))
                Debug.LogException(exception, this);
        }
        #endregion
    }
}
