using System;

namespace RPG.CameraSystem
{
    /// <summary>
    /// 指定持续 Shake 是自动结束，还是保持到调用方主动停止。
    /// </summary>
    public enum CameraShakeLifetime
    {
        Timed,
        Sustained
    }

    /// <summary>
    /// 保存 Manager 内部持续 Shake 请求的稳定外部句柄。
    /// </summary>
    public readonly struct CameraShakeHandle : IEquatable<CameraShakeHandle>
    {
        internal int Id { get; }

        /// <summary>
        /// 创建只允许 Manager 分配的内部句柄。
        /// </summary>
        /// <param name="id">请求表中的稳定编号。</param>
        internal CameraShakeHandle(int id) => Id = id;

        /// <summary>
        /// 判断两个句柄是否指向同一请求编号。
        /// </summary>
        /// <param name="other">待比较句柄。</param>
        /// <returns>编号相同时返回 true。</returns>
        public bool Equals(CameraShakeHandle other) => Id == other.Id;

        /// <summary>
        /// 判断对象是否为相同 Shake 句柄。
        /// </summary>
        /// <param name="obj">待比较对象。</param>
        /// <returns>类型和编号均相同时返回 true。</returns>
        public override bool Equals(object obj) => obj is CameraShakeHandle other && Equals(other);

        /// <summary>
        /// 返回句柄编号对应的稳定哈希值。
        /// </summary>
        /// <returns>句柄哈希值。</returns>
        public override int GetHashCode() => Id;
    }

    /// <summary>
    /// 保存 Manager 内部单次 Impulse 事件的稳定外部句柄。
    /// </summary>
    public readonly struct CameraImpulseHandle : IEquatable<CameraImpulseHandle>
    {
        internal int Id { get; }

        /// <summary>
        /// 创建只允许 Manager 分配的内部句柄。
        /// </summary>
        /// <param name="id">Impulse 请求表中的稳定编号。</param>
        internal CameraImpulseHandle(int id) => Id = id;

        /// <summary>
        /// 判断两个句柄是否指向同一 Impulse 事件。
        /// </summary>
        /// <param name="other">待比较句柄。</param>
        /// <returns>编号相同时返回 true。</returns>
        public bool Equals(CameraImpulseHandle other) => Id == other.Id;

        /// <summary>
        /// 判断对象是否为相同 Impulse 句柄。
        /// </summary>
        /// <param name="obj">待比较对象。</param>
        /// <returns>类型和编号均相同时返回 true。</returns>
        public override bool Equals(object obj) => obj is CameraImpulseHandle other && Equals(other);

        /// <summary>
        /// 返回句柄编号对应的稳定哈希值。
        /// </summary>
        /// <returns>句柄哈希值。</returns>
        public override int GetHashCode() => Id;
    }
}
