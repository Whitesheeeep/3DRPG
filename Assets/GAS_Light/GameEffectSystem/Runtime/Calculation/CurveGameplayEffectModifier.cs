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
            GameEffectRuntime runtime)
        {
            float multiplier = levelCurve == null ? 1f : levelCurve.Evaluate(runtime.Level);
            return baseMagnitude * multiplier;
        }
    }
}
