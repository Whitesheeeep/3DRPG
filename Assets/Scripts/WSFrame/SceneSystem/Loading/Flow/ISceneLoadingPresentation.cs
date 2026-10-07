using System.Threading;
using Cysharp.Threading.Tasks;

namespace WS_Modules.SceneModule
{
    /// <summary>为通用场景流程提供可选的进度、成功与终态展示边界。</summary>
    public interface ISceneLoadingPresentation
    {
        /// <summary>在任务树开始前准备进度展示界面。</summary>
        /// <param name="snapshot">尚未开始任务树的流程快照。</param>
        /// <param name="cancellationToken">在任务树启动前取消展示准备的令牌。</param>
        /// <returns>展示层已准备好接收流程进度后的异步任务。</returns>
        UniTask PrepareAsync(SceneLoadExecutionSnapshot snapshot, CancellationToken cancellationToken);

        /// <summary>把最新流程和任务进度应用到可见加载界面。</summary>
        /// <param name="snapshot">最新不可变状态快照。</param>
        void Present(SceneLoadExecutionSnapshot snapshot);

        /// <summary>展示完整成功状态并完成本次展示收尾。</summary>
        /// <param name="snapshot">根任务及最终校验成功后的快照。</param>
        /// <returns>展示层成功收尾后的异步任务。</returns>
        UniTask CompleteAsync(SceneLoadExecutionSnapshot snapshot);

        /// <summary>在流程异常后显示失败终态。</summary>
        /// <param name="snapshot">失败终态快照。</param>
        void PresentFailure(SceneLoadExecutionSnapshot snapshot);

        /// <summary>显示取消终态，具体交互由展示层决定。</summary>
        /// <param name="snapshot">取消终态快照。</param>
        void PresentCancellation(SceneLoadExecutionSnapshot snapshot);
    }
}
