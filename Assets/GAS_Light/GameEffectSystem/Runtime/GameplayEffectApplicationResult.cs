using WS_Modules.GAS.AbilitySystemComponent;

namespace WS_Modules.GAS.GameplayEffect
{
    /// <summary>保存一次 GameplayEffectSpec 成功应用后的 Runtime 与计算结果。</summary>
    public sealed class GameplayEffectApplicationResult
    {
        #region 属性

        /// <summary>获取本次成功应用的 Spec。</summary>
        public GameplayEffectSpec Spec { get; }

        /// <summary>获取接收本次应用的 Target ASC。</summary>
        public GameplayAbilitySystemComponent Target { get; }

        /// <summary>获取新建或更新后的 Active Runtime；Instant GE 为 null。</summary>
        public GameEffectRuntime ActiveEffect { get; }

        /// <summary>获取本次计算结果；未立即执行的周期应用使用 Empty。</summary>
        public GameplayEffectCalculationOutput CalculationOutput { get; }

        #endregion

        #region 构造

        /// <summary>创建一次 GE 应用成功结果。</summary>
        /// <param name="spec">成功应用的封存 Spec。</param>
        /// <param name="target">接收应用的 Target ASC。</param>
        /// <param name="activeEffect">Active Runtime；Instant 时为 null。</param>
        /// <param name="calculationOutput">本次计算结果。</param>
        internal GameplayEffectApplicationResult(
            GameplayEffectSpec spec,
            GameplayAbilitySystemComponent target,
            GameEffectRuntime activeEffect,
            GameplayEffectCalculationOutput calculationOutput)
        {
            Spec = spec;
            Target = target;
            ActiveEffect = activeEffect;
            CalculationOutput = calculationOutput;
        }

        #endregion
    }
}
