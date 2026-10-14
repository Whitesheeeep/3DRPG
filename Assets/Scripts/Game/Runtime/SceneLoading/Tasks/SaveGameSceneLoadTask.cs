using System.Threading;
using Cysharp.Threading.Tasks;
using RPG.Character;
using RPG.Game.Saving;
using UnityEngine;
using WS_Modules.LogModule;
using WS_Modules.SceneModule;

namespace RPG.Game.Loading.Tasks
{
    /// <summary>在玩家初始化、窗口预热和出生定位之后保存当前档。</summary>
    [CreateAssetMenu(fileName = "SaveGameSceneLoadTask", menuName = "RPG/Scene Loading/Save Game")]
    public sealed class SaveGameSceneLoadTask : SceneLoadTask
    {
        /// <summary>保存需要目标场景已激活。</summary>
        public override bool RequiresTargetSceneReady => true;
        /// <summary>实际等待保存结束；普通存储失败保留游戏并由设定窗口提示重试。</summary>
        /// <param name="context">场景上下文。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>保存或跳过结束。</returns>
        public override async UniTask ExecuteAsync(SceneLoadContext context, CancellationToken cancellationToken)
        {
            if (!context.TargetSceneReady || PlayerController.Instance == null || !PlayerController.Instance.IsReady)
                throw new System.InvalidOperationException("[SaveGameSceneLoadTask] 必须在目标场景和玩家就绪之后执行。");
            WSLog.Log($"[SaveGameSceneLoadTask] 开始自动保存，sceneId={context.Config.SceneId}。");
            context.ReportProgress(0f);
            var result = await GameArchitecture.Interface.GetSystem<GameSaveSystem>().SaveCurrentAsync(true, cancellationToken);
            if (!result.IsSuccess) WSLog.LogError("[SaveGameSceneLoadTask] 自动保存失败，保留游戏并提示重试：" + result.Message);
            context.ReportProgress(1f);
            WSLog.Log($"[SaveGameSceneLoadTask] 保存步骤完成，success={result.IsSuccess}。");
        }
    }
}
