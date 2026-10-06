using System.Threading;
using Cysharp.Threading.Tasks;
using RPG.Game.UI;
using UnityEngine;
using WS_Modules.LogModule;
using WS_Modules.SceneModule;

namespace RPG.Game.Loading.Tasks
{
    /// <summary>等待项目常驻窗口预加载服务完成被配置的预加载任务。</summary>
    [CreateAssetMenu(fileName = "WindowPreloadSceneLoadTask", menuName = "RPG/Scene Loading/Preload Windows")]
    public sealed class WindowPreloadSceneLoadTask : SceneLoadTask
    {
        #region 场景依赖

        /// <summary>配置任务可在目标场景加载期间与场景任务并行。</summary>
        public override bool RequiresTargetSceneReady => false;

        #endregion

        #region 执行

        /// <summary>重用项目窗口预加载共享任务并等待其结果。</summary>
        /// <param name="context">本次流程上下文。</param>
        /// <param name="cancellationToken">只取消当前等待，不重置共享预加载状态。</param>
        /// <returns>所有配置窗口完成初始化后的异步任务。</returns>
        public override async UniTask ExecuteAsync(SceneLoadContext context, CancellationToken cancellationToken)
        {
            GameWindowPreloadService service = GameWindowPreloadService.Instance;
            if (service == null)
                throw new MissingReferenceException("[WindowPreloadSceneLoadTask] GameWindowPreloadService 单例不存在。");

            WSLog.Log($"[WindowPreloadSceneLoadTask] 开始等待窗口预加载，sceneId={context.Config.SceneId}。");
            service.PreloadProgressChanged += context.ReportProgress;
            context.ReportProgress(service.PreloadProgress);
            try
            {
                await service.PrepareAsync().AttachExternalCancellation(cancellationToken);
            }
            finally
            {
                service.PreloadProgressChanged -= context.ReportProgress;
            }
            WSLog.Log($"[WindowPreloadSceneLoadTask] 窗口预加载完成，sceneId={context.Config.SceneId}。");
        }

        #endregion
    }
}
