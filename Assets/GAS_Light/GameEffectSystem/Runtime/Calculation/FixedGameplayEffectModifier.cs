using System;
using UnityEngine;

namespace WS_Modules.GAS.GameplayEffect
{
    /// <summary>输出作者配置的固定 Magnitude，不隐式应用 Level 或 StackCount。</summary>
    [Serializable]
    public sealed class FixedGameplayEffectModifier : GameplayEffectModifier
    {
        [SerializeField] private float magnitude;

        /// <summary>固定 Modifier 不读取上下文，直接返回作者配置值。</summary>
        /// <param name="context">本次 GE 应用上下文；固定 Modifier 不使用。</param>
        /// <returns>作者配置的固定 Magnitude。</returns>
        protected override float CalculateMagnitude(
            GameplayEffectCalculationContext context) => CalculateConfiguredMagnitude();

        /// <summary>读取固定 Modifier 的作者配置值。</summary>
        /// <returns>固定 Magnitude。</returns>
        private float CalculateConfiguredMagnitude() => magnitude;

        /// <summary>返回不依赖运行时上下文的固定数值。</summary>
        /// <param name="level">未使用的等级参数。</param>
        /// <param name="value">固定 Magnitude。</param>
        /// <returns>始终返回 true。</returns>
        internal override bool TryCalculateStaticMagnitude(int level, out float value)
        {
            value = CalculateConfiguredMagnitude();
            return true;
        }
    }
}
