using System.Threading;
using Cysharp.Threading.Tasks;
using RPG.Character;
using UnityEngine;
using WS_Modules.LogModule;
using WS_Modules.SceneModule;

namespace RPG.Game.Loading.Tasks
{
    /// <summary>启动并等待 PlayerController、角色队伍和活动角色完成初始化。</summary>
    [CreateAssetMenu(fileName = "PlayerInitializationSceneLoadTask", menuName = "RPG/Scene Loading/Initialize Player")]
    public sealed class PlayerInitializationSceneLoadTask : SceneLoadTask
    {
        #region 场景依赖

        /// <summary>玩家依赖场景 Player 入口，必须安排在目标场景就绪之后。</summary>
        public override bool RequiresTargetSceneReady => true;

        #endregion

        #region 执行

        /// <summary>等待可由多个调用方共享的玩家初始化，并记录任务开始和完成。</summary>
        /// <param name="context">本次场景准备上下文。</param>
        /// <param name="cancellationToken">取消当前等待，不终止由玩家持有的共享初始化。</param>
        /// <returns>PlayerController 完成收尾后的异步任务。</returns>
        public override async UniTask ExecuteAsync(SceneLoadContext context, CancellationToken cancellationToken)
        {
            if (!context.TargetSceneReady)
                throw new System.InvalidOperationException($"[PlayerInitializationSceneLoadTask] 目标场景尚未就绪，sceneId={context.Config.SceneId}。");
            PlayerController playerController = PlayerController.Instance;
            if (playerController == null)
                throw new MissingReferenceException("[PlayerInitializationSceneLoadTask] PlayerController 单例不存在。");

            WSLog.Log($"[PlayerInitializationSceneLoadTask] 开始等待玩家初始化，sceneId={context.Config.SceneId}。");
            playerController.BeginScenePreparation();
            await playerController.InitializeForSceneAsync(cancellationToken);
            WSLog.Log($"[PlayerInitializationSceneLoadTask] 玩家初始化完成，sceneId={context.Config.SceneId}，player={playerController.name}。");
        }

        #endregion
    }
}
