using System;
using System.Collections.Generic;
using System.Text;
using RPG.Game.UI.Task;
using RPG.TaskSystem;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using WS_Modules.UIModule;
using WS_Modules.LogModule;

namespace RPG.Game.UI.Controllers
{
    /// <summary>
    /// 协调任务分类、条目选择、详情展示和任务面板后端操作。
    /// </summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖根节点上的 TaskWindowDataComponent，以及 TaskItem Prefab 根节点上的 TaskItemView。")]
    public sealed class TaskWindowController : MonoBehaviour
    {
        #region 依赖字段

        // 依赖字段：窗口所有静态控件来自 DataComponent；动态条目必须在条目 Prefab 上绑定 TaskItemView。
        private TaskWindowDataComponent data;
        private ITaskWindowBackend backend;

        #endregion

        #region 状态

        private readonly List<TaskItemView> taskItemViews = new List<TaskItemView>();
        private readonly List<UnityAction> categoryButtonListeners = new List<UnityAction>();
        private UnityAction trackingButtonListener;
        private TaskCategoryId currentCategory = new TaskCategoryId(TaskCategoryCatalog.MainIdValue);
        private TaskId? selectedTaskId;
        private TaskId? lastAcknowledgeRequestedTaskId;
        private TaskWindowSnapshot currentSnapshot;
        private bool initialized;
        private bool disposed;
        private bool windowVisible;
        private bool selectionInitialized;
        private bool refreshInProgress;
        private bool refreshPending;
        private bool preferTrackedOnPendingRefresh;

        #endregion

        #region 生命周期

        /// <summary>
        /// 初始化序列化 UI 依赖并绑定页签和追踪按钮事件。
        /// </summary>
        /// <param name="windowData">根节点上已校验的窗口 UI 配置。</param>
        /// <exception cref="ArgumentNullException">窗口配置为空时抛出。</exception>
        /// <exception cref="InvalidOperationException">控制器已经初始化时抛出。</exception>
        public void Initialize(TaskWindowDataComponent windowData)
        {
            if (windowData == null)
                throw new ArgumentNullException(nameof(windowData));
            if (initialized)
                throw new InvalidOperationException("[TaskWindowController] 不允许重复初始化同一控制器。");

            data = windowData;
            RegisterButtonListeners();
            data.LocationSection.SetActive(false);
            data.TimeRemainingPanel.SetActive(false);
            data.TaskDetailsPanel.SetActive(false);
            data.RewardSection.SetActive(false);
            initialized = true;
            RenderCategorySelection();
            WSLog.Log("[TaskWindowController] 初始化完成，页签和追踪按钮监听已注册。");
        }

        /// <summary>
        /// 窗口显示时订阅后端变化并读取最新快照。
        /// </summary>
        public void OnWindowShown()
        {
            if (disposed || windowVisible)
                return;

            windowVisible = true;
            SubscribeBackend();
            RefreshSnapshot(!selectionInitialized);
            WSLog.Log("[TaskWindowController] 窗口显示，任务快照已刷新。");
        }

        /// <summary>
        /// 窗口隐藏时注销后端事件，避免隐藏窗口继续响应任务变化。
        /// </summary>
        public void OnWindowHidden()
        {
            if (!windowVisible)
                return;

            windowVisible = false;
            UnsubscribeBackend();
            WSLog.Log("[TaskWindowController] 窗口隐藏，后端变化订阅已注销。");
        }

        /// <summary>
        /// 销毁时幂等注销事件、按钮监听并销毁动态任务条目。
        /// </summary>
        public void Dispose()
        {
            if (disposed)
                return;

            OnWindowHidden();
            disposed = true;
            UnregisterButtonListeners();
            ClearTaskItems();
            backend = null;
            currentSnapshot = null;
            WSLog.Log("[TaskWindowController] 控制器已释放，动态条目和事件引用已清理。");
        }

        #endregion

        #region 后端绑定

        /// <summary>
        /// 更换任务面板后端并按当前窗口可见状态同步事件订阅。
        /// </summary>
        /// <param name="taskBackend">调用方持有的任务面板后端；为空时显示空态。</param>
        /// <exception cref="ObjectDisposedException">控制器已经销毁时抛出。</exception>
        public void BindBackend(ITaskWindowBackend taskBackend)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(TaskWindowController));
            if (ReferenceEquals(backend, taskBackend))
                return;

            UnsubscribeBackend();
            backend = taskBackend;
            if (windowVisible)
                SubscribeBackend();

