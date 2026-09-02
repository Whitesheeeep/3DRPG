using System;
using UnityEngine;

namespace WS_Modules.Utilities
{
    /// <summary>
    /// 使用 Unity Debug.DrawLine 绘制 UE 风格的运行时线框几何。
    /// </summary>
    public static class DebugUtility
    {
        #region 常量与字段

        private const int DefaultSegments = 24;
        private const float FullCircleDegrees = 360f;
        private const float SameDirectionTolerance = 0.000001f;

        // 每一项是一条立方体边的两个顶点索引，顶点顺序对应局部坐标的八个角点。
        private static readonly int[] CubeEdgeIndices =
        {
            0, 1, 1, 3, 3, 2, 2, 0,
            4, 5, 5, 7, 7, 6, 6, 4,
            0, 4, 1, 5, 2, 6, 3, 7
        };

        #endregion

        #region 公开绘制

        /// <summary>
        /// 绘制一个可旋转的 Cube 线框。
        /// </summary>
        /// <param name="center">Cube 的世界空间中心。</param>
        /// <param name="size">Cube 的完整三轴世界尺寸。</param>
        /// <param name="rotation">将 Cube 局部坐标旋转到世界空间的姿态。</param>
        /// <param name="color">线框颜色。</param>
        /// <param name="duration">线段持续显示的秒数；零表示仅显示当前帧。</param>
        /// <param name="depthTest">是否允许线段被场景中的遮挡物隐藏。</param>
        public static void DrawCube(Vector3 center, Vector3 size, Quaternion rotation,
            Color color, float duration = 0f, bool depthTest = true)
        {
            ValidateDuration(duration);
            ValidateFinitePositive(size.x, nameof(size));
            ValidateFinitePositive(size.y, nameof(size));
            ValidateFinitePositive(size.z, nameof(size));

            Vector3 halfSize = size * 0.5f;
            Vector3[] corners =
            {
                new(-halfSize.x, -halfSize.y, -halfSize.z),
                new(-halfSize.x, -halfSize.y, halfSize.z),
                new(-halfSize.x, halfSize.y, -halfSize.z),
                new(-halfSize.x, halfSize.y, halfSize.z),
                new(halfSize.x, -halfSize.y, -halfSize.z),
                new(halfSize.x, -halfSize.y, halfSize.z),
                new(halfSize.x, halfSize.y, -halfSize.z),
                new(halfSize.x, halfSize.y, halfSize.z)
            };

            // 先把局部角点旋转到世界空间，再按固定索引绘制 12 条边。
            for (int index = 0; index < CubeEdgeIndices.Length; index += 2)
            {
                Vector3 start = center + rotation * corners[CubeEdgeIndices[index]];
                Vector3 end = center + rotation * corners[CubeEdgeIndices[index + 1]];
                DrawSegment(start, end, color, duration, depthTest);
            }
        }

        /// <summary>
        /// 绘制由三个互相垂直大圆组成的 Sphere 线框。
        /// </summary>
        /// <param name="center">Sphere 的世界空间中心。</param>
        /// <param name="radius">Sphere 半径。</param>
        /// <param name="color">线框颜色。</param>
        /// <param name="duration">线段持续显示的秒数；零表示仅显示当前帧。</param>
        /// <param name="depthTest">是否允许线段被场景中的遮挡物隐藏。</param>
        /// <param name="segments">每个完整圆的离散线段数，必须不小于 4。</param>
        public static void DrawSphere(Vector3 center, float radius, Color color,
            float duration = 0f, bool depthTest = true, int segments = DefaultSegments)
        {
            ValidateDuration(duration);
            ValidateFinitePositive(radius, nameof(radius));
            ValidateSegments(segments);

            // 三个大圆分别位于 XY、XZ 和 YZ 平面，足以表达球体的空间朝向和大小。
            DrawCircle(center, Vector3.up, Vector3.right, radius, segments,
                color, duration, depthTest);
            DrawCircle(center, Vector3.up, Vector3.forward, radius, segments,
                color, duration, depthTest);
            DrawCircle(center, Vector3.right, Vector3.forward, radius, segments,
                color, duration, depthTest);
        }

