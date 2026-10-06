using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using WS_Modules.LogModule;

namespace WS_Modules.SceneModule
{
    /// <summary>把 Addressables 场景激活作为组合任务树中的一个叶子节点。</summary>
    [CreateAssetMenu(fileName = "AddressableSceneLoadTask", menuName = "WSFrame/Scene Loading/Load Scene")]
    public sealed class AddressableSceneLoadTask : SceneLoadTask
    {
        #region 加载依赖

        /// <summary>场景节点通过上下文切换地址；运行时组合树只需保存此类型资产。</summary>
        public override bool RequiresTargetSceneReady => false;

        #endregion

        #region 执行

        /// <summary>加载并验证本次配置场景；初始化当前场景时只登记现有活动场景。</summary>
        /// <param name="context">本次流程上下文。</param>
        /// <param name="cancellationToken">协作式取消令牌。</param>
        /// <returns>场景激活和验证完成后的异步任务。</returns>
        public override async UniTask ExecuteAsync(SceneLoadContext context, CancellationToken cancellationToken)
        {
            if (context.InitializesCurrentScene)
            {
                Scene currentScene = context.ActiveScene;
                if (!currentScene.IsValid() || currentScene.name != context.Config.SceneName)
                    throw new System.InvalidOperationException(
                        $"[AddressableSceneLoadTask] 当前场景与配置不一致，configured={context.Config.SceneName}，current={currentScene.name}。");
                context.SetTargetScene(currentScene);
                WSLog.Log($"[AddressableSceneLoadTask] Play Mode 初始化当前场景，scene={currentScene.name}。");
                return;
            }

            Scene loadedScene = await context.SceneModule.LoadAsync(
                context.Config, makeActive: true, cancellationToken, context.ReportProgress);
            context.SetTargetScene(loadedScene);
        }

        #endregion
    }
}
