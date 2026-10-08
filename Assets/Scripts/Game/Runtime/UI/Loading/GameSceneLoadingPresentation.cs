using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using RPG.Game.UI;
using WS_Modules.LogModule;
using WS_Modules.SceneModule;
using WS_Modules.UIModule;

namespace RPG.Game.UI.Loading
{
    /// <summary>把场景加载系统的展示接口连接到 WSFrame 窗口生命周期。</summary>
    public sealed class GameSceneLoadingPresentation : ISceneLoadingPresentation
    {
        #region 展示依赖字段

        // 窗口实例由 UIManager 持有；适配器只缓存当前流程使用的呈现对象。
        private LoadingWindow loadingWindow;

        #endregion

        #region 窗口展示生命周期

        /// <summary>等待 UIManager 就绪，弹出全屏加载窗口并至少让遮罩绘制一帧。</summary>
        /// <param name="snapshot">尚未开始任务树的流程快照。</param>
        /// <param name="cancellationToken">窗口准备阶段协作式取消令牌。</param>
        /// <returns>加载界面可见后的异步任务。</returns>
        /// <exception cref="InvalidOperationException">UIManager 无法打开 LoadingWindow 时抛出。</exception>
        /// <exception cref="OperationCanceledException">调用方在窗口准备期间取消时抛出。</exception>
        public async UniTask PrepareAsync(
            SceneLoadExecutionSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            await UniTask.WaitUntil(() => UIManager.Instance.IsInitialized)
                .AttachExternalCancellation(cancellationToken);
            LoadingWindow openedWindow = await UIManager.Instance.PopUpWindowAsync<LoadingWindow>();
            if (openedWindow == null)
                throw new InvalidOperationException("[GameSceneLoadingPresentation] UIManager 未能打开 LoadingWindow。");

            loadingWindow = openedWindow;
            loadingWindow.Present(snapshot);
            // 加载任务树可能立即进入耗时工作；先让 Unity 完成一次全屏遮罩绘制。
            await UniTask.NextFrame(cancellationToken);
            WSLog.Log($"[GameSceneLoadingPresentation] LoadingWindow 已显示，sceneId={snapshot.SceneId}，flowId={snapshot.FlowId}。");
        }

        /// <summary>把最新不可变进度快照写入当前加载窗口。</summary>
        /// <param name="snapshot">当前流程状态快照。</param>
        public void Present(SceneLoadExecutionSnapshot snapshot)
        {
            if (loadingWindow == null) return;
            loadingWindow.Present(snapshot);
        }

        /// <summary>展示成功终态并关闭加载遮罩。</summary>
        /// <param name="snapshot">所有加载任务均已完成的终态快照。</param>
        /// <returns>成功状态可见且加载窗口关闭后的异步任务。</returns>
        public async UniTask CompleteAsync(SceneLoadExecutionSnapshot snapshot)
        {
            if (loadingWindow != null)
            {
                loadingWindow.Present(snapshot);
                // 成功文案保持短暂可见，避免 100% 只出现一个渲染帧。
                await UniTask.DelayFrame(30, PlayerLoopTiming.Update);
                await UIManager.Instance.HideWindowAsync<LoadingWindow>();
                loadingWindow = null;
            }
            WSLog.Log($"[GameSceneLoadingPresentation] 加载成功展示已结束，sceneId={snapshot.SceneId}。");
        }

        /// <summary>显示失败原因并保留全屏遮罩，等待用户明确关闭错误提示。</summary>
        /// <param name="snapshot">失败流程的终态快照。</param>
        public void PresentFailure(SceneLoadExecutionSnapshot snapshot)
        {
            if (loadingWindow == null)
            {
                WSLog.LogError($"[GameSceneLoadingPresentation] 加载失败时 LoadingWindow 不可用，sceneId={snapshot.SceneId}，error={snapshot.FailureMessage}");
                return;
            }

            loadingWindow.Present(snapshot);
        }

        /// <summary>显示取消原因并保留全屏遮罩，等待用户明确关闭提示。</summary>
        /// <param name="snapshot">取消流程的终态快照。</param>
        public void PresentCancellation(SceneLoadExecutionSnapshot snapshot)
        {
            if (loadingWindow == null)
            {
                WSLog.LogWarning($"[GameSceneLoadingPresentation] 取消时 LoadingWindow 不可用，sceneId={snapshot.SceneId}。");
                return;
            }

            loadingWindow.Present(snapshot);
        }

        #endregion
    }
}
