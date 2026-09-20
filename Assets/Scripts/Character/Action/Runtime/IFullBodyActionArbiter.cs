using WS_Modules.GAS.GameplayAbilitySystem;

namespace RPG.Character
{
    /// <summary>提供给 FullBody GameplayAbilityTask 的最小动作注册契约。</summary>
    public interface IFullBodyActionArbiter
    {
        /// <summary>登记一个已经成功启动表现的 FullBody Ability Runtime。</summary>
        /// <param name="runtime">拥有本次 FullBody 表现的活动 Runtime。</param>
        /// <returns>负责幂等注销的生命周期 Handle。</returns>
        FullBodyActionHandle RegisterFullBodyAction(GameplayAbilityRuntime runtime);
    }
}
