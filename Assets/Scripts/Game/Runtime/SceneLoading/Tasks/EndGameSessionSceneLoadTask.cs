using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using WS_Modules.SceneModule;

namespace RPG.Game.Loading.Tasks
{
    /// <summary>GameStart 已加载后清理跨场景 Player、窗口及旧业务会话。</summary>
    [CreateAssetMenu(fileName = "EndGameSessionSceneLoadTask", menuName = "RPG/Scene Loading/End Game Session")]
    public sealed class EndGameSessionSceneLoadTask : SceneLoadTask
    {
        /// <summary>仅在主界面目标场景已就绪时开始清理。</summary>
        public override bool RequiresTargetSceneReady => true;
        /// <summary>等待销毁和业务重置完成，完成事件之后才允许重新选档。</summary>
        /// <param name="context">场景上下文。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>清理完成。</returns>
        public override async UniTask ExecuteAsync(SceneLoadContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!context.TargetSceneReady || context.Config.SceneId != Saving.GameSaveSystem.TitleSceneId)
                throw new System.InvalidOperationException("[EndGameSessionSceneLoadTask] 只能在主界面场景就绪后清理会话。");
            // 清理一旦开始必须完整结束，不能因中途取消留下已销毁 Player 的半个会话。
            await GameArchitecture.Interface.GetSystem<GameSceneFlowSystem>().EndSessionForTitleAsync();
            context.ReportProgress(1f);
        }
    }
}
