using System;
using System.Collections.Generic;
using RPG.Game.UI.Views.Task;
using RPG.TaskSystemNS;
using UnityEngine;
using WS_Modules.UIModule;

namespace RPG.Game.UI.Task
{
    /// <summary>显示任务分类页签和按分类分区的活动任务列表。</summary>
    public sealed class TaskWindowView : IDisposable
    {
        #region 依赖字段
        private readonly TaskWindowDataComponent data;
        private readonly List<TaskItemView> taskItemViews = new List<TaskItemView>();
        private readonly List<TaskItemView> createdTaskItemViews = new List<TaskItemView>();
        private readonly TaskCategoryTabView[] categoryTabViews;
        #endregion

        #region 生命周期
        /// <summary>连接列表和分类页签，并收集 Prefab 中可复用的任务行。</summary>
        /// <param name="windowData">任务窗口显式引用集合。</param>
        /// <param name="initialFilter">当前窗口保存的分类筛选。</param>
        /// <param name="onCategoryRequested">用户点击分类页签后的回调。</param>
        public TaskWindowView(
            TaskWindowDataComponent windowData,
            E_TaskCategoryFilter initialFilter,
            Action<E_TaskCategoryFilter> onCategoryRequested)
        {
            data = windowData ?? throw new ArgumentNullException(nameof(windowData));
            if (onCategoryRequested == null) throw new ArgumentNullException(nameof(onCategoryRequested));
            data.ValidateConfiguration();

            categoryTabViews = data.CategoryTabViews;
            for (int index = 0; index < categoryTabViews.Length; index++)
            {
                categoryTabViews[index].Bind(onCategoryRequested);
                categoryTabViews[index].SetSelected(categoryTabViews[index].CategoryFilter == initialFilter);
            }

            AddExistingTaskItems(data.MainQuestRoot);
            AddExistingTaskItems(data.SideQuestRoot);
        }

        /// <summary>释放页签回调并销毁运行时额外创建的任务行。</summary>
        public void Dispose()
        {
            for (int index = 0; index < categoryTabViews.Length; index++) categoryTabViews[index].Unbind();
            for (int index = 0; index < createdTaskItemViews.Count; index++)
                if (createdTaskItemViews[index] != null) UnityEngine.Object.Destroy(createdTaskItemViews[index].gameObject);
            createdTaskItemViews.Clear();
            taskItemViews.Clear();
        }
        #endregion

        #region 列表展示
        /// <summary>按当前筛选显示活动任务，并刷新条目的选中、未读及追踪状态。</summary>
        /// <param name="records">当前筛选后的活动任务记录。</param>
        /// <param name="definitionById">读取任务标题和分类的配置查询。</param>
        /// <param name="categoryFilter">当前选中的分类页签。</param>
        /// <param name="selectedTaskId">当前选中任务。</param>
        /// <param name="unreadTaskIds">尚未确认查看的任务集合。</param>
        /// <param name="trackedTaskId">当前追踪任务。</param>
        /// <param name="onSelect">任务条目选择回调。</param>
        public void RenderTaskList(
            IReadOnlyList<TaskRecord> records,
            Func<TaskId, TaskDefinition> definitionById,
            E_TaskCategoryFilter categoryFilter,
            TaskId selectedTaskId,
            IReadOnlyCollection<TaskId> unreadTaskIds,
            TaskId trackedTaskId,
            Action<TaskId> onSelect)
        {
            for (int index = 0; index < records.Count; index++)
            {
                TaskRecord record = records[index];
                TaskDefinition definition = definitionById(record.TaskId);
                bool isMain = definition.CategoryId.Value == "main";

                RectTransform parent = isMain ? data.MainQuestRoot : data.SideQuestRoot;
                TaskItemView itemView = GetOrCreateTaskItem(index, parent);
                TaskStageDefinition stage = definition.TryGetStage(record.CurrentStageId, out TaskStageDefinition currentStage, out _)
                    ? currentStage
                    : throw new InvalidOperationException($"任务 {record.TaskId} 的当前阶段找不到配置。");
                itemView.gameObject.name = $"TaskItem_{record.TaskId.Value}";
                itemView.Bind(
                    definition.Title,
                    $"{(record.State == E_TaskLifecycleState.Claimable ? "可领取奖励" : stage.Title)} · {record.TaskId.Value}",
                    record.TaskId == selectedTaskId,
                    ContainsTaskId(unreadTaskIds, record.TaskId),
                    trackedTaskId == record.TaskId,
                    () => onSelect(record.TaskId));
            }

            for (int index = records.Count; index < taskItemViews.Count; index++)
                taskItemViews[index].gameObject.SetActive(false);

            // 分类页签决定标题分区是否显示；分区为空时仍保留标题，只隐藏没有绑定任务的条目。
            data.MainQuestRoot.gameObject.SetActive(categoryFilter != E_TaskCategoryFilter.Side);
            data.SideQuestRoot.gameObject.SetActive(categoryFilter != E_TaskCategoryFilter.Main);
            for (int index = 0; index < categoryTabViews.Length; index++)
                categoryTabViews[index].SetSelected(categoryTabViews[index].CategoryFilter == categoryFilter);
        }

        /// <summary>获取或创建列表条目，并将复用对象放入正确的分类容器。</summary>
        /// <param name="index">当前任务在筛选结果中的顺序。</param>
        /// <param name="parent">主线或支线列表根节点。</param>
        /// <returns>可以绑定当前任务事实的列表条目。</returns>
        private TaskItemView GetOrCreateTaskItem(int index, RectTransform parent)
        {
            if (index < taskItemViews.Count)
            {
                TaskItemView pooledView = taskItemViews[index];
                pooledView.transform.SetParent(parent, false);
                pooledView.gameObject.SetActive(true);
                return pooledView;
            }

            GameObject itemObject = UnityEngine.Object.Instantiate(data.TaskItemPrefab, parent, false);
            TaskItemView itemView = itemObject.GetComponent<TaskItemView>();
            taskItemViews.Add(itemView);
            createdTaskItemViews.Add(itemView);
            return itemView;
        }

        /// <summary>收集 Prefab 中已有的任务行作为首批复用对象。</summary>
        /// <param name="parent">主线或支线任务容器。</param>
        private void AddExistingTaskItems(RectTransform parent)
        {
            TaskItemView[] existingViews = parent.GetComponentsInChildren<TaskItemView>(true);
            for (int index = 0; index < existingViews.Length; index++)
                if (!taskItemViews.Contains(existingViews[index])) taskItemViews.Add(existingViews[index]);
        }

        /// <summary>检查未读任务快照中是否包含指定任务。</summary>
        /// <param name="taskIds">任务未读集合。</param>
        /// <param name="taskId">待检查任务。</param>
        /// <returns>任务尚未查看时返回 true。</returns>
        private static bool ContainsTaskId(IReadOnlyCollection<TaskId> taskIds, TaskId taskId)
        {
            foreach (TaskId candidate in taskIds)
                if (candidate == taskId) return true;
            return false;
        }
        #endregion
    }
}
