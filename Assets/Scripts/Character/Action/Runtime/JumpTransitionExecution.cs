using System;
using RPG.PlayerInputSystem;
using WS_Modules.GAS.AbilitySystemComponent;

namespace RPG.Character
{
    /// <summary>在 Grounded Jump 窗口中先预检 Locomotion，再取消当前 FullBody Ability。</summary>
    internal sealed class JumpTransitionExecution
    {
        // 依赖字段：ASC 只负责取消 Runtime，查询服务只负责复用 Locomotion 环境条件。
        private readonly GameplayAbilitySystemComponent abilitySystemComponent;
        private readonly GroundedJumpTransitionQueryService jumpTransitionQueryService;

        /// <summary>创建只负责取消 GA、不直接切换 Locomotion 的跳跃转换执行器。</summary>
        /// <param name="sourceAbilitySystemComponent">拥有 FullBody Runtime 的角色 ASC。</param>
        /// <param name="sourceQueryService">复用 Grounded Jump 条件的查询服务。</param>
        internal JumpTransitionExecution(
            GameplayAbilitySystemComponent sourceAbilitySystemComponent,
            GroundedJumpTransitionQueryService sourceQueryService)
        {
            abilitySystemComponent = sourceAbilitySystemComponent ??
                throw new ArgumentNullException(nameof(sourceAbilitySystemComponent));
            jumpTransitionQueryService = sourceQueryService ??
                throw new ArgumentNullException(nameof(sourceQueryService));
        }

        /// <summary>在有效 Jump Press 确实存在可进入路径时取消当前 FullBody Runtime。</summary>
        /// <param name="inputRequests">当前玩家输入请求缓冲区。</param>
        /// <param name="execution">当前 FullBody Skill 执行。</param>
        /// <returns>成功取消 Runtime、允许随后 FSM 自行提交 Jump 路径时返回 true。</returns>
        internal bool TryExecute(
            IPlayerInputRequestBuffer inputRequests,
            FullBodySkillExecution execution)
        {
            if (execution == null || inputRequests == null)
                return false;
            if (!inputRequests.TryGetRequest(PlayerInputType.Jump, out IReadOnlyPlayerInputRequest request) ||
                !request.HasBufferedPress)
                return false;
            if (!jumpTransitionQueryService.CanInterruptFullBodyActionByBufferedJump())
                return false;

            // 取消会同步释放 FullBodyActionHandle；本方法不消费 Press，随后 FSM Transition 仍是提交权威。
            return abilitySystemComponent.TryCancelAbility(execution.Runtime);
        }
    }
}
