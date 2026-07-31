using System;
using System.Collections.Generic;
using UnityEngine;
using WS_Modules.GAS.TAG;
#if UNITY_EDITOR
using WS_Modules.GAS.AttributeSystem;
#endif

namespace WS_Modules.GAS.GameplayEffect
{
    /// <summary>保存可复用的 Gameplay Effect 作者配置；运行时状态由 Runtime 独立承载。</summary>
    [CreateAssetMenu(fileName = "GameplayEffectData", menuName = "WSFrame/GAS/Gameplay Effect")]
    public sealed class GameplayEffectData : ScriptableObject
    {
        #region 基础配置

        [SerializeField, TextArea] private string description;
        [SerializeField, Tooltip("决定 GE 是立即结算、有限持续还是无限持续。")]
        private E_GameEffectDurationType durationType;
        [SerializeField, Min(0f), Tooltip("Duration GE 的完整持续时间；其他类型忽略。")]
        private float duration;
        [SerializeField, Min(0f), Tooltip("大于 0 时按周期执行 Instant 结算；支持 Duration 与 Infinite。")]
        private float period;
        [SerializeField, Tooltip("周期 GE 应用成功时是否立即执行第一轮。")]
        private bool executePeriodicOnApplication;

        #endregion

        #region Tag 与数值计算

        [SerializeField, Tooltip("目标必须满足该查询才能应用；空查询表示不限制。")]
        private GameplayTagQuery targetTagQuery;
        [SerializeField, Tooltip("非 Instant GE 激活期间赋予 Target 的标签。")]
        private GameplayTag[] grantedTags = Array.Empty<GameplayTag>();
        [SerializeReference, Tooltip("按列表顺序执行；每项生成一个最终 Attribute Modifier。")]
        private List<GameplayEffectModifier> modifiers = new();

        #endregion

#if UNITY_EDITOR
        #region Editor 校验范围

        [SerializeField, Tooltip("仅用于 GE Editor 校验；空列表表示扫描项目全部 AttributeSet。")]
        private List<GameplayAttributeSet> validationSets = new();

        #endregion
#endif

        #region 叠层策略

        [SerializeField, Tooltip("决定重复应用是否合并到现有 Runtime。")]
        private E_GameEffectStackingType stackingType;
        [SerializeField, Min(1), Tooltip("合并叠层时允许的最大层数；None 时忽略。")]
        private int maxStackCount = 1;
        [SerializeField, Tooltip("达到最大层数后是否完全拒绝本次应用。")]
        private bool denyOverflowApplication = true;
        [SerializeField, Tooltip("成功重复应用时如何更新剩余持续时间。")]
        private E_GameEffectStackingDurationPolicy stackingDurationPolicy;
        [SerializeField, Tooltip("成功重复应用时如何更新下一次周期计时。")]
        private E_GameEffectStackingPeriodPolicy stackingPeriodPolicy;
        [SerializeField, Tooltip("Duration 到期时如何处理现有层数。")]
        private E_GameEffectStackingExpirationPolicy stackingExpirationPolicy;

        #endregion

        #region 属性

        /// <summary>获取供编辑器和日志显示的说明。</summary>
        public string Description => description;
        /// <summary>获取持续时间类型。</summary>
        public E_GameEffectDurationType DurationType => durationType;
        /// <summary>获取 Duration GE 的完整持续时间。</summary>
        public float Duration => duration;
        /// <summary>获取周期时长；零表示非周期持续效果。</summary>
        public float Period => period;
        /// <summary>获取周期 GE 是否在应用成功时立即执行第一轮。</summary>
        public bool ExecutePeriodicOnApplication => executePeriodicOnApplication;
        /// <summary>获取目标应用 Tag 查询。</summary>
        public GameplayTagQuery TargetTagQuery => targetTagQuery;
        /// <summary>获取激活期间赋予 Target 的标签。</summary>
        public IReadOnlyList<GameplayTag> GrantedTags => grantedTags;
        /// <summary>获取按顺序计算并提交的 GE Modifier 作者配置。</summary>
        public IReadOnlyList<GameplayEffectModifier> Modifiers => modifiers;
        /// <summary>获取重复应用时的合并规则。</summary>
        public E_GameEffectStackingType StackingType => stackingType;
        /// <summary>获取允许的最大叠层数。</summary>
        public int MaxStackCount => maxStackCount;
        /// <summary>获取达到最大层数时是否完全拒绝应用。</summary>
        public bool DenyOverflowApplication => denyOverflowApplication;
        /// <summary>获取成功重复应用时的持续时间规则。</summary>
        public E_GameEffectStackingDurationPolicy StackingDurationPolicy => stackingDurationPolicy;
        /// <summary>获取成功重复应用时的周期计时规则。</summary>
        public E_GameEffectStackingPeriodPolicy StackingPeriodPolicy => stackingPeriodPolicy;
        /// <summary>获取 Duration 到期时的叠层规则。</summary>
        public E_GameEffectStackingExpirationPolicy StackingExpirationPolicy => stackingExpirationPolicy;
        /// <summary>获取该配置是否为周期 GE。</summary>
        public bool IsPeriodic => period > 0f;

        #endregion

        #region Modifier 计算契约

        // 汇总所有 Modifier 声明的动态输入 Key，供 Controller 在唯一公开失败入口统一检查。
        internal void CollectRequiredSetByCallerKeys(ISet<GameplayTag> keys)
        {
            for (int i = 0; i < Modifiers.Count; i++)
                Modifiers[i].CollectRequiredSetByCallerKeys(keys);
        }

        #endregion
    }
}
