using System;
using System.Collections.Generic;
using RPG.TaskSystem;
using UnityEngine;

namespace RPG.Game.UI.Task
{
    #region 任务快照

    /// <summary>
    /// 保存一次任务面板刷新所需的只读快照。
    /// </summary>
    public sealed class TaskWindowSnapshot
    {
        private readonly IReadOnlyList<TaskWindowTaskViewData> tasks;

        /// <summary>
        /// 创建只读任务快照。
        /// </summary>
        /// <param name="tasks">当前可展示的任务数据。</param>
        /// <param name="trackedTaskId">当前追踪任务；没有追踪时为空。</param>
        /// <exception cref="ArgumentNullException">任务集合为空引用时抛出。</exception>
        /// <exception cref="ArgumentException">任务集合包含空任务数据时抛出。</exception>
        public TaskWindowSnapshot(IEnumerable<TaskWindowTaskViewData> tasks, TaskId? trackedTaskId)
        {
            if (tasks == null)
                throw new ArgumentNullException(nameof(tasks));

            List<TaskWindowTaskViewData> taskCopy = new List<TaskWindowTaskViewData>(tasks);
            if (taskCopy.Contains(null))
                throw new ArgumentException("任务快照不能包含空任务数据。", nameof(tasks));

            this.tasks = taskCopy.AsReadOnly();
            TrackedTaskId = trackedTaskId;
        }

        /// <summary>获取只读任务列表。</summary>
        public IReadOnlyList<TaskWindowTaskViewData> Tasks => tasks;

        /// <summary>获取当前追踪任务 ID。</summary>
        public TaskId? TrackedTaskId { get; }
    }

    #endregion

    #region 任务条目与详情数据

    /// <summary>
    /// 表示单个任务列表项和详情面板所需的展示数据。
    /// </summary>
    public sealed class TaskWindowTaskViewData
    {
        private readonly IReadOnlyList<TaskWindowObjectiveViewData> objectives;
        private readonly IReadOnlyList<TaskWindowRewardViewData> rewards;

        /// <summary>
        /// 创建不可变任务展示数据。
        /// </summary>
        /// <param name="taskId">稳定任务 ID。</param>
        /// <param name="categoryId">主线或支线分类 ID。</param>
        /// <param name="title">任务标题。</param>
        /// <param name="description">任务说明。</param>
        /// <param name="stageTitle">当前阶段标题。</param>
        /// <param name="stageDescription">当前阶段说明。</param>
        /// <param name="objectives">当前阶段目标列表。</param>
        /// <param name="rewards">奖励名称和数量。</param>
        /// <param name="state">现有 TaskSystem 生命周期状态。</param>
        /// <param name="isUnread">该任务是否尚未查看。</param>
        /// <param name="categoryIcon">可选分类图标；为空时沿用 Prefab 默认图标。</param>
        /// <exception cref="ArgumentException">任务或分类 ID 无效时抛出。</exception>
        /// <exception cref="ArgumentNullException">目标或奖励集合为空引用时抛出。</exception>
        public TaskWindowTaskViewData(
            TaskId taskId,
            TaskCategoryId categoryId,
            string title,
            string description,
            string stageTitle,
            string stageDescription,
            IEnumerable<TaskWindowObjectiveViewData> objectives,
            IEnumerable<TaskWindowRewardViewData> rewards,
            TaskLifecycleState state,
            bool isUnread,
            Sprite categoryIcon = null)
        {
            if (!taskId.IsValid)
                throw new ArgumentException("任务面板数据必须包含有效 TaskId。", nameof(taskId));
            if (!categoryId.IsValid || !TaskCategoryCatalog.IsDefined(categoryId))
                throw new ArgumentException("任务面板数据必须使用已登记的任务分类。", nameof(categoryId));
            if (objectives == null)
                throw new ArgumentNullException(nameof(objectives));
            if (rewards == null)
                throw new ArgumentNullException(nameof(rewards));

            List<TaskWindowObjectiveViewData> objectiveCopy = new List<TaskWindowObjectiveViewData>(objectives);
            List<TaskWindowRewardViewData> rewardCopy = new List<TaskWindowRewardViewData>(rewards);
            if (objectiveCopy.Contains(null) || rewardCopy.Contains(null))
                throw new ArgumentException("任务面板目标和奖励不能包含空数据项。");

            TaskId = taskId;
            CategoryId = categoryId;
            Title = title ?? string.Empty;
            Description = description ?? string.Empty;
            StageTitle = stageTitle ?? string.Empty;
            StageDescription = stageDescription ?? string.Empty;
            this.objectives = objectiveCopy.AsReadOnly();
            this.rewards = rewardCopy.AsReadOnly();
            State = state;
            IsUnread = isUnread;
            CategoryIcon = categoryIcon;
        }

