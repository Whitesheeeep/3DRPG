using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using RPG.Character;
using RPG.Game.Loading;
using UnityEngine;
using UnityEngine.SceneManagement;
using WS_Modules.LogModule;
using WS_Modules.SceneModule;

namespace RPG.Game.Loading.Tasks
{
    /// <summary>在目标场景中取得唯一出生点并将已初始化玩家定位到该处。</summary>
    [CreateAssetMenu(fileName = "PlayerSpawnSceneLoadTask", menuName = "RPG/Scene Loading/Place Player At Spawn")]
    public sealed class PlayerSpawnSceneLoadTask : SceneLoadTask
    {
        #region 场景依赖

        /// <summary>出生点仅在目标场景激活后可查询。</summary>
        public override bool RequiresTargetSceneReady => true;

        #endregion

        #region 执行

        /// <summary>通过目标 Scene 根节点的组件查询取得唯一出生点并应用世界位姿。</summary>
        /// <param name="context">本次场景流程上下文。</param>
        /// <param name="cancellationToken">任务取消令牌。</param>
        /// <returns>玩家位姿更新完成后的异步任务。</returns>
        public override UniTask ExecuteAsync(SceneLoadContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!context.TargetSceneReady)
            {
                WSLog.LogError($"[PlayerSpawnSceneLoadTask] 目标场景尚未就绪，sceneId={context.Config.SceneId}。");
                throw new System.InvalidOperationException("[PlayerSpawnSceneLoadTask] 目标场景尚未就绪。");
            }

            PlayerController playerController = PlayerController.Instance;
            if (playerController == null || !playerController.IsReady)
            {
                WSLog.LogError($"[PlayerSpawnSceneLoadTask] PlayerController 或角色队伍尚未就绪，sceneId={context.Config.SceneId}。");
                throw new MissingReferenceException("[PlayerSpawnSceneLoadTask] PlayerController 或已就绪角色队伍不存在。");
            }

            List<PlayerSpawnPoint> spawnPoints = CollectSpawnPoints(context.ActiveScene);
            if (spawnPoints.Count != 1)
            {
                WSLog.LogError($"[PlayerSpawnSceneLoadTask] 目标场景必须恰好包含一个出生点，scene={context.ActiveScene.name}，count={spawnPoints.Count}。");
                throw new System.InvalidOperationException(
                    $"[PlayerSpawnSceneLoadTask] 目标场景必须恰好包含一个 PlayerSpawnPoint，scene={context.ActiveScene.name}，count={spawnPoints.Count}。");
            }

            PlayerSpawnPoint spawnPoint = spawnPoints[0];
            Vector3 horizontalForward = Vector3.ProjectOnPlane(spawnPoint.WorldForward, Vector3.up);
            if (horizontalForward.sqrMagnitude <= 0.0001f)
            {
                WSLog.LogError($"[PlayerSpawnSceneLoadTask] 出生点 +Z 方向没有有效水平投影，spawnPoint={spawnPoint.name}。");
                throw new System.InvalidOperationException(
                    $"[PlayerSpawnSceneLoadTask] 出生点 +Z 方向没有有效水平投影，spawnPoint={spawnPoint.name}。");
            }

            playerController.ApplySpawnPose(spawnPoint.WorldPosition, horizontalForward);
            WSLog.Log($"[PlayerSpawnSceneLoadTask] 玩家已定位到场景出生点，sceneId={context.Config.SceneId}，spawnPoint={spawnPoint.name}。");
            return UniTask.CompletedTask;
        }

        /// <summary>只在目标 Scene 的根节点层级查询出生点，避免误取其他加载场景中的组件。</summary>
        /// <param name="scene">通过 SceneLoadingContext 校验并激活的目标场景。</param>
        /// <returns>该 Scene 中的所有出生点组件。</returns>
        private static List<PlayerSpawnPoint> CollectSpawnPoints(Scene scene)
        {
            var spawnPoints = new List<PlayerSpawnPoint>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                spawnPoints.AddRange(root.GetComponentsInChildren<PlayerSpawnPoint>(includeInactive: true));
            }
            return spawnPoints;
        }

        #endregion
    }
}
