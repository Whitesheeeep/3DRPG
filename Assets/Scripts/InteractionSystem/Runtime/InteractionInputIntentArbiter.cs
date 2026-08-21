using RPG.Character.State;
using RPG.PlayerInputSystem;
using WS_Modules.GAS.Generated;
using WS_Modules.GAS.TAG;

namespace RPG.InteractionSystem
{
    /// <summary>把玩家交互输入请求映射为当前帧 Interaction Intent 的仲裁策略。</summary>
    public sealed class InteractionInputIntentArbiter : GameplayInputIntentArbiter
    {
        #region 仲裁

        /// <summary>将交互 Press 请求转换为对应的 Intent Tag；Release 阶段不产生交互 Intent。</summary>
        /// <param name="request">当前输入请求。</param>
        /// <param name="stage">待仲裁的输入阶段。</param>
        /// <param name="stateBlackboard">玩家状态黑板。</param>
        /// <param name="intentTag">命中时输出的交互 Intent Tag。</param>
        /// <returns>当前请求属于交互输入且应发布 Intent 时返回 true。</returns>
        protected override bool TryResolveIntent(IReadOnlyPlayerInputRequest request,
            PlayerInputRequestStage stage, PlayerStateBlackboard stateBlackboard, out GameplayTag intentTag)
        {
            intentTag = GameplayTag.Empty;
            if (stage != PlayerInputRequestStage.Press) return false;

            switch (request.InputType)
            {
                case PlayerInputType.Interact:
                    intentTag = GameplayTags.Tag_Intent_Interaction_Execute;
                    return true;
                case PlayerInputType.InteractionPrevious:
                    intentTag = GameplayTags.Tag_Intent_Interaction_Previous;
                    return true;
                case PlayerInputType.InteractionNext:
                    intentTag = GameplayTags.Tag_Intent_Interaction_Next;
                    return true;
                default:
                    return false;
            }
        }

        #endregion
    }
}
