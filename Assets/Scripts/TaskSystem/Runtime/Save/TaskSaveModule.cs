using System;
using System.Collections.Generic;
using RPG.SaveSystem;

namespace RPG.TaskSystem
{
    #region 任务存档快照

    /// <summary>
    /// 保存任务 Manager 所需最小事实的版本化快照。
    /// </summary>
    [Serializable]
    public sealed class TaskSaveSnapshot : ISaveModuleSnapshot
    {
        /// <summary>
        /// 活动任务记录列表。
        /// </summary>
        public List<TaskRecordSnapshot> ActiveTasks { get; set; } = new List<TaskRecordSnapshot>();

        /// <summary>
        /// 已完成任务稳定标识列表。
        /// </summary>
        public List<string> CompletedTaskIds { get; set; } = new List<string>();

        /// <summary>
        /// 当前追踪任务标识；没有追踪任务时为空字符串。
        /// </summary>
        public string TrackedTaskId { get; set; } = string.Empty;

        /// <summary>
        /// 尚未确认的活动任务稳定标识列表。
        /// </summary>
        public List<string> UnreadTaskIds { get; set; } = new List<string>();

        /// <summary>
        /// 校验不依赖当前数据库的快照结构约束。
        /// </summary>
        /// <exception cref="InvalidOperationException">快照包含空列表项、非法 ID 或非法数值时抛出。</exception>
        public void ValidateShape()
        {
            if (ActiveTasks == null || CompletedTaskIds == null || UnreadTaskIds == null)
            {
                throw new InvalidOperationException("任务快照的集合字段不能为 null。");
            }

            foreach (TaskRecordSnapshot record in ActiveTasks)
            {
                if (record == null)
                {
                    throw new InvalidOperationException("任务快照包含空活动任务记录。");
                }

                _ = new TaskId(record.TaskId);
                if (record.ObjectiveProgress == null ||
                    (record.State != TaskLifecycleState.InProgress &&
                     record.State != TaskLifecycleState.Claimable))
                {
                    throw new InvalidOperationException("任务快照活动记录的状态或目标列表非法。");
                }

                foreach (TaskObjectiveProgressSnapshot progress in record.ObjectiveProgress)
                {
                    if (progress == null || progress.Required <= 0 || progress.Current < 0)
                    {
                        throw new InvalidOperationException("任务快照目标进度数值非法。");
                    }

                    _ = new ObjectiveId(progress.ObjectiveId);
                }
            }

            foreach (string taskId in CompletedTaskIds)
            {
                _ = new TaskId(taskId);
            }

            if (!string.IsNullOrEmpty(TrackedTaskId))
            {
                _ = new TaskId(TrackedTaskId);
            }

            foreach (string taskId in UnreadTaskIds)
            {
                _ = new TaskId(taskId);
            }
        }
    }

    /// <summary>
    /// 表示任务存档中的单个活动任务记录。
    /// </summary>
    [Serializable]
    public sealed class TaskRecordSnapshot
    {
        /// <summary>
        /// 任务稳定标识字符串。
        /// </summary>
        public string TaskId { get; set; } = string.Empty;

        /// <summary>
        /// 活动任务生命周期状态。
        /// </summary>
        public TaskLifecycleState State { get; set; }

        /// <summary>
        /// 目标进度列表。
        /// </summary>
        public List<TaskObjectiveProgressSnapshot> ObjectiveProgress { get; set; } =
            new List<TaskObjectiveProgressSnapshot>();
    }

    /// <summary>
    /// 表示任务存档中的单个目标进度。
    /// </summary>
    [Serializable]
    public sealed class TaskObjectiveProgressSnapshot
    {
        /// <summary>
        /// 目标稳定标识字符串。
        /// </summary>
        public string ObjectiveId { get; set; } = string.Empty;

        /// <summary>
        /// 当前进度值。
        /// </summary>
        public int Current { get; set; }

        /// <summary>
        /// 目标需求值。
        /// </summary>
        public int Required { get; set; }
    }

    #endregion

    /// <summary>
    /// 将 TaskManager 状态适配到 SaveSystem 的版本化模块契约。
    /// </summary>
    public sealed class TaskSaveModule : SaveModule<TaskSaveSnapshot>
    {
        #region 模块标识

        /// <summary>任务存档模块的稳定 ID。</summary>
        public static readonly SaveModuleId StableModuleId = new SaveModuleId("task");

        #endregion

        #region 依赖字段

        // 依赖字段：任务事实仍由 TaskManager 持有，模块只负责转换快照。
        private readonly TaskManager taskManager;

        #endregion

        /// <summary>
        /// 创建任务存档模块。
        /// </summary>
        /// <param name="taskManager">任务状态 Manager。</param>
        /// <exception cref="ArgumentNullException">Manager 为空时抛出。</exception>
        public TaskSaveModule(TaskManager taskManager)
            : base(
                StableModuleId,
                1,
                SaveMissingModulePolicy.Required)
        {
            this.taskManager = taskManager ?? throw new ArgumentNullException(nameof(taskManager));
        }

        /// <summary>从任务 Manager 采集强类型任务状态快照。</summary>
        /// <returns>当前任务快照。</returns>
        protected override TaskSaveSnapshot CaptureTypedSnapshot() => taskManager.CaptureSnapshot();

        /// <summary>将已校验的任务快照恢复到任务 Manager。</summary>
        /// <param name="snapshot">已校验的当前版本快照。</param>
        protected override void RestoreTypedSnapshot(TaskSaveSnapshot snapshot) =>
            taskManager.RestoreSnapshot(snapshot);

        /// <summary>
        /// 校验任务快照的结构约束，不改变运行时状态。
        /// </summary>
        /// <param name="snapshot">待校验快照。</param>
        protected override void ValidateTypedSnapshot(TaskSaveSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            snapshot.ValidateShape();
        }

    }
}
