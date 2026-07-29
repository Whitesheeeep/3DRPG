using System;
using UnityEngine;

namespace WS_Modules.GAS.AttributeSystem
{
    /// <summary>
    /// 同时表示 AttributeSet 中的配置模板与 Container 中克隆后的独立运行时数据。
    /// </summary>
    [Serializable]
    public sealed class GameplayAttributeDefinition
    {
        #region 配置字段
        [SerializeField]
        private GameplayAttribute attribute = GameplayAttribute.Empty;
        [SerializeField, Tooltip("Stat 为普通状态或者数值属性， Resource 为生命、法力等资源属性。")]
        private GameplayAttributeType type;
        [SerializeField]
        private float defaultValue;
        [SerializeField]
        private float minValue = float.NegativeInfinity;
        [SerializeField]
        private float maxValue = float.PositiveInfinity;
        #endregion

        #region 运行时字段
        [SerializeField, HideInInspector]
        private float baseValue;
        [SerializeField, HideInInspector]
        private float currentValue;
        [SerializeField, HideInInspector]
        private GameplayAttributeSet ownerSet;
        [NonSerialized]
        private AttributeAggregator aggregator;
        #endregion

        #region 属性
        /// <summary>获取 Attribute 身份。</summary>
        public GameplayAttribute Attribute => attribute;

        /// <summary>获取作者分类。</summary>
        public GameplayAttributeType Type => type;

        /// <summary>获取运行时初始化与重置使用的默认值。</summary>
        public float DefaultValue => defaultValue;

        /// <summary>获取固定最小值；NegativeInfinity 表示无下限。</summary>
        public float MinValue => minValue;

        /// <summary>获取固定最大值；PositiveInfinity 表示无上限。</summary>
        public float MaxValue => maxValue;

        internal float BaseValue => baseValue;

        /// <summary>获取经过当前聚合结果计算后的有效值。</summary>
        public float CurrentValue => currentValue;

        internal GameplayAttributeSet OwnerSet => ownerSet;

        internal AttributeAggregator Aggregator => aggregator ??= new AttributeAggregator();
        #endregion

        #region 运行时构造与修改
        /// <summary>为 Unity 内联序列化创建空配置模板；运行时由 Container 克隆。</summary>
        public GameplayAttributeDefinition()
        {
        }

        // 从 Set 模板复制全部配置，并创建互不共享的运行时数值实例。
        private GameplayAttributeDefinition(
            GameplayAttributeDefinition source,
            GameplayAttributeSet owner)
        {
            attribute = source.attribute;
            type = source.type;
            defaultValue = source.defaultValue;
            minValue = source.minValue;
            maxValue = source.maxValue;
            baseValue = source.defaultValue;
            currentValue = source.defaultValue;
            ownerSet = owner;
            aggregator = new AttributeAggregator();
        }

        // 创建 Container 专用运行时副本，禁止直接复用 Set 中的引用对象。
        internal static GameplayAttributeDefinition CreateRuntimeCopy(
            GameplayAttributeDefinition source,
            GameplayAttributeSet owner) =>
            new(source, owner);

        // 仅由 Container 在完成 Pre 校验后提交 BaseValue。
        internal void SetBaseValue(float value) => baseValue = value;

        // 仅由 Container 在完成 Pre 校验后提交 CurrentValue。
        internal void SetCurrentValue(float value) => currentValue = value;

        // 丢弃全部运行时 Modifier，并让 CurrentValue 回到不含 Modifier 的 BaseValue。
        internal void ResetAggregation()
        {
            aggregator = new AttributeAggregator();
            currentValue = baseValue;
        }

        /// <summary>校验模板能否安全导入运行时 List，不修改任何状态。</summary>
        /// <param name="error">失败时返回具体配置问题。</param>
        /// <returns>Attribute 与全部数值字段合法时返回 true。</returns>
        public bool TryValidateTemplate(out string error)
        {
            if (!attribute.IsValid)
            {
                error = "AttributeId 非法。";
                return false;
            }

            if (type != GameplayAttributeType.Stat &&
                type != GameplayAttributeType.Resource)
            {
                error = $"Attribute {attribute.Id} 的 Type 非法。";
                return false;
            }

            if (float.IsNaN(defaultValue) || float.IsInfinity(defaultValue))
            {
                error = $"Attribute {attribute.Id} 的 DefaultValue 必须是有限数值。";
                return false;
            }

            if (float.IsNaN(minValue) || float.IsPositiveInfinity(minValue))
            {
                error = $"Attribute {attribute.Id} 的 MinValue 只能是有限值或 NegativeInfinity。";
                return false;
            }

            if (float.IsNaN(maxValue) || float.IsNegativeInfinity(maxValue))
            {
                error = $"Attribute {attribute.Id} 的 MaxValue 只能是有限值或 PositiveInfinity。";
                return false;
            }

            if (minValue > maxValue)
            {
                error = $"Attribute {attribute.Id} 的 MinValue 不能大于 MaxValue。";
                return false;
            }

            if (defaultValue < minValue || defaultValue > maxValue)
            {
                error = $"Attribute {attribute.Id} 的 DefaultValue 不在固定边界内。";
                return false;
            }

            error = string.Empty;
            return true;
        }
        #endregion
    }
}