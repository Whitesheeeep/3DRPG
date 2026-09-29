namespace RPG.TaskSystem
{
    /// <summary>
    /// 表示具体任务目标运行时的订阅生命周期。
    /// </summary>
    public interface ITaskObjectiveRuntime
    {
        /// <summary>
        /// 启动该目标所需的类型事件监听；重复调用必须无副作用。
        /// </summary>
        void StartListening();

        /// <summary>
        /// 取消该目标所有事件监听；重复调用必须安全。
        /// </summary>
        void StopListening();
    }
}
