using System;

namespace RPG.Game.UI.Escape
{
    /// <summary>
    /// 标识一次 Esc Command 注册，用于窗口隐藏时精确注销对应的退出动作。
    /// </summary>
    public readonly struct EscCommandRegistration : IEquatable<EscCommandRegistration>
    {
        /// <summary>创建 Esc 注册标识。</summary>
        /// <param name="id">Manager 分配的注册序号。</param>
        public EscCommandRegistration(long id)
        {
            Id = id;
        }

        /// <summary>获取注册序号。</summary>
        public long Id { get; }

        /// <summary>判断注册标识是否有效。</summary>
        public bool IsValid => Id > 0;

        /// <summary>判断两个注册标识是否相等。</summary>
        /// <param name="other">另一个注册标识。</param>
        /// <returns>相等时返回 true。</returns>
        public bool Equals(EscCommandRegistration other) => Id == other.Id;

        /// <summary>判断对象是否为相同注册标识。</summary>
        /// <param name="obj">待比较对象。</param>
        /// <returns>相等时返回 true。</returns>
        public override bool Equals(object obj) => obj is EscCommandRegistration other && Equals(other);

        /// <summary>获取注册标识哈希值。</summary>
        /// <returns>哈希值。</returns>
        public override int GetHashCode() => Id.GetHashCode();

        /// <summary>判断两个注册标识是否相等。</summary>
        public static bool operator ==(EscCommandRegistration left, EscCommandRegistration right) => left.Equals(right);

        /// <summary>判断两个注册标识是否不相等。</summary>
        public static bool operator !=(EscCommandRegistration left, EscCommandRegistration right) => !left.Equals(right);
    }
}
