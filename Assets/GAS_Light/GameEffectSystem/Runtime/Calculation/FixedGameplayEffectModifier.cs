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
            AbilitySystemComponentBase source,
            AbilitySystemComponentBase target,
            GameEffectRuntime runtime) => magnitude;
    }
}
