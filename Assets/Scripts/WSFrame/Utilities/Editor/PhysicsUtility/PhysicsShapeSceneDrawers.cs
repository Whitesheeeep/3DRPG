#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace WS_Modules.Utilities.Editor
{
    /// <summary>
    /// 绘制并编辑通用 PhysicsShapeData 的各类 Scene 几何和 Handle。
    /// </summary>
    internal static class PhysicsShapeSceneDrawers
    {
        #region 入口

        /// <summary>
        /// 绘制当前编辑会话的形状并按当前 Unity 工具编辑其局部数据。
        /// </summary>
        /// <param name="root">形状数据所属的宿主 Transform。</param>
        /// <param name="property">形状数据的 SerializedProperty。</param>
        /// <param name="type">当前形状类型。</param>
        /// <param name="color">线框颜色。</param>
        /// <param name="handleMode">当前 Scene 工具对应的编辑模式。</param>
        /// <returns>本次绘制是否修改了序列化数据。</returns>
        public static bool DrawHandles(Transform root, SerializedProperty property,
            PhysicsShapeType type, Color color, Tool handleMode)
        {
            SerializedProperty localPositionProperty = property.FindPropertyRelative("localPosition");
            SerializedProperty localEulerProperty = property.FindPropertyRelative("localEulerAngles");
            Vector3 localPosition = localPositionProperty.vector3Value;
            Vector3 localEulerAngles = localEulerProperty.vector3Value;
            ResolveWorldPose(root, localPosition, localEulerAngles, out Vector3 worldPosition,
                out Quaternion worldRotation);

            using (new Handles.DrawingScope(color))
            {
                switch (type)
                {
                    case PhysicsShapeType.Box:
                        DrawBox(root, property, localPosition, localEulerAngles);
                        break;
                    case PhysicsShapeType.Sphere:
                        DrawSphere(root, property, worldPosition);
                        break;
                    case PhysicsShapeType.Capsule:
                        DrawCapsule(root, property, localPosition, localEulerAngles);
                        break;
                    case PhysicsShapeType.Sector:
                        DrawSector(root, property, localPosition, localEulerAngles);
                        break;
                    case PhysicsShapeType.Ray:
                        DrawRay(root, property, worldPosition, worldRotation);
                        break;
                }
            }

            EditorGUI.BeginChangeCheck();
            DrawPoseHandle(root, handleMode, ref localPosition, ref localEulerAngles);
            DrawShapeHandle(root, property, type, localPosition, localEulerAngles, handleMode);
            if (!EditorGUI.EndChangeCheck()) return false;

            localPositionProperty.vector3Value = localPosition;
            localEulerProperty.vector3Value = localEulerAngles;
            return true;
        }

        #endregion

        #region 线框绘制

        /// <summary>绘制 Box 的局部线框。</summary>
        private static void DrawBox(Transform root, SerializedProperty property,
            Vector3 localPosition, Vector3 localEulerAngles)
        {
            using Handles.DrawingScope scope = new(root.localToWorldMatrix *
                Matrix4x4.TRS(localPosition, Quaternion.Euler(localEulerAngles), Vector3.one));
            Handles.DrawWireCube(Vector3.zero, property.FindPropertyRelative("size").vector3Value);
        }

        /// <summary>绘制 Sphere 的三向圆线框。</summary>
        private static void DrawSphere(Transform root, SerializedProperty property,
            Vector3 worldPosition)
        {
            // Runtime 查询以最大宿主缩放构造包围球，这里使用相同半径保证线框与查询一致。
            float radius = property.FindPropertyRelative("radius").floatValue * MaxComponent(GetScale(root));
            Handles.DrawWireDisc(worldPosition, Vector3.up, radius);
            Handles.DrawWireDisc(worldPosition, Vector3.right, radius);
            Handles.DrawWireDisc(worldPosition, Vector3.forward, radius);
        }

        /// <summary>绘制 Capsule 的端面圆和纵向边线。</summary>
        private static void DrawCapsule(Transform root, SerializedProperty property,
            Vector3 localPosition, Vector3 localEulerAngles)
        {
            ResolveWorldPose(root, localPosition, localEulerAngles, out Vector3 center,
                out Quaternion worldRotation);
            Vector3 scale = GetScale(root);
            PhysicsCapsuleAxis axis = (PhysicsCapsuleAxis)property.FindPropertyRelative("capsuleAxis").enumValueIndex;
            float radius = property.FindPropertyRelative("radius").floatValue *
                GetPerpendicularScale(scale, axis);
            float height = property.FindPropertyRelative("height").floatValue *
                GetScaleComponent(scale, axis);
            Vector3 axisVector = worldRotation * GetAxis(axis);
            float halfLine = Mathf.Max(0f, height * 0.5f - radius);
            Vector3 a = center + axisVector * halfLine;
            Vector3 b = center - axisVector * halfLine;
            Handles.DrawWireDisc(a, axisVector, radius);
            Handles.DrawWireDisc(b, axisVector, radius);
            Vector3 tangentA = Vector3.Cross(axisVector, Vector3.forward);
            if (tangentA.sqrMagnitude < 0.01f) tangentA = Vector3.Cross(axisVector, Vector3.up);
            tangentA.Normalize();
            Vector3 tangentB = Vector3.Cross(axisVector, tangentA).normalized;
            Handles.DrawLine(a + tangentA * radius, b + tangentA * radius);
            Handles.DrawLine(a - tangentA * radius, b - tangentA * radius);
            Handles.DrawLine(a + tangentB * radius, b + tangentB * radius);
            Handles.DrawLine(a - tangentB * radius, b - tangentB * radius);
        }

        /// <summary>绘制 Sector 的上下边界、弧线和侧边。</summary>
        private static void DrawSector(Transform root, SerializedProperty property,
            Vector3 localPosition, Vector3 localEulerAngles)
        {
            float innerRadius = property.FindPropertyRelative("innerRadius").floatValue;
            float outerRadius = property.FindPropertyRelative("outerRadius").floatValue;
            float angle = property.FindPropertyRelative("angle").floatValue;
            float height = property.FindPropertyRelative("height").floatValue;
            using Handles.DrawingScope scope = new(root.localToWorldMatrix *
                Matrix4x4.TRS(localPosition, Quaternion.Euler(localEulerAngles), Vector3.one));
            float halfHeight = height * 0.5f;
            Vector3 from = Quaternion.Euler(0f, -angle * 0.5f, 0f) * Vector3.forward;
            Vector3 to = Quaternion.Euler(0f, angle * 0.5f, 0f) * Vector3.forward;
            DrawSectorLevel(Vector3.down * halfHeight, from, to, innerRadius, outerRadius, angle);
            DrawSectorLevel(Vector3.up * halfHeight, from, to, innerRadius, outerRadius, angle);
            Handles.DrawLine(Vector3.down * halfHeight + from * outerRadius,
                Vector3.up * halfHeight + from * outerRadius);
            Handles.DrawLine(Vector3.down * halfHeight + to * outerRadius,
                Vector3.up * halfHeight + to * outerRadius);
        }

        /// <summary>绘制 Sector 一个高度层上的弧线和径向边。</summary>
        private static void DrawSectorLevel(Vector3 center, Vector3 from, Vector3 to,
            float innerRadius, float outerRadius, float angle)
        {
            Handles.DrawWireArc(center, Vector3.up, from, angle, outerRadius);
            if (innerRadius > 0f)
                Handles.DrawWireArc(center, Vector3.up, from, angle, innerRadius);
            Handles.DrawLine(center + from * innerRadius, center + from * outerRadius);
            Handles.DrawLine(center + to * innerRadius, center + to * outerRadius);
        }

        /// <summary>绘制 Ray 的起点、方向线和终点。</summary>
        private static void DrawRay(Transform root, SerializedProperty property,
            Vector3 worldPosition, Quaternion worldRotation)
        {
            float length = property.FindPropertyRelative("length").floatValue;
            Vector3 localDirection = root.InverseTransformDirection(worldRotation * Vector3.forward);
            Vector3 scaledDirection = root.TransformVector(localDirection);
            float directionScale = scaledDirection.magnitude;
            Vector3 direction = scaledDirection / Mathf.Max(directionScale, 0.0001f);
            Vector3 end = worldPosition + direction * length * directionScale;
            Handles.DrawLine(worldPosition, end);
            Handles.DotHandleCap(0, end, worldRotation, HandleUtility.GetHandleSize(end) * 0.08f,
                EventType.Repaint);
        }

        #endregion

        #region Handle 编辑

        /// <summary>根据 Move 或 Rotate 工具编辑局部位置和局部旋转。</summary>
        private static void DrawPoseHandle(Transform root, Tool handleMode,
            ref Vector3 localPosition, ref Vector3 localEulerAngles)
        {
            ResolveWorldPose(root, localPosition, localEulerAngles, out Vector3 worldPosition,
                out Quaternion worldRotation);
            if (handleMode == Tool.Move)
            {
                worldPosition = Handles.PositionHandle(worldPosition, worldRotation);
                localPosition = root.InverseTransformPoint(worldPosition);
            }
            else if (handleMode == Tool.Rotate)
            {
                worldRotation = Handles.RotationHandle(worldRotation, worldPosition);
                localEulerAngles = (Quaternion.Inverse(root.rotation) * worldRotation).eulerAngles;
            }
        }

        /// <summary>根据 Scale 工具编辑当前形状的尺寸或 Ray 长度。</summary>
        private static void DrawShapeHandle(Transform root, SerializedProperty property,
            PhysicsShapeType type, Vector3 localPosition, Vector3 localEulerAngles, Tool handleMode)
        {
            if (handleMode != Tool.Scale) return;
            ResolveWorldPose(root, localPosition, localEulerAngles, out Vector3 worldPosition,
                out Quaternion worldRotation);
            Vector3 scale = GetScale(root);
            float handleSize = HandleUtility.GetHandleSize(worldPosition);
            switch (type)
            {
                case PhysicsShapeType.Box:
                    Vector3 worldSize = Handles.ScaleHandle(
                        Vector3.Scale(property.FindPropertyRelative("size").vector3Value, scale),
                        worldPosition, worldRotation, handleSize);
                    property.FindPropertyRelative("size").vector3Value = Divide(worldSize, scale);
                    break;
                case PhysicsShapeType.Sphere:
                    float sphereRadius = Handles.RadiusHandle(worldRotation, worldPosition,
                        property.FindPropertyRelative("radius").floatValue * MaxComponent(scale));
                    property.FindPropertyRelative("radius").floatValue =
                        Mathf.Max(0.001f, sphereRadius / MaxComponent(scale));
                    break;
                case PhysicsShapeType.Capsule:
                    DrawCapsuleHandle(root, property, worldPosition, worldRotation, scale, handleSize);
                    break;
                case PhysicsShapeType.Sector:
                    DrawSectorHandle(root, property, worldPosition, worldRotation, scale, handleSize);
                    break;
                case PhysicsShapeType.Ray:
                    DrawRayHandle(root, property, worldPosition, worldRotation);
                    break;
            }
        }

        /// <summary>编辑 Capsule 的半径和总高度。</summary>
        private static void DrawCapsuleHandle(Transform root, SerializedProperty property,
            Vector3 worldPosition, Quaternion worldRotation, Vector3 scale, float handleSize)
        {
            PhysicsCapsuleAxis axis = (PhysicsCapsuleAxis)property.FindPropertyRelative("capsuleAxis").enumValueIndex;
            float radiusScale = GetPerpendicularScale(scale, axis);
            float axisScale = GetScaleComponent(scale, axis);
            float radius = property.FindPropertyRelative("radius").floatValue * radiusScale;
            float height = property.FindPropertyRelative("height").floatValue * axisScale;
            radius = Mathf.Max(0.001f, Handles.RadiusHandle(worldRotation, worldPosition, radius));
            Vector3 worldAxis = worldRotation * GetAxis(axis);
            height = Mathf.Max(radius * 2f, Handles.ScaleValueHandle(height,
                worldPosition + worldAxis * height * 0.5f, worldRotation,
                handleSize, Handles.CubeHandleCap, 0.1f));
            property.FindPropertyRelative("radius").floatValue = radius / radiusScale;
            property.FindPropertyRelative("height").floatValue = height / axisScale;
        }

        /// <summary>编辑 Sector 的半径、角度和高度。</summary>
        private static void DrawSectorHandle(Transform root, SerializedProperty property,
            Vector3 worldPosition, Quaternion worldRotation, Vector3 scale, float handleSize)
        {
            float scaleFactor = MaxComponent(scale);
            SerializedProperty innerProperty = property.FindPropertyRelative("innerRadius");
            SerializedProperty outerProperty = property.FindPropertyRelative("outerRadius");
            SerializedProperty angleProperty = property.FindPropertyRelative("angle");
            SerializedProperty heightProperty = property.FindPropertyRelative("height");
            float outer = Mathf.Max(0.001f, Handles.RadiusHandle(worldRotation, worldPosition,
                outerProperty.floatValue * scaleFactor));
            float inner = Mathf.Clamp(Handles.ScaleValueHandle(innerProperty.floatValue * scaleFactor,
                worldPosition + worldRotation * Vector3.forward * innerProperty.floatValue * scaleFactor,
                worldRotation, handleSize, Handles.CubeHandleCap, 0.1f), 0f, outer);
            float angle = Mathf.Clamp(Handles.ScaleValueHandle(angleProperty.floatValue,
                worldPosition + worldRotation * Vector3.right * outer, worldRotation,
                handleSize, Handles.CubeHandleCap, 1f), 0.01f, 360f);
            float height = Mathf.Max(0.001f, Handles.ScaleValueHandle(heightProperty.floatValue * scaleFactor,
                worldPosition + worldRotation * Vector3.up * heightProperty.floatValue * scaleFactor * 0.5f,
                worldRotation, handleSize, Handles.CubeHandleCap, 0.1f));
            innerProperty.floatValue = inner / scaleFactor;
            outerProperty.floatValue = outer / scaleFactor;
            angleProperty.floatValue = angle;
            heightProperty.floatValue = height / scaleFactor;
        }

        /// <summary>编辑 Ray 的局部长度。</summary>
        private static void DrawRayHandle(Transform root, SerializedProperty property,
            Vector3 worldPosition, Quaternion worldRotation)
        {
            Vector3 direction = worldRotation * Vector3.forward;
            float directionScale = root.TransformVector(Quaternion.Euler(
                property.FindPropertyRelative("localEulerAngles").vector3Value) * Vector3.forward).magnitude;
            float worldLength = property.FindPropertyRelative("length").floatValue * directionScale;
            Vector3 end = worldPosition + direction * worldLength;
            Vector3 movedEnd = Handles.Slider(end, direction, HandleUtility.GetHandleSize(end) * 0.1f,
                Handles.CubeHandleCap, 0f);
            float movedLength = Vector3.Dot(movedEnd - worldPosition, direction);
            property.FindPropertyRelative("length").floatValue = Mathf.Max(0.001f,
                movedLength / Mathf.Max(directionScale, 0.0001f));
        }

        #endregion

        #region 坐标辅助

        /// <summary>将局部姿态转换为世界位置和旋转。</summary>
        private static void ResolveWorldPose(Transform root, Vector3 localPosition,
            Vector3 localEulerAngles, out Vector3 worldPosition, out Quaternion worldRotation)
        {
            worldPosition = root.TransformPoint(localPosition);
            worldRotation = root.rotation * Quaternion.Euler(localEulerAngles);
        }

        /// <summary>读取宿主 Transform 的绝对缩放。</summary>
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
            PhysicsCapsuleAxis.X => Mathf.Max(scale.x, 0.0001f),
            PhysicsCapsuleAxis.Z => Mathf.Max(scale.z, 0.0001f),
            _ => Mathf.Max(scale.y, 0.0001f)
        };

        /// <summary>取得垂直于胶囊轴向的最大缩放分量。</summary>
        private static float GetPerpendicularScale(Vector3 scale, PhysicsCapsuleAxis axis) => axis switch
        {
            PhysicsCapsuleAxis.X => Mathf.Max(scale.y, scale.z, 0.0001f),
            PhysicsCapsuleAxis.Z => Mathf.Max(scale.x, scale.y, 0.0001f),
            _ => Mathf.Max(scale.x, scale.z, 0.0001f)
        };

        /// <summary>按宿主缩放反算局部尺寸。</summary>
        private static Vector3 Divide(Vector3 value, Vector3 divisor) => new(
            value.x / Mathf.Max(divisor.x, 0.0001f), value.y / Mathf.Max(divisor.y, 0.0001f),
            value.z / Mathf.Max(divisor.z, 0.0001f));

        /// <summary>取得缩放向量最大分量。</summary>
        private static float MaxComponent(Vector3 value) => Mathf.Max(value.x, Mathf.Max(value.y, value.z));

        #endregion
    }
}
#endif
