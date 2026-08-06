using System;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>表示单个 GameplayAbilityCtrl 内部单调分配且不复用的 Ability Spec 标识。</summary>
    public readonly struct GameplayAbilityHandle : IEquatable<GameplayAbilityHandle>
    {
        #region 常量与属性
        /// <summary>获取不指向任何 Ability Spec 的保留值。</summary>
        public static GameplayAbilityHandle Invalid => default;
        /// <summary>获取当前 Controller 内部的 Spec 标识；零表示非法。</summary>
        public int Id { get; }
        /// <summary>获取该 Handle 是否具有非零标识。</summary>
        public bool IsValid => Id != 0;
        #endregion

        #region 构造与比较
        // Handle 只能由 GameplayAbilityCtrl 分配，避免调用方伪造有效身份。
        internal GameplayAbilityHandle(int id) => Id = id;

        /// <summary>判断两个 Handle 是否具有相同标识。</summary>
        public bool Equals(GameplayAbilityHandle other) => Id == other.Id;

        /// <summary>判断对象是否为相同标识的 GameplayAbilityHandle。</summary>
        public override bool Equals(object obj) => obj is GameplayAbilityHandle other && Equals(other);

        /// <summary>返回基于 Handle 标识的哈希码。</summary>
        public override int GetHashCode() => Id;

        /// <summary>返回便于日志识别的 Handle 文本。</summary>
        public override string ToString() => IsValid ? $"GameplayAbilityHandle({Id})" : "Invalid";
        #endregion

        #region 运算符
        /// <summary>判断两个 Handle 是否相等。</summary>
        public static bool operator ==(GameplayAbilityHandle left, GameplayAbilityHandle right) => left.Equals(right);

        /// <summary>判断两个 Handle 是否不相等。</summary>
        public static bool operator !=(GameplayAbilityHandle left, GameplayAbilityHandle right) => !left.Equals(right);
        #endregion
    }
}