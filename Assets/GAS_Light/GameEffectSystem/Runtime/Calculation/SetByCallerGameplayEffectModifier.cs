using System;
using System.Collections.Generic;
using UnityEngine;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayEffect
{
    /// <summary>通过稳定 GameplayTag Key 读取调用方为本次 GE 提供的 Magnitude。</summary>
    [Serializable]
    public sealed class SetByCallerGameplayEffectModifier : GameplayEffectModifier
    {
        [SerializeField] private GameplayTag key = GameplayTag.Empty;

        /// <summary>读取 Spec 中已经封存的 SetByCaller Magnitude。</summary>
        /// <param name="context">提供 Spec SetByCaller 的计算上下文。</param>
        /// <returns>Key 存在时返回 Magnitude，否则返回 NaN 使整体计算失败。</returns>
        protected override float CalculateMagnitude(
            GameplayEffectCalculationContext context) =>
            context.TryGetSetByCaller(key, out float value) ? value : float.NaN;

        /// <summary>登记本 Modifier 在封存前必须具备的动态 Key。</summary>
        /// <param name="keys">接收 GE 必需 SetByCaller Key 的集合。</param>
        protected internal override void CollectRequiredSetByCallerKeys(ISet<GameplayTag> keys)
        {
            keys.Add(key);
        }
    }
}
