using System;
using UnityEngine;

namespace WS_Modules.Utilities
{
    /// <summary>
    /// 将局部 Physics 形状转换到宿主 Transform 的世界空间并执行 NonAlloc 查询。
    /// </summary>
    public static class PhysicsUtility
    {
        #region 查询缓存

        // Sector 查询先使用可复用候选缓冲，再将通过几何筛选的结果写入调用方缓冲。
        private static Collider[] sectorCandidates = new Collider[64];

        #endregion

        #region 体积查询

        /// <summary>
        /// 根据形状类型执行 Box、Sphere、Capsule 或 Sector 的 NonAlloc 体积查询。
        /// </summary>
        /// <param name="origin">形状局部坐标所属的宿主 Transform。</param>
        /// <param name="data">需要执行的局部形状数据。</param>
        /// <param name="results">接收命中 Collider 的调用方缓冲区。</param>
        /// <param name="layerMask">参与查询的层掩码。</param>
        /// <param name="queryTriggerInteraction">触发器参与查询的策略。</param>
        /// <returns>写入 results 的命中数量；缓冲区不足时遵循 Unity NonAlloc 的截断语义。</returns>
        /// <exception cref="ArgumentNullException">origin、data 或 results 为空。</exception>
        /// <exception cref="ArgumentException">data 类型为 Ray、None 或尺寸不合法。</exception>
        public static int OverlapNonAlloc(Transform origin, PhysicsShapeData data,
            Collider[] results, int layerMask = Physics.DefaultRaycastLayers,
            QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal)
        {
            ValidateCommonArguments(origin, data, results);
            ValidateVolume(data);
            ResolvePose(origin, data, out Vector3 center, out Quaternion rotation);
            Vector3 scale = Abs(origin.lossyScale);

            return data.Type switch
            {
                PhysicsShapeType.Box => Physics.OverlapBoxNonAlloc(center,
                    Vector3.Scale(data.Size, scale) * 0.5f, results, rotation,
                    layerMask, queryTriggerInteraction),
                PhysicsShapeType.Sphere => Physics.OverlapSphereNonAlloc(center,
                    data.Radius * MaxComponent(scale), results, layerMask,
                    queryTriggerInteraction),
                PhysicsShapeType.Capsule => OverlapCapsule(data, center, rotation,
                    scale, results, layerMask, queryTriggerInteraction),
                PhysicsShapeType.Sector => OverlapSector(origin, data, center,
                    scale, results, layerMask, queryTriggerInteraction),
                _ => throw new ArgumentException("当前形状不支持体积查询。", nameof(data))
            };
        }

        /// <summary>
        /// 根据 Ray 数据执行局部姿态转换后的 NonAlloc 射线查询。
        /// </summary>
        /// <param name="origin">形状局部坐标所属的宿主 Transform。</param>
        /// <param name="data">类型必须为 Ray 的局部形状数据。</param>
        /// <param name="results">接收命中 RaycastHit 的调用方缓冲区。</param>
        /// <param name="layerMask">参与查询的层掩码。</param>
        /// <param name="queryTriggerInteraction">触发器参与查询的策略。</param>
        /// <returns>写入 results 的命中数量；缓冲区不足时遵循 Unity NonAlloc 的截断语义。</returns>
        /// <exception cref="ArgumentNullException">origin、data 或 results 为空。</exception>
        /// <exception cref="ArgumentException">data 类型不是 Ray 或长度不合法。</exception>
        public static int RaycastNonAlloc(Transform origin, PhysicsShapeData data,
            RaycastHit[] results, int layerMask = Physics.DefaultRaycastLayers,
            QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal)
        {
            if (origin == null) throw new ArgumentNullException(nameof(origin));
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (results == null) throw new ArgumentNullException(nameof(results));
            if (results.Length == 0) throw new ArgumentException("Raycast 缓冲区不能为空。", nameof(results));
            if (data.Type != PhysicsShapeType.Ray)
                throw new ArgumentException("RaycastNonAlloc 只接受 Ray 形状。", nameof(data));
            if (data.Length <= 0f || float.IsNaN(data.Length) || float.IsInfinity(data.Length))
                throw new ArgumentException("Ray 长度必须为有限正数。", nameof(data));

            ResolvePose(origin, data, out Vector3 start, out _);
            Vector3 scaledDirection = origin.TransformVector(Quaternion.Euler(data.LocalEulerAngles) * Vector3.forward);
            float directionScale = scaledDirection.magnitude;
            if (directionScale <= Mathf.Epsilon)
                throw new ArgumentException("Ray 的宿主 Transform 不能在方向上缩放为零。", nameof(origin));

            // TransformVector 已把宿主非均匀缩放折算到方向；单位化后长度仍保持局部数据语义。
            Vector3 worldDirection = scaledDirection / directionScale;
            float worldLength = data.Length * directionScale;
            return Physics.RaycastNonAlloc(start, worldDirection, results, worldLength,
                layerMask, queryTriggerInteraction);
        }

