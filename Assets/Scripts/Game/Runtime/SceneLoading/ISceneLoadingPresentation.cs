using System.Threading;
using Cysharp.Threading.Tasks;
using WS_Modules.SceneModule;

namespace RPG.Game.Loading
{
    /// <summary>隔离场景流程与具体窗口实现的展示生命周期边界。</summary>
    public interface ISceneLoadingPresentation
    {
        /// <summary>打开加载窗口并等待其至少显示一帧。</summary>
        /// <param name="snapshot">尚未开始任务树的流程快照。</param>
        /// <param name="cancellationToken">在任务树启动前取消窗口准备的令牌。</param>
        /// <returns>加载界面已准备好接收进度后的异步任务。</returns>
        UniTask PrepareAsync(SceneLoadExecutionSnapshot snapshot, CancellationToken cancellationToken);

        /// <summary>把最新流程和任务进度应用到可见加载界面。</summary>
        /// <param name="snapshot">最新不可变状态快照。</param>
        void Present(SceneLoadExecutionSnapshot snapshot);

        /// <summary>展示完整成功状态、恢复 HUD 并结束本次遮罩。</summary>
        /// <param name="snapshot">根任务及最终校验成功后的快照。</param>
        /// <returns>展示成功收尾的异步任务。</returns>
        UniTask CompleteAsync(SceneLoadExecutionSnapshot snapshot);

        /// <summary>在流程异常后保留遮罩并显示错误摘要。</summary>
        /// <param name="snapshot">失败终态快照。</param>
        void PresentFailure(SceneLoadExecutionSnapshot snapshot);

        /// <summary>显示取消终态并让使用者明确关闭遮罩。</summary>
        /// <param name="snapshot">取消终态快照。</param>
        void PresentCancellation(SceneLoadExecutionSnapshot snapshot);
    }
}
