using System.Collections.Generic;
using RPG.CurrencySystemNS;

namespace RPG.TaskSystem
{
    #region 查询与命令结果

    /// <summary>
    /// 表示任务对玩家当前可见的接取状态。
    /// </summary>
    public enum TaskAvailabilityStatus
    {
        /// <summary>任务 ID 不存在。</summary>
        NotFound = 0,
        /// <summary>至少一个接取条件未满足。</summary>
        Locked = 1,
        /// <summary>满足接取条件且尚未接取或完成。</summary>
        Available = 2,
        /// <summary>任务当前处于活动集合中。</summary>
        Active = 3,
        /// <summary>任务已经完成。</summary>
        Completed = 4
    }

    /// <summary>
    /// 标识任务接取命令的调用来源，仅用于诊断和事件上下文。
    /// </summary>
    public enum TaskAcceptSource
    {
        /// <summary>来源尚未指定。</summary>
        Unknown = 0,
        /// <summary>玩家主动操作。</summary>
        Player = 1,
        /// <summary>NPC 交互。</summary>
        Npc = 2,
        /// <summary>对话动作。</summary>
        Dialogue = 3,
        /// <summary>场景或剧情触发器。</summary>
        Trigger = 4,
        /// <summary>剧情系统。</summary>
        Story = 5,
        /// <summary>编辑器手动测试。</summary>
        Test = 6
    }

    /// <summary>
    /// 表示一个接取条件未满足的结构化原因。
    /// </summary>
    public sealed class TaskAvailabilityReason
    {
        /// <summary>
        /// 创建条件失败原因。
        /// </summary>
        /// <param name="kind">失败原因类型。</param>
        /// <param name="relatedTaskId">关联任务；没有关联任务时为默认值。</param>
        /// <param name="message">可供界面展示的说明。</param>
        public TaskAvailabilityReason(TaskAvailabilityReasonKind kind, TaskId relatedTaskId, string message)
        {
            Kind = kind;
            RelatedTaskId = relatedTaskId;
            Message = message ?? string.Empty;
        }

        /// <summary>
        /// 获取原因类型。
        /// </summary>
        public TaskAvailabilityReasonKind Kind { get; }

        /// <summary>
        /// 获取关联任务标识。
        /// </summary>
        public TaskId RelatedTaskId { get; }

        /// <summary>
        /// 获取说明文本。
        /// </summary>
        public string Message { get; }
    }

    /// <summary>
    /// 接取资格失败的原因种类。
    /// </summary>
    public enum TaskAvailabilityReasonKind
    {
        /// <summary>前置任务尚未完成。</summary>
        PrerequisiteNotCompleted = 0
    }

    /// <summary>
    /// 表示任务资格查询状态和可读原因。
    /// </summary>
    public sealed class TaskAvailabilityResult
    {
        /// <summary>
        /// 创建资格查询结果。
        /// </summary>
        /// <param name="status">当前任务接取状态。</param>
        /// <param name="reasons">未满足条件的原因。</param>
        public TaskAvailabilityResult(
            TaskAvailabilityStatus status,
            IReadOnlyList<TaskAvailabilityReason> reasons)
        {
            Status = status;
            Reasons = reasons ?? new TaskAvailabilityReason[0];
        }

        /// <summary>
        /// 获取当前接取状态。
        /// </summary>
        public TaskAvailabilityStatus Status { get; }

        /// <summary>
        /// 获取条件未满足的原因列表。
        /// </summary>
        public IReadOnlyList<TaskAvailabilityReason> Reasons { get; }
    }

    /// <summary>
    /// 表示任务命令被拒绝或完成的原因。
    /// </summary>
    public enum TaskCommandFailure
    {
        /// <summary>命令成功。</summary>
        None = 0,
        /// <summary>任务定义不存在。</summary>
        TaskNotFound = 1,
        /// <summary>任务已经处于活动状态。</summary>
        AlreadyActive = 2,
        /// <summary>任务已经完成。</summary>
        AlreadyCompleted = 3,
        /// <summary>接取条件未满足。</summary>
        ConditionNotMet = 4,
        /// <summary>任务还未达到领奖状态。</summary>
        NotClaimable = 5,
        /// <summary>货币奖励预检或发放被拒绝。</summary>
        RewardRejected = 6,
        /// <summary>该任务已有一次领奖流程正在执行。</summary>
        RewardClaimInProgress = 7
    }

    /// <summary>
    /// 表示一次接取任务命令的结果。
    /// </summary>
    public sealed class TaskAcceptResult
    {
        /// <summary>
        /// 创建接取结果。
        /// </summary>
        /// <param name="failure">拒绝原因；成功时为 None。</param>
        /// <param name="availability">本次接取使用的资格查询结果。</param>
        public TaskAcceptResult(TaskCommandFailure failure, TaskAvailabilityResult availability)
        {
            Failure = failure;
            Availability = availability;
        }

        /// <summary>
        /// 获取接取是否成功。
        /// </summary>
        public bool Succeeded => Failure == TaskCommandFailure.None;

        /// <summary>
        /// 获取接取拒绝原因。
        /// </summary>
        public TaskCommandFailure Failure { get; }

        /// <summary>
        /// 获取接取时的资格快照。
        /// </summary>
        public TaskAvailabilityResult Availability { get; }
    }

    /// <summary>
    /// 表示一次手动领奖命令的结果。
    /// </summary>
    public sealed class TaskClaimResult
    {
        /// <summary>
        /// 创建领奖结果。
        /// </summary>
        /// <param name="failure">拒绝原因；成功时为 None。</param>
        /// <param name="currencyStatus">货币钱包操作状态。</param>
        public TaskClaimResult(TaskCommandFailure failure, CurrencyOperationStatus? currencyStatus)
        {
            Failure = failure;
            CurrencyStatus = currencyStatus;
        }

        /// <summary>
        /// 获取领奖是否成功。
        /// </summary>
        public bool Succeeded => Failure == TaskCommandFailure.None;

        /// <summary>
        /// 获取领奖拒绝原因。
        /// </summary>
        public TaskCommandFailure Failure { get; }

        /// <summary>
        /// 获取货币预检或发放状态。
        /// </summary>
        public CurrencyOperationStatus? CurrencyStatus { get; }
    }

    #endregion
}
