using UnityEngine;

namespace RPG.Character
{
    /// <summary>保存一个物理步内由运动生产者提交的世界空间位移和旋转。</summary>
    public readonly struct FixedMotionRequest
    {
        /// <summary>创建 Fixed 运动提交。</summary>
        /// <param name="translation">本物理步希望应用的世界空间位移。</param>
        /// <param name="rotation">本物理步希望附加到 CharacterRoot 的旋转。</param>
        public FixedMotionRequest(Vector3 translation, Quaternion rotation)
        {
            Translation = translation;
            Rotation = rotation;
        }

        /// <summary>获取世界空间位移。</summary>
        public Vector3 Translation { get; }
        /// <summary>获取附加旋转。</summary>
        public Quaternion Rotation { get; }

        /// <summary>创建只包含位移的运动提交。</summary>
        /// <param name="translation">本物理步世界空间位移。</param>
        /// <returns>旋转为单位四元数的提交。</returns>
        public static FixedMotionRequest TranslationOnly(Vector3 translation) =>
            new FixedMotionRequest(translation, Quaternion.identity);
    }
}
