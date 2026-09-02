using System;

namespace RPG.ItemSystem
{
    /// <summary>武器和圣遗物共享的独立装备实例标识。</summary>
    [Serializable]
    public readonly struct EquipmentInstanceId : IEquatable<EquipmentInstanceId>, IComparable<EquipmentInstanceId>
    {
        private readonly string value;

        /// <summary>创建装备实例标识。</summary>
        /// <param name="value">非空稳定字符串。</param>
        public EquipmentInstanceId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("EquipmentInstanceId 不能为空。", nameof(value));
            }

            this.value = value.Trim();
        }

        /// <summary>获取字符串值。</summary>
        public string Value => value;

        /// <summary>判断标识是否有效。</summary>
        public bool IsValid => !string.IsNullOrWhiteSpace(value);

        /// <summary>创建一个新的随机装备实例标识。</summary>
        /// <returns>新的实例标识。</returns>
        public static EquipmentInstanceId Create() => new EquipmentInstanceId(Guid.NewGuid().ToString("N"));

        /// <summary>比较两个实例标识。</summary>
        /// <param name="other">另一个实例标识。</param>
        /// <returns>序数比较结果。</returns>
        public int CompareTo(EquipmentInstanceId other) => string.Compare(Value, other.Value, StringComparison.Ordinal);

        /// <summary>判断两个实例标识是否相等。</summary>
        /// <param name="other">另一个实例标识。</param>
        /// <returns>相等时返回 true。</returns>
        public bool Equals(EquipmentInstanceId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

        /// <summary>判断对象是否为相同实例标识。</summary>
        /// <param name="obj">待比较对象。</param>
        /// <returns>相等时返回 true。</returns>
        public override bool Equals(object obj) => obj is EquipmentInstanceId other && Equals(other);

        /// <summary>获取实例标识哈希值。</summary>
        /// <returns>哈希值。</returns>
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);

        /// <summary>获取实例标识文本。</summary>
        /// <returns>字符串值。</returns>
        public override string ToString() => Value ?? string.Empty;

        /// <summary>比较两个实例标识。</summary>
        public static bool operator ==(EquipmentInstanceId left, EquipmentInstanceId right) => left.Equals(right);

        /// <summary>比较两个实例标识。</summary>
        public static bool operator !=(EquipmentInstanceId left, EquipmentInstanceId right) => !left.Equals(right);
    }
}
