using System;
using UnityEngine;

namespace WS_Modules.Utilities
{
    /// <summary>
    /// 标识通用 Physics 查询和 Scene Handle 支持的几何类型。
    /// </summary>
    public enum PhysicsShapeType
    {
        /// <summary>未选择有效几何类型。</summary>
        None = 0,
        /// <summary>盒体。</summary>
        Box = 1,
        /// <summary>球体。</summary>
        Sphere = 2,
        /// <summary>胶囊体。</summary>
        Capsule = 3,
        /// <summary>水平扇形柱体。</summary>
        Sector = 4,
        /// <summary>射线。</summary>
        Ray = 5
    }

    /// <summary>
    /// 标识胶囊体在局部空间中沿用的轴向。
    /// </summary>
    public enum PhysicsCapsuleAxis
    {
        /// <summary>局部 X 轴。</summary>
        X = 0,
        /// <summary>局部 Y 轴。</summary>
        Y = 1,
        /// <summary>局部 Z 轴。</summary>
        Z = 2
    }

    /// <summary>
    /// 保存相对于所属 Transform 的通用 Physics 形状数据。
    /// </summary>
    [Serializable]
    public sealed class PhysicsShapeData
    {
        #region 类型与公共姿态
        [SerializeField]
        private PhysicsShapeType type = PhysicsShapeType.Box;
        [SerializeField, Tooltip("是否允许宿主的 OnDrawGizmos 绘制该形状")]
        private bool canDrawGizmos = true;
        [SerializeField]
        private Vector3 localPosition;
        [SerializeField]
        private Vector3 localEulerAngles;

        /// <summary>获取当前形状类型。</summary>
        public PhysicsShapeType Type => type;

        /// <summary>获取是否允许宿主的 OnDrawGizmos 绘制该形状。</summary>
        public bool CanDrawGizmos => canDrawGizmos;

        /// <summary>获取相对于宿主 Transform 的局部位置。</summary>
        public Vector3 LocalPosition => localPosition;

        /// <summary>获取相对于宿主 Transform 的局部欧拉角。</summary>
        public Vector3 LocalEulerAngles => localEulerAngles;
        #endregion

        #region 形状尺寸
        [SerializeField]
        private Vector3 size = Vector3.one;
        [SerializeField, Min(0.001f)]
        private float radius = 0.5f;
        [SerializeField, Min(0.001f)]
        private float height = 2f;
        [SerializeField]
        private PhysicsCapsuleAxis capsuleAxis = PhysicsCapsuleAxis.Y;
        [SerializeField, Min(0f)]
        private float innerRadius;
        [SerializeField, Min(0.001f)]
        private float outerRadius = 2f;
        [SerializeField, Range(0.01f, 360f)]
        private float angle = 90f;
        [SerializeField, Min(0.001f)]
        private float length = 5f;

        /// <summary>获取 Box 的完整局部尺寸。</summary>
        public Vector3 Size => size;

        /// <summary>获取 Sphere 或 Capsule 的半径。</summary>
        public float Radius => radius;

        /// <summary>获取 Capsule 或 Sector 的局部高度。</summary>
        public float Height => height;

        /// <summary>获取 Capsule 的局部轴向。</summary>
        public PhysicsCapsuleAxis CapsuleAxis => capsuleAxis;

        /// <summary>获取 Sector 的内半径。</summary>
        public float InnerRadius => innerRadius;

        /// <summary>获取 Sector 的外半径。</summary>
        public float OuterRadius => outerRadius;

        /// <summary>获取 Sector 的水平夹角，单位为度。</summary>
        public float Angle => angle;

        /// <summary>获取 Ray 的局部长度。</summary>
        public float Length => length;
        #endregion

        #region 构造

        /// <summary>
        /// 创建使用默认 Box 参数的通用 Physics 形状。
        /// </summary>
        public PhysicsShapeData()
        {
        }

        #endregion

        #region 调试可视化

        /// <summary>
        /// 在运行时按 Physics 查询的世界空间换算绘制当前形状的 Debug 线框。
        /// </summary>
        /// <param name="attachedTransform">局部位置和旋转所属的宿主 Transform。</param>
        /// <param name="color">线框颜色。</param>
        /// <param name="duration">线段持续显示的秒数；零表示仅显示当前帧。</param>
        /// <param name="depthTest">是否允许线段被场景中的遮挡物隐藏。</param>
        /// <param name="segments">曲线形状的离散线段数，必须不小于 4。</param>
        /// <exception cref="ArgumentNullException">attachedTransform 为空。</exception>
        public void DrawDebug(Transform attachedTransform, Color color, float duration = 0f,
            bool depthTest = true, int segments = 24)
        {
            if (attachedTransform == null)
                throw new ArgumentNullException(nameof(attachedTransform));

            // 显式 Debug 调用不受 CanDrawGizmos 控制，避免把编辑器显示开关误当作运行时开关。
            DebugUtility.DrawPhysicsShape(attachedTransform, this, color, duration,
                depthTest, segments);
        }

        /// <summary>
        /// 由宿主 MonoBehaviour 在 OnDrawGizmos 或 OnDrawGizmosSelected 中调用，绘制当前形状。
        /// </summary>
        /// <param name="attachedTransform">局部位置和旋转所属的宿主 Transform。</param>
        /// <exception cref="ArgumentNullException">attachedTransform 为空。</exception>
        public void OnDrawGizmos(Transform attachedTransform)
        {
            if (attachedTransform == null) throw new ArgumentNullException(nameof(attachedTransform));
            if (!CanDrawGizmos) return;
            PhysicsShapeGizmoDrawer.Draw(attachedTransform, this);
        }

        #endregion
    }
}
