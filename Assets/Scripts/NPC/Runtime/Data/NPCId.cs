using System;

namespace RPG.NPC
{
    /// <summary>
    /// 表示可跨场景定位同一剧情角色的稳定 NPC 标识。
    /// </summary>
    public readonly struct NPCId : IEquatable<NPCId>, IComparable<NPCId>
    {
        /// <summary>NPC 稳定标识允许的最大字符数。</summary>
        public const int MaxLength = 128;

        /// <summary>创建 NPC 标识并校验其可作为稳定注册键。</summary>
        /// <param name="value">区分大小写的稳定 NPC 字符串。</param>
        /// <exception cref="ArgumentException">标识为空白、过长或含有路径分隔符时抛出。</exception>
        public NPCId(string value)
        {
            if (!IsValidValue(value))
                throw new ArgumentException("NPC ID 必须是非空且不超过 128 个字符的稳定标识。", nameof(value));

            Value = value;
        }

        /// <summary>获取稳定字符串值。</summary>
        public string Value { get; }

        /// <summary>获取当前 ID 是否有效。</summary>
        public bool IsValid => !string.IsNullOrEmpty(Value);

        /// <summary>尝试从 Inspector 字符串创建稳定 NPC 标识。</summary>
        /// <param name="value">待校验字符串。</param>
        /// <param name="npcId">创建成功后的 NPC 标识。</param>
        /// <returns>字符串符合规则时返回 true。</returns>
        public static bool TryCreate(string value, out NPCId npcId)
        {
            if (IsValidValue(value))
            {
                npcId = new NPCId(value);
                return true;
            }

            npcId = default;
            return false;
        }

        /// <summary>按 Ordinal 规则比较 NPC 标识。</summary>
        /// <param name="other">待比较标识。</param>
        /// <returns>比较结果。</returns>
        public int CompareTo(NPCId other) =>
            string.Compare(Value ?? string.Empty, other.Value ?? string.Empty, StringComparison.Ordinal);

        /// <summary>判断 NPC 标识是否相等。</summary>
        /// <param name="other">待比较标识。</param>
        /// <returns>字符串值按 Ordinal 规则相等时返回 true。</returns>
        public bool Equals(NPCId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        /// <summary>判断对象是否为相同 NPC 标识。</summary>
        /// <param name="obj">待比较对象。</param>
        /// <returns>对象为相同标识时返回 true。</returns>
        public override bool Equals(object obj) => obj is NPCId other && Equals(other);

        /// <summary>获取与 Ordinal 相等规则一致的哈希值。</summary>
        /// <returns>标识哈希值。</returns>
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);

        /// <summary>返回稳定字符串值。</summary>
        /// <returns>稳定 NPC 字符串。</returns>
        public override string ToString() => Value ?? string.Empty;

        /// <summary>判断两个 NPC 标识是否相等。</summary>
        /// <param name="left">左侧标识。</param>
        /// <param name="right">右侧标识。</param>
        /// <returns>相等时返回 true。</returns>
        public static bool operator ==(NPCId left, NPCId right) => left.Equals(right);

        /// <summary>判断两个 NPC 标识是否不相等。</summary>
        /// <param name="left">左侧标识。</param>
        /// <param name="right">右侧标识。</param>
        /// <returns>不相等时返回 true。</returns>
        public static bool operator !=(NPCId left, NPCId right) => !left.Equals(right);

        /// <summary>验证 NPC ID 的稳定键格式。</summary>
        /// <param name="value">待验证文本。</param>
        /// <returns>文本非空、长度受限且不包含路径或控制分隔符时返回 true。</returns>
        private static bool IsValidValue(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.Length <= MaxLength &&
                   value.IndexOfAny(new[] { '/', '\\', '\r', '\n', '\t' }) < 0;
        }
    }
}