        /// <summary>
        /// 绘制沿给定旋转局部 Y 轴的 Capsule 线框。
        /// </summary>
        /// <param name="center">Capsule 的世界空间中心。</param>
        /// <param name="radius">Capsule 半径。</param>
        /// <param name="height">Capsule 包含两个半球的总高度。</param>
        /// <param name="rotation">将 Capsule 局部坐标旋转到世界空间的姿态。</param>
        /// <param name="color">线框颜色。</param>
        /// <param name="duration">线段持续显示的秒数；零表示仅显示当前帧。</param>
        /// <param name="depthTest">是否允许线段被场景中的遮挡物隐藏。</param>
        /// <param name="segments">完整圆的离散线段数，必须不小于 4。</param>
        public static void DrawCapsule(Vector3 center, float radius, float height,
            Quaternion rotation, Color color, float duration = 0f, bool depthTest = true,
            int segments = DefaultSegments)
        {
            ValidateDuration(duration);
            ValidateFinitePositive(radius, nameof(radius));
            ValidateFinitePositive(height, nameof(height));
            if (height < radius * 2f)
                throw new ArgumentOutOfRangeException(nameof(height), height,
                    "Capsule 高度不能小于半径的两倍。");
            ValidateSegments(segments);

            Vector3 axis = rotation * Vector3.up;
            Vector3 tangent = rotation * Vector3.right;
            Vector3 bitangent = rotation * Vector3.forward;
            float halfCylinderLength = height * 0.5f - radius;
            Vector3 topCenter = center + axis * halfCylinderLength;
            Vector3 bottomCenter = center - axis * halfCylinderLength;

            // 两个截面圆连接柱体侧边；半球弧线从截面圆逐步收敛到两端极点。
            DrawCircle(topCenter, tangent, bitangent, radius, segments, color, duration, depthTest);
            DrawCircle(bottomCenter, tangent, bitangent, radius, segments, color, duration, depthTest);
            DrawSegment(topCenter + tangent * radius, bottomCenter + tangent * radius,
                color, duration, depthTest);
            DrawSegment(topCenter - tangent * radius, bottomCenter - tangent * radius,
                color, duration, depthTest);
            DrawSegment(topCenter + bitangent * radius, bottomCenter + bitangent * radius,
                color, duration, depthTest);
            DrawSegment(topCenter - bitangent * radius, bottomCenter - bitangent * radius,
                color, duration, depthTest);

            DrawHemisphere(topCenter, axis, tangent, radius, true, segments,
                color, duration, depthTest);
            DrawHemisphere(topCenter, axis, -tangent, radius, true, segments,
                color, duration, depthTest);
            DrawHemisphere(topCenter, axis, bitangent, radius, true, segments,
                color, duration, depthTest);
            DrawHemisphere(topCenter, axis, -bitangent, radius, true, segments,
                color, duration, depthTest);
            DrawHemisphere(bottomCenter, axis, tangent, radius, false, segments,
                color, duration, depthTest);
            DrawHemisphere(bottomCenter, axis, -tangent, radius, false, segments,
                color, duration, depthTest);
            DrawHemisphere(bottomCenter, axis, bitangent, radius, false, segments,
                color, duration, depthTest);
            DrawHemisphere(bottomCenter, axis, -bitangent, radius, false, segments,
                color, duration, depthTest);
        }

        /// <summary>
        /// 绘制以局部 +Z 为中心方向、局部 Y 为高度轴的 Sector 扇形柱体线框。
        /// </summary>
        /// <param name="center">Sector 的世界空间中心。</param>
        /// <param name="innerRadius">内半径；零表示实心扇形。</param>
        /// <param name="outerRadius">外半径。</param>
        /// <param name="angle">水平展开角度，单位为度，范围为 (0, 360]。</param>
        /// <param name="height">Sector 的完整高度。</param>
        /// <param name="rotation">将 Sector 局部坐标旋转到世界空间的姿态。</param>
        /// <param name="color">线框颜色。</param>
        /// <param name="duration">线段持续显示的秒数；零表示仅显示当前帧。</param>
        /// <param name="depthTest">是否允许线段被场景中的遮挡物隐藏。</param>
        /// <param name="segments">圆弧的离散线段数，必须不小于 4。</param>
        public static void DrawSector(Vector3 center, float innerRadius, float outerRadius,
            float angle, float height, Quaternion rotation, Color color, float duration = 0f,
            bool depthTest = true, int segments = DefaultSegments)
        {
            ValidateDuration(duration);
            ValidateFiniteNonNegative(innerRadius, nameof(innerRadius));
            ValidateFinitePositive(outerRadius, nameof(outerRadius));
            ValidateFinitePositive(angle, nameof(angle));
            ValidateFinitePositive(height, nameof(height));
            if (innerRadius > outerRadius)
                throw new ArgumentOutOfRangeException(nameof(innerRadius), innerRadius,
                    "Sector 内半径不能大于外半径。");
            if (angle > FullCircleDegrees)
                throw new ArgumentOutOfRangeException(nameof(angle), angle,
                    "Sector 角度不能大于 360 度。");
            ValidateSegments(segments);

            Vector3 up = rotation * Vector3.up;
            Vector3 from = rotation * (Quaternion.Euler(0f, -angle * 0.5f, 0f) * Vector3.forward);
            Vector3 to = rotation * (Quaternion.Euler(0f, angle * 0.5f, 0f) * Vector3.forward);
            float halfHeight = height * 0.5f;
            Vector3 bottom = center - up * halfHeight;
            Vector3 top = center + up * halfHeight;

            // 上下两层共享同一组局部方向，保证旋转后的扇形边界完全重合。
            DrawSectorLevel(bottom, up, from, innerRadius, outerRadius, angle, segments,
                color, duration, depthTest);
            DrawSectorLevel(top, up, from, innerRadius, outerRadius, angle, segments,
                color, duration, depthTest);

            // 连接扇形边界的高度线；实心扇形在中心补一条竖线以闭合尖端。
            DrawSegment(bottom + from * outerRadius, top + from * outerRadius,
                color, duration, depthTest);
            if (!ApproximatelySameDirection(from, to))
                DrawSegment(bottom + to * outerRadius, top + to * outerRadius,
                    color, duration, depthTest);

            if (innerRadius > Mathf.Epsilon)
            {
                DrawSegment(bottom + from * innerRadius, top + from * innerRadius,
                    color, duration, depthTest);
                if (!ApproximatelySameDirection(from, to))
                    DrawSegment(bottom + to * innerRadius, top + to * innerRadius,
                        color, duration, depthTest);
            }
            else
            {
                DrawSegment(bottom, top, color, duration, depthTest);
            }
        }

