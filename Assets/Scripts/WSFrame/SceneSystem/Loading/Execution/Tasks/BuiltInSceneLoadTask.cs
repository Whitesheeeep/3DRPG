using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using WS_Modules.LogModule;

namespace WS_Modules.SceneModule
{
    /// <summary>加载 Build Settings 中的内置场景，适用于必须保留为构建启动入口的主界面。</summary>
    [CreateAssetMenu(fileName = "BuiltInSceneLoadTask", menuName = "WSFrame/Scene Loading/Load Built-in Scene")]
    public sealed class BuiltInSceneLoadTask : SceneLoadTask
    {
        /// <summary>此节点负责加载并公开目标场景。</summary>
        public override bool ProvidesTargetScene => true;

        /// <summary>观察不可取消的 Unity 场景操作直到完成，再处理调用方取消。</summary>
        /// <param name="context">配置和进度上下文。</param>
        /// <param name="cancellationToken">在操作边界处理的协作令牌。</param>
        /// <returns>场景已加载并登记后的任务。</returns>
        public override async UniTask ExecuteAsync(SceneLoadContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (context.InitializesCurrentScene) { context.SetTargetScene(context.ActiveScene); return; }
            if (!Application.CanStreamedLevelBeLoaded(context.Config.SceneName))
                throw new InvalidOperationException($"[BuiltInSceneLoadTask] 场景未启用在 Build Settings：{context.Config.SceneName}。");
            WSLog.Log($"[BuiltInSceneLoadTask] 开始加载内置场景，sceneId={context.Config.SceneId}。");
            AsyncOperation operation = SceneManager.LoadSceneAsync(context.Config.SceneName, context.Config.LoadMode);
            while (!operation.isDone)
            {
                context.ReportProgress(operation.progress);
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
            Scene scene = SceneManager.GetSceneByName(context.Config.SceneName);
            if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("[BuiltInSceneLoadTask] 目标场景未正确加载。");
            if (SceneManager.GetActiveScene() != scene && !SceneManager.SetActiveScene(scene))
                throw new InvalidOperationException("[BuiltInSceneLoadTask] 目标场景不能设为活动场景。");
            context.SetTargetScene(scene);
            if (context.Config.LoadMode == LoadSceneMode.Single) context.SceneModule.ReleaseReplacedSceneHandles();
            context.ReportProgress(1f);
            WSLog.Log($"[BuiltInSceneLoadTask] 内置场景加载完成，sceneId={context.Config.SceneId}。");
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
