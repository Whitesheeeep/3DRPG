using System;
using System.Collections.Generic;
using UnityEngine;
using WS_Modules.Utilities;

namespace RPG.SkillSystem
{
    /// <summary>
    /// 将攻击检测配置转换为 Physics NonAlloc 查询，并按固定顺序完成目标解析、自身排除、业务过滤和调试绘制。
    /// </summary>
    internal sealed class PhysicsAttackDetectionService
    {
        #region 查询状态

        private const float WeaponTraceRadius = 0.02f;
        private static readonly Color DebugColor = Color.red;
        private readonly SkillRuntimeContext context;
        private Collider[] buffer = new Collider[32];
        private bool drawAttackDetectionDebug;
        private float attackDetectionDebugDuration;

        #endregion

        #region 创建与调试配置

        /// <summary>
        /// 创建绑定到单次技能上下文的检测服务。
        /// </summary>
        /// <param name="context">提供角色、攻击设置和命中发布入口的执行上下文。</param>
        public PhysicsAttackDetectionService(SkillRuntimeContext context)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>
        /// 设置当前执行是否绘制攻击查询形状，以及线框保留时间。
        /// </summary>
        /// <param name="enabled">是否启用调试绘制。</param>
        /// <param name="duration">线框持续秒数；零表示当前帧。</param>
        /// <exception cref="ArgumentOutOfRangeException">持续时间不是有限非负数。</exception>
        public void SetDebugDrawing(bool enabled, float duration)
        {
            if (duration < 0f || float.IsNaN(duration) || float.IsInfinity(duration))
                throw new ArgumentOutOfRangeException(nameof(duration), duration,
                    "攻击检测调试绘制持续时间必须为有限非负数。");

            drawAttackDetectionDebug = enabled;
            attackDetectionDebugDuration = duration;
        }

        #endregion

        #region 体积检测

        /// <summary>
        /// 执行当前攻击 Clip 的体积查询并发布首次命中的业务目标。
        /// </summary>
        /// <param name="clip">产生检测的攻击片段。</param>
        /// <param name="frame">当前逻辑帧。</param>
        /// <param name="data">Box、Sphere、Capsule 或 Sector 配置。</param>
        /// <param name="bindingMatrix">当前 Clip 绑定 Marker 的世界矩阵。</param>
        /// <param name="hitTargetIds">该 Detection ID 在本次执行中已经发布过的目标实例 ID。</param>
        public void DetectVolume(AttackDetectionSkillClipConfig clip, int frame,
            AttackDetectionDataBase data, Matrix4x4 bindingMatrix, HashSet<int> hitTargetIds)
        {
            switch (data)
            {
                case BoxAttackDetectionData box:
                    DetectBox(clip, frame, box, bindingMatrix, hitTargetIds);
                    break;
                case SphereAttackDetectionData sphere:
                    DetectSphere(clip, frame, sphere, bindingMatrix, hitTargetIds);
                    break;
                case CapsuleAttackDetectionData capsule:
                    DetectCapsule(clip, frame, capsule, bindingMatrix, hitTargetIds);
                    break;
                case SectorAttackDetectionData sector:
                    DetectSector(clip, frame, sector, bindingMatrix, hitTargetIds);
                    break;
                case null:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(data), data.GetType(),
                        "当前攻击检测配置不能作为体积查询执行。");
            }
        }

        /// <summary>
        /// 查询局部立方体对应的世界空间碰撞体，并绘制相同的查询形状。
        /// </summary>
        private void DetectBox(AttackDetectionSkillClipConfig clip, int frame,
            BoxAttackDetectionData data, Matrix4x4 bindingMatrix, HashSet<int> hitTargetIds)
        {
            Vector3 center = bindingMatrix.MultiplyPoint3x4(data.LocalPosition);
            Quaternion rotation = bindingMatrix.rotation * Quaternion.Euler(data.LocalEulerAngles);
            Vector3 scale = GetMatrixScale(bindingMatrix);
            Vector3 size = Vector3.Scale(data.Size, scale);
            DrawDebugCube(center, size, rotation);
            int count = Query(buffer => Physics.OverlapBoxNonAlloc(center, size * 0.5f, buffer, rotation,
                context.AttackSettings.LayerMask, context.AttackSettings.TriggerInteraction));
            PublishResults(clip, frame, center, count, hitTargetIds, null);
        }

