using System;
using System.Collections.Generic;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayEffect
{
    /// <summary>通过稳定 GameplayTag Key 读取调用方为本次 GE 提供的 Magnitude。</summary>
    [Serializable]
    public sealed class SetByCallerGameplayEffectModifier : GameplayEffectModifier
    {
        [SerializeField] private GameplayTag key = GameplayTag.Empty;

        // Controller 已在生成 Modifier 前统一确认必需 Key，此处直接读取值。
        protected override float CalculateMagnitude(
            AbilitySystemComponentBase source,
            AbilitySystemComponentBase target,
            GameEffectRuntime runtime) => runtime.GetSetByCaller(key);

        // 向 Controller 登记本 Modifier 在本次应用前必须具备的动态值 Key。
        protected internal override void CollectRequiredSetByCallerKeys(ISet<GameplayTag> keys)
        {
            keys.Add(key);
        }
    }
}
