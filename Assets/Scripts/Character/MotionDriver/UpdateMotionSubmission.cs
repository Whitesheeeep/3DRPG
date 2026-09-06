using UnityEngine;

namespace RPG.Character
{
    /// <summary>
    /// 保存一次 Update 阶段由运动生产者提交的世界空间位移和旋转。
    /// </summary>
    public readonly struct UpdateMotionSubmission
    {
        /// <summary>
        /// 创建 Update 阶段运动提交。
        /// </summary>
        /// <param name="translation">当前 Update 希望应用的世界空间位移。</param>
        /// <param name="rotation">当前 Update 希望附加到 CharacterRoot 的旋转。</param>
        public UpdateMotionSubmission(Vector3 translation, Quaternion rotation)
        {
            Translation = translation;
            Rotation = rotation;
        }

        /// <summary>获取世界空间位移。</summary>
        public Vector3 Translation { get; }

        /// <summary>获取附加旋转。</summary>
        public Quaternion Rotation { get; }

        /// <summary>
        /// 创建只包含位移的 Update 提交。
        /// </summary>
        /// <param name="translation">当前 Update 的世界空间位移。</param>
        /// <returns>旋转为单位四元数的 Update 提交。</returns>
        public static UpdateMotionSubmission TranslationOnly(Vector3 translation) =>
            new UpdateMotionSubmission(translation, Quaternion.identity);
    }
}
