using System;
using UnityEngine;

namespace WS_Modules.GAS.TAG
{
    /// <summary>
    /// 表示一个运行时 Gameplay Tag；身份和值相等性仅由稳定 TagId 决定。
    /// </summary>
    [Serializable]
    public struct GameplayTag : IEquatable<GameplayTag>
    {
        #region 常量与字段
        /// <summary>非法 Tag 使用的保留 ID。</summary>
        public const int InvalidId = -1;
        /// <summary>表示未设置或非法标签的空值。</summary>
        public static readonly GameplayTag Empty = new GameplayTag(InvalidId);
        [SerializeField]
        private int id;
        #endregion

        #region 属性
        /// <summary>获取稳定 TagId。</summary>
        public int Id => id;
        /// <summary>获取该值是否可能表示有效标签；是否存在需由 Manager 验证。</summary>
        public bool IsValid => id >= 0;
        #endregion

        #region 构造与比较
        /// <summary>使用稳定 ID 创建 Gameplay Tag。</summary>
        /// <param name="id">稳定 TagId；负数表示非法标签。</param>
        public GameplayTag(int id) => this.id = id;

        /// <summary>判断两个标签是否具有相同 TagId。</summary>
        public bool Equals(GameplayTag other) => id == other.id;

        /// <summary>判断对象是否为具有相同 TagId 的 Gameplay Tag。</summary>
        public override bool Equals(object obj) => obj is GameplayTag other && Equals(other);

        /// <summary>返回基于 TagId 的哈希码。</summary>
        public override int GetHashCode() => id;

        /// <summary>判断当前实际标签是否匹配查询标签；实际子标签可匹配自身及任意祖先。比如：A.1 可以匹配 A.1 或者 A，但是 A 不能匹配 A.1。</summary>
        /// <param name="queryTag">作为查询条件的标签。</param>
        /// <returns>当前标签存在且能满足查询条件时返回 true。</returns>
        public bool MatchesTag(GameplayTag queryTag) => GameplayTagManager.Instance.MatchesTag(this, queryTag);

        /// <summary>判断当前实际标签是否与查询标签精确相等且均存在于数据库。</summary>
        public bool MatchesTagExact(GameplayTag queryTag) =>
            GameplayTagManager.Instance.MatchesTagExact(this, queryTag);

        /// <summary>返回用于日志和调试的稳定 ID 文本。</summary>
        public override string ToString() => IsValid ? $"GameplayTag({id})" : "GameplayTag.Empty";
        #endregion

        #region 运算符
        /// <summary>判断两个标签的 TagId 是否相同。</summary>
        public static bool operator ==(GameplayTag left, GameplayTag right) => left.Equals(right);

        /// <summary>判断两个标签的 TagId 是否不同。</summary>
        public static bool operator !=(GameplayTag left, GameplayTag right) => !left.Equals(right);
        #endregion
    }
}