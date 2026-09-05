using UnityEngine;

namespace RPG.Character
{
    /// <summary>保存一次 Animator 阶段由业务提交的世界空间根运动增量。</summary>
    public readonly struct AnimatorMotionSubmission
    {
        /// <summary>创建 Animator 阶段提交。</summary>
        /// <param name="translation">世界空间位移增量。</param>
        /// <param name="rotation">相对 CharacterRoot 的旋转增量。</param>
        public AnimatorMotionSubmission(Vector3 translation, Quaternion rotation)
        {
            Translation = translation;
            Rotation = rotation;
        }

        /// <summary>获取世界空间位移增量。</summary>
        public Vector3 Translation { get; }
        /// <summary>获取相对根节点的旋转增量。</summary>
        public Quaternion Rotation { get; }
    }
}
