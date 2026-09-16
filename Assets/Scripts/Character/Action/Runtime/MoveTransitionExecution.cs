using System;
using WS_Modules.GAS.AbilitySystemComponent;

namespace RPG.Character
{
    /// <summary>在 Move 转换窗口中取消当前 FullBody Ability，不消费连续移动输入。</summary>
    internal sealed class MoveTransitionExecution
    {
        // 依赖字段：持续输入本身由 Blackboard 提供，执行器只需要通过 ASC 取消 Runtime。
        private readonly GameplayAbilitySystemComponent abilitySystemComponent;

        /// <summary>创建持续移动转换执行器。</summary>
        /// <param name="sourceAbilitySystemComponent">拥有当前 FullBody Runtime 的角色 ASC。</param>
        internal MoveTransitionExecution(GameplayAbilitySystemComponent sourceAbilitySystemComponent)
        {
            abilitySystemComponent = sourceAbilitySystemComponent ??
                throw new ArgumentNullException(nameof(sourceAbilitySystemComponent));
        }

        /// <summary>取消当前 FullBody Runtime，让下层持续推进的 Locomotion 直接显露。</summary>
        /// <param name="execution">当前 FullBody Skill 执行。</param>
        /// <returns>Runtime 成功进入取消终态时返回 true。</returns>
        internal bool TryExecute(FullBodySkillExecution execution) =>
            execution != null && abilitySystemComponent.TryCancelAbility(execution.Runtime);
    }
}
