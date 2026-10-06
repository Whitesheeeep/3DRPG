using WS_Modules.SceneModule;

namespace RPG.Game.Loading
{
    /// <summary>声明场景加载流程对外发布的状态事件。</summary>
    public enum E_SceneLoadingEventType
    {
        /// <summary>流程级快照中任一状态发生变化。</summary>
        SnapshotChanged,
        /// <summary>一个任务引用位置的状态或局部进度发生变化。</summary>
        TaskChanged,
        /// <summary>场景加载与全部后续任务成功结束。</summary>
        Completed,
        /// <summary>流程因校验后执行错误而失败。</summary>
        Failed,
        /// <summary>调用方协作式取消了流程等待。</summary>
        Cancelled
    }

    /// <summary>携带完整场景加载流程快照的状态通知。</summary>
    public sealed class SceneLoadingEventArgs
    {
        /// <summary>获取不可变流程状态快照。</summary>
        public SceneLoadExecutionSnapshot Snapshot { get; }

        /// <summary>创建流程状态通知。</summary>
        /// <param name="snapshot">流程状态快照。</param>
        public SceneLoadingEventArgs(SceneLoadExecutionSnapshot snapshot) => Snapshot = snapshot;
    }

    /// <summary>携带任务树中一个具体引用位置的状态通知。</summary>
    public sealed class SceneLoadingTaskEventArgs
    {
        /// <summary>获取触发通知的任务快照。</summary>
        public SceneLoadTaskSnapshot Task { get; }

        /// <summary>创建任务状态通知。</summary>
        /// <param name="task">任务进度快照。</param>
        public SceneLoadingTaskEventArgs(SceneLoadTaskSnapshot task) => Task = task;
    }
}
