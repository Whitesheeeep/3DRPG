using System;
using UnityEngine;

namespace RPG.Character
{
    /// <summary>标识玩家队伍中的一个角色配置。</summary>
    [Serializable]
    public struct CharacterId : IEquatable<CharacterId>
    {
        [SerializeField] private string value;

        /// <summary>使用稳定字符串创建角色标识。</summary>
        /// <param name="value">跨场景保持不变的角色键。</param>
        public CharacterId(string value) => this.value = value;

        /// <inheritdoc />
        public bool Equals(CharacterId other) => string.Equals(value, other.value, StringComparison.Ordinal);

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is CharacterId other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => value == null ? 0 : StringComparer.Ordinal.GetHashCode(value);

        /// <inheritdoc />
        public override string ToString() => value ?? string.Empty;

        /// <summary>比较两个角色标识是否相同。</summary>
        public static bool operator ==(CharacterId left, CharacterId right) => left.Equals(right);

        /// <summary>比较两个角色标识是否不同。</summary>
        public static bool operator !=(CharacterId left, CharacterId right) => !left.Equals(right);
    }
}
