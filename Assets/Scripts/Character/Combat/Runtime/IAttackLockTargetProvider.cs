using WS_Modules.GAS.AbilitySystemComponent;

namespace RPG.Character.Combat
{
    /// <summary>向攻击技能提供当前锁定目标，供攻击转向优先采用。</summary>
    public interface IAttackLockTargetProvider
    {
        /// <summary>尝试取得当前锁定的 Ability System Component。</summary>
        /// <param name="target">成功时返回当前锁定目标；没有锁定时为 null。</param>
        /// <returns>存在锁定目标时返回 true。</returns>
        bool TryGetAttackTarget(out GameplayAbilitySystemComponent target);
    }
}
