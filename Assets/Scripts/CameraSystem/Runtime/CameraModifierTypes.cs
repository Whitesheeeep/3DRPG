using System;
using UnityEngine;

namespace RPG.CameraSystem
{
    /// <summary>
    /// 标识修饰状态影响的独立摄像机通道。
    /// </summary>
    [Flags]
    public enum CameraModifierChannel
    {
        None = 0,
        Lens = 1 << 0,
        Shake = 1 << 1
    }

    /// <summary>
    /// 作为 Manager 内部请求表的稳定外部句柄。
    /// </summary>
    public readonly struct CameraModifierHandle : IEquatable<CameraModifierHandle>
    {
        internal int Id { get; }

        /// <summary>创建内部句柄。</summary>
        internal CameraModifierHandle(int id) => Id = id;

        /// <summary>判断两个句柄是否指向同一请求。</summary>
        public bool Equals(CameraModifierHandle other) => Id == other.Id;
        /// <summary>判断对象是否为同一请求句柄。</summary>
        public override bool Equals(object obj) => obj is CameraModifierHandle other && Equals(other);
        /// <summary>返回句柄稳定哈希。</summary>
        public override int GetHashCode() => Id;
    }

    /// <summary>
    /// 保存一个技能当前帧已经求值完成的摄像机修饰结果。
    /// </summary>
    public readonly struct CameraModifierState
    {
        public CameraModifierChannel AffectedChannels { get; }
        public CameraModifierChannel ExclusiveChannels { get; }
        public float FovScale { get; }
        public Vector3 LocalPositionOffset { get; }
        public Vector3 LocalRotationOffset { get; }

        /// <summary>创建不可变逐帧修饰结果。</summary>
        public CameraModifierState(CameraModifierChannel affectedChannels,
            CameraModifierChannel exclusiveChannels, float fovScale,
            Vector3 localPositionOffset, Vector3 localRotationOffset)
        {
            AffectedChannels = affectedChannels;
            ExclusiveChannels = exclusiveChannels & affectedChannels;
            FovScale = Mathf.Max(0.01f, fovScale);
            LocalPositionOffset = localPositionOffset;
            LocalRotationOffset = localRotationOffset;
        }

        public static CameraModifierState Identity => new(CameraModifierChannel.None,
            CameraModifierChannel.None, 1f, Vector3.zero, Vector3.zero);
    }
}
