#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace RPG.SkillSystem.Editor
{
    #region 通用体积 Drawer

    /// <summary>
    /// 集中提供体积检测 Drawer 的角色局部空间换算和通用位置旋转 Handle。
    /// </summary>
    internal abstract class VolumeSceneDrawer<TData> : IAttackDetectionSceneDrawer
        where TData : VolumeAttackDetectionDataBase
    {
        public Type DataType => typeof(TData);
        public virtual bool SupportsHandles => true;
        public bool RequiresWeaponTraceMarkers => false;

        /// <summary>
        /// 绘制具体检测形状。
        /// </summary>
        public abstract void Draw(in AttackDetectionSceneDrawContext context,
            AttackDetectionDataBase data);

        /// <summary>
        /// 根据当前 Unity 工具绘制具体检测数据的单类场景 Handle，并返回独立快照。
        /// </summary>
        /// <param name="context">当前 Clip 的场景绘制上下文。</param>
        /// <param name="data">当前权威数据或本地草稿。</param>
        /// <param name="mode">当前允许编辑的位置、旋转或形状类别。</param>
        /// <returns>本次 Scene GUI 后的独立检测数据。</returns>
        public abstract AttackDetectionDataBase DrawHandles(
            in AttackDetectionSceneDrawContext context, AttackDetectionDataBase data,
            AttackDetectionHandleMode mode);

        /// <summary>
        /// 把配置中的局部位置和旋转转换到当前绑定矩阵的世界空间。
        /// </summary>
        /// <param name="bindingMatrix">当前 Clip 的绑定世界矩阵。</param>
        /// <param name="data">需要换算的局部体积配置。</param>
        /// <param name="position">换算后的世界中心。</param>
        /// <param name="rotation">换算后的世界旋转。</param>
        protected static void ResolveWorldPose(Matrix4x4 bindingMatrix, TData data,
            out Vector3 position, out Quaternion rotation)
        {
            position = bindingMatrix.MultiplyPoint3x4(data.LocalPosition);
            rotation = bindingMatrix.rotation * Quaternion.Euler(data.LocalEulerAngles);
        }

        /// <summary>
        /// 仅绘制当前 Unity 工具对应的位置或旋转 Handle，并把结果转换回绑定局部空间。
        /// </summary>
        /// <param name="bindingMatrix">当前 Clip 的绑定世界矩阵。</param>
        /// <param name="data">当前局部体积配置。</param>
        /// <param name="mode">本次 Handle 编辑类别。</param>
        /// <param name="localPosition">读写局部位置。</param>
        /// <param name="localEulerAngles">读写局部欧拉角。</param>
        protected static void DrawPoseHandle(Matrix4x4 bindingMatrix, TData data,
            AttackDetectionHandleMode mode,
            ref Vector3 localPosition, ref Vector3 localEulerAngles)
        {
            ResolveWorldPose(bindingMatrix, data, out Vector3 worldPosition, out Quaternion worldRotation);
            if (mode == AttackDetectionHandleMode.Position)
            {
                worldPosition = Handles.PositionHandle(worldPosition, worldRotation);
                localPosition = bindingMatrix.inverse.MultiplyPoint3x4(worldPosition);
            }
            else if (mode == AttackDetectionHandleMode.Rotation)
            {
                worldRotation = Handles.RotationHandle(worldRotation, worldPosition);
                localEulerAngles = (Quaternion.Inverse(bindingMatrix.rotation) * worldRotation).eulerAngles;
            }
        }

        /// <summary>
        /// 使用绑定矩阵和检测局部姿态创建形状绘制矩阵。
        /// </summary>
        /// <param name="bindingMatrix">当前 Clip 的绑定世界矩阵。</param>
        /// <param name="data">当前局部体积配置。</param>
        /// <returns>将体积局部空间转换到世界空间的矩阵。</returns>
        protected static Matrix4x4 CreateShapeMatrix(Matrix4x4 bindingMatrix, TData data) =>
            bindingMatrix * Matrix4x4.TRS(
                data.LocalPosition, Quaternion.Euler(data.LocalEulerAngles), Vector3.one);
    }

    #endregion

    #region Box Drawer

    /// <summary>
    /// 绘制并编辑 Box 检测的局部位置、旋转和三轴尺寸。
    /// </summary>
    internal sealed class BoxSceneDrawer : VolumeSceneDrawer<BoxAttackDetectionData>
    {
        /// <summary>
        /// 绘制 Box 线框。
        /// </summary>
        public override void Draw(in AttackDetectionSceneDrawContext context,
            AttackDetectionDataBase data)
        {
            BoxAttackDetectionData box = (BoxAttackDetectionData)data;
            Matrix4x4 matrix = CreateShapeMatrix(context.BindingMatrix, box);
            using (new Handles.DrawingScope(context.FillColor, matrix))
                AttackDetectionSolidGeometry.DrawBox(box.Size);
            using (new Handles.DrawingScope(context.Color, matrix))
                Handles.DrawWireCube(Vector3.zero, box.Size);
        }

        /// <summary>
        /// 根据当前工具绘制 Box 的位置、旋转或三轴尺寸 Handle。
        /// </summary>
        public override AttackDetectionDataBase DrawHandles(
            in AttackDetectionSceneDrawContext context, AttackDetectionDataBase data,
            AttackDetectionHandleMode mode)
        {
            BoxAttackDetectionData box = (BoxAttackDetectionData)data;
            Vector3 position = box.LocalPosition;
            Vector3 rotation = box.LocalEulerAngles;
            DrawPoseHandle(context.BindingMatrix, box, mode, ref position, ref rotation);
            Vector3 size = box.Size;
            if (mode == AttackDetectionHandleMode.Shape)
            {
                ResolveWorldPose(context.BindingMatrix, box, out Vector3 worldPosition, out Quaternion worldRotation);
                size = Handles.ScaleHandle(size, worldPosition, worldRotation,
                    HandleUtility.GetHandleSize(worldPosition));
                size = new Vector3(Mathf.Max(0.001f, Mathf.Abs(size.x)),
                    Mathf.Max(0.001f, Mathf.Abs(size.y)), Mathf.Max(0.001f, Mathf.Abs(size.z)));
            }
            return new BoxAttackDetectionData(position, rotation, size);
        }
    }

    #endregion

    #region Sphere Drawer

    /// <summary>
    /// 绘制并编辑 Sphere 检测的局部位置和半径。
    /// </summary>
    internal sealed class SphereSceneDrawer : VolumeSceneDrawer<SphereAttackDetectionData>
    {
        /// <summary>
        /// 绘制球形线框。
        /// </summary>
        public override void Draw(in AttackDetectionSceneDrawContext context,
            AttackDetectionDataBase data)
        {
            SphereAttackDetectionData sphere = (SphereAttackDetectionData)data;
            ResolveWorldPose(context.BindingMatrix, sphere, out Vector3 position, out _);
            Matrix4x4 matrix = Matrix4x4.TRS(position, Quaternion.identity, Vector3.one);
            using (new Handles.DrawingScope(context.FillColor, matrix))
                AttackDetectionSolidGeometry.DrawSphere(sphere.Radius, context.SurfaceSegments);
            using (new Handles.DrawingScope(context.Color))
            {
                Handles.DrawWireDisc(position, Vector3.up, sphere.Radius);
                Handles.DrawWireDisc(position, Vector3.right, sphere.Radius);
                Handles.DrawWireDisc(position, Vector3.forward, sphere.Radius);
            }
        }

        /// <summary>
        /// 根据当前工具绘制 Sphere 的位置或半径 Handle；旋转模式只保留线框。
        /// </summary>
        public override AttackDetectionDataBase DrawHandles(
            in AttackDetectionSceneDrawContext context, AttackDetectionDataBase data,
            AttackDetectionHandleMode mode)
        {
            SphereAttackDetectionData sphere = (SphereAttackDetectionData)data;
            Vector3 position = sphere.LocalPosition;
            float radius = sphere.Radius;
            ResolveWorldPose(context.BindingMatrix, sphere, out Vector3 worldPosition, out _);
            if (mode == AttackDetectionHandleMode.Position)
            {
                worldPosition = Handles.PositionHandle(worldPosition, context.BindingMatrix.rotation);
                position = context.BindingMatrix.inverse.MultiplyPoint3x4(worldPosition);
            }
            else if (mode == AttackDetectionHandleMode.Shape)
            {
                radius = Mathf.Max(0.001f, Handles.RadiusHandle(
                    context.BindingMatrix.rotation, worldPosition, radius));
            }
            return new SphereAttackDetectionData(position, radius);
        }
    }

    #endregion

    #region Capsule Drawer

    /// <summary>
    /// 绘制并编辑 Capsule 检测的局部位置、旋转、半径和高度。
    /// </summary>
    internal sealed class CapsuleSceneDrawer : VolumeSceneDrawer<CapsuleAttackDetectionData>
    {
        /// <summary>
        /// 绘制胶囊体近似线框。
        /// </summary>
        public override void Draw(in AttackDetectionSceneDrawContext context,
            AttackDetectionDataBase data)
        {
            CapsuleAttackDetectionData capsule = (CapsuleAttackDetectionData)data;
            Matrix4x4 matrix = CreateShapeMatrix(context.BindingMatrix, capsule);
            using (new Handles.DrawingScope(context.FillColor, matrix))
                AttackDetectionSolidGeometry.DrawCapsule(capsule.Radius, capsule.Height,
                    capsule.Axis, context.SurfaceSegments);
            using Handles.DrawingScope scope = new(context.Color, matrix);
            Vector3 axis = AxisVector(capsule.Axis);
            float halfLine = Mathf.Max(0f, capsule.Height * 0.5f - capsule.Radius);
            Vector3 a = axis * halfLine;
            Vector3 b = -a;
            Handles.DrawWireDisc(a, axis, capsule.Radius);
            Handles.DrawWireDisc(b, axis, capsule.Radius);
            Vector3 tangentA = Vector3.Cross(axis, Vector3.forward);
            if (tangentA.sqrMagnitude < 0.01f) tangentA = Vector3.Cross(axis, Vector3.up);
            tangentA.Normalize();
            Vector3 tangentB = Vector3.Cross(axis, tangentA).normalized;
            Handles.DrawLine(a + tangentA * capsule.Radius, b + tangentA * capsule.Radius);
            Handles.DrawLine(a - tangentA * capsule.Radius, b - tangentA * capsule.Radius);
            Handles.DrawLine(a + tangentB * capsule.Radius, b + tangentB * capsule.Radius);
            Handles.DrawLine(a - tangentB * capsule.Radius, b - tangentB * capsule.Radius);
        }

        /// <summary>
        /// 根据当前工具绘制 Capsule 的位置、旋转或形状尺寸 Handle。
        /// </summary>
        public override AttackDetectionDataBase DrawHandles(
            in AttackDetectionSceneDrawContext context, AttackDetectionDataBase data,
            AttackDetectionHandleMode mode)
        {
            CapsuleAttackDetectionData capsule = (CapsuleAttackDetectionData)data;
            Vector3 position = capsule.LocalPosition;
            Vector3 rotation = capsule.LocalEulerAngles;
            DrawPoseHandle(context.BindingMatrix, capsule, mode, ref position, ref rotation);
            float radius = capsule.Radius;
            float height = capsule.Height;
            if (mode == AttackDetectionHandleMode.Shape)
            {
                ResolveWorldPose(context.BindingMatrix, capsule, out Vector3 worldPosition,
                    out Quaternion worldRotation);
                radius = Mathf.Max(0.001f, Handles.RadiusHandle(
                    worldRotation, worldPosition, radius));
                Vector3 worldAxis = worldRotation * AxisVector(capsule.Axis);
                height = Mathf.Max(radius * 2f, Handles.ScaleValueHandle(height,
                    worldPosition + worldAxis * height * 0.5f, worldRotation,
                    HandleUtility.GetHandleSize(worldPosition), Handles.CubeHandleCap, 0.1f));
            }
            return new CapsuleAttackDetectionData(position, rotation, radius, height, capsule.Axis);
        }

        /// <summary>把配置轴向转换为局部单位向量。</summary>
        /// <param name="axis">胶囊配置轴向。</param>
        /// <returns>对应的局部单位轴。</returns>
        private static Vector3 AxisVector(CapsuleAxis axis) => axis switch
        {
            CapsuleAxis.X => Vector3.right,
            CapsuleAxis.Z => Vector3.forward,
            _ => Vector3.up
        };
    }

    #endregion

    #region Sector Drawer

    /// <summary>
    /// 绘制并编辑 Sector 检测的局部姿态、内外半径、角度和高度。
    /// </summary>
    internal sealed class SectorSceneDrawer : VolumeSceneDrawer<SectorAttackDetectionData>
    {
        /// <summary>
        /// 绘制扇形柱体上下边界和侧边。
        /// </summary>
        public override void Draw(in AttackDetectionSceneDrawContext context,
            AttackDetectionDataBase data)
        {
            SectorAttackDetectionData sector = (SectorAttackDetectionData)data;
            Matrix4x4 matrix = CreateShapeMatrix(context.BindingMatrix, sector);
            using (new Handles.DrawingScope(context.FillColor, matrix))
                AttackDetectionSolidGeometry.DrawSector(sector.InnerRadius, sector.OuterRadius,
                    sector.Angle, sector.Height, context.SurfaceSegments);
            using Handles.DrawingScope scope = new(context.Color, matrix);
            float halfHeight = sector.Height * 0.5f;
            Vector3 from = Quaternion.Euler(0f, -sector.Angle * 0.5f, 0f) * Vector3.forward;
            Vector3 to = Quaternion.Euler(0f, sector.Angle * 0.5f, 0f) * Vector3.forward;
            foreach (float y in new[] { -halfHeight, halfHeight })
            {
                Vector3 center = Vector3.up * y;
                Handles.DrawWireArc(center, Vector3.up, from, sector.Angle, sector.OuterRadius);
                if (sector.InnerRadius > 0f)
                    Handles.DrawWireArc(center, Vector3.up, from, sector.Angle, sector.InnerRadius);
                Handles.DrawLine(center + from * sector.InnerRadius, center + from * sector.OuterRadius);
                Handles.DrawLine(center + to * sector.InnerRadius, center + to * sector.OuterRadius);
            }
            Handles.DrawLine(Vector3.down * halfHeight + from * sector.OuterRadius,
                Vector3.up * halfHeight + from * sector.OuterRadius);
            Handles.DrawLine(Vector3.down * halfHeight + to * sector.OuterRadius,
                Vector3.up * halfHeight + to * sector.OuterRadius);
        }

        /// <summary>
        /// 根据当前工具绘制 Sector 的位置、旋转或形状参数 Handle。
        /// </summary>
        public override AttackDetectionDataBase DrawHandles(
            in AttackDetectionSceneDrawContext context, AttackDetectionDataBase data,
            AttackDetectionHandleMode mode)
        {
            SectorAttackDetectionData sector = (SectorAttackDetectionData)data;
            Vector3 position = sector.LocalPosition;
            Vector3 rotation = sector.LocalEulerAngles;
            DrawPoseHandle(context.BindingMatrix, sector, mode, ref position, ref rotation);
            float inner = sector.InnerRadius;
            float outer = sector.OuterRadius;
            float angle = sector.Angle;
            float height = sector.Height;
            if (mode == AttackDetectionHandleMode.Shape)
            {
                ResolveWorldPose(context.BindingMatrix, sector, out Vector3 worldPosition,
                    out Quaternion worldRotation);
                outer = Mathf.Max(0.001f, Handles.RadiusHandle(
                    worldRotation, worldPosition, outer));
                inner = Mathf.Clamp(Handles.ScaleValueHandle(inner,
                    worldPosition + worldRotation * Vector3.forward * inner, worldRotation,
                    HandleUtility.GetHandleSize(worldPosition), Handles.CubeHandleCap, 0.1f), 0f, outer);
                angle = Mathf.Clamp(Handles.ScaleValueHandle(angle,
                    worldPosition + worldRotation * Vector3.right * outer, worldRotation,
                    HandleUtility.GetHandleSize(worldPosition), Handles.CubeHandleCap, 1f), 0.01f, 360f);
                height = Mathf.Max(0.001f, Handles.ScaleValueHandle(height,
                    worldPosition + worldRotation * Vector3.up * height * 0.5f, worldRotation,
                    HandleUtility.GetHandleSize(worldPosition), Handles.CubeHandleCap, 0.1f));
            }
            return new SectorAttackDetectionData(position, rotation, inner, outer, angle, height);
        }
    }

    #endregion

    #region WeaponTrace Drawer

    /// <summary>
    /// 只读绘制武器刀刃在上一采样帧到当前帧之间的扫掠线。
    /// </summary>
    internal sealed class WeaponTraceSceneDrawer : IAttackDetectionSceneDrawer
    {
        public Type DataType => typeof(WeaponTraceAttackDetectionData);
        public bool SupportsHandles => false;
        public bool RequiresWeaponTraceMarkers => true;

        /// <summary>
        /// 绘制前后刀刃以及沿刀刃插值点连接出的扫掠线。
        /// </summary>
        public void Draw(in AttackDetectionSceneDrawContext context, AttackDetectionDataBase data)
        {
            WeaponTraceAttackDetectionData trace = (WeaponTraceAttackDetectionData)data;
            if (!context.WeaponSegment.HasValue) return;
            using Handles.DrawingScope scope = new(context.Color);
            WeaponTraceSweepSegment segment = context.WeaponSegment.Value;
            Handles.DrawLine(segment.PreviousRoot, segment.PreviousTip);
            Handles.DrawLine(segment.CurrentRoot, segment.CurrentTip);
            int count = Mathf.Max(2, trace.SamplePointCount);
            for (int index = 0; index < count; index++)
            {
                float t = index / (float)(count - 1);
                Handles.DrawLine(Vector3.Lerp(segment.PreviousRoot, segment.PreviousTip, t),
                    Vector3.Lerp(segment.CurrentRoot, segment.CurrentTip, t));
            }
        }

        /// <summary>
        /// WeaponTrace 几何来自当前激活 MarkerProvider 的固定 Socket，不允许通过技能 Clip Handle 编辑。
        /// </summary>
        public AttackDetectionDataBase DrawHandles(in AttackDetectionSceneDrawContext context,
            AttackDetectionDataBase data, AttackDetectionHandleMode mode) => data;
    }

    #endregion
}
#endif
