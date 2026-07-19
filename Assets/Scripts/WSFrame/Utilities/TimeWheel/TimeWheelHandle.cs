namespace WS_Modules.Utilities
{
    /// <summary>
    /// 业务层持有的值类型句柄，不直接引用可被对象池复用的任务数据。
    /// </summary>
    public readonly struct TimeWheelHandle
    {
        internal readonly long SchedulerId;
        internal readonly long TaskId;
        internal readonly int Version;

        internal TimeWheelHandle(long schedulerId, long taskId, int version)
        {
            SchedulerId = schedulerId;
            TaskId = taskId;
            Version = version;
        }

        public bool IsValid => SchedulerId != IdGenerator.InvalidId && TaskId != IdGenerator.InvalidId;

        internal bool BelongsTo(long schedulerId)
        {
            return IsValid && SchedulerId == schedulerId;
        }
    }
}
