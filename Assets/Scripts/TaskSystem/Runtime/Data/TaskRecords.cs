using System;
using System.Collections.Generic;

namespace RPG.TaskSystem
{
    #region 状态与进度

    /// <summary>
    /// 表示活动任务可以持久化的生命周期状态。
    /// </summary>
    public enum TaskLifecycleState
    {
        /// <summary>任务已接取，至少一个目标尚未完成。</summary>
        InProgress = 0,

        /// <summary>所有目标当前均已完成，等待领奖。</summary>
        Claimable = 1
    }

    /// <summary>
    /// 保存单个任务目标的当前整数进度。
    /// </summary>
    public sealed class TaskObjectiveProgress
    {
        private int current;

        /// <summary>
        /// 创建一个目标进度对象。
        /// </summary>
        /// <param name="objectiveId">目标稳定标识。</param>
        /// <param name="required">目标完成所需数量。</param>
        /// <param name="current">初始当前值。</param>
        /// <exception cref="ArgumentException">目标标识无效时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException">数量范围非法时抛出。</exception>
        public TaskObjectiveProgress(ObjectiveId objectiveId, int required, int current = 0)
        {
            if (!objectiveId.IsValid)
            {
                throw new ArgumentException("目标进度必须使用有效 ObjectiveId。", nameof(objectiveId));
            }

            if (required <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(required), "目标需求必须大于零。");
            }

            ObjectiveId = objectiveId;
            Required = required;
            SetCurrent(current);
        }

        /// <summary>
        /// 获取目标标识。
        /// </summary>
        public ObjectiveId ObjectiveId { get; }

        /// <summary>
        /// 获取目标需求数量。
        /// </summary>
        public int Required { get; }

        /// <summary>
        /// 获取当前进度。
        /// </summary>
        public int Current => current;

        /// <summary>
        /// 获取当前进度是否已经达到需求。
        /// </summary>
        public bool IsComplete => current >= Required;

        /// <summary>
        /// 设置状态型目标的当前值，并将其限制在合法范围内。
        /// </summary>
        /// <param name="value">新的当前值。</param>
        internal void SetCurrent(int value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "目标当前进度不能为负数。");
            }

            current = Math.Min(value, Required);
        }

        /// <summary>
        /// 累加事件型目标的进度，并限制到需求上限。
        /// </summary>
        /// <param name="delta">本次增加量。</param>
        internal void AddCurrent(int delta)
        {
            if (delta < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(delta), "累计目标增加量不能为负数。");
            }

            SetCurrent((long)current + delta > int.MaxValue ? Required : current + delta);
        }
    }

    /// <summary>
    /// 表示一个已经接取的运行时任务记录，不包含配置资产引用和事件句柄。
    /// </summary>
    public sealed class TaskRecord
    {
        private readonly Dictionary<ObjectiveId, TaskObjectiveProgress> progressById;

        /// <summary>
        /// 按任务定义创建初始运行时记录。
        /// </summary>
        /// <param name="definition">任务静态定义。</param>
        /// <exception cref="ArgumentNullException">定义为空时抛出。</exception>
        public TaskRecord(TaskDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            definition.Validate();
            TaskId = definition.TaskId;
            State = TaskLifecycleState.InProgress;
            progressById = new Dictionary<ObjectiveId, TaskObjectiveProgress>();
            for (int index = 0; index < definition.Objectives.Count; index++)
            {
                TaskObjectiveDefinition objective = definition.Objectives[index];
                progressById.Add(
                    objective.ObjectiveId,
                    new TaskObjectiveProgress(objective.ObjectiveId, objective.Required));
            }
        }

        /// <summary>
        /// 获取任务稳定标识。
        /// </summary>
        public TaskId TaskId { get; }

        /// <summary>
        /// 获取或设置任务生命周期状态。
        /// </summary>
        public TaskLifecycleState State { get; private set; }

        /// <summary>
        /// 获取所有目标进度的只读枚举。
        /// </summary>
        public IEnumerable<TaskObjectiveProgress> ObjectiveProgress => progressById.Values;

        /// <summary>
        /// 尝试获取指定目标进度。
        /// </summary>
        /// <param name="objectiveId">目标标识。</param>
        /// <param name="progress">目标进度。</param>
        /// <returns>找到目标时返回 true。</returns>
        public bool TryGetProgress(ObjectiveId objectiveId, out TaskObjectiveProgress progress) =>
            progressById.TryGetValue(objectiveId, out progress);

        /// <summary>
        /// 更新目标当前值并重新计算任务状态。
        /// </summary>
        /// <param name="objectiveId">目标标识。</param>
        /// <param name="value">目标当前值。</param>
        /// <returns>值或任务状态发生变化时返回 true。</returns>
        internal bool SetObjectiveProgress(ObjectiveId objectiveId, int value)
        {
            if (!progressById.TryGetValue(objectiveId, out TaskObjectiveProgress progress))
            {
                return false;
            }

            int previous = progress.Current;
            TaskLifecycleState previousState = State;
            progress.SetCurrent(value);
            RefreshState();
            return previous != progress.Current || previousState != State;
        }

        /// <summary>
        /// 累加目标事件进度并重新计算任务状态。
        /// </summary>
        /// <param name="objectiveId">目标标识。</param>
        /// <param name="delta">增加量。</param>
        /// <returns>值或任务状态发生变化时返回 true。</returns>
        internal bool AddObjectiveProgress(ObjectiveId objectiveId, int delta)
        {
            if (!progressById.TryGetValue(objectiveId, out TaskObjectiveProgress progress))
            {
                return false;
            }

            int previous = progress.Current;
            TaskLifecycleState previousState = State;
            progress.AddCurrent(delta);
            RefreshState();
            return previous != progress.Current || previousState != State;
        }

        /// <summary>
        /// 将已校验的任务状态设置为指定值。
        /// </summary>
        /// <param name="state">新的生命周期状态。</param>
        internal void SetState(TaskLifecycleState state)
        {
            State = state;
        }

        /// <summary>
        /// 根据全部目标当前值刷新任务状态；Live 目标允许从 Claimable 回退。
        /// </summary>
        private void RefreshState()
        {
            bool allComplete = true;
            foreach (TaskObjectiveProgress progress in progressById.Values)
            {
                if (!progress.IsComplete)
                {
                    allComplete = false;
                    break;
                }
            }

            State = allComplete ? TaskLifecycleState.Claimable : TaskLifecycleState.InProgress;
        }
    }

    #endregion
}
