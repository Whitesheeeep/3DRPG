namespace RPG.TaskSystem
{
    #region 任务事实事件

    /// <summary>
    /// 描述一个任务已经成功接取的事实及其调用来源。
    /// </summary>
    public struct TaskAcceptedEventArgs
    {
        /// <summary>
        /// 创建任务接取事件数据。
        /// </summary>
        /// <param name="taskId">已接取任务标识。</param>
        /// <param name="source">调用接取入口的来源。</param>
        public TaskAcceptedEventArgs(TaskId taskId, TaskAcceptSource source)
        {
            TaskId = taskId;
            Source = source;
        }

        /// <summary>
        /// 获取已接取任务标识。
        /// </summary>
        public TaskId TaskId { get; }

        /// <summary>
        /// 获取接取调用来源。
        /// </summary>
        public TaskAcceptSource Source { get; }
    }

    /// <summary>
    /// 描述一个当前阶段目标的进度变化。
    /// </summary>
    public struct TaskObjectiveProgressChangedEventArgs
    {
        /// <summary>
        /// 创建目标进度变化事件数据。
        /// </summary>
        /// <param name="taskId">任务标识。</param>
        /// <param name="stageId">当前阶段标识。</param>
        /// <param name="objectiveId">目标标识。</param>
        /// <param name="previousValue">变化前进度。</param>
        /// <param name="currentValue">变化后进度。</param>
        public TaskObjectiveProgressChangedEventArgs(
            TaskId taskId,
            TaskStageId stageId,
            ObjectiveId objectiveId,
            int previousValue,
            int currentValue)
        {
            TaskId = taskId;
            StageId = stageId;
            ObjectiveId = objectiveId;
            PreviousValue = previousValue;
            CurrentValue = currentValue;
        }

        /// <summary>获取任务标识。</summary>
        public TaskId TaskId { get; }

        /// <summary>获取阶段标识。</summary>
        public TaskStageId StageId { get; }

        /// <summary>获取目标标识。</summary>
        public ObjectiveId ObjectiveId { get; }

        /// <summary>获取变化前进度。</summary>
        public int PreviousValue { get; }

        /// <summary>获取变化后进度。</summary>
        public int CurrentValue { get; }
    }

    /// <summary>
    /// 描述任务已经切换到下一阶段的事实。
    /// </summary>
    public struct TaskStageChangedEventArgs
    {
        /// <summary>
        /// 创建阶段切换事件数据。
        /// </summary>
        /// <param name="taskId">任务标识。</param>
        /// <param name="previousStageId">切换前阶段。</param>
        /// <param name="currentStageId">切换后阶段。</param>
        public TaskStageChangedEventArgs(TaskId taskId, TaskStageId previousStageId, TaskStageId currentStageId)
        {
            TaskId = taskId;
            PreviousStageId = previousStageId;
            CurrentStageId = currentStageId;
        }

        /// <summary>获取任务标识。</summary>
        public TaskId TaskId { get; }

        /// <summary>获取切换前阶段。</summary>
        public TaskStageId PreviousStageId { get; }

        /// <summary>获取切换后阶段。</summary>
        public TaskStageId CurrentStageId { get; }
    }

    /// <summary>
    /// 描述任务生命周期由进行中切换到待提交状态。
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

        /// <summary>获取任务标识。</summary>
        public TaskId TaskId { get; }

        /// <summary>获取变化前状态。</summary>
        public TaskLifecycleState PreviousState { get; }

        /// <summary>获取变化后状态。</summary>
        public TaskLifecycleState CurrentState { get; }
    }

    /// <summary>
    /// 描述一项任务的奖励已经可以提交领取。
    /// </summary>
    public struct TaskRewardClaimableEventArgs
    {
        /// <summary>
        /// 创建奖励待提交事件数据。
        /// </summary>
        /// <param name="taskId">任务标识。</param>
        public TaskRewardClaimableEventArgs(TaskId taskId)
        {
            TaskId = taskId;
        }

        /// <summary>获取可领奖任务标识。</summary>
        public TaskId TaskId { get; }
    }

    /// <summary>
    /// 描述一次性任务奖励已经成功领取并完成的事实。
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

        /// <summary>获取已完成任务标识。</summary>
        public TaskId TaskId { get; }
    }

    /// <summary>
    /// 描述当前追踪任务发生变化的事实。
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

        /// <summary>获取变化前追踪任务。</summary>
        public TaskId PreviousTaskId { get; }

        /// <summary>获取变化后追踪任务。</summary>
        public TaskId CurrentTaskId { get; }
    }

    /// <summary>
    /// 描述玩家已经确认查看具体任务并清除了未读状态。
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

        /// <summary>获取已确认查看的任务标识。</summary>
        public TaskId TaskId { get; }
    }

    #endregion
}