        #endregion

        #region 形状实现

        /// <summary>
        /// 将胶囊体局部尺寸换算为世界空间并执行 Unity 胶囊查询。
        /// </summary>
        private static int OverlapCapsule(PhysicsShapeData data,
            Vector3 center, Quaternion rotation, Vector3 scale, Collider[] results,
            int layerMask, QueryTriggerInteraction queryTriggerInteraction)
        {
            Vector3 axis = GetAxis(data.CapsuleAxis);
            float axisScale = GetScaleComponent(scale, data.CapsuleAxis);
            float radiusScale = GetPerpendicularScale(scale, data.CapsuleAxis);
            float radius = data.Radius * radiusScale;
            float totalHeight = Mathf.Max(data.Height * axisScale, radius * 2f);
            float halfSegment = Mathf.Max(0f, (totalHeight - radius * 2f) * 0.5f);
            Vector3 worldAxis = rotation * axis;
            Vector3 pointA = center + worldAxis * halfSegment;
            Vector3 pointB = center - worldAxis * halfSegment;
            return Physics.OverlapCapsuleNonAlloc(pointA, pointB, radius, results,
                layerMask, queryTriggerInteraction);
        }

        /// <summary>
        /// 使用包围球收集 Sector 候选，并在形状局部空间完成扇形几何筛选。
        /// </summary>
        private static int OverlapSector(Transform origin, PhysicsShapeData data,
            Vector3 center, Vector3 scale, Collider[] results,
            int layerMask, QueryTriggerInteraction queryTriggerInteraction)
        {
            float broadRadius = Mathf.Sqrt(data.OuterRadius * data.OuterRadius +
                data.Height * data.Height * 0.25f) * MaxComponent(scale);
            int candidateCount = CollectSectorCandidates(center, broadRadius, layerMask,
                queryTriggerInteraction);
            Quaternion inverseShapeRotation = Quaternion.Inverse(Quaternion.Euler(data.LocalEulerAngles));
            int resultCount = 0;

            for (int index = 0; index < candidateCount; index++)
            {
                Collider collider = sectorCandidates[index];
                if (collider == null) continue;
                Vector3 shapePoint = inverseShapeRotation *
                    (origin.InverseTransformPoint(collider.transform.position) - data.LocalPosition);
                if (!IsInsideSector(shapePoint, data)) continue;
                if (resultCount < results.Length)
                    results[resultCount++] = collider;
            }

            return resultCount;
        }

        /// <summary>
        /// 重复扩大内部候选缓冲，直到确认包围球查询没有因容量截断。
        /// </summary>
        private static int CollectSectorCandidates(Vector3 center, float radius, int layerMask,
            QueryTriggerInteraction queryTriggerInteraction)
        {
            while (true)
            {
                int count = Physics.OverlapSphereNonAlloc(center, radius, sectorCandidates,
                    layerMask, queryTriggerInteraction);
                if (count < sectorCandidates.Length) return count;
                if (sectorCandidates.Length > 1_048_576)
                    throw new InvalidOperationException("Sector 候选 Collider 数量超过内部缓冲上限。");
                Array.Resize(ref sectorCandidates, sectorCandidates.Length * 2);
            }
        }

        /// <summary>
        /// 判断点是否落入以局部 +Z 为中心方向的水平扇形柱体。
        /// </summary>
        private static bool IsInsideSector(Vector3 point, PhysicsShapeData data)
        {
            if (Mathf.Abs(point.y) > data.Height * 0.5f) return false;
            Vector2 horizontal = new(point.x, point.z);
            float distance = horizontal.magnitude;
            if (distance < data.InnerRadius || distance > data.OuterRadius) return false;
            if (data.Angle >= 359.999f || distance <= Mathf.Epsilon) return true;
            float halfAngle = data.Angle * 0.5f;
            float angle = Vector2.Angle(Vector2.up, horizontal);
            return angle <= halfAngle;
        }

