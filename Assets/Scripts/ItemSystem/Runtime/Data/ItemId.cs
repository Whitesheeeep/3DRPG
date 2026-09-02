using System;
using Sirenix.OdinInspector;

namespace RPG.ItemSystem
{
    /// <summary>物品定义使用的稳定字符串标识。</summary>
    [Serializable]
    public struct ItemId : IEquatable<ItemId>, IComparable<ItemId>
    {
        [UnityEngine.SerializeField, LabelText("值")] private string value;

        /// <summary>创建物品标识。</summary>
        /// <param name="value">非空稳定标识。</param>
        public ItemId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("ItemId 不能为空。", nameof(value));
            this.value = value.Trim();
        }

        /// <summary>获取原始字符串值。</summary>
        public string Value => value;

        /// <summary>判断标识是否有效。</summary>
        public bool IsValid => !string.IsNullOrWhiteSpace(value);

        /// <summary>尝试创建物品标识。</summary>
        /// <param name="rawValue">待解析文本。</param>
        /// <param name="itemId">成功时返回标识。</param>
        /// <returns>文本有效时返回 true。</returns>
        public static bool TryCreate(string rawValue, out ItemId itemId)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                itemId = default;
                return false;
            }

            itemId = new ItemId(rawValue);
            return true;
        }

        /// <summary>比较两个物品标识。</summary>
        /// <param name="other">另一个标识。</param>
        /// <returns>比较结果。</returns>
        public int CompareTo(ItemId other) => string.Compare(Value, other.Value, StringComparison.Ordinal);

        /// <summary>判断两个物品标识是否相同。</summary>
        /// <param name="other">另一个标识。</param>
        /// <returns>相同时返回 true。</returns>
        public bool Equals(ItemId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

        /// <summary>判断对象是否为相同物品标识。</summary>
        /// <param name="obj">待比较对象。</param>
        /// <returns>相同时返回 true。</returns>
        public override bool Equals(object obj) => obj is ItemId other && Equals(other);

        /// <summary>获取物品标识哈希值。</summary>
        /// <returns>哈希值。</returns>
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);

        /// <summary>获取显示文本。</summary>
        /// <returns>原始字符串。</returns>
        public override string ToString() => Value ?? string.Empty;

        /// <summary>比较两个物品标识。</summary>
        public static bool operator ==(ItemId left, ItemId right) => left.Equals(right);

        /// <summary>比较两个物品标识。</summary>
        public static bool operator !=(ItemId left, ItemId right) => !left.Equals(right);
    }
}
