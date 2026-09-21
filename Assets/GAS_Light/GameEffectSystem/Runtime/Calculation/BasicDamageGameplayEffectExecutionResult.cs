namespace WS_Modules.GAS.GameplayEffect
{
    /// <summary>保存基础伤害 Execution 的暴击状态和最终伤害元数据。</summary>
    public sealed class BasicDamageGameplayEffectExecutionResult : GameplayEffectExecutionResult
    {
        #region 属性

        /// <summary>获取本次伤害是否发生暴击。</summary>
        public bool IsCritical { get; }

        /// <summary>获取防御计算前的伤害。</summary>
        public float DamageBeforeDefense { get; }

        /// <summary>获取经过防御计算后的最终伤害。</summary>
        public float FinalDamage { get; }

        #endregion

        #region 构造

        /// <summary>创建基础伤害计算结果。</summary>
        /// <param name="isCritical">是否暴击。</param>
        /// <param name="damageBeforeDefense">防御计算前伤害。</param>
        /// <param name="finalDamage">最终伤害。</param>
        public BasicDamageGameplayEffectExecutionResult(
            bool isCritical,
            float damageBeforeDefense,
            float finalDamage)
        {
            IsCritical = isCritical;
            DamageBeforeDefense = damageBeforeDefense;
            FinalDamage = finalDamage;
        }

        #endregion
    }
}
