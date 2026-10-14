using System.Collections.Generic;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.GameplayAbilitySystem;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>定义由准备、蓄力等待和释放 SkillConfig 三段 Task 执行的蓄力冲刺能力。</summary>
    public sealed class ChargedDashGameplayAbilityData : AsynchronousGameplayAbilityData
    {
        #region Runtime 工厂

        /// <summary>为每次技能激活创建独立的手势状态和共享控制权 Runtime。</summary>
        /// <param name="activationId">当前 Controller 分配的激活标识。</param>
        /// <param name="spec">本次激活使用的已授予 Ability Spec。</param>
        /// <param name="source">释放 Ability 的 Source ASC。</param>
        /// <param name="setByCaller">本次激活复制的 SetByCaller 快照。</param>
        /// <returns>本次激活专属的蓄力冲刺 Runtime。</returns>
        protected override GameplayAbilityRuntime CreateRuntime(
            int activationId,
            GameplayAbilitySpec spec,
            GameplayAbilitySystemComponent source,
            IReadOnlyDictionary<GameplayTag, float> setByCaller) =>
            new ChargedDashGameplayAbilityRuntime(activationId, spec, source, setByCaller, this);

        #endregion
    }
}
