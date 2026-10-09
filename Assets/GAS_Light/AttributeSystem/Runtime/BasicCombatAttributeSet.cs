using UnityEngine;
using WS_Modules.GAS.Generated;

namespace WS_Modules.GAS.AttributeSystem
{
    /// <summary>提供角色与敌人通用的战斗属性边界和生命上限联动。</summary>
    [CreateAssetMenu(fileName = "BasicCombatAttributeSet", menuName = "WSFrame/GAS/Basic Combat Attribute Set")]
    public sealed class BasicCombatAttributeSet : GameplayAttributeSet
    {
        #region 数值约束

        /// <summary>写入永久基础值前应用非负、动态生命上限和暴击率边界。</summary>
        /// <param name="attributes">当前 Attribute 快照。</param>
        /// <param name="attribute">待修改的属性。</param>
        /// <param name="newValue">待校正的候选基础值。</param>
        protected override void PreAttributeBaseChange(
            IReadOnlyGameplayAttributeContainer attributes,
            GameplayAttribute attribute,
            ref float newValue)
        {
            base.PreAttributeBaseChange(attributes, attribute, ref newValue);
            ClampCombatValue(attributes, attribute, ref newValue);
        }

        /// <summary>写入当前结算值前应用非负、动态生命上限和暴击率边界。</summary>
        /// <param name="attributes">当前 Attribute 快照。</param>
        /// <param name="attribute">待修改的属性。</param>
        /// <param name="newValue">待校正的候选结算值。</param>
        protected override void PreAttributeChange(
            IReadOnlyGameplayAttributeContainer attributes,
            GameplayAttribute attribute,
            ref float newValue)
        {
            base.PreAttributeChange(attributes, attribute, ref newValue);
            ClampCombatValue(attributes, attribute, ref newValue);
        }

        /// <summary>MaxHealth 当前值变化时保持 Health 百分比，并在同一 FIFO 事务提交。</summary>
        /// <param name="context">受控的后续修改上下文。</param>
        /// <param name="attribute">发生变化的属性。</param>
        /// <param name="oldValue">变化前的有效值。</param>
        /// <param name="newValue">变化后的有效值。</param>
        protected override void PostAttributeChange(
            GameplayAttributePostChangeContext context,
            GameplayAttribute attribute,
            float oldValue,
            float newValue)
        {
            if (attribute != GameplayAttributes.Attribute_MaxHealth || oldValue <= 0f)
                return;

            if (!context.Attributes.TryGetCurrentValue(GameplayAttributes.Attribute_Health, out float currentHealth))
                return;

            // 保持当前生命百分比；健康资源的新值仍会经过同一 Set 的动态上限校验。
            float healthRatio = Mathf.Clamp01(currentHealth / oldValue);
            float adjustedHealth = newValue * healthRatio;
            if (!context.RequestSetValue(GameplayAttributes.Attribute_Health, adjustedHealth))
            {
                Debug.LogWarning(
                    $"[BasicCombatAttributeSet] MaxHealth={oldValue}->{newValue} 后无法排入 Health 百分比同步，health={currentHealth}。");
                return;
            }

            Debug.Log(
                $"[BasicCombatAttributeSet] MaxHealth 变化后保持 Health 百分比，ratio={healthRatio:0.###}, health={currentHealth}->{adjustedHealth}。");
        }

        #endregion

        #region 内部校正

        /// <summary>按战斗属性语义补充 Definition 固定边界。</summary>
        /// <param name="attributes">当前 Attribute 快照。</param>
        /// <param name="attribute">待修改的属性。</param>
        /// <param name="value">待校正的候选值。</param>
        private static void ClampCombatValue(
            IReadOnlyGameplayAttributeContainer attributes,
            GameplayAttribute attribute,
            ref float value)
        {
            if (attribute == GameplayAttributes.Attribute_Health)
            {
                float maximumHealth = float.PositiveInfinity;
                if (attributes.TryGetCurrentValue(GameplayAttributes.Attribute_MaxHealth, out float configuredMaximum))
                    maximumHealth = Mathf.Max(0f, configuredMaximum);
                value = Mathf.Clamp(value, 0f, maximumHealth);
                return;
            }

            if (attribute == GameplayAttributes.Attribute_CriticalChance)
            {
                value = Mathf.Clamp01(value);
                return;
            }

            if (attribute == GameplayAttributes.Attribute_MaxHealth ||
                attribute == GameplayAttributes.Attribute_MP ||
                attribute == GameplayAttributes.Attribute_Armor ||
                attribute == GameplayAttributes.Attribute_Speed ||
                attribute == GameplayAttributes.Attribute_AttackPower ||
                attribute == GameplayAttributes.Attribute_CriticalDamage)
                value = Mathf.Max(0f, value);
        }

        #endregion
    }
}