        /// <summary>
        /// 查询局部球体对应的世界空间碰撞体，并绘制相同的查询形状。
        /// </summary>
        private void DetectSphere(AttackDetectionSkillClipConfig clip, int frame,
            SphereAttackDetectionData data, Matrix4x4 bindingMatrix, HashSet<int> hitTargetIds)
        {
            Vector3 center = bindingMatrix.MultiplyPoint3x4(data.LocalPosition);
            float radius = data.Radius * MaxAbsComponent(GetMatrixScale(bindingMatrix));
            DrawDebugSphere(center, radius);
            int count = Query(buffer => Physics.OverlapSphereNonAlloc(center, radius, buffer,
                context.AttackSettings.LayerMask, context.AttackSettings.TriggerInteraction));
            PublishResults(clip, frame, center, count, hitTargetIds, null);
        }

        /// <summary>
        /// 查询局部胶囊体对应的世界空间碰撞体，并绘制相同的查询形状。
        /// </summary>
        private void DetectCapsule(AttackDetectionSkillClipConfig clip, int frame,
            CapsuleAttackDetectionData data, Matrix4x4 bindingMatrix, HashSet<int> hitTargetIds)
        {
            Vector3 center = bindingMatrix.MultiplyPoint3x4(data.LocalPosition);
            Quaternion rotation = bindingMatrix.rotation * Quaternion.Euler(data.LocalEulerAngles);
            Vector3 scale = GetMatrixScale(bindingMatrix);
            Vector3 localAxis = GetAxis(data.Axis);
            Vector3 worldAxis = rotation * localAxis;
            float axisScale = GetAxisScale(scale, data.Axis);
            float radialScale = GetRadialScale(scale, data.Axis);
            float radius = data.Radius * radialScale;
            float height = Mathf.Max(data.Height * axisScale, radius * 2f);
            float halfLine = Mathf.Max(0f, height * 0.5f - radius);
            Vector3 pointA = center + worldAxis * halfLine;
            Vector3 pointB = center - worldAxis * halfLine;
            DrawDebugCapsule(center, radius, height, rotation * GetCapsuleAxisRotation(data.Axis));
            int count = Query(buffer => Physics.OverlapCapsuleNonAlloc(pointA, pointB, radius, buffer,
                context.AttackSettings.LayerMask, context.AttackSettings.TriggerInteraction));
            PublishResults(clip, frame, center, count, hitTargetIds, null);
        }

        /// <summary>
        /// 先使用包围球粗查，再在检测局部空间中过滤扇形半径、高度和角度。
        /// </summary>
        private void DetectSector(AttackDetectionSkillClipConfig clip, int frame,
            SectorAttackDetectionData data, Matrix4x4 bindingMatrix, HashSet<int> hitTargetIds)
        {
            Vector3 center = bindingMatrix.MultiplyPoint3x4(data.LocalPosition);
            Quaternion rotation = bindingMatrix.rotation * Quaternion.Euler(data.LocalEulerAngles);
            Vector3 scale = GetMatrixScale(bindingMatrix);
            float scaleRadius = MaxAbsComponent(scale);
            float outerRadius = data.OuterRadius * scaleRadius;
            float innerRadius = data.InnerRadius * scaleRadius;
            float height = data.Height * Mathf.Abs(scale.y);
            float halfHeight = height * 0.5f;
            DrawDebugSector(center, innerRadius, outerRadius, data.Angle, height, rotation);
            int count = Query(buffer => Physics.OverlapSphereNonAlloc(center,
                Mathf.Sqrt(outerRadius * outerRadius + halfHeight * halfHeight), buffer,
                context.AttackSettings.LayerMask, context.AttackSettings.TriggerInteraction));

            Quaternion inverse = Quaternion.Inverse(rotation);
            Predicate<Collider> sectorPredicate = collider =>
            {
                Vector3 point = GetClosestPoint(collider, center);
                Vector3 local = inverse * (point - center);
                float planarRadius = new Vector2(local.x, local.z).magnitude;
                if (planarRadius < innerRadius || planarRadius > outerRadius || Mathf.Abs(local.y) > halfHeight)
                    return false;
                if (planarRadius <= Mathf.Epsilon) return innerRadius <= Mathf.Epsilon;
                float angle = Vector3.Angle(Vector3.forward, new Vector3(local.x, 0f, local.z));
                return angle <= data.Angle * 0.5f;
            };

            PublishResults(clip, frame, center, count, hitTargetIds, sectorPredicate);
        }