        #endregion

        #region 几何辅助

        /// <summary>
        /// 绘制由两个正交局部基向量定义的圆形线框。
        /// </summary>
        /// <param name="center">圆心世界坐标。</param>
        /// <param name="firstAxis">圆所在平面的第一条单位轴。</param>
        /// <param name="secondAxis">圆所在平面的第二条单位轴。</param>
        /// <param name="radius">圆半径。</param>
        /// <param name="segments">圆的线段数。</param>
        /// <param name="color">线段颜色。</param>
        /// <param name="duration">线段持续显示的秒数。</param>
        /// <param name="depthTest">是否进行深度测试。</param>
        private static void DrawCircle(Vector3 center, Vector3 firstAxis, Vector3 secondAxis,
            float radius, int segments, Color color, float duration, bool depthTest)
        {
            Vector3 previous = center + firstAxis * radius;
            for (int index = 1; index <= segments; index++)
            {
                float radians = index * Mathf.PI * 2f / segments;
                Vector3 direction = firstAxis * Mathf.Cos(radians) +
                    secondAxis * Mathf.Sin(radians);
                Vector3 current = center + direction * radius;
                DrawSegment(previous, current, color, duration, depthTest);
                previous = current;
            }
        }

        /// <summary>
        /// 绘制 Capsule 的一条四分之一圆弧，从赤道连接到半球极点。
        /// </summary>
        /// <param name="center">半球截面圆心。</param>
        /// <param name="axis">Capsule 高度方向。</param>
        /// <param name="radialAxis">半球所在经线平面的径向轴。</param>
        /// <param name="radius">半球半径。</param>
        /// <param name="topHemisphere">是否绘制沿正轴方向的半球。</param>
        /// <param name="segments">完整圆的线段数。</param>
        /// <param name="color">线段颜色。</param>
        /// <param name="duration">线段持续显示的秒数。</param>
        /// <param name="depthTest">是否进行深度测试。</param>
        private static void DrawHemisphere(Vector3 center, Vector3 axis, Vector3 radialAxis,
            float radius, bool topHemisphere, int segments, Color color, float duration,
            bool depthTest)
        {
            Vector3 poleAxis = topHemisphere ? axis : -axis;
            int arcSegments = Mathf.Max(2, segments / 2);
            Vector3 previous = center + radialAxis * radius;
            for (int index = 1; index <= arcSegments; index++)
            {
                float radians = Mathf.PI * 0.5f * index / arcSegments;
                Vector3 direction = radialAxis * Mathf.Cos(radians) +
                    poleAxis * Mathf.Sin(radians);
                Vector3 current = center + direction * radius;
                DrawSegment(previous, current, color, duration, depthTest);
                previous = current;
            }
        }

