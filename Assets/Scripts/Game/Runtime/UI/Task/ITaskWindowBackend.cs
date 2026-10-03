using System;
using RPG.TaskSystem;

namespace RPG.Game.UI.Task
{
    /// <summary>
    /// 为任务面板提供可观察快照、未读确认和追踪意图的后端契约。
    /// </summary>
    public interface ITaskWindowBackend
    {
        /// <summary>
        /// 任务快照实际变化后触发，界面收到通知后重新读取快照。
        /// </summary>
        event Action Changed;

        /// <summary>
        /// 获取当前面板需要显示的任务与追踪状态。
        /// </summary>
        /// <returns>只读任务快照。</returns>
        TaskWindowSnapshot GetSnapshot();

        /// <summary>
        /// 确认玩家已经查看指定任务，用于清除该任务的未读标记。
        /// </summary>
        /// <param name="taskId">已显示在详情区的任务 ID。</param>
        void AcknowledgeTask(TaskId taskId);

        /// <summary>
        /// 请求追踪指定任务。
        /// </summary>
        /// <param name="taskId">要追踪的任务 ID。</param>
        /// <returns>后端接受该请求时返回 true。</returns>
        bool TrySetTrackedTask(TaskId taskId);

        /// <summary>
        /// 请求取消当前任务追踪。
        /// </summary>
        void ClearTrackedTask();
    }
}
