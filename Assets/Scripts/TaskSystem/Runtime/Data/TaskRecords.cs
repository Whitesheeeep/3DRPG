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
        /// <summary>任务已接取，当前阶段仍有目标未完成。</summary>
        InProgress = 0,
        /// <summary>所有阶段目标已完成，等待玩家提交领奖。</summary>
        Claimable = 1
    }

    /// <summary>
    /// 保存当前阶段单个目标的整数进度。
    /// </summary>
    public sealed class TaskObjectiveProgress
    {
        private int current;

        /// <summary>
        /// 创建目标进度。
        /// </summary>
        /// <param name="objectiveId">当前阶段内稳定目标标识。</param>
        /// <param name="required">目标需求数量。</param>
        /// <param name="current">初始进度。</param>
        /// <exception cref="ArgumentException">目标 ID 非法时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException">需求或当前值非法时抛出。</exception>
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
        /// 判断进度是否达到目标需求。
        /// </summary>
        public bool IsComplete => current >= Required;

        /// <summary>
        /// 设置状态型目标进度并限制在需求上限内。
        /// </summary>
        /// <param name="value">新的非负进度。</param>
        /// <exception cref="ArgumentOutOfRangeException">进度为负数时抛出。</exception>
        internal void SetCurrent(int value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "目标当前进度不能为负数。");
            }

            current = Math.Min(value, Required);
        }

        /// <summary>
        /// 累加事件型目标进度并限制在需求上限内。
        /// </summary>
        /// <param name="delta">非负增加量。</param>
        /// <exception cref="ArgumentOutOfRangeException">增加量为负数时抛出。</exception>
        internal void AddCurrent(int delta)
        {
            if (delta < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(delta), "累计目标增加量不能为负数。");
            }

            current = (int)Math.Min((long)current + delta, Required);
        }
    }

    /// <summary>
    /// 表示玩家已接取任务的可存档状态数据，仅保存当前阶段进度。
    /// </summary>
    public sealed class TaskRecord
    {
        // key：当前阶段内的 ObjectiveId；value：该目标的需求和已累计进度。
        private readonly Dictionary<ObjectiveId, TaskObjectiveProgress> objectiveProgressByIdMap =
            new Dictionary<ObjectiveId, TaskObjectiveProgress>();

        /// <summary>
        /// 根据任务第一阶段创建初始活动记录。
        /// </summary>
        /// <param name="definition">已通过配置校验的任务资产。</param>
        /// <exception cref="ArgumentNullException">定义为空时抛出。</exception>
        public TaskRecord(TaskDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            TaskId = definition.TaskId;
            State = TaskLifecycleState.InProgress;
            ActivateStage(definition.Stages[0]);
        }

        /// <summary>
        /// 获取任务稳定标识。
        /// </summary>
        public TaskId TaskId { get; }

        /// <summary>
        /// 获取当前阶段稳定标识。
        /// </summary>
        public TaskStageId CurrentStageId { get; private set; }

        /// <summary>
        /// 获取任务生命周期状态。
        /// </summary>
        public TaskLifecycleState State { get; private set; }

        /// <summary>
        /// 获取当前阶段目标进度。
        /// </summary>
        public IReadOnlyCollection<TaskObjectiveProgress> ObjectiveProgress => objectiveProgressByIdMap.Values;

        /// <summary>
        /// 尝试获取当前阶段指定目标进度。
        /// </summary>
        /// <param name="objectiveId">目标标识。</param>
        /// <param name="progress">找到的进度记录。</param>
        /// <returns>当前阶段包含该目标时返回 true。</returns>
        public bool TryGetProgress(ObjectiveId objectiveId, out TaskObjectiveProgress progress) =>
            objectiveProgressByIdMap.TryGetValue(objectiveId, out progress);

        /// <summary>
        /// 判断当前阶段所有目标是否都已完成。
        /// </summary>
        /// <returns>全部完成时返回 true。</returns>
        public bool IsCurrentStageComplete()
        {
            foreach (TaskObjectiveProgress progress in objectiveProgressByIdMap.Values)
            {
                if (!progress.IsComplete)
                {
                    return false;
                }
            }

            return objectiveProgressByIdMap.Count > 0;
        }

        /// <summary>
        /// 设置当前阶段目标并报告进度是否变化。
        /// </summary>
        /// <param name="objectiveId">目标标识。</param>
        /// <param name="value">新的非负进度。</param>
        /// <returns>目标进度实际变化时返回 true。</returns>
        internal bool SetObjectiveProgress(ObjectiveId objectiveId, int value)
        {
            if (!objectiveProgressByIdMap.TryGetValue(objectiveId, out TaskObjectiveProgress progress))
            {
                return false;
            }

            int previous = progress.Current;
            progress.SetCurrent(value);
            return previous != progress.Current;
        }

        /// <summary>
        /// 累加当前阶段目标并报告进度是否变化。
        /// </summary>
        /// <param name="objectiveId">目标标识。</param>
        /// <param name="delta">非负增加量。</param>
        /// <returns>目标进度实际变化时返回 true。</returns>
        internal bool AddObjectiveProgress(ObjectiveId objectiveId, int delta)
        {
            if (!objectiveProgressByIdMap.TryGetValue(objectiveId, out TaskObjectiveProgress progress))
            {
                return false;
            }

            int previous = progress.Current;
            progress.AddCurrent(delta);
            return previous != progress.Current;
        }

        /// <summary>
        /// 清空旧阶段进度并初始化新阶段的目标集合。
        /// </summary>
        /// <param name="stage">要激活的阶段定义。</param>
        internal void ActivateStage(TaskStageDefinition stage)
        {
            CurrentStageId = stage.StageId;
            State = TaskLifecycleState.InProgress;
            objectiveProgressByIdMap.Clear();
            for (int index = 0; index < stage.Objectives.Count; index++)
            {
                TaskObjectiveDefinition objective = stage.Objectives[index];
                objectiveProgressByIdMap.Add(
                    objective.ObjectiveId,
                    new TaskObjectiveProgress(objective.ObjectiveId, objective.Required));
            }
        }

        /// <summary>
        /// 恢复经过快照校验的生命周期状态。
        /// </summary>
        /// <param name="state">存档中的状态。</param>
        internal void SetState(TaskLifecycleState state)
        {
            State = state;
        }
    }

    #endregion
}
