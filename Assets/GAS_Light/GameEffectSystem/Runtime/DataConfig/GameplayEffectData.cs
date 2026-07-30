using System;
using System.Collections.Generic;
using GAS;
using UnityEngine;

namespace WS_Modules.GAS.GameplayEffect
{
    /// <summary>
    /// 编辑器数据
    /// </summary>
    [Serializable]
    public sealed class GameplayEffectData
    {
        [SerializeField]
        private string Name;
        [SerializeField]
        private string Description;

        /// <summary>
        /// 游戏效果ID
        /// </summary>
        [SerializeField]
        private string EffectId;

        /// <summary>
        /// 游戏效果等级
        /// </summary>
        [SerializeField]
        private int Level = 1;

        [Tooltip("游戏效果持续时间策略")]
        [SerializeField]
        private E_GameEffectDurationType GE_DurationType;

        /// <summary>
        /// 游戏效果持续时间
        /// </summary>
        [Tooltip("GE 持续时间，对于 Instant 和 Infinite 无效")]
        [SerializeField]
        private float Duration = 0f;

        /// <summary>
        /// 效果轮次周期时长，对于 Period 适用
        /// </summary>
        [Tooltip("GE 每多长时间触发一次，对于 Instant 和 Infinite 无效")]
        [SerializeField]
        private float Period = 0f;

        // 该 GE 的 Modifier 结果
        [SerializeField]
        private List<Modifier> Modifiers;

        #region 策略
        [Header("叠层策略")]
        [SerializeField]
        private int MaxStackCount = 1;

        [Tooltip("GE 叠层策略：不合并叠层、按 Source 合并叠层、按 Target 合并叠层")]
        [SerializeField]
        private E_GameEffectStackingType GE_StackingType;

        [Tooltip("GE 叠层持续时间策略：每次成功应用都会把剩余时间重置为完整持续时间、成功增加叠层时不改变当前剩余时间、成功增加叠层时将剩余时间增加到完整持续时间")]
        [SerializeField]
        private E_GameEffectStackingDurationPolicy GE_StackingDurationPolicy;

        [Tooltip("GE 叠层周期刷新策略：成功应用后刷新当次周期结算；成功叠层不影响当次周期结算；成功叠层后刷新当次周期结算")]
        [SerializeField]
        private E_GameEffectStackingPeriodPolicy GE_StackingPeriodPolicy;

        [Tooltip("GE 叠层过期策略：当叠层过期时移除所有叠层；当叠层过期时移除最早的叠层；当叠层过期时移除最晚的叠层")]
        [SerializeField]
        private E_GameEffectStackingExpirationPolicy GE_StackingExpirationPolicy;

        [Tooltip("GE 叠层修饰器策略：始终只结算一次；Modifier 叠加")]
        [SerializeField]
        private E_GameEffectModifierStackPolicy GE_ModifierStackPolicy;
        #endregion
    }
}