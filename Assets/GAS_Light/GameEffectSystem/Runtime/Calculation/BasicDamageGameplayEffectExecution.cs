using System;
using System.Collections.Generic;
using UnityEngine;
using WS_Modules.GAS.Generated;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayEffect
{
    /// <summary>按攻击力、技能倍率、暴击和防御计算一次基础伤害。</summary>
    [Serializable]
    public sealed class BasicDamageGameplayEffectExecution : GameplayEffectExecution
    {
        #region 运行时计算

        /// <summary>
        /// 读取结算时的 Source/Target 属性，并输出对 Target Health 的负向 Add Modifier。
        /// </summary>
        /// <param name="context">本次 GE 应用的实时上下文。</param>
        /// <returns>成功时返回伤害 Modifier 和元数据；输入非法时返回 null。</returns>
        public override GameplayEffectExecutionOutput Calculate(
            GameplayEffectCalculationContext context)
        {
            if (context == null ||
                !context.TryGetSourceAttribute(GameplayAttributes.Attribute_AttackPower, out float attack) ||
                !context.TryGetSourceAttribute(GameplayAttributes.Attribute_CriticalChance, out float criticalChance) ||
                !context.TryGetSourceAttribute(GameplayAttributes.Attribute_CriticalDamage, out float criticalDamage) ||
                !context.TryGetTargetAttribute(GameplayAttributes.Attribute_Armor, out float armor) ||
                !context.TryGetTargetAttribute(GameplayAttributes.Attribute_Health, out float targetHealth) ||
                !context.TryGetSetByCaller(
                    GameplayTags.Tag_Data_Damage_Multiplier,
                    out float skillMultiplier))
                return null;

            if (!IsFinite(attack) || !IsFinite(criticalChance) ||
                !IsFinite(criticalDamage) || !IsFinite(armor) ||
                !IsFinite(targetHealth) || !IsFinite(skillMultiplier))
                return null;

            float clampedArmor = Mathf.Max(0f, armor);
            float clampedCriticalChance = Mathf.Clamp01(criticalChance);
            float clampedCriticalDamage = Mathf.Max(0f, criticalDamage);
            bool isCritical = UnityEngine.Random.value < clampedCriticalChance;
            float damageBeforeDefense = Mathf.Max(0f, attack * skillMultiplier);
            if (isCritical)
                damageBeforeDefense *= 1f + clampedCriticalDamage;

            float finalDamage = Mathf.Max(
                0f,
                damageBeforeDefense * 100f / (100f + clampedArmor));
            if (!IsFinite(damageBeforeDefense) || !IsFinite(finalDamage)) return null;

            AttributeModifier modifier = context.CreateModifier(
                GameplayAttributes.Attribute_Health,
                AttributeModifierType.Add,
                -finalDamage);
            var result = new BasicDamageGameplayEffectExecutionResult(
                isCritical,
                damageBeforeDefense,
                finalDamage);
            return new GameplayEffectExecutionOutput(new[] { modifier }, result);
        }

        #endregion

        #region 配置契约

        /// <summary>登记基础伤害使用的倍率 SetByCaller Key。</summary>
        /// <param name="keys">GE 汇总的动态 Key 集合。</param>
        protected internal override void CollectRequiredSetByCallerKeys(
            ISet<GameplayTag> keys)
        {
            keys.Add(GameplayTags.Tag_Data_Damage_Multiplier);
        }

        /// <summary>登记基础伤害需要读取的 Source 属性。</summary>
        /// <param name="attributes">GE 汇总的 Source Attribute 集合。</param>
        protected internal override void CollectRequiredSourceAttributes(
            ISet<GameplayAttribute> attributes)
        {
            attributes.Add(GameplayAttributes.Attribute_AttackPower);
            attributes.Add(GameplayAttributes.Attribute_CriticalChance);
            attributes.Add(GameplayAttributes.Attribute_CriticalDamage);
        }

        /// <summary>登记基础伤害需要读取的 Target 属性。</summary>
        /// <param name="attributes">GE 汇总的 Target Attribute 集合。</param>
        protected internal override void CollectRequiredTargetAttributes(
            ISet<GameplayAttribute> attributes)
        {
            attributes.Add(GameplayAttributes.Attribute_Armor);
            attributes.Add(GameplayAttributes.Attribute_Health);
        }

        #endregion

        #region 校验

        /// <summary>判断一个伤害中间值是否为有限数。</summary>
        /// <param name="value">待校验值。</param>
        /// <returns>不是 NaN 且不是 Infinity 时返回 true。</returns>
        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        #endregion
    }
}