            RefreshSnapshot(!selectionInitialized && backend != null);
            WSLog.Log($"[TaskWindowController] 后端绑定完成，hasBackend={backend != null}。");
        }

        /// <summary>窗口可见期间订阅当前后端的任务快照变化。</summary>
        private void SubscribeBackend()
        {
            if (backend == null)
                return;

            backend.Changed -= HandleBackendChanged;
            backend.Changed += HandleBackendChanged;
        }

        /// <summary>注销当前后端的快照变化回调。</summary>
        private void UnsubscribeBackend()
        {
            if (backend != null)
                backend.Changed -= HandleBackendChanged;
        }

        #endregion

        #region 分类与选择

        /// <summary>按主线、支线顺序绑定页签，并注册追踪按钮回调。</summary>
        private void RegisterButtonListeners()
        {
            categoryButtonListeners.Clear();
            for (int index = 0; index < data.CategoryButtons.Count; index++)
            {
                TaskCategoryId categoryId = new TaskCategoryId(
                    index == 0 ? TaskCategoryCatalog.MainIdValue : TaskCategoryCatalog.SideIdValue);
                UnityAction listener = () => SelectCategory(categoryId);
                data.CategoryButtons[index].onClick.AddListener(listener);
                categoryButtonListeners.Add(listener);
            }

            trackingButtonListener = HandleTrackingButtonClicked;
            data.TrackingButton.onClick.AddListener(trackingButtonListener);
        }

        /// <summary>对称移除初始化时注册的所有窗口按钮回调。</summary>
        private void UnregisterButtonListeners()
        {
            for (int index = 0; index < categoryButtonListeners.Count; index++)
            {
                if (index < data.CategoryButtons.Count)
                    data.CategoryButtons[index].onClick.RemoveListener(categoryButtonListeners[index]);
            }

            if (trackingButtonListener != null)
                data.TrackingButton.onClick.RemoveListener(trackingButtonListener);

            categoryButtonListeners.Clear();
            trackingButtonListener = null;
        }

        /// <summary>切换任务分类并按该分类的第一条任务重新选择。</summary>
        /// <param name="categoryId">页签对应的任务分类。</param>
        private void SelectCategory(TaskCategoryId categoryId)
        {
            if (disposed || currentCategory == categoryId)
                return;

            currentCategory = categoryId;
            selectedTaskId = null;
            lastAcknowledgeRequestedTaskId = null;
            selectionInitialized = true;
            RenderCategorySelection();
            RefreshSnapshot(false);
            WSLog.Log($"[TaskWindowController] 切换任务分类，categoryId={categoryId.Value}。");
        }

        /// <summary>响应任务条目点击并刷新选中详情。</summary>
        /// <param name="taskId">被点击任务的稳定 ID。</param>
        private void HandleTaskSelected(TaskId taskId)
        {
            if (disposed || selectedTaskId == taskId)
                return;

            selectedTaskId = taskId;
            lastAcknowledgeRequestedTaskId = null;
            RefreshSnapshot(false);
            WSLog.Log($"[TaskWindowController] 选择任务，taskId={taskId.Value}。");
        }

        /// <summary>根据当前分类同步页签背景与底部横条显示。</summary>
        private void RenderCategorySelection()
        {
            for (int index = 0; index < data.CategoryButtons.Count; index++)
            {
                bool isSelected = (index == 0 && currentCategory.Value == TaskCategoryCatalog.MainIdValue) ||
                                  (index == 1 && currentCategory.Value == TaskCategoryCatalog.SideIdValue);
                data.CategorySelectionBackgrounds[index].SetActive(isSelected);
                data.CategorySelectionUnderlines[index].SetActive(isSelected);
            }
        }

        #endregion

        #region 快照与界面刷新

        /// <summary>查询并渲染一次后端快照，同时合并同步触发的重入刷新。</summary>
        /// <param name="preferTrackedWhenNoSelection">没有有效选择时是否优先选择当前追踪任务。</param>
        private void RefreshSnapshot(bool preferTrackedWhenNoSelection)
        {
            if (!initialized || disposed)
                return;
            if (refreshInProgress)
            {
                refreshPending = true;
                preferTrackedOnPendingRefresh |= preferTrackedWhenNoSelection;
                return;
            }

            refreshInProgress = true;
            bool preferTracked = preferTrackedWhenNoSelection;
            try
            {
                do
                {
                    refreshPending = false;
                    RenderSnapshotOnce(preferTracked);
                    preferTracked = preferTrackedOnPendingRefresh;
                    preferTrackedOnPendingRefresh = false;
                } while (refreshPending);
            }
            finally
            {
                refreshInProgress = false;
            }
        }

        /// <summary>按当前分类过滤快照，重建条目并刷新详情和未读状态。</summary>
        /// <param name="preferTrackedWhenNoSelection">没有有效选择时是否优先选择当前追踪任务。</param>
        private void RenderSnapshotOnce(bool preferTrackedWhenNoSelection)
        {
            if (backend == null)
            {
                currentSnapshot = null;
                ClearTaskItems();
                ClearDetails();
                SetCategoryRootsActive(false);
                data.EmptyStateText.text = "任务数据尚未连接";
                data.EmptyStateText.gameObject.SetActive(true);
                RenderCategorySelection();
                return;
            }

            currentSnapshot = backend.GetSnapshot();
            if (currentSnapshot == null)
                throw new InvalidOperationException("[TaskWindowController] 后端 GetSnapshot() 返回空引用。");

            List<TaskWindowTaskViewData> visibleTasks = new List<TaskWindowTaskViewData>();
            for (int index = 0; index < currentSnapshot.Tasks.Count; index++)
            {
                TaskWindowTaskViewData task = currentSnapshot.Tasks[index];
                if (task.CategoryId == currentCategory)
                    visibleTasks.Add(task);
            }

            TaskWindowTaskViewData selectedTask = FindSelectedTask(visibleTasks);
            if (selectedTask == null && preferTrackedWhenNoSelection && currentSnapshot.TrackedTaskId.HasValue)
                selectedTask = FindTask(visibleTasks, currentSnapshot.TrackedTaskId.Value);
            if (selectedTask == null && visibleTasks.Count > 0)
                selectedTask = visibleTasks[0];

            selectedTaskId = selectedTask == null ? (TaskId?)null : selectedTask.TaskId;
            selectionInitialized = true;
            RenderCategorySelection();
            RenderTaskRows(visibleTasks, selectedTask);
            RenderDetails(selectedTask);
            data.EmptyStateText.text = "目前沒有任務";
            data.EmptyStateText.gameObject.SetActive(visibleTasks.Count == 0);
            SetCategoryRootsActive(visibleTasks.Count > 0);

            // 只有详情文字已经写入可见面板后才确认未读，避免单纯查询快照就清标记。
            if (selectedTask != null && selectedTask.IsUnread &&
                lastAcknowledgeRequestedTaskId != selectedTask.TaskId)
            {
                lastAcknowledgeRequestedTaskId = selectedTask.TaskId;
                backend.AcknowledgeTask(selectedTask.TaskId);
            }
            else if (selectedTask == null || !selectedTask.IsUnread)
            {
                lastAcknowledgeRequestedTaskId = null;
            }
        }

        /// <summary>在当前分类任务中保留仍存在的选中项。</summary>
        /// <param name="tasks">本次分类过滤后的任务列表。</param>
        /// <returns>有效选中项；已不存在时返回 null。</returns>
        private TaskWindowTaskViewData FindSelectedTask(IReadOnlyList<TaskWindowTaskViewData> tasks)
        {
            if (!selectedTaskId.HasValue)
                return null;

            return FindTask(tasks, selectedTaskId.Value);
        }

        /// <summary>按稳定任务 ID 在指定列表中查找展示数据。</summary>
        /// <param name="tasks">待搜索的任务列表。</param>
        /// <param name="taskId">目标任务 ID。</param>
        /// <returns>找到时返回任务数据，否则返回 null。</returns>
        private static TaskWindowTaskViewData FindTask(IReadOnlyList<TaskWindowTaskViewData> tasks, TaskId taskId)
        {
            for (int index = 0; index < tasks.Count; index++)
            {
                if (tasks[index].TaskId == taskId)
                    return tasks[index];
            }

            return null;
        }

        /// <summary>清理旧实例并按当前分类动态创建任务条目 Prefab。</summary>
        /// <param name="tasks">要展示的分类任务列表。</param>
        /// <param name="selectedTask">当前详情选择；没有选择时为空。</param>
        private void RenderTaskRows(IReadOnlyList<TaskWindowTaskViewData> tasks, TaskWindowTaskViewData selectedTask)
        {
            ClearTaskItems();
            Transform parent = currentCategory.Value == TaskCategoryCatalog.MainIdValue
                ? data.MainQuestSectionRoot.transform
                : data.SideQuestSectionRoot.transform;

            for (int index = 0; index < tasks.Count; index++)
            {
                TaskWindowTaskViewData task = tasks[index];
                GameObject instance = Instantiate(data.TaskItemPrefab, parent, false);
                TaskItemView itemView = instance.GetComponent<TaskItemView>();
                if (itemView == null)
                {
                    Destroy(instance);
                    throw new InvalidOperationException("[TaskWindowController] TaskItem Prefab 根节点缺少 TaskItemView。");
                }

                itemView.Initialize(task.TaskId, HandleTaskSelected);
                bool isSelected = selectedTask != null && selectedTask.TaskId == task.TaskId;
                bool isTracked = currentSnapshot.TrackedTaskId.HasValue &&
                                 currentSnapshot.TrackedTaskId.Value == task.TaskId;
                itemView.Render(task, isSelected, isTracked);
                taskItemViews.Add(itemView);
            }
        }

        /// <summary>将任务、阶段、目标、奖励和追踪状态写入详情区。</summary>
        /// <param name="task">当前选中的任务；为空时清空详情。</param>
        private void RenderDetails(TaskWindowTaskViewData task)
        {
            if (task == null)
            {
                ClearDetails();
                return;
            }

            data.TaskDetailsPanel.SetActive(true);
            data.TaskTitleText.text = task.Title;

            StringBuilder stageAndObjectives = new StringBuilder(task.StageTitle);
            for (int index = 0; index < task.Objectives.Count; index++)
            {
                TaskWindowObjectiveViewData objective = task.Objectives[index];
                if (stageAndObjectives.Length > 0)
                    stageAndObjectives.AppendLine();
                stageAndObjectives.Append("• ").Append(objective.Text)
                    .Append("  ").Append(objective.Current).Append('/').Append(objective.Required);
            }
            data.StageAndObjectivesText.text = stageAndObjectives.ToString();

            StringBuilder description = new StringBuilder(task.Description);
            if (!string.IsNullOrWhiteSpace(task.StageDescription))
            {
                if (description.Length > 0)
                    description.AppendLine().AppendLine();
                description.Append(task.StageDescription);
            }
            data.TaskDescriptionText.text = description.ToString();

            StringBuilder rewards = new StringBuilder();
            for (int index = 0; index < task.Rewards.Count; index++)
            {
                TaskWindowRewardViewData reward = task.Rewards[index];
                if (rewards.Length > 0)
                    rewards.AppendLine();
                rewards.Append(reward.Name).Append(" × ").Append(reward.Amount);
            }
            data.RewardsText.text = rewards.ToString();
            data.RewardSection.SetActive(task.Rewards.Count > 0);

            bool isTracked = currentSnapshot.TrackedTaskId.HasValue &&
                             currentSnapshot.TrackedTaskId.Value == task.TaskId;
            data.TrackingButton.interactable = true;
            data.TrackingButtonLabel.text = isTracked ? "取消追踪" : "开始追踪";
        }

        /// <summary>清空详情展示并禁用无任务时的追踪操作。</summary>
        private void ClearDetails()
        {
            data.TaskDetailsPanel.SetActive(false);
            data.TaskTitleText.text = string.Empty;
            data.StageAndObjectivesText.text = string.Empty;
            data.TaskDescriptionText.text = string.Empty;
            data.RewardsText.text = string.Empty;
            data.RewardSection.SetActive(false);
            data.TrackingButton.interactable = false;
            data.TrackingButtonLabel.text = "开始追踪";
        }

        /// <summary>只显示当前分类的任务分组根节点。</summary>
        /// <param name="hasTasks">当前分类是否存在可显示任务。</param>
        private void SetCategoryRootsActive(bool hasTasks)
        {
            bool showMain = hasTasks && currentCategory.Value == TaskCategoryCatalog.MainIdValue;
            bool showSide = hasTasks && currentCategory.Value == TaskCategoryCatalog.SideIdValue;
            data.MainQuestSectionRoot.SetActive(showMain);
            data.SideQuestSectionRoot.SetActive(showSide);
        }

        /// <summary>解绑并销毁控制器持有的所有动态条目实例。</summary>
        private void ClearTaskItems()
        {
            for (int index = 0; index < taskItemViews.Count; index++)
            {
                TaskItemView itemView = taskItemViews[index];
                if (itemView == null)
                    continue;

                itemView.UnbindSelection();
                itemView.gameObject.SetActive(false);
                Destroy(itemView.gameObject);
            }
            taskItemViews.Clear();
        }

        #endregion

        #region 用户意图与事件

        /// <summary>根据快照状态将追踪按钮点击转成开始或取消追踪请求。</summary>
        private void HandleTrackingButtonClicked()
        {
            if (backend == null || !selectedTaskId.HasValue || currentSnapshot == null)
                return;

            TaskId taskId = selectedTaskId.Value;
            bool isTracked = currentSnapshot.TrackedTaskId.HasValue &&
                             currentSnapshot.TrackedTaskId.Value == taskId;
            bool succeeded = isTracked ? ClearTrackedTask() : backend.TrySetTrackedTask(taskId);
            if (!succeeded)
            {
                WSLog.LogWarning($"[TaskWindowController] 追踪操作被后端拒绝，taskId={taskId.Value}。");
                return;
            }

            WSLog.Log($"[TaskWindowController] 已提交{(isTracked ? "取消" : "开始")}追踪请求，taskId={taskId.Value}。");
        }

        /// <summary>向后端提交清除当前追踪任务的请求。</summary>
        /// <returns>接口未报告失败时返回 true。</returns>
        private bool ClearTrackedTask()
        {
            backend.ClearTrackedTask();
            return true;
        }

        /// <summary>后端任务数据变化时刷新当前可见窗口。</summary>
        private void HandleBackendChanged()
        {
            if (windowVisible)
                RefreshSnapshot(false);
        }

        #endregion
    }
}
