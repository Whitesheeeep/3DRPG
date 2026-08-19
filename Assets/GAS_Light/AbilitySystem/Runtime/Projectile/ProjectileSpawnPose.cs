using UnityEngine;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>保存一枚投射物在世界空间中的生成位置、旋转和移动方向。</summary>
    public readonly struct ProjectileSpawnPose
    {
        /// <summary>创建一枚不可变的投射物世界 Pose。</summary>
        /// <param name="position">世界生成位置。</param>
        /// <param name="rotation">世界生成旋转。</param>
        /// <param name="direction">世界移动方向。</param>
        public ProjectileSpawnPose(Vector3 position, Quaternion rotation, Vector3 direction)
        {
            Position = position;
            Rotation = rotation;
            Direction = direction;
        }

        /// <summary>获取世界生成位置。</summary>
        public Vector3 Position { get; }
        /// <summary>获取世界生成旋转。</summary>
        public Quaternion Rotation { get; }
        /// <summary>获取世界移动方向。</summary>
        public Vector3 Direction { get; }
    }
}
