using UnityEngine;

namespace WS_Modules.Utilities
{
    /// <summary>
    /// 使用 Unity Gizmos 绘制通用 PhysicsShapeData 的运行时可视化几何。
    /// </summary>
    internal static class PhysicsShapeGizmoDrawer
    {
        #region 常量

        private const int SectorSegments = 24;
        private const int CircleSegments = 16;
        private static readonly Color GizmoColor = new(0.2f, 0.85f, 1f, 1f);

        #endregion

        #region 入口

        /// <summary>
        /// 按形状类型绘制 Gizmo，并恢复调用前的 Gizmos 全局状态。
        /// </summary>
        /// <param name="root">局部坐标所属的宿主 Transform。</param>
        /// <param name="data">需要绘制的形状数据。</param>
        internal static void Draw(Transform root, PhysicsShapeData data)
        {
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;
            try
            {
                Gizmos.color = GizmoColor;
                switch (data.Type)
                {
                    case PhysicsShapeType.Box:
                        DrawBox(root, data);
                        break;
                    case PhysicsShapeType.Sphere:
                        DrawSphere(root, data);
                        break;
                    case PhysicsShapeType.Capsule:
                        DrawCapsule(root, data);
                        break;
                    case PhysicsShapeType.Sector:
                        DrawSector(root, data);
                        break;
                    case PhysicsShapeType.Ray:
                        DrawRay(root, data);
                        break;
                }
            }
            finally
            {
                Gizmos.matrix = previousMatrix;
                Gizmos.color = previousColor;
            }
        }

        #endregion

        #region 形状绘制

        /// <summary>绘制 Box 线框。</summary>
        private static void DrawBox(Transform root, PhysicsShapeData data)
        {
            Gizmos.matrix = CreateShapeMatrix(root, data);
            Gizmos.DrawWireCube(Vector3.zero, data.Size);
        }

        /// <summary>绘制 Sphere 线框。</summary>
        private static void DrawSphere(Transform root, PhysicsShapeData data)
        {
            ResolveWorldPose(root, data, out Vector3 position, out _);
            Gizmos.DrawWireSphere(position, data.Radius * MaxComponent(GetScale(root)));
        }

        /// <summary>绘制 Capsule 的端面圆和纵向边线。</summary>
        private static void DrawCapsule(Transform root, PhysicsShapeData data)
        {
            ResolveWorldPose(root, data, out Vector3 center, out Quaternion rotation);
            Vector3 scale = GetScale(root);
            Vector3 axis = rotation * GetAxis(data.CapsuleAxis);
            float radius = data.Radius * GetPerpendicularScale(scale, data.CapsuleAxis);
            float height = data.Height * GetScaleComponent(scale, data.CapsuleAxis);
            float halfLine = Mathf.Max(0f, height * 0.5f - radius);
            Vector3 pointA = center + axis * halfLine;
            Vector3 pointB = center - axis * halfLine;
            DrawCircle(pointA, axis, radius);
            DrawCircle(pointB, axis, radius);

            Vector3 tangentA = Vector3.Cross(axis, Vector3.forward);
            if (tangentA.sqrMagnitude < 0.01f) tangentA = Vector3.Cross(axis, Vector3.up);
            tangentA.Normalize();
            Vector3 tangentB = Vector3.Cross(axis, tangentA).normalized;
            Gizmos.DrawLine(pointA + tangentA * radius, pointB + tangentA * radius);
            Gizmos.DrawLine(pointA - tangentA * radius, pointB - tangentA * radius);
            Gizmos.DrawLine(pointA + tangentB * radius, pointB + tangentB * radius);
            Gizmos.DrawLine(pointA - tangentB * radius, pointB - tangentB * radius);
        }

        /// <summary>绘制 Sector 的上下弧线、内弧线和两侧高度边。</summary>
        private static void DrawSector(Transform root, PhysicsShapeData data)
        {
            Gizmos.matrix = CreateShapeMatrix(root, data);
            float halfHeight = data.Height * 0.5f;
            Vector3 from = Quaternion.Euler(0f, -data.Angle * 0.5f, 0f) * Vector3.forward;
            Vector3 to = Quaternion.Euler(0f, data.Angle * 0.5f, 0f) * Vector3.forward;
            DrawSectorLevel(Vector3.down * halfHeight, from, data);
            DrawSectorLevel(Vector3.up * halfHeight, from, data);
            Gizmos.DrawLine(Vector3.down * halfHeight + from * data.OuterRadius,
                Vector3.up * halfHeight + from * data.OuterRadius);
            Gizmos.DrawLine(Vector3.down * halfHeight + to * data.OuterRadius,
                Vector3.up * halfHeight + to * data.OuterRadius);
        }

        /// <summary>绘制一个水平高度层上的 Sector 外弧、内弧和径向边。</summary>
        private static void DrawSectorLevel(Vector3 center, Vector3 from,
            PhysicsShapeData data)
        {
            DrawArc(center, from, data.OuterRadius, data.Angle);
            if (data.InnerRadius > 0f)
                DrawArc(center, from, data.InnerRadius, data.Angle);
            Vector3 to = Quaternion.Euler(0f, data.Angle, 0f) * from;
            Gizmos.DrawLine(center + from * data.InnerRadius, center + from * data.OuterRadius);
            Gizmos.DrawLine(center + to * data.InnerRadius, center + to * data.OuterRadius);
        }

