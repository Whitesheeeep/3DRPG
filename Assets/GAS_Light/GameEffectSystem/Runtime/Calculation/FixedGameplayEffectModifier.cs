using System;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;

namespace WS_Modules.GAS.GameplayEffect
{
    /// <summary>输出作者配置的固定 Magnitude，不隐式应用 Level 或 StackCount。</summary>
    [Serializable]
    public sealed class FixedGameplayEffectModifier : GameplayEffectModifier
    {
        [SerializeField] private float magnitude;

        // 固定计算直接返回作者值，合法性由 AttributeContainer 最终边界保障。
        protected override float CalculateMagnitude(
            GameplayAbilitySystemComponent source,
            GameplayAbilitySystemComponent target,
            GameEffectRuntime runtime) => CalculateConfiguredMagnitude();

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
