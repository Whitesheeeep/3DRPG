using System;
using UnityEngine;

namespace WS_Modules.GAS.AttributeSystem
{
    /// <summary>保存未来 GameplayEffect 可内联序列化的 Attribute Modifier 作者配置。</summary>
    [Serializable]
    public sealed class AttributeModifierConfig
    {
        #region 字段

        [SerializeField] private GameplayAttribute attribute = GameplayAttribute.Empty;
        [SerializeField] private AttributeModifierType type;
        [SerializeField] private float magnitude;
        [SerializeField] private int priority;

        #endregion

        #region 属性

        /// <summary>获取待修改的 Attribute。</summary>
        public GameplayAttribute Attribute => attribute;

        /// <summary>获取数值运算类型。</summary>
        public AttributeModifierType Type => type;

        /// <summary>获取配置的运算数值。</summary>
        public float Magnitude => magnitude;

        /// <summary>获取从小到大执行的优先级。</summary>
        public int Priority => priority;

        #endregion

        #region 构造与校验

        /// <summary>创建供 Unity 内联序列化的空 Modifier 配置。</summary>
        public AttributeModifierConfig()
        {
        }

        /// <summary>使用明确参数创建 Modifier 配置。</summary>
        /// <param name="attribute">待修改 Attribute。</param>
        /// <param name="type">Add、Multiply 或 Override 运算。</param>
        /// <param name="magnitude">有限运算数值。</param>
        /// <param name="priority">从小到大执行的优先级。</param>
        public AttributeModifierConfig(
            GameplayAttribute attribute,
            AttributeModifierType type,
            float magnitude,
            int priority = 0)
        {
            this.attribute = attribute;
            this.type = type;
            this.magnitude = magnitude;
            this.priority = priority;
        }

        // 校验 Config 能否安全参与 Instant 结算或创建运行时 Modifier，不修改任何状态。
        internal bool IsValid() =>
            attribute.IsValid &&
            type is AttributeModifierType.Add or AttributeModifierType.Multiply or AttributeModifierType.Override &&
            IsFinite(magnitude);

        // Modifier 的作者数值必须有限，聚合溢出由 Aggregator 继续校验。
        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        #endregion
    }
}
