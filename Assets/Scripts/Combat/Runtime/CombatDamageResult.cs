using WS_Modules.GAS.GameplayEffect;

namespace RPG.Combat
{
    /// <summary>组合一次 GE 伤害结算与可选 Gameplay 命中快照，供受击反馈共同消费。</summary>
    public sealed class CombatDamageResult
    {
        #region 属性

        /// <summary>获取本次即时伤害对应的命中快照；直接或周期结算时为空。</summary>
        public CombatHitSnapshot? HitSnapshot { get; }

        /// <summary>获取 GAS 原样返回的 GE 结算结果。</summary>
        public GameplayEffectApplicationResult EffectResult { get; }

        /// <summary>获取执行输出中的基础伤害元数据。</summary>
        public BasicDamageGameplayEffectExecutionResult DamageExecution { get; }

        /// <summary>获取本次 GE 结算出的最终伤害值。</summary>
        public float FinalDamage => DamageExecution.FinalDamage;

        /// <summary>获取本次伤害是否暴击。</summary>
        public bool IsCritical => DamageExecution.IsCritical;

        #endregion

        #region 构造

        /// <summary>创建已经确认存在正伤害 Execution 的 Gameplay 结果。</summary>
        /// <param name="effectResult">GAS 成功执行并提交的 GE 结果。</param>
        /// <param name="hitSnapshot">仅属于本次同步伤害提交的可选命中快照。</param>
        /// <param name="damageExecution">GE 输出中的正伤害结果。</param>
        internal CombatDamageResult(
            GameplayEffectApplicationResult effectResult,
            CombatHitSnapshot? hitSnapshot,
            BasicDamageGameplayEffectExecutionResult damageExecution)
        {
            EffectResult = effectResult;
            HitSnapshot = hitSnapshot;
            DamageExecution = damageExecution;
        }

        #endregion
    }
}
