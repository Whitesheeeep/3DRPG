using System;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.AttributeSystem;

namespace RPG.Character
{
    /// <summary>定义角色资源初始值的来源。</summary>
    public enum CharacterResourceInitialValueMode
    {
        /// <summary>使用 Resource Definition 的 DefaultValue。</summary>
        [InspectorName("资源定义默认值")]
        DefinitionDefault = 0,
        /// <summary>使用对应容量属性的当前值作为初始值。</summary>
        [InspectorName("容量属性当前值")]
        FullCapacity = 1
    }

    /// <summary>声明一个资源属性及其可选容量属性的关系。</summary>
    [Serializable]
    public sealed class CharacterResourceRule
    {
        #region 配置字段

        [SerializeField, LabelText("资源 Attribute")] private GameplayAttribute resourceAttribute = GameplayAttribute.Empty;
        [SerializeField, LabelText("容量 Attribute")] private GameplayAttribute capacityAttribute = GameplayAttribute.Empty;
        [SerializeField, LabelText("初始值模式"), Tooltip("选择资源的初始值来源，可从资源定义的默认值或容量属性的当前值中选择。")] private CharacterResourceInitialValueMode initialValueMode;

        #endregion

        #region 属性

        /// <summary>获取资源 Attribute。</summary>
        public GameplayAttribute ResourceAttribute => resourceAttribute;

        /// <summary>获取容量 Attribute；无容量映射时为空。</summary>
        public GameplayAttribute CapacityAttribute => capacityAttribute;

        /// <summary>获取资源初始值模式。</summary>
        public CharacterResourceInitialValueMode InitialValueMode => initialValueMode;

        #endregion

        #region 生命周期

        /// <summary>创建空资源规则，供 Unity 序列化和编辑器配置使用。</summary>
        public CharacterResourceRule()
        {
        }

        /// <summary>创建运行时资源规则。</summary>
        /// <param name="resource">资源 Attribute。</param>
        /// <param name="capacity">容量 Attribute；无容量时传 Empty。</param>
        /// <param name="mode">资源初始值模式。</param>
        public CharacterResourceRule(
            GameplayAttribute resource,
            GameplayAttribute capacity,
            CharacterResourceInitialValueMode mode)
        {
            resourceAttribute = resource;
            capacityAttribute = capacity;
            initialValueMode = mode;
        }

        #endregion
    }

    /// <summary>保存一个角色资源的当前值；Stat 不进入该快照。</summary>
    public readonly struct CharacterResourceValue
    {
        /// <summary>创建资源快照值。</summary>
        /// <param name="attribute">资源 Attribute。</param>
        /// <param name="currentValue">资源当前值。</param>
        public CharacterResourceValue(GameplayAttribute attribute, float currentValue)
        {
            Attribute = attribute;
            CurrentValue = currentValue;
        }

        /// <summary>获取资源 Attribute。</summary>
        public GameplayAttribute Attribute { get; }

        /// <summary>获取资源当前值。</summary>
        public float CurrentValue { get; }
    }
}
