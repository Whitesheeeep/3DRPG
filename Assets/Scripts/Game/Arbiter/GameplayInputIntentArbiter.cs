using WS_Modules.GAS.TAG;
using RPG.Character.State;

namespace RPG.PlayerInputSystem
{
    /// <summary>定义一个无 Unity 生命周期的输入 Intent 仲裁策略。</summary>
    public abstract class GameplayInputIntentArbiter
    {
        #region 仲裁
        /// <summary>由具体游戏规则决定一个请求阶段是否应产生 Intent Tag。</summary>
        protected abstract bool TryResolveIntent(IReadOnlyPlayerInputRequest request,
            PlayerInputRequestStage stage, PlayerStateBlackboard stateBlackboard, out GameplayTag intentTag);

        /// <summary>供仲裁管理器调用派生规则，同时保持规则入口仅对派生类开放。</summary>
        /// <param name="request">当前只读输入 Request。</param>
        /// <param name="stage">当前待仲裁阶段。</param>
        /// <param name="stateBlackboard">玩家状态黑板。</param>
        /// <param name="intentTag">命中时返回应发布的 Intent Tag。</param>
        /// <returns>当前策略接受该阶段并产出有效 Intent 时返回 true。</returns>
        internal bool ResolveIntent(IReadOnlyPlayerInputRequest request, PlayerInputRequestStage stage,
            PlayerStateBlackboard stateBlackboard, out GameplayTag intentTag) =>
            TryResolveIntent(request, stage, stateBlackboard, out intentTag);
        #endregion
    }
}
