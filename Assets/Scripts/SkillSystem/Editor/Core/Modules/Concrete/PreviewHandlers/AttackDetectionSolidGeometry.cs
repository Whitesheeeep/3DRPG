#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 使用即时 Handles 多边形绘制攻击检测体的半透明封闭表面，不创建可保存的 Mesh 或场景对象。
    /// </summary>
    internal static class AttackDetectionSolidGeometry
    {
        #region 复用顶点缓冲

        private const int MinimumSurfaceSegments = 8;
        private const float FullCircleAngle = 360f;
        private const float FullCircleEpsilon = 0.001f;

        private static readonly Vector3[] triangleVertices = new Vector3[3];
        private static readonly Vector3[] quadVertices = new Vector3[4];

        #endregion

        #region 体积绘制

        // 绘制以局部原点为中心的六面体表面；调用方负责设置 Handles.matrix 和颜色。
        internal static void DrawBox(Vector3 size)
        {
            Vector3 half = size * 0.5f;
            Vector3 nnn = new(-half.x, -half.y, -half.z);
            Vector3 nnp = new(-half.x, -half.y, half.z);
            Vector3 npn = new(-half.x, half.y, -half.z);
            Vector3 npp = new(-half.x, half.y, half.z);
            Vector3 pnn = new(half.x, -half.y, -half.z);
            Vector3 pnp = new(half.x, -half.y, half.z);
            Vector3 ppn = new(half.x, half.y, -half.z);
            Vector3 ppp = new(half.x, half.y, half.z);

            DrawQuad(nnn, pnn, ppn, npn);
            DrawQuad(pnp, nnp, npp, ppp);
            DrawQuad(nnp, nnn, npn, npp);
            DrawQuad(pnn, pnp, ppp, ppn);
            DrawQuad(npn, ppn, ppp, npp);
            DrawQuad(nnp, pnp, pnn, nnn);
        }

        // 按经纬度曲面分段绘制局部原点球体，极点使用三角面避免退化四边形。
        internal static void DrawSphere(float radius, int surfaceSegments)
        {
            int longitudeSegments = Mathf.Max(MinimumSurfaceSegments, surfaceSegments);
            int latitudeSegments = Mathf.Max(4, longitudeSegments / 2);
            for (int latitude = 0; latitude < latitudeSegments; latitude++)
            {
                float lowerAngle = -Mathf.PI * 0.5f + Mathf.PI * latitude / latitudeSegments;
                float upperAngle = -Mathf.PI * 0.5f + Mathf.PI * (latitude + 1) / latitudeSegments;
                float lowerRadius = Mathf.Cos(lowerAngle) * radius;
                float upperRadius = Mathf.Cos(upperAngle) * radius;
                float lowerY = Mathf.Sin(lowerAngle) * radius;
                float upperY = Mathf.Sin(upperAngle) * radius;

                for (int longitude = 0; longitude < longitudeSegments; longitude++)
                {
                    float angle0 = Mathf.PI * 2f * longitude / longitudeSegments;
                    float angle1 = Mathf.PI * 2f * (longitude + 1) / longitudeSegments;
                    Vector3 lower0 = RingPoint(lowerRadius, lowerY, angle0);
                    Vector3 lower1 = RingPoint(lowerRadius, lowerY, angle1);
                    Vector3 upper0 = RingPoint(upperRadius, upperY, angle0);
                    Vector3 upper1 = RingPoint(upperRadius, upperY, angle1);
                    if (latitude == 0)
                        DrawTriangle(lower0, upper1, upper0);
                    else if (latitude == latitudeSegments - 1)
                        DrawTriangle(lower0, lower1, upper0);
                    else
                        DrawQuad(lower0, lower1, upper1, upper0);
                }
            }
        }

        // 以指定局部轴向绘制胶囊表面，轮廓由圆柱段和两个半球连续组成。
        internal static void DrawCapsule(float radius, float height, CapsuleAxis axis,
            int surfaceSegments)
        {
            int longitudeSegments = Mathf.Max(MinimumSurfaceSegments, surfaceSegments);
            int hemisphereSegments = Mathf.Max(2, longitudeSegments / 4);
            int profilePointCount = hemisphereSegments * 2 + 2;
            float halfLine = Mathf.Max(0f, height * 0.5f - radius);

            for (int profile = 0; profile < profilePointCount - 1; profile++)
            {
                ResolveCapsuleProfile(profile, hemisphereSegments, radius, halfLine,
                    out float lowerRadius, out float lowerY);
                ResolveCapsuleProfile(profile + 1, hemisphereSegments, radius, halfLine,
                    out float upperRadius, out float upperY);

                for (int longitude = 0; longitude < longitudeSegments; longitude++)
                {
                    float angle0 = Mathf.PI * 2f * longitude / longitudeSegments;
                    float angle1 = Mathf.PI * 2f * (longitude + 1) / longitudeSegments;
                    Vector3 lower0 = MapCapsuleAxis(RingPoint(lowerRadius, lowerY, angle0), axis);
                    Vector3 lower1 = MapCapsuleAxis(RingPoint(lowerRadius, lowerY, angle1), axis);
                    Vector3 upper0 = MapCapsuleAxis(RingPoint(upperRadius, upperY, angle0), axis);
                    Vector3 upper1 = MapCapsuleAxis(RingPoint(upperRadius, upperY, angle1), axis);
                    if (profile == 0)
                        DrawTriangle(lower0, upper1, upper0);
                    else if (profile == profilePointCount - 2)
                        DrawTriangle(lower0, lower1, upper0);
                    else
                        DrawQuad(lower0, lower1, upper1, upper0);
                }
            }
        }

        // 绘制扇形柱体的上下表面、内外弧面及首尾封口，内半径为零时使用三角扇。
        internal static void DrawSector(float innerRadius, float outerRadius, float angle,
            float height, int surfaceSegments)
        {
            int arcSegments = Mathf.Max(1,
                Mathf.CeilToInt(Mathf.Max(MinimumSurfaceSegments, surfaceSegments) * angle / FullCircleAngle));
            float halfHeight = height * 0.5f;
            float startAngle = -angle * 0.5f * Mathf.Deg2Rad;
            float angleStep = angle * Mathf.Deg2Rad / arcSegments;
            bool hasInnerSurface = innerRadius > Mathf.Epsilon;

            for (int index = 0; index < arcSegments; index++)
            {
                Vector3 direction0 = Direction(startAngle + angleStep * index);
                Vector3 direction1 = Direction(startAngle + angleStep * (index + 1));
                Vector3 outerBottom0 = direction0 * outerRadius + Vector3.down * halfHeight;
                Vector3 outerBottom1 = direction1 * outerRadius + Vector3.down * halfHeight;
                Vector3 outerTop0 = direction0 * outerRadius + Vector3.up * halfHeight;
                Vector3 outerTop1 = direction1 * outerRadius + Vector3.up * halfHeight;

                if (hasInnerSurface)
                {
                    Vector3 innerBottom0 = direction0 * innerRadius + Vector3.down * halfHeight;
                    Vector3 innerBottom1 = direction1 * innerRadius + Vector3.down * halfHeight;
                    Vector3 innerTop0 = direction0 * innerRadius + Vector3.up * halfHeight;
                    Vector3 innerTop1 = direction1 * innerRadius + Vector3.up * halfHeight;
                    DrawQuad(innerTop0, outerTop0, outerTop1, innerTop1);
                    DrawQuad(innerBottom1, outerBottom1, outerBottom0, innerBottom0);
                    DrawQuad(innerBottom1, innerBottom0, innerTop0, innerTop1);
                }
                else
                {
                    DrawTriangle(Vector3.up * halfHeight, outerTop0, outerTop1);
                    DrawTriangle(Vector3.down * halfHeight, outerBottom1, outerBottom0);
                }

                DrawQuad(outerBottom0, outerBottom1, outerTop1, outerTop0);
            }

            if (angle < FullCircleAngle - FullCircleEpsilon)
            {
                Vector3 startDirection = Direction(startAngle);
                Vector3 endDirection = Direction(startAngle + angle * Mathf.Deg2Rad);
                DrawSectorCap(startDirection, innerRadius, outerRadius, halfHeight);
                DrawSectorCap(endDirection, innerRadius, outerRadius, halfHeight);
            }
        }

        #endregion

        #region 几何辅助

        // 计算胶囊沿局部 Y 轴的轮廓环；两个赤道环之间形成圆柱段。
        private static void ResolveCapsuleProfile(int profile, int hemisphereSegments,
            float radius, float halfLine, out float ringRadius, out float y)
        {
            float angle;
            float centerY;
            if (profile <= hemisphereSegments)
            {
                angle = -Mathf.PI * 0.5f + Mathf.PI * 0.5f * profile / hemisphereSegments;
                centerY = -halfLine;
            }
            else
            {
                int upperProfile = profile - hemisphereSegments - 1;
                angle = Mathf.PI * 0.5f * upperProfile / hemisphereSegments;
                centerY = halfLine;
            }

            ringRadius = Mathf.Cos(angle) * radius;
            y = centerY + Mathf.Sin(angle) * radius;
        }

        // 将以 Y 为轴生成的胶囊顶点映射到配置指定的 X、Y 或 Z 局部轴。
        private static Vector3 MapCapsuleAxis(Vector3 point, CapsuleAxis axis) => axis switch
        {
            CapsuleAxis.X => new Vector3(point.y, point.x, point.z),
            CapsuleAxis.Z => new Vector3(point.x, point.z, point.y),
            _ => point
        };

        // 生成以局部 Y 为高度轴的圆环顶点。
        private static Vector3 RingPoint(float radius, float y, float angle) =>
            new(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);

        // 生成扇形局部 XZ 平面中的径向单位向量。
        private static Vector3 Direction(float radians) =>
            new(Mathf.Sin(radians), 0f, Mathf.Cos(radians));

        // 绘制扇形起点或终点的径向矩形封口。
        private static void DrawSectorCap(Vector3 direction, float innerRadius,
            float outerRadius, float halfHeight)
        {
            DrawQuad(direction * innerRadius + Vector3.down * halfHeight,
                direction * outerRadius + Vector3.down * halfHeight,
                direction * outerRadius + Vector3.up * halfHeight,
                direction * innerRadius + Vector3.up * halfHeight);
        }

        // 复用固定三顶点缓冲；Scene GUI 在 Editor 主线程中串行调用，绘制完成后即可覆盖。
        private static void DrawTriangle(Vector3 a, Vector3 b, Vector3 c)
        {
            triangleVertices[0] = a;
            triangleVertices[1] = b;
            triangleVertices[2] = c;
            Handles.DrawAAConvexPolygon(triangleVertices);
        }

        // 复用固定四顶点缓冲，避免每个曲面分片创建 params 数组。
        private static void DrawQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            quadVertices[0] = a;
            quadVertices[1] = b;
            quadVertices[2] = c;
            quadVertices[3] = d;
            Handles.DrawAAConvexPolygon(quadVertices);
        }

        #endregion
    }
}
#endif