using System;

namespace RPG.TaskSystem
{
    #region 任务事实事件

    /// <summary>
    /// 描述一个任务已经成功接取的事实事件。
    /// </summary>
    public struct TaskAcceptedEventArgs
    {
        /// <summary>
        /// 创建任务接取事件数据。
        /// </summary>
        /// <param name="taskId">已接取任务标识。</param>
        public TaskAcceptedEventArgs(TaskId taskId)
        {
            TaskId = taskId;
        }

        /// <summary>
        /// 获取已接取任务标识。
        /// </summary>
        public TaskId TaskId { get; }
    }

    /// <summary>
    /// 描述一个任务目标进度发生变化的事实事件。
    /// </summary>
    public struct TaskObjectiveProgressChangedEventArgs
    {
        /// <summary>
        /// 创建目标进度变化事件数据。
        /// </summary>
        /// <param name="taskId">任务标识。</param>
        /// <param name="objectiveId">目标标识。</param>
        /// <param name="previousValue">变化前进度。</param>
        /// <param name="currentValue">变化后进度。</param>
        public TaskObjectiveProgressChangedEventArgs(
            TaskId taskId,
            ObjectiveId objectiveId,
            int previousValue,
            int currentValue)
        {
            TaskId = taskId;
            ObjectiveId = objectiveId;
            PreviousValue = previousValue;
            CurrentValue = currentValue;
        }

        /// <summary>
        /// 获取任务标识。
        /// </summary>
        public TaskId TaskId { get; }

        /// <summary>
        /// 获取目标标识。
        /// </summary>
        public ObjectiveId ObjectiveId { get; }

        /// <summary>
        /// 获取变化前进度。
        /// </summary>
        public int PreviousValue { get; }

        /// <summary>
        /// 获取变化后进度。
        /// </summary>
        public int CurrentValue { get; }
    }

    /// <summary>
    /// 描述任务从进行中和可领取之间发生状态变化的事实事件。
    /// </summary>
    public struct TaskStateChangedEventArgs
    {
        /// <summary>
        /// 创建任务状态变化事件数据。
        /// </summary>
        /// <param name="taskId">任务标识。</param>
        /// <param name="previousState">变化前状态。</param>
        /// <param name="currentState">变化后状态。</param>
        public TaskStateChangedEventArgs(
            TaskId taskId,
            TaskLifecycleState previousState,
            TaskLifecycleState currentState)
        {
            TaskId = taskId;
            PreviousState = previousState;
            CurrentState = currentState;
        }

        /// <summary>
        /// 获取任务标识。
        /// </summary>
        public TaskId TaskId { get; }

        /// <summary>
        /// 获取变化前状态。
        /// </summary>
        public TaskLifecycleState PreviousState { get; }

        /// <summary>
        /// 获取变化后状态。
        /// </summary>
        public TaskLifecycleState CurrentState { get; }
    }

    /// <summary>
    /// 描述一次性任务奖励已经成功领取并完成的事实事件。
    /// </summary>
    public struct TaskCompletedEventArgs
    {
        /// <summary>
        /// 创建任务完成事件数据。
        /// </summary>
        /// <param name="taskId">已完成任务标识。</param>
        public TaskCompletedEventArgs(TaskId taskId)
        {
            TaskId = taskId;
        }

        /// <summary>
        /// 获取已完成任务标识。
        /// </summary>
        public TaskId TaskId { get; }
    }

    /// <summary>
    /// 描述当前追踪任务发生变化的事实事件。
    /// </summary>
    public struct TaskTrackedChangedEventArgs
    {
        /// <summary>
        /// 创建追踪任务变化事件数据。
        /// </summary>
        /// <param name="previousTaskId">变化前追踪任务。</param>
        /// <param name="currentTaskId">变化后追踪任务。</param>
        public TaskTrackedChangedEventArgs(TaskId previousTaskId, TaskId currentTaskId)
        {
            PreviousTaskId = previousTaskId;
            CurrentTaskId = currentTaskId;
        }

        /// <summary>
        /// 获取变化前追踪任务。
        /// </summary>
        public TaskId PreviousTaskId { get; }

        /// <summary>
        /// 获取变化后追踪任务。
        /// </summary>
        public TaskId CurrentTaskId { get; }
    }

    /// <summary>
    /// 描述玩家确认查看具体任务的事实事件。
    /// </summary>
    public struct TaskAcknowledgedEventArgs
    {
        /// <summary>
        /// 创建任务确认查看事件数据。
        /// </summary>
        /// <param name="taskId">已经确认查看的任务。</param>
        public TaskAcknowledgedEventArgs(TaskId taskId)
        {
            TaskId = taskId;
        }

        /// <summary>
        /// 获取已经确认查看的任务标识。
        /// </summary>
        public TaskId TaskId { get; }
    }

    #endregion
}
