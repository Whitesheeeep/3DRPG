namespace RPG.TaskSystemNS
{
    /// <summary>
    /// 为目标 Runtime 提供受限的任务身份与进度访问；进度写入由 TaskRuntime 校验。
    /// </summary>
    public interface ITaskObjectiveRuntimeContext
    {
        /// <summary>
        /// 获取所属任务标识。
        /// </summary>
        TaskId TaskId { get; }

        /// <summary>
        /// 获取目标标识。
        /// </summary>
        ObjectiveId ObjectiveId { get; }

        /// <summary>
        /// 获取所属阶段标识；原阶段实例失效后由 TaskRuntime 忽略迟到写入。
        /// </summary>
        TaskStageId StageId { get; }

        /// <summary>
        /// 获取目标需求数量。
        /// </summary>
        int Required { get; }

        /// <summary>
        /// 获取当前进度。
        /// </summary>
        int Current { get; }

        /// <summary>
        /// 累加事件型目标进度。
        /// </summary>
        /// <param name="delta">非负增加量。</param>
        void AddProgress(int delta);

        /// <summary>
        /// 设置状态型目标当前值。
        /// </summary>
        /// <param name="value">新的非负当前值。</param>
        void SetProgress(int value);
    }
}
