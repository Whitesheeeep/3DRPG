using System;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;

namespace WS_Modules.GAS.GameplayEffect
{
    /// <summary>按 BaseMagnitude 与等级曲线倍率生成 Modifier，不隐式应用 StackCount。</summary>
    [Serializable]
    public sealed class CurveGameplayEffectModifier : GameplayEffectModifier
    {
        [SerializeField] private float baseMagnitude;
        [SerializeField] private AnimationCurve levelCurve;

        // 使用 Runtime.Level 采样倍率；未配置曲线时保持基础值。
        protected override float CalculateMagnitude(
            GameplayAbilitySystemComponent source,
            GameplayAbilitySystemComponent target,
            GameEffectRuntime runtime) => CalculateConfiguredMagnitude(runtime.Level);

        /// <summary>使用指定等级计算曲线 Modifier 的作者数值。</summary>
        /// <param name="level">曲线采样等级。</param>
        /// <returns>基础 Magnitude 与曲线倍率的乘积。</returns>
        private float CalculateConfiguredMagnitude(int level)
        {
            float multiplier = levelCurve == null ? 1f : levelCurve.Evaluate(level);
            return baseMagnitude * multiplier;
        }

        /// <summary>使用指定等级采样曲线并返回静态 Magnitude。</summary>
        /// <param name="level">曲线采样等级。</param>
        /// <param name="value">计算结果。</param>
        /// <returns>始终返回 true。</returns>
        internal override bool TryCalculateStaticMagnitude(int level, out float value)
        {
            value = CalculateConfiguredMagnitude(level);
            return true;
        }
    }
}