        /// <summary>获取稳定任务 ID。</summary>
        public TaskId TaskId { get; }

        /// <summary>获取任务分类 ID。</summary>
        public TaskCategoryId CategoryId { get; }

        /// <summary>获取任务标题。</summary>
        public string Title { get; }

        /// <summary>获取任务说明。</summary>
        public string Description { get; }

        /// <summary>获取当前阶段标题。</summary>
        public string StageTitle { get; }

        /// <summary>获取当前阶段说明。</summary>
        public string StageDescription { get; }

        /// <summary>获取当前阶段目标。</summary>
        public IReadOnlyList<TaskWindowObjectiveViewData> Objectives => objectives;

        /// <summary>获取任务奖励展示项。</summary>
        public IReadOnlyList<TaskWindowRewardViewData> Rewards => rewards;

        /// <summary>获取任务生命周期状态。</summary>
        public TaskLifecycleState State { get; }

        /// <summary>获取该任务是否未读。</summary>
        public bool IsUnread { get; }

        /// <summary>获取可选分类图标。</summary>
        public Sprite CategoryIcon { get; }

        /// <summary>
        /// 创建未读状态更新后的新展示对象，保留后端快照不可变约定。
        /// </summary>
        /// <param name="isUnread">更新后的未读状态。</param>
        /// <returns>复制后的任务展示数据。</returns>
        public TaskWindowTaskViewData WithUnread(bool isUnread)
        {
            return new TaskWindowTaskViewData(
                TaskId, CategoryId, Title, Description, StageTitle, StageDescription,
                objectives, rewards, State, isUnread, CategoryIcon);
        }
    }

    /// <summary>
    /// 表示任务当前阶段中的一项目标和进度。
    /// </summary>
    public sealed class TaskWindowObjectiveViewData
    {
        /// <summary>
        /// 创建单项任务目标展示数据。
        /// </summary>
        /// <param name="text">面向玩家的目标文字。</param>
        /// <param name="current">当前完成数量。</param>
        /// <param name="required">目标需求数量。</param>
        /// <exception cref="ArgumentOutOfRangeException">当前值为负数或需求数量不是正数时抛出。</exception>
        public TaskWindowObjectiveViewData(string text, int current, int required)
        {
            if (current < 0)
                throw new ArgumentOutOfRangeException(nameof(current), "目标当前进度不能为负数。");
            if (required <= 0)
                throw new ArgumentOutOfRangeException(nameof(required), "目标需求数量必须大于零。");

            Text = text ?? string.Empty;
            Current = current;
            Required = required;
        }

        /// <summary>获取目标文字。</summary>
        public string Text { get; }

        /// <summary>获取当前进度。</summary>
        public int Current { get; }

        /// <summary>获取需求数量。</summary>
        public int Required { get; }
    }

    /// <summary>
    /// 表示奖励区需要展示的一种奖励名称和数量。
    /// </summary>
    public sealed class TaskWindowRewardViewData
    {
        /// <summary>
        /// 创建奖励展示数据。
        /// </summary>
        /// <param name="name">奖励名称。</param>
        /// <param name="amount">奖励数量。</param>
        /// <exception cref="ArgumentOutOfRangeException">奖励数量为负数时抛出。</exception>
        public TaskWindowRewardViewData(string name, int amount)
        {
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount), "奖励展示数量不能为负数。");

            Name = name ?? string.Empty;
            Amount = amount;
        }

        /// <summary>获取奖励名称。</summary>
        public string Name { get; }

        /// <summary>获取奖励数量。</summary>
        public int Amount { get; }
    }

    #endregion
}
