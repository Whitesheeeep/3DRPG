using System;
using UnityEngine;

namespace WS_Modules.GAS.AttributeSystem
{
    /// <summary>表示一个由全局稳定整数 ID 唯一标识的 Gameplay Attribute。</summary>
    [Serializable]
    public struct GameplayAttribute : IEquatable<GameplayAttribute>
    {
        #region 常量与字段

        /// <summary>表示未设置或非法 Attribute 的保留 ID。</summary>
        public const int InvalidId = -1;

        /// <summary>获取空 Attribute。</summary>
        public static readonly GameplayAttribute Empty = new(InvalidId, string.Empty);

        [SerializeField] private int id;
        [SerializeField] private string name;

        #endregion

        #region 属性

        /// <summary>获取全局稳定 AttributeId。</summary>
        public int Id => id;

        /// <summary>获取当前 Registry 提供的运行时展示名称。</summary>
        public string Name => name ?? string.Empty;

        /// <summary>获取 ID 是否位于有效数值范围；是否存在由 Container 验证。</summary>
        public bool IsValid => id >= 0;

        #endregion

        #region 构造与比较

        /// <summary>使用稳定 ID 创建 Gameplay Attribute。</summary>
        /// <param name="id">全局稳定 ID；负数表示非法值。</param>
        public GameplayAttribute(int id)
        {
            this.id = id;
            name = "NONE";
        }

        /// <summary>使用稳定 ID 与展示名称创建 Gameplay Attribute。</summary>
        /// <param name="id">全局稳定 ID；负数表示非法值。</param>
        /// <param name="name">Registry 中的作者名称；可为空。</param>
        public GameplayAttribute(int id, string name)
        {
            this.id = id;
            this.name = name ?? string.Empty;
        }

        /// <summary>判断两个 Attribute 是否具有相同 ID。</summary>
        /// <param name="other">待比较 Attribute。</param>
        /// <returns>ID 相同时返回 true。</returns>
        public bool Equals(GameplayAttribute other) => id == other.id;

        /// <summary>判断对象是否为具有相同 ID 的 Gameplay Attribute。</summary>
        /// <param name="obj">待比较对象。</param>
        /// <returns>对象类型和 ID 都相同时返回 true。</returns>
        public override bool Equals(object obj) => obj is GameplayAttribute other && Equals(other);

        /// <summary>返回基于稳定 ID 的哈希码。</summary>
        /// <returns>稳定 ID。</returns>
        public override int GetHashCode() => id;

        /// <summary>返回用于日志和调试的 AttributeId 文本。</summary>
        /// <returns>有效 ID 或 Empty 文本。</returns>
        public override string ToString()
        {
            if (!IsValid) return "GameplayAttribute.Empty";
            return string.IsNullOrWhiteSpace(Name)
                ? $"GameplayAttribute({id})"
                : $"{Name}({id})";
        }

        #endregion

        #region 运算符

        /// <summary>判断两个 Attribute 的 ID 是否相同。</summary>
        public static bool operator ==(GameplayAttribute left, GameplayAttribute right) => left.Equals(right);

        /// <summary>判断两个 Attribute 的 ID 是否不同。</summary>
        public static bool operator !=(GameplayAttribute left, GameplayAttribute right) => !left.Equals(right);

        #endregion
    }
}