        #endregion

        #region 武器轨迹

        /// <summary>
        /// 沿刀根到刀尖插值采样，并在上一姿态与当前姿态之间执行细胶囊扫掠。
        /// </summary>
        /// <param name="clip">产生检测的攻击片段。</param>
        /// <param name="frame">当前逻辑帧。</param>
        /// <param name="data">刀刃插值点数量。</param>
        /// <param name="previousRoot">上一检测姿态的刀根世界位置。</param>
        /// <param name="previousTip">上一检测姿态的刀尖世界位置。</param>
        /// <param name="currentRoot">当前姿态的刀根世界位置。</param>
        /// <param name="currentTip">当前姿态的刀尖世界位置。</param>
        /// <param name="hasPreviousPose">是否已有上一检测姿态。</param>
        /// <param name="hitTargetIds">该 Detection ID 在本次执行中已经发布过的目标实例 ID。</param>
        public void DetectWeaponTrace(AttackDetectionSkillClipConfig clip, int frame,
            WeaponTraceAttackDetectionData data, Vector3 previousRoot, Vector3 previousTip,
            Vector3 currentRoot, Vector3 currentTip, bool hasPreviousPose,
            HashSet<int> hitTargetIds)
        {
            // 先绘制并检测当前刀身，再逐点检测上一姿态到当前姿态的扫掠胶囊。
            DrawDebugCapsuleBetween(currentRoot, currentTip);
            int pointCount = Mathf.Max(2, data.SamplePointCount);
            int bladeCount = Query(buffer => Physics.OverlapCapsuleNonAlloc(currentRoot, currentTip,
                WeaponTraceRadius, buffer, context.AttackSettings.LayerMask,
                context.AttackSettings.TriggerInteraction));
            PublishResults(clip, frame, (currentRoot + currentTip) * 0.5f,
                bladeCount, hitTargetIds, null);

            for (int index = 0; index < pointCount; index++)
            {
                float t = index / (float)(pointCount - 1);
                Vector3 currentPoint = Vector3.Lerp(currentRoot, currentTip, t);
                Vector3 previousPoint = hasPreviousPose
                    ? Vector3.Lerp(previousRoot, previousTip, t)
                    : currentPoint;
                DrawDebugCapsuleBetween(previousPoint, currentPoint);
                int count = Query(buffer => Physics.OverlapCapsuleNonAlloc(previousPoint, currentPoint,
                    WeaponTraceRadius, buffer, context.AttackSettings.LayerMask,
                    context.AttackSettings.TriggerInteraction));
                PublishResults(clip, frame, currentPoint, count, hitTargetIds, null);
            }
        }

        #endregion

        #region 调试绘制

        /// <summary>绘制当前采样的 Box 查询形状。</summary>
        private void DrawDebugCube(Vector3 center, Vector3 size, Quaternion rotation)
        {
            if (drawAttackDetectionDebug)
                DebugUtility.DrawCube(center, size, rotation, DebugColor,
                    attackDetectionDebugDuration, true);
        }

        /// <summary>绘制当前采样的 Sphere 查询形状。</summary>
        private void DrawDebugSphere(Vector3 center, float radius)
        {
            if (drawAttackDetectionDebug)
                DebugUtility.DrawSphere(center, radius, DebugColor,
                    attackDetectionDebugDuration, true);
        }

