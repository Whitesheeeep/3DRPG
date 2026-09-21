using System;

namespace WS_Modules.GAS.AttributeSystem
{
    /// <summary>描述一次需要写入 GAS BaseValue 或 Resource CurrentValue 的属性值。</summary>
    public readonly struct GameplayAttributeValue
    {
        /// <summary>创建属性值。</summary>
        /// <param name="attribute">目标 Gameplay Attribute。</param>
        /// <param name="value">待写入的有限数值。</param>
        /// <exception cref="ArgumentException">Attribute 无效时抛出。</exception>
        public GameplayAttributeValue(GameplayAttribute attribute, float value)
        {
            if (!attribute.IsValid)
                throw new ArgumentException("GameplayAttributeValue 必须包含有效 Attribute。", nameof(attribute));
            Attribute = attribute;
            Value = value;
        }

        /// <summary>获取目标 Attribute。</summary>
        public GameplayAttribute Attribute { get; }

        /// <summary>获取待写入数值。</summary>
        public float Value { get; }
    }
}
