using System.Collections.Generic;
using UnityEngine;

namespace WS_Modules.GAS.AttributeSystem
{
    /// <summary>保存 Attribute 配置模板，并提供可覆写的 UE 风格 Pre/Post 数值规则。</summary>
    [CreateAssetMenu(fileName = "GameplayAttributeSet", menuName = "WSFrame/GAS/Gameplay Attribute Set")]
    public class GameplayAttributeSet : ScriptableObject
    {
        #region 字段与属性

        [SerializeField] private List<GameplayAttributeDefinition> definitions = new();

        /// <summary>获取 Set 中用于初始化 Container 的配置模板。</summary>
        public IReadOnlyList<GameplayAttributeDefinition> Definitions => definitions;

        #endregion

        #region 可覆写规则

        // 内部结算值写入前执行；覆写实现必须调用 base 以保留固定 Min/Max Clamp。
        protected virtual void PreAttributeBaseChange(
            IReadOnlyGameplayAttributeContainer attributes,
            GameplayAttribute attribute,
            ref float newValue)
        {
            ClampByDefinition(attributes, attribute, ref newValue);
        }

        // 内部结算值提交后执行；业务联动应通过 Context 排队，避免递归直接写数据。
        protected virtual void PostAttributeBaseChange(
            GameplayAttributePostChangeContext context,
            GameplayAttribute attribute,
            float oldValue,
            float newValue)
        {
        }

        // CurrentValue 写入前执行；覆写实现必须调用 base 以保留固定 Min/Max Clamp。
        protected virtual void PreAttributeChange(
            IReadOnlyGameplayAttributeContainer attributes,
            GameplayAttribute attribute,
            ref float newValue)
        {
            ClampByDefinition(attributes, attribute, ref newValue);
        }

        // CurrentValue 提交后执行；死亡、资源联动等反应由具体 Set 决定。
        protected virtual void PostAttributeChange(
            GameplayAttributePostChangeContext context,
            GameplayAttribute attribute,
            float oldValue,
            float newValue)
        {
        }

        #endregion

        #region Container 分派

        // 将 Container 的 Base Pre 阶段转发给派生 Set。
        internal void DispatchPreAttributeBaseChange(
            IReadOnlyGameplayAttributeContainer attributes,
            GameplayAttribute attribute,
            ref float newValue) =>
            PreAttributeBaseChange(attributes, attribute, ref newValue);

        // 将 Container 的 Base Post 阶段转发给派生 Set。
        internal void DispatchPostAttributeBaseChange(
            GameplayAttributePostChangeContext context,
            GameplayAttribute attribute,
            float oldValue,
            float newValue) =>
            PostAttributeBaseChange(context, attribute, oldValue, newValue);

        // 将 Container 的 Current Pre 阶段转发给派生 Set。
        internal void DispatchPreAttributeChange(
            IReadOnlyGameplayAttributeContainer attributes,
            GameplayAttribute attribute,
            ref float newValue) =>
            PreAttributeChange(attributes, attribute, ref newValue);

        // 将 Container 的 Current Post 阶段转发给派生 Set。
        internal void DispatchPostAttributeChange(
            GameplayAttributePostChangeContext context,
            GameplayAttribute attribute,
            float oldValue,
            float newValue) =>
            PostAttributeChange(context, attribute, oldValue, newValue);

        #endregion

        #region 内部辅助

        // 使用运行时 Definition 中已复制的边界执行通用固定 Clamp。
        private static void ClampByDefinition(
            IReadOnlyGameplayAttributeContainer attributes,
            GameplayAttribute attribute,
            ref float newValue)
        {
            if (!attributes.TryGetDefinition(attribute, out GameplayAttributeDefinition definition)) return;
            newValue = Mathf.Clamp(newValue, definition.MinValue, definition.MaxValue);
        }

        #endregion
    }
}