        /// <summary>绘制 Ray 的起点、方向线和终点标记。</summary>
        private static void DrawRay(Transform root, PhysicsShapeData data)
        {
            ResolveWorldPose(root, data, out Vector3 start, out Quaternion rotation);
            Vector3 localDirection = Quaternion.Inverse(root.rotation) * rotation * Vector3.forward;
            Vector3 scaledDirection = root.TransformVector(localDirection);
            float directionScale = scaledDirection.magnitude;
            if (directionScale <= Mathf.Epsilon) return;
            Vector3 direction = scaledDirection / directionScale;
            Vector3 end = start + direction * data.Length * directionScale;
            Gizmos.DrawLine(start, end);
            float markerSize = Mathf.Max(0.02f, data.Length * directionScale * 0.04f);
            Gizmos.DrawLine(end - rotation * Vector3.up * markerSize,
                end + rotation * Vector3.up * markerSize);
            Gizmos.DrawLine(end - rotation * Vector3.right * markerSize,
                end + rotation * Vector3.right * markerSize);
        }

        #endregion

        #region 几何辅助

        /// <summary>绘制垂直于给定法线的圆形线框。</summary>
        private static void DrawCircle(Vector3 center, Vector3 normal, float radius)
        {
            Vector3 tangent = Vector3.Cross(normal, Vector3.forward);
            if (tangent.sqrMagnitude < 0.01f) tangent = Vector3.Cross(normal, Vector3.up);
            tangent.Normalize();
            Vector3 bitangent = Vector3.Cross(normal, tangent).normalized;
            Vector3 previous = center + tangent * radius;
            for (int index = 1; index <= CircleSegments; index++)
            {
                float angle = index * Mathf.PI * 2f / CircleSegments;
                Vector3 current = center + (tangent * Mathf.Cos(angle) +
                    bitangent * Mathf.Sin(angle)) * radius;
                Gizmos.DrawLine(previous, current);
                previous = current;
            }
        }

        /// <summary>绘制以局部 +Z 为起始方向的水平圆弧。</summary>
        private static void DrawArc(Vector3 center, Vector3 from, float radius, float angle)
        {
            Vector3 previous = center + from * radius;
            for (int index = 1; index <= SectorSegments; index++)
            {
                float currentAngle = angle * index / SectorSegments;
                Vector3 currentDirection = Quaternion.Euler(0f, currentAngle, 0f) * from;
                Vector3 current = center + currentDirection * radius;
                Gizmos.DrawLine(previous, current);
                previous = current;
            }
        }

        /// <summary>创建宿主 Transform 和形状局部姿态组成的 Gizmos 矩阵。</summary>
        private static Matrix4x4 CreateShapeMatrix(Transform root, PhysicsShapeData data) =>
            root.localToWorldMatrix * Matrix4x4.TRS(data.LocalPosition,
                Quaternion.Euler(data.LocalEulerAngles), Vector3.one);

        /// <summary>解析形状局部姿态到世界位置和旋转。</summary>
        private static void ResolveWorldPose(Transform root, PhysicsShapeData data,
            out Vector3 position, out Quaternion rotation)
        {
            position = root.TransformPoint(data.LocalPosition);
            rotation = root.rotation * Quaternion.Euler(data.LocalEulerAngles);
        }

        /// <summary>取得宿主 Transform 的绝对缩放。</summary>
        private static Vector3 GetScale(Transform root) => new(Mathf.Abs(root.lossyScale.x),
            Mathf.Abs(root.lossyScale.y), Mathf.Abs(root.lossyScale.z));

        /// <summary>取得胶囊局部轴向单位向量。</summary>
        private static Vector3 GetAxis(PhysicsCapsuleAxis axis) => axis switch
        {
            PhysicsCapsuleAxis.X => Vector3.right,
            PhysicsCapsuleAxis.Z => Vector3.forward,
            _ => Vector3.up
        };

        /// <summary>取得胶囊轴向缩放分量。</summary>
        private static float GetScaleComponent(Vector3 scale, PhysicsCapsuleAxis axis) => axis switch
        {
            PhysicsCapsuleAxis.X => scale.x,
            PhysicsCapsuleAxis.Z => scale.z,
            _ => scale.y
        };

        /// <summary>取得垂直于胶囊轴向的最大缩放分量。</summary>
        private static float GetPerpendicularScale(Vector3 scale, PhysicsCapsuleAxis axis) => axis switch
        {
            PhysicsCapsuleAxis.X => Mathf.Max(scale.y, scale.z),
            PhysicsCapsuleAxis.Z => Mathf.Max(scale.x, scale.y),
            _ => Mathf.Max(scale.x, scale.z)
        };

        /// <summary>取得向量三个分量中的最大值。</summary>
        private static float MaxComponent(Vector3 value) => Mathf.Max(value.x, Mathf.Max(value.y, value.z));

        #endregion
    }
}
