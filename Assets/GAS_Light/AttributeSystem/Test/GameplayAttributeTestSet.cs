#if UNITY_EDITOR
using UnityEngine;
using WS_Modules.GAS.Generated;

namespace WS_Modules.GAS.AttributeSystem
{
    /// <summary>
    /// 使用 Health、MaxHealth、Armor 与 MP 验证 Attribute Pre/Post 和关联结算规则。
    /// </summary>
    public sealed class GameplayAttributeTestSet : GameplayAttributeSet
    {
        #region Pre 规则

        // 保留 Definition 固定边界，并输出 BaseValue 候选值的修正过程。
        protected override void PreAttributeBaseChange(
            IReadOnlyGameplayAttributeContainer attributes,
            GameplayAttribute attribute,
            ref float newValue)
        {
            float requestedValue = newValue;
            base.PreAttributeBaseChange(attributes, attribute, ref newValue);
            Debug.Log(
                $"[AttributeTest][Base Pre] Attribute={GetAttributeName(attribute)}, " +
                $"Requested={requestedValue}, Result={newValue}");
        }

        // 保留固定边界后，再使用 MaxHealth.CurrentValue 限制 Health 的动态上限。
        protected override void PreAttributeChange(
            IReadOnlyGameplayAttributeContainer attributes,
            GameplayAttribute attribute,
            ref float newValue)
        {
            float requestedValue = newValue;
            base.PreAttributeChange(attributes, attribute, ref newValue);

            if (attribute == GameplayAttributes.Attribute_Health &&
                attributes.TryGetCurrentValue(
                    GameplayAttributes.Attribute_MaxHealth,
                    out float maxHealth))
            {
                newValue = Mathf.Clamp(newValue, 0f, maxHealth);
                Debug.Log("[AttributeTest][Current Pre] Health 被 MaxHealth 限制为 " + newValue);
            }

            Debug.Log(
                $"[AttributeTest][Current Pre] Attribute={GetAttributeName(attribute)}, " +
                $"Requested={requestedValue}, Result={newValue}");
        }

        #endregion

        #region Post 规则

        // Base Post 只记录永久结算变化，不在回调栈内直接修改其他 Attribute。
        protected override void PostAttributeBaseChange(
            GameplayAttributePostChangeContext context,
            GameplayAttribute attribute,
            float oldValue,
            float newValue)
        {
            Debug.Log(
                $"[AttributeTest][Base Post] Attribute={GetAttributeName(attribute)}, " +
                $"Old={oldValue}, New={newValue}");
        }

        // Current Post 验证 MaxHealth 到 Health 的单向 FIFO 联动及资源耗尽通知。
        protected override void PostAttributeChange(
            GameplayAttributePostChangeContext context,
            GameplayAttribute attribute,
            float oldValue,
            float newValue)
        {
            Debug.Log(
                $"[AttributeTest][Current Post] Attribute={GetAttributeName(attribute)}, " +
                $"Old={oldValue}, New={newValue}");

            if (attribute == GameplayAttributes.Attribute_MaxHealth &&
                context.Attributes.TryGetCurrentValue(
                    GameplayAttributes.Attribute_Health,
                    out float currentHealth) &&
                !context.RequestSetValue(
                    GameplayAttributes.Attribute_Health,
                    currentHealth))
            {
                Debug.LogWarning(
                    "[AttributeTest] MaxHealth → Health FIFO 请求被拒绝，请检查修改环或事务状态。");
            }

            if (attribute == GameplayAttributes.Attribute_Health &&
                Mathf.Approximately(newValue, 0f))
            {
                Debug.Log("[AttributeTest][Trigger] Health 已归零，触发死亡测试事件。");
            }

            if (attribute == GameplayAttributes.Attribute_MP &&
                Mathf.Approximately(newValue, 0f))
            {
                Debug.Log("[AttributeTest][Trigger] MP 已归零，触发资源耗尽测试事件。");
            }
        }

        #endregion

        #region 日志辅助

        // 使用生成常量提供可读日志，同时保留未知 Attribute 的稳定 ID。
        private static string GetAttributeName(GameplayAttribute attribute)
        {
            if (attribute == GameplayAttributes.Attribute_Health) return "Health";
            if (attribute == GameplayAttributes.Attribute_MaxHealth) return "MaxHealth";
            if (attribute == GameplayAttributes.Attribute_Armor) return "Armor";
            if (attribute == GameplayAttributes.Attribute_MP) return "MP";
            return $"Id:{attribute.Id}";
        }

        #endregion
    }
}
#endif