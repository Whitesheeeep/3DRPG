using WS_Modules.GAS.GameplayAbilitySystem;
using RPG.SkillSystem;

namespace RPG.Character
{
    /// <summary>提供给 FullBody GameplayAbilityTask 的最小动作注册契约。</summary>
    public interface IFullBodyActionArbiter
    {
        /// <summary>登记一个已经成功启动表现的 FullBody Ability Runtime。</summary>
        /// <param name="runtime">拥有本次 FullBody 表现的活动 Runtime。</param>
        /// <param name="initialTransitions">当前 Skill Phase 开放的转换窗口。</param>
        /// <returns>负责权限更新与幂等注销的生命周期 Handle。</returns>
        FullBodyActionHandle RegisterFullBodyAction(
            GameplayAbilityRuntime runtime,
            SkillTransitionMask initialTransitions);
    }
}