        /// <summary>
        /// 绘制水平圆弧，圆弧旋转轴由世界空间 up 向量确定。
        /// </summary>
        /// <param name="center">圆弧圆心。</param>
        /// <param name="up">圆弧平面的法线方向。</param>
        /// <param name="from">圆弧起始方向。</param>
        /// <param name="radius">圆弧半径。</param>
        /// <param name="angle">圆弧角度。</param>
        /// <param name="segments">圆弧线段数。</param>
        /// <param name="color">线段颜色。</param>
        /// <param name="duration">线段持续显示的秒数。</param>
        /// <param name="depthTest">是否进行深度测试。</param>
        private static void DrawArc(Vector3 center, Vector3 up, Vector3 from, float radius,
            float angle, int segments, Color color, float duration, bool depthTest)
        {
            Vector3 previous = center + from * radius;
            for (int index = 1; index <= segments; index++)
            {
                float currentAngle = angle * index / segments;
                Vector3 direction = Quaternion.AngleAxis(currentAngle, up) * from;
                Vector3 current = center + direction * radius;
                DrawSegment(previous, current, color, duration, depthTest);
                previous = current;
            }
        }

        /// <summary>
        /// 绘制 Sector 的一个高度层，包括内外圆弧和两条径向边。
        /// </summary>
        /// <param name="center">当前高度层的世界空间中心。</param>
        /// <param name="up">Sector 的世界空间高度轴。</param>
        /// <param name="from">扇形起始方向。</param>
        /// <param name="innerRadius">内半径。</param>
        /// <param name="outerRadius">外半径。</param>
        /// <param name="angle">展开角度。</param>
        /// <param name="segments">弧线线段数。</param>
        /// <param name="color">线段颜色。</param>
        /// <param name="duration">线段持续显示的秒数。</param>
        /// <param name="depthTest">是否进行深度测试。</param>
        private static void DrawSectorLevel(Vector3 center, Vector3 up, Vector3 from,
            float innerRadius, float outerRadius, float angle, int segments, Color color,
            float duration, bool depthTest)
        {
            DrawArc(center, up, from, outerRadius, angle, segments, color, duration, depthTest);
            if (innerRadius > Mathf.Epsilon)
                DrawArc(center, up, from, innerRadius, angle, segments, color, duration, depthTest);

            Vector3 to = Quaternion.AngleAxis(angle, up) * from;
            DrawSegment(center + from * innerRadius, center + from * outerRadius,
                color, duration, depthTest);
            if (!ApproximatelySameDirection(from, to))
                DrawSegment(center + to * innerRadius, center + to * outerRadius,
                    color, duration, depthTest);
        }

        /// <summary>
        /// 将单条几何边提交给 Unity，并统一传递持续时间和深度测试选项。
        /// </summary>
        /// <param name="start">线段起点。</param>
        /// <param name="end">线段终点。</param>
        /// <param name="color">线段颜色。</param>
        /// <param name="duration">线段持续显示的秒数。</param>
        /// <param name="depthTest">是否进行深度测试。</param>
        private static void DrawSegment(Vector3 start, Vector3 end, Color color, float duration,
            bool depthTest)
        {
            Debug.DrawLine(start, end, color, duration, depthTest);
        }

        /// <summary>
        /// 判断两个方向是否代表同一条 Sector 边界方向。
        /// </summary>
        /// <param name="first">第一个世界空间方向。</param>
        /// <param name="second">第二个世界空间方向。</param>
        /// <returns>方向足够接近时返回 true。</returns>
        private static bool ApproximatelySameDirection(Vector3 first, Vector3 second) =>
            Vector3.Dot(first.normalized, second.normalized) > 1f - SameDirectionTolerance;

        #endregion

        #region 参数校验

        /// <summary>
        /// 校验持续时间必须是有限非负数。
        /// </summary>
        /// <param name="duration">待校验的持续时间。</param>
        private static void ValidateDuration(float duration)
        {
            if (duration < 0f || float.IsNaN(duration) || float.IsInfinity(duration))
                throw new ArgumentOutOfRangeException(nameof(duration), duration,
                    "持续时间必须为有限非负数。");
        }

        /// <summary>
        /// 校验浮点数必须是有限正数。
        /// </summary>
        /// <param name="value">待校验的数值。</param>
        /// <param name="parameterName">参数名称。</param>
        private static void ValidateFinitePositive(float value, string parameterName)
        {
            if (value <= 0f || float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(parameterName, value,
                    "数值必须为有限正数。");
        }

        /// <summary>
        /// 校验浮点数必须是有限非负数。
        /// </summary>
        /// <param name="value">待校验的数值。</param>
        /// <param name="parameterName">参数名称。</param>
        private static void ValidateFiniteNonNegative(float value, string parameterName)
        {
            if (value < 0f || float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(parameterName, value,
                    "数值必须为有限非负数。");
        }

        /// <summary>
        /// 校验曲线离散线段数量满足几何构造的最低要求。
        /// </summary>
        /// <param name="segments">待校验的线段数量。</param>
        private static void ValidateSegments(int segments)
        {
            if (segments < 4)
                throw new ArgumentOutOfRangeException(nameof(segments), segments,
                    "segments 必须不小于 4。");
        }

        #endregion
    }
}