        #endregion

        #region 校验与坐标

        /// <summary>校验查询的引用和缓冲区契约。</summary>
        private static void ValidateCommonArguments(Transform origin, PhysicsShapeData data,
            Collider[] results)
        {
            if (origin == null) throw new ArgumentNullException(nameof(origin));
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (results == null) throw new ArgumentNullException(nameof(results));
            if (results.Length == 0) throw new ArgumentException("Overlap 缓冲区不能为空。", nameof(results));
        }

        /// <summary>校验体积形状类型及其尺寸边界。</summary>
        private static void ValidateVolume(PhysicsShapeData data)
        {
            if (data.Type is PhysicsShapeType.None or PhysicsShapeType.Ray)
                throw new ArgumentException("当前形状不是可重叠查询的体积。", nameof(data));
            if (data.Type == PhysicsShapeType.Box &&
                (!IsFinitePositive(data.Size.x) || !IsFinitePositive(data.Size.y) ||
                 !IsFinitePositive(data.Size.z)))
                throw new ArgumentException("Box 的三轴尺寸必须为正数。", nameof(data));
            if ((data.Type is PhysicsShapeType.Sphere or PhysicsShapeType.Capsule) &&
                !IsFinitePositive(data.Radius))
                throw new ArgumentException("半径必须为有限正数。", nameof(data));
            if (data.Type == PhysicsShapeType.Capsule &&
                (!IsFinitePositive(data.Height) || data.Height < data.Radius * 2f))
                throw new ArgumentException("Capsule 高度必须为正数且不小于直径。", nameof(data));
            if (data.Type == PhysicsShapeType.Sector &&
                (!IsFinitePositive(data.OuterRadius) || !IsFiniteNonNegative(data.InnerRadius) ||
                 data.InnerRadius > data.OuterRadius || !IsFinitePositive(data.Height) ||
                 !IsFinitePositive(data.Angle) || data.Angle > 360f))
                throw new ArgumentException("Sector 的半径、高度或角度参数不合法。", nameof(data));
        }

        /// <summary>解析局部位置和旋转到宿主 Transform 的世界空间。</summary>
        private static void ResolvePose(Transform origin, PhysicsShapeData data,
            out Vector3 position, out Quaternion rotation)
        {
            position = origin.TransformPoint(data.LocalPosition);
            rotation = origin.rotation * Quaternion.Euler(data.LocalEulerAngles);
        }

        /// <summary>取得局部轴向对应的单位向量。</summary>
        private static Vector3 GetAxis(PhysicsCapsuleAxis axis) => axis switch
        {
            PhysicsCapsuleAxis.X => Vector3.right,
            PhysicsCapsuleAxis.Z => Vector3.forward,
            _ => Vector3.up
        };

        /// <summary>取得胶囊轴向对应的宿主缩放分量。</summary>
        private static float GetScaleComponent(Vector3 scale, PhysicsCapsuleAxis axis) => axis switch
        {
            PhysicsCapsuleAxis.X => scale.x,
            PhysicsCapsuleAxis.Z => scale.z,
            _ => scale.y
        };

        /// <summary>取得垂直于胶囊轴向的最大缩放，保证半径查询不遗漏目标。</summary>
        private static float GetPerpendicularScale(Vector3 scale, PhysicsCapsuleAxis axis) => axis switch
        {
            PhysicsCapsuleAxis.X => Mathf.Max(scale.y, scale.z),
            PhysicsCapsuleAxis.Z => Mathf.Max(scale.x, scale.y),
            _ => Mathf.Max(scale.x, scale.z)
        };

        /// <summary>判断数值是否为有限正数。</summary>
        private static bool IsFinitePositive(float value) => value > 0f &&
            !float.IsNaN(value) && !float.IsInfinity(value);

        /// <summary>判断数值是否为有限非负数。</summary>
        private static bool IsFiniteNonNegative(float value) => value >= 0f &&
            !float.IsNaN(value) && !float.IsInfinity(value);

        /// <summary>取得 Transform 缩放的绝对值，避免负缩放改变碰撞尺寸。</summary>
        private static Vector3 Abs(Vector3 value) => new(Mathf.Abs(value.x),
            Mathf.Abs(value.y), Mathf.Abs(value.z));

        /// <summary>取得向量三个分量中的最大值。</summary>
        private static float MaxComponent(Vector3 value) => Mathf.Max(value.x, Mathf.Max(value.y, value.z));

        #endregion
    }
}
