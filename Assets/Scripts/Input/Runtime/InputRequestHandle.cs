using System;

namespace RPG.PlayerInputSystem
{
    /// <summary>唯一标识某输入类型、手势版本和 Press 或 Release 阶段。</summary>
    [Serializable]
    public readonly struct InputRequestHandle : IEquatable<InputRequestHandle>
    {
        #region 属性
        /// <summary>获取输入类型。是哪个意思：跳跃？</summary>
        public PlayerInputType InputType { get; }
        /// <summary>获取手势版本。</summary>
        public uint GestureVersion { get; }
        /// <summary>获取请求阶段。按下还是释放。</summary>
        public PlayerInputRequestStage Stage { get; }
        #endregion

        #region 构造与比较
        /// <summary>创建输入阶段句柄。</summary>
        /// <param name="inputType">输入类型。</param>
        /// <param name="gestureVersion">手势版本。</param>
        /// <param name="stage">请求阶段。</param>
        public InputRequestHandle(PlayerInputType inputType, uint gestureVersion, PlayerInputRequestStage stage)
        {
            InputType = inputType;
            GestureVersion = gestureVersion;
            Stage = stage;
        }

        /// <summary>判断两个句柄是否指向同一请求阶段。</summary>
        /// <param name="other">另一个句柄。</param>
        /// <returns>全部身份字段一致时返回 true。</returns>
        public bool Equals(InputRequestHandle other) => InputType == other.InputType &&
                                                        GestureVersion == other.GestureVersion &&
                                                        Stage == other.Stage;

        /// <summary>判断对象是否为相同句柄。</summary>
        /// <param name="obj">待比较对象。</param>
        /// <returns>对象为相同句柄时返回 true。</returns>
        public override bool Equals(object obj) => obj is InputRequestHandle other && Equals(other);

        /// <summary>返回句柄身份字段组合的哈希码。</summary>
        /// <returns>稳定哈希码。</returns>
        public override int GetHashCode() => HashCode.Combine((int)InputType, GestureVersion, (int)Stage);

        /// <summary>返回便于诊断的句柄文本。</summary>
        /// <returns>包含输入类型、版本和阶段的文本。</returns>
        public override string ToString() => $"{InputType}:{GestureVersion}:{Stage}";

        /// <summary>判断两个句柄是否相等。</summary>
        public static bool operator ==(InputRequestHandle left, InputRequestHandle right) => left.Equals(right);

        /// <summary>判断两个句柄是否不相等。</summary>
        public static bool operator !=(InputRequestHandle left, InputRequestHandle right) => !left.Equals(right);
        #endregion
    }
}