        /// <summary>绘制当前采样的 Capsule 查询形状。</summary>
        private void DrawDebugCapsule(Vector3 center, float radius, float height, Quaternion rotation)
        {
            if (drawAttackDetectionDebug)
                DebugUtility.DrawCapsule(center, radius, height, rotation, DebugColor,
                    attackDetectionDebugDuration, true);
        }

        /// <summary>绘制当前采样的 Sector 查询形状。</summary>
        private void DrawDebugSector(Vector3 center, float innerRadius, float outerRadius,
            float angle, float height, Quaternion rotation)
        {
            if (drawAttackDetectionDebug)
                DebugUtility.DrawSector(center, innerRadius, outerRadius, angle, height,
                    rotation, DebugColor, attackDetectionDebugDuration, true);
        }

        /// <summary>将 WeaponTrace 的端点扫掠转换为 DebugUtility 使用的局部 Y 轴胶囊。</summary>
        private void DrawDebugCapsuleBetween(Vector3 start, Vector3 end)
        {
            if (!drawAttackDetectionDebug) return;
            Vector3 direction = end - start;
            float distance = direction.magnitude;
            Quaternion rotation = distance > Mathf.Epsilon
                ? Quaternion.FromToRotation(Vector3.up, direction / distance)
                : Quaternion.identity;
            DrawDebugCapsule((start + end) * 0.5f, WeaponTraceRadius,
                Mathf.Max(WeaponTraceRadius * 2f, distance + WeaponTraceRadius * 2f), rotation);
        }

        #endregion

        #region 查询与过滤

        /// <summary>
        /// 执行 NonAlloc 查询，并在缓冲区占满时扩容重试，避免静默漏掉目标。
        /// </summary>
        /// <param name="query">写入指定 Collider 缓冲区的 Physics 查询。</param>
        /// <returns>缓冲区中有效 Collider 数量。</returns>
        private int Query(Func<Collider[], int> query)
        {
            int count = query(buffer);
            while (count == buffer.Length)
            {
                buffer = new Collider[buffer.Length * 2];
                count = query(buffer);
            }
            return count;
        }

        /// <summary>
        /// 按 LayerMask 查询后的固定规则解析目标、排除自身、执行业务过滤并完成 Detection ID 去重。
        /// 同一 Detection ID 在一次技能执行内只允许同一目标第一次命中发布事件。
        /// </summary>
        /// <param name="clip">产生检测的攻击片段。</param>
        /// <param name="frame">当前逻辑帧。</param>
        /// <param name="queryCenter">计算近似命中点使用的查询中心。</param>
        /// <param name="count">缓冲区有效数量。</param>
        /// <param name="hitTargetIds">Detection ID 共享的目标去重集合。</param>
        /// <param name="predicate">形状的额外精确过滤；无额外过滤时为空。</param>
        private void PublishResults(AttackDetectionSkillClipConfig clip, int frame,
            Vector3 queryCenter, int count, HashSet<int> hitTargetIds, Predicate<Collider> predicate)
        {
            GameObject owner = context.Actor.Owner;
            ISkillAttackTargetFilter filter = context.AttackSettings.TargetFilter;
            for (int index = 0; index < count; index++)
            {
                Collider collider = buffer[index];
                if (collider == null || predicate != null && !predicate(collider)) continue;

                GameObject target = filter != null
                    ? filter.ResolveTarget(collider)
                    : collider.attachedRigidbody != null
                        ? collider.attachedRigidbody.gameObject
                        : collider.gameObject;
                if (target == null || IsOwnerHierarchy(owner.transform, target.transform)) continue;
                if (filter != null && !filter.CanHit(owner, target, collider)) continue;
                if (!hitTargetIds.Add(target.GetInstanceID())) continue;

                Vector3 point = GetClosestPoint(collider, queryCenter);
                context.HitPublisher?.Invoke(new SkillHitEventArgs(
                    context.ExecutionId, context.Request.Config, owner, clip, frame,
                    target, collider, point));
            }
        }

        /// <summary>
        /// 判断目标是否为施法者自身或其子层级，避免自伤 Collider 进入业务过滤器。
        /// </summary>
        /// <param name="owner">施法者根节点。</param>
        /// <param name="target">待检查目标节点。</param>
        /// <returns>属于施法者层级时返回 true。</returns>
        private static bool IsOwnerHierarchy(Transform owner, Transform target) =>
            target == owner || target.IsChildOf(owner);

