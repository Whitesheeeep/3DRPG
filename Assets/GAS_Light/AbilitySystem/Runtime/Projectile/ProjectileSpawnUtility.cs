using UnityEngine;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>按局部发射参数计算批量 Projectile 的世界 Pose，不持有对象或 Ability 状态。</summary>
    public static class ProjectileSpawnUtility
    {
        /// <summary>计算当前批次指定序号的投射物 Pose。</summary>
        /// <param name="origin">Marker 或 Source Transform 发射参考。</param>
        /// <param name="localPosition">相对参考 Transform 的局部位置。</param>
        /// <param name="localEulerAngles">相对参考 Transform 的局部旋转。</param>
        /// <param name="spreadAngle">全部投射物覆盖的总扇形角。</param>
        /// <param name="projectileCount">本批次发射数量。</param>
        /// <param name="index">当前投射物在批次中的零基序号。</param>
        /// <returns>当前投射物的世界生成 Pose。</returns>
        public static ProjectileSpawnPose CalculatePose(
            Transform origin,
            Vector3 localPosition,
            Vector3 localEulerAngles,
            float spreadAngle,
            int projectileCount,
            int index)
        {
            return CalculatePose(origin.localToWorldMatrix, localPosition, localEulerAngles,
                spreadAngle, projectileCount, index);
        }

        /// <summary>使用指定世界矩阵计算编辑器任意帧的投射物 Pose。</summary>
        /// <param name="originMatrix">Marker 在目标帧的世界矩阵。</param>
        /// <param name="localPosition">相对 Marker 的局部位置。</param>
        /// <param name="localEulerAngles">相对 Marker 的局部旋转。</param>
        /// <param name="spreadAngle">全部投射物覆盖的总扇形角。</param>
        /// <param name="projectileCount">本批次发射数量。</param>
        /// <param name="index">当前投射物在批次中的零基序号。</param>
        /// <returns>当前投射物的世界生成 Pose。</returns>
        public static ProjectileSpawnPose CalculatePose(
            Matrix4x4 originMatrix,
            Vector3 localPosition,
            Vector3 localEulerAngles,
            float spreadAngle,
            int projectileCount,
            int index)
        {
            Vector3 originForward = originMatrix.MultiplyVector(Vector3.forward).normalized;
            Vector3 originUp = originMatrix.MultiplyVector(Vector3.up).normalized;
            if (originForward.sqrMagnitude <= 0f) originForward = Vector3.forward;
            if (originUp.sqrMagnitude <= 0f) originUp = Vector3.up;
            Quaternion baseRotation = Quaternion.LookRotation(originForward, originUp) *
                                       Quaternion.Euler(localEulerAngles);
            float offsetAngle = projectileCount <= 1
                ? 0f
                : -spreadAngle * 0.5f + spreadAngle * index / (projectileCount - 1);
            Quaternion rotation = Quaternion.AngleAxis(offsetAngle, originUp) * baseRotation;
            Vector3 direction = rotation * Vector3.forward;
            return new ProjectileSpawnPose(
                originMatrix.MultiplyPoint3x4(localPosition),
                rotation,
                direction.sqrMagnitude > 0f ? direction.normalized : originForward);
        }
    }
}
