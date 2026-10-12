using WS_Modules.GAS.AbilitySystemComponent;

namespace RPG.Combat
{
    /// <summary>将一次物理命中解析为实际接收 Gameplay Effect 的 ASC。</summary>
    public interface IGameplayAbilityTargetProvider
    {
        /// <summary>尝试提供当前命中对象实际对应的 Target ASC。</summary>
        /// <param name="target">解析成功时返回的目标 ASC。</param>
        /// <returns>当前对象可接受技能命中时返回 true。</returns>
        GameplayAbilitySystemComponent TryGetTargetASC();
    }
}