        /// <summary>
        /// 从世界矩阵读取绝对缩放，保持与 Transform.lossyScale 相同的非负尺寸语义。
        /// </summary>
        /// <param name="matrix">需要读取的世界矩阵。</param>
        /// <returns>矩阵三个基向量的长度组成的绝对缩放。</returns>
        private static Vector3 GetMatrixScale(Matrix4x4 matrix) => new(
            matrix.GetColumn(0).magnitude,
            matrix.GetColumn(1).magnitude,
            matrix.GetColumn(2).magnitude);

        /// <summary>
        /// 获取 Collider 到指定位置的安全最近点；支持的基础形状使用精确查询，其余类型使用 Bounds 近似。
        /// </summary>
        /// <param name="collider">需要计算最近点的查询结果 Collider。</param>
        /// <param name="position">用于计算最近点的世界坐标。</param>
        /// <returns>支持类型的精确最近点，或不支持类型的世界包围盒最近点。</returns>
        private static Vector3 GetClosestPoint(Collider collider, Vector3 position)
        {
            bool supportsExactClosestPoint = collider is BoxCollider or SphereCollider or CapsuleCollider ||
                                             collider is MeshCollider { convex: true };
            return supportsExactClosestPoint
                ? collider.ClosestPoint(position)
                : collider.bounds.ClosestPoint(position);
        }

        /// <summary>返回缩放的最大绝对分量。</summary>
        /// <param name="scale">世界缩放。</param>
        /// <returns>最大绝对缩放。</returns>
        private static float MaxAbsComponent(Vector3 scale) =>
            Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));

        /// <summary>将胶囊配置轴向转换为单位局部向量。</summary>
        /// <param name="axis">胶囊轴向枚举。</param>
        /// <returns>对应局部轴。</returns>
        private static Vector3 GetAxis(CapsuleAxis axis) => axis switch
        {
            CapsuleAxis.X => Vector3.right,
            CapsuleAxis.Y => Vector3.up,
            CapsuleAxis.Z => Vector3.forward,
            _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, null)
        };

        /// <summary>取得胶囊轴向对应的世界缩放绝对值。</summary>
        /// <param name="scale">绑定矩阵的世界缩放。</param>
        /// <param name="axis">胶囊局部轴向。</param>
        /// <returns>轴向缩放。</returns>
        private static float GetAxisScale(Vector3 scale, CapsuleAxis axis) => axis switch
        {
            CapsuleAxis.X => Mathf.Abs(scale.x),
            CapsuleAxis.Y => Mathf.Abs(scale.y),
            CapsuleAxis.Z => Mathf.Abs(scale.z),
            _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, null)
        };

        /// <summary>
        /// 取得胶囊轴向垂直平面的最大缩放，保证非均匀缩放下查询不会小于配置体积。
        /// </summary>
        /// <param name="scale">绑定矩阵的世界缩放。</param>
        /// <param name="axis">胶囊局部轴向。</param>
        /// <returns>径向缩放。</returns>
        private static float GetRadialScale(Vector3 scale, CapsuleAxis axis) => axis switch
        {
            CapsuleAxis.X => Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)),
            CapsuleAxis.Y => Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)),
            CapsuleAxis.Z => Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y)),
            _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, null)
        };

        /// <summary>将指定胶囊局部轴旋转到 DebugUtility 使用的局部 Y 轴。</summary>
        /// <param name="axis">胶囊配置的局部轴向。</param>
        /// <returns>将局部 Y 轴转到配置轴向的旋转。</returns>
        private static Quaternion GetCapsuleAxisRotation(CapsuleAxis axis) => axis switch
        {
            CapsuleAxis.X => Quaternion.FromToRotation(Vector3.up, Vector3.right),
            CapsuleAxis.Z => Quaternion.FromToRotation(Vector3.up, Vector3.forward),
            _ => Quaternion.identity
        };

        #endregion
    }
}
