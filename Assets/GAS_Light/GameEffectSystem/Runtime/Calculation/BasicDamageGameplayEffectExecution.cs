using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.Generated;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.TAG;
using WS_Modules.LogModule;

namespace WS_Modules.GAS.GameplayEffect
{
    /// <summary>按攻击力、技能倍率、暴击和防御计算一次基础伤害。</summary>
    [Serializable]
    public class BasicDamageGameplayEffectExecution : GameplayEffectExecution
    {
        #region 调试配置

        /// <summary>按需记录每次伤害计算，默认关闭以避免连续命中产生大量诊断日志。</summary>
        [SerializeField, LabelText("输出计算日志")]
        [Tooltip("输出计算明细或失败原因；计算伤害不等于经过属性约束后的实际扣血量。")]
        private bool enableCalculationDebug;

        #endregion

        #region 运行时计算

        /// <summary>
        /// 读取结算时的 Source/Target 属性，并输出对 Target Health 的负向 Add Modifier。
        /// </summary>
        /// <param name="context">本次 GE 应用的实时上下文。</param>
        /// <returns>成功时返回伤害 Modifier 和元数据；输入非法时返回 null。</returns>
        public override GameplayEffectExecutionOutput Calculate(
            GameplayEffectCalculationContext context)
        {
            if (context == null)
            {
                LogCalculationFailure(context, "计算上下文缺失");
                return null;
            }

            if (!context.TryGetSourceAttribute(GameplayAttributes.Attribute_AttackPower, out float attack) ||
                !context.TryGetSourceAttribute(GameplayAttributes.Attribute_CriticalChance, out float criticalChance) ||
                !context.TryGetSourceAttribute(GameplayAttributes.Attribute_CriticalDamage, out float criticalDamage) ||
                !context.TryGetTargetAttribute(GameplayAttributes.Attribute_Armor, out float armor) ||
                !context.TryGetTargetAttribute(GameplayAttributes.Attribute_Health, out float targetHealth))
            {
                LogCalculationFailure(context,
                    "必需属性缺失：Source 需要 AttackPower、CriticalChance、CriticalDamage，Target 需要 Armor、Health");
                return null;
            }

            if (!TryGetDamageMultiplier(context, out float damageMultiplier))
            {
                LogCalculationFailure(context, "伤害倍率读取失败，缺少本 Execution 必需的倍率 SetByCaller");
                return null;
            }

            if (!IsFinite(attack) || !IsFinite(criticalChance) ||
                !IsFinite(criticalDamage) || !IsFinite(armor) ||
                !IsFinite(targetHealth) || !IsFinite(damageMultiplier))
            {
                LogCalculationFailure(context,
                    $"输入包含非有限值，attack={attack}，multiplier={damageMultiplier}，" +
                    $"criticalChance={criticalChance}，criticalDamage={criticalDamage}，" +
                    $"armor={armor}，targetHealth={targetHealth}");
                return null;
            }

            // 保持原有公式与一次随机采样；诊断直接复用本次结算数据，不重新判定暴击。
            float clampedArmor = Mathf.Max(0f, armor);
            float clampedCriticalChance = Mathf.Clamp01(criticalChance);
            float clampedCriticalDamage = Mathf.Max(0f, criticalDamage);
            bool isCritical = UnityEngine.Random.value < clampedCriticalChance;
            float damageBeforeDefense = Mathf.Max(0f, attack * damageMultiplier);
            if (isCritical)
                damageBeforeDefense *= 1f + clampedCriticalDamage;

            float finalDamage = Mathf.Max(
                0f,
                damageBeforeDefense * 100f / (100f + clampedArmor));
            if (!IsFinite(damageBeforeDefense) || !IsFinite(finalDamage))
            {
                LogCalculationFailure(context,
                    $"计算结果溢出，attack={attack}，multiplier={damageMultiplier}，" +
                    $"isCritical={isCritical}，criticalDamage={clampedCriticalDamage}，" +
                    $"armor={clampedArmor}，damageBeforeDefense={damageBeforeDefense}，finalDamage={finalDamage}");
                return null;
            }

            AttributeModifier modifier = context.CreateModifier(
                GameplayAttributes.Attribute_Health,
                AttributeModifierType.Add,
                -finalDamage);
            var result = new BasicDamageGameplayEffectExecutionResult(
                isCritical,
                damageBeforeDefense,
                finalDamage);
            // 此处尚未提交 Health Modifier；只记录计算结果，不将其误报为实际扣血。
            if (enableCalculationDebug)
            {
                WSLog.Log(
                    $"[BasicDamageGameplayEffectExecution] 伤害计算结果，GE='{context.Spec.Data.name}'，" +
                    $"source='{context.Source.name}'，target='{context.Target.name}'，" +
                    $"attack={attack:F3}，multiplier={damageMultiplier:F3}，" +
                    $"criticalChance={criticalChance:F3}（生效={clampedCriticalChance:F3}），" +
                    $"criticalDamage={criticalDamage:F3}（生效={clampedCriticalDamage:F3}），" +
                    $"isCritical={isCritical}，damageBeforeDefense={damageBeforeDefense:F3}，" +
                    $"armor={armor:F3}（生效={clampedArmor:F3}），finalDamage={finalDamage:F3}；非实际扣血量。");
            }
            return new GameplayEffectExecutionOutput(new[] { modifier }, result);
        }

        /// <summary>读取本次 Execution 使用的伤害倍率，供专用伤害类型扩展倍率来源。</summary>
        /// <param name="context">本次 GE 应用的实时上下文。</param>
        /// <param name="damageMultiplier">成功时返回有限性将在主计算流程校验的组合倍率。</param>
        /// <returns>基础伤害倍率存在时返回 true。</returns>
        protected virtual bool TryGetDamageMultiplier(
            GameplayEffectCalculationContext context,
            out float damageMultiplier) =>
            context.TryGetSetByCaller(
                GameplayTags.Tag_Data_Damage_Multiplier,
                out damageMultiplier);

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

        #region 计算诊断

        /// <summary>仅在诊断开启时记录计算被拒绝的原因，不改变返回结果或尝试恢复。</summary>
        /// <param name="context">本次计算上下文；上下文缺失分支允许为 null。</param>
        /// <param name="reason">当前失败分支的原因与必要数值。</param>
        private void LogCalculationFailure(GameplayEffectCalculationContext context, string reason)
        {
            if (!enableCalculationDebug) return;

            // 只有上下文或属性不完整的失败路径使用缺失标记，正常计算仍遵循既有契约。
            WSLog.LogWarning(
                $"[BasicDamageGameplayEffectExecution] 伤害计算失败，GE='{context?.Spec.Data.name ?? "缺失"}'，" +
                $"source='{(context?.Source != null ? context.Source.name : "缺失")}'，" +
                $"target='{(context?.Target != null ? context.Target.name : "缺失")}'，原因：{reason}。");
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
