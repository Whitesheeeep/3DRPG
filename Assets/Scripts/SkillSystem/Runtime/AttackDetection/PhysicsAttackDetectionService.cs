using System;
using System.Collections.Generic;
using UnityEngine;

namespace RPG.SkillSystem
{
    /// <summary>
    /// 将攻击检测配置转换为 Physics NonAlloc 查询，并按固定顺序完成目标解析、自身排除和业务过滤。
    /// </summary>
    internal sealed class PhysicsAttackDetectionService
    {
        #region 查询状态

        private const float WeaponTraceRadius = 0.02f;
        private readonly SkillRuntimeContext context;
        private Collider[] buffer = new Collider[32];

        #endregion

        #region 创建

        /// <summary>
        /// 创建绑定到单次技能上下文的检测服务。
        /// </summary>
        /// <param name="context">提供角色、攻击设置和命中发布入口的执行上下文。</param>
        public PhysicsAttackDetectionService(SkillRuntimeContext context)
        {
            this.context = context;
        }

        #endregion

        #region 体积检测

        /// <summary>
        /// 执行当前攻击 Clip 的体积查询并发布首次命中的业务目标。
        /// </summary>
        /// <param name="clip">产生检测的攻击片段。</param>
        /// <param name="frame">当前逻辑帧。</param>
        /// <param name="data">Box、Sphere、Capsule 或 Sector 配置。</param>
        /// <param name="hitTargets">该 Clip 生命周期内已经发布过的目标实例 ID。</param>
        public void DetectVolume(AttackDetectionSkillClipConfig clip, int frame,
            AttackDetectionDataBase data, HashSet<int> hitTargets)
        {
            switch (data)
            {
                case BoxAttackDetectionData box:
                    DetectBox(clip, frame, box, hitTargets);
                    break;
                case SphereAttackDetectionData sphere:
                    DetectSphere(clip, frame, sphere, hitTargets);
                    break;
                case CapsuleAttackDetectionData capsule:
                    DetectCapsule(clip, frame, capsule, hitTargets);
                    break;
                case SectorAttackDetectionData sector:
                    DetectSector(clip, frame, sector, hitTargets);
                    break;
                case null:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(data), data.GetType(),
                        "当前攻击检测配置不能作为体积查询执行。");
            }
        }

        /// <summary>
        /// 查询局部立方体对应的世界空间碰撞体。
        /// </summary>
        /// <param name="clip">产生检测的攻击片段。</param>
        /// <param name="frame">当前逻辑帧。</param>
        /// <param name="data">立方体局部参数。</param>
        /// <param name="hitTargets">Clip 内去重集合。</param>
        private void DetectBox(AttackDetectionSkillClipConfig clip, int frame,
            BoxAttackDetectionData data, HashSet<int> hitTargets)
        {
            Transform origin = context.Actor.Origin;
            Vector3 center = origin.TransformPoint(data.LocalPosition);
            Quaternion rotation = origin.rotation * Quaternion.Euler(data.LocalEulerAngles);
            Vector3 scale = Abs(origin.lossyScale);
            Vector3 halfExtents = Vector3.Scale(data.Size * 0.5f, scale);
            int count = Query(buffer => Physics.OverlapBoxNonAlloc(center, halfExtents, buffer, rotation,
                context.AttackSettings.LayerMask, context.AttackSettings.TriggerInteraction));
            PublishResults(clip, frame, center, count, hitTargets, null);
        }

        /// <summary>
        /// 查询局部球体对应的世界空间碰撞体。
        /// </summary>
        /// <param name="clip">产生检测的攻击片段。</param>
        /// <param name="frame">当前逻辑帧。</param>
        /// <param name="data">球体局部参数。</param>
        /// <param name="hitTargets">Clip 内去重集合。</param>
        private void DetectSphere(AttackDetectionSkillClipConfig clip, int frame,
            SphereAttackDetectionData data, HashSet<int> hitTargets)
        {
            Transform origin = context.Actor.Origin;
            Vector3 center = origin.TransformPoint(data.LocalPosition);
            float radius = data.Radius * MaxAbsComponent(origin.lossyScale);
            int count = Query(buffer => Physics.OverlapSphereNonAlloc(center, radius, buffer,
                context.AttackSettings.LayerMask, context.AttackSettings.TriggerInteraction));
            PublishResults(clip, frame, center, count, hitTargets, null);
        }

        /// <summary>
        /// 查询局部胶囊体对应的世界空间碰撞体。
        /// </summary>
        /// <param name="clip">产生检测的攻击片段。</param>
        /// <param name="frame">当前逻辑帧。</param>
        /// <param name="data">胶囊体局部参数。</param>
        /// <param name="hitTargets">Clip 内去重集合。</param>
        private void DetectCapsule(AttackDetectionSkillClipConfig clip, int frame,
            CapsuleAttackDetectionData data, HashSet<int> hitTargets)
        {
            Transform origin = context.Actor.Origin;
            Vector3 center = origin.TransformPoint(data.LocalPosition);
            Quaternion rotation = origin.rotation * Quaternion.Euler(data.LocalEulerAngles);
            Vector3 localAxis = GetAxis(data.Axis);
            Vector3 worldAxis = rotation * localAxis;
            float axisScale = GetAxisScale(origin.lossyScale, data.Axis);
            float radialScale = GetRadialScale(origin.lossyScale, data.Axis);
            float radius = data.Radius * radialScale;
            float halfLine = Mathf.Max(0f, data.Height * axisScale * 0.5f - radius);
            Vector3 pointA = center + worldAxis * halfLine;
            Vector3 pointB = center - worldAxis * halfLine;
            int count = Query(buffer => Physics.OverlapCapsuleNonAlloc(pointA, pointB, radius, buffer,
                context.AttackSettings.LayerMask, context.AttackSettings.TriggerInteraction));
            PublishResults(clip, frame, center, count, hitTargets, null);
        }

        /// <summary>
        /// 先使用包围球粗查，再在检测局部空间中过滤扇形半径、高度和角度。
        /// </summary>
        /// <param name="clip">产生检测的攻击片段。</param>
        /// <param name="frame">当前逻辑帧。</param>
        /// <param name="data">扇形柱体局部参数。</param>
        /// <param name="hitTargets">Clip 内去重集合。</param>
        private void DetectSector(AttackDetectionSkillClipConfig clip, int frame,
            SectorAttackDetectionData data, HashSet<int> hitTargets)
        {
            Transform origin = context.Actor.Origin;
            Vector3 center = origin.TransformPoint(data.LocalPosition);
            Quaternion rotation = origin.rotation * Quaternion.Euler(data.LocalEulerAngles);
            float scale = MaxAbsComponent(origin.lossyScale);
            float outerRadius = data.OuterRadius * scale;
            float innerRadius = data.InnerRadius * scale;
            float halfHeight = data.Height * Mathf.Abs(origin.lossyScale.y) * 0.5f;
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

            PublishResults(clip, frame, center, count, hitTargets, sectorPredicate);
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
        /// <param name="hitTargets">Clip 内去重集合。</param>
        public void DetectWeaponTrace(AttackDetectionSkillClipConfig clip, int frame,
            WeaponTraceAttackDetectionData data, Vector3 previousRoot, Vector3 previousTip,
            Vector3 currentRoot, Vector3 currentTip, bool hasPreviousPose, HashSet<int> hitTargets)
        {
            // 检测刀身 的包围胶囊，避免刀尖或刀根在上一帧和当前帧之间快速移动时漏掉目标。
            int pointCount = Mathf.Max(2, data.SamplePointCount);
            int bladeCount = Query(buffer => Physics.OverlapCapsuleNonAlloc(currentRoot, currentTip,
                WeaponTraceRadius, buffer, context.AttackSettings.LayerMask,
                context.AttackSettings.TriggerInteraction));
            PublishResults(clip, frame, (currentRoot + currentTip) * 0.5f,
                bladeCount, hitTargets, null);
            // 检测移动的刀尖和刀根，避免刀身在上一帧和当前帧之间快速移动时漏掉目标。
            for (int index = 0; index < pointCount; index++)
            {
                float t = index / (float)(pointCount - 1);
                Vector3 currentPoint = Vector3.Lerp(currentRoot, currentTip, t);
                Vector3 previousPoint = hasPreviousPose
                    ? Vector3.Lerp(previousRoot, previousTip, t)
                    : currentPoint;
                int count = Query(buffer => Physics.OverlapCapsuleNonAlloc(previousPoint, currentPoint,
                    WeaponTraceRadius, buffer, context.AttackSettings.LayerMask,
                    context.AttackSettings.TriggerInteraction));
                PublishResults(clip, frame, currentPoint, count, hitTargets, null);
            }
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
        /// 按 LayerMask 查询后的固定规则解析目标、排除自身、执行业务过滤并完成 Clip 内去重。
        /// </summary>
        /// <param name="clip">产生检测的攻击片段。</param>
        /// <param name="frame">当前逻辑帧。</param>
        /// <param name="queryCenter">计算近似命中点使用的查询中心。</param>
        /// <param name="count">缓冲区有效数量。</param>
        /// <param name="hitTargets">Clip 内目标去重集合。</param>
        /// <param name="predicate">形状的额外精确过滤；无额外过滤时为空。</param>
        private void PublishResults(AttackDetectionSkillClipConfig clip, int frame,
            Vector3 queryCenter, int count, HashSet<int> hitTargets, Predicate<Collider> predicate)
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
                if (!hitTargets.Add(target.GetInstanceID())) continue;

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
        /// 返回向量各分量绝对值。
        /// </summary>
        /// <param name="value">原始向量。</param>
        /// <returns>绝对值向量。</returns>
        private static Vector3 Abs(Vector3 value) =>
            new(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));

        /// <summary>
        /// 获取 Collider 到指定位置的安全最近点；支持的基础形状使用精确查询，其余类型使用 Bounds 近似。
        /// </summary>
        /// <param name="collider">需要计算最近点的查询结果 Collider。</param>
        /// <param name="position">用于计算最近点的世界坐标。</param>
        /// <returns>支持类型的精确最近点，或不支持类型的世界包围盒最近点。</returns>
        private static Vector3 GetClosestPoint(Collider collider, Vector3 position)
        {
            // Unity 仅允许基础 Collider 和凸 MeshCollider 调用 ClosestPoint；其他类型必须避免触发原生警告。
            bool supportsExactClosestPoint = collider is BoxCollider or SphereCollider or CapsuleCollider ||
                                             collider is MeshCollider { convex: true };
            return supportsExactClosestPoint
                ? collider.ClosestPoint(position)
                : collider.bounds.ClosestPoint(position);
        }

        /// <summary>
        /// 返回缩放的最大绝对分量。
        /// </summary>
        /// <param name="scale">世界缩放。</param>
        /// <returns>最大绝对缩放。</returns>
        private static float MaxAbsComponent(Vector3 scale) =>
            Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));

        /// <summary>
        /// 将胶囊配置轴向转换为单位局部向量。
        /// </summary>
        /// <param name="axis">胶囊轴向枚举。</param>
        /// <returns>对应局部轴。</returns>
        private static Vector3 GetAxis(CapsuleAxis axis) => axis switch
        {
            CapsuleAxis.X => Vector3.right,
            CapsuleAxis.Y => Vector3.up,
            CapsuleAxis.Z => Vector3.forward,
            _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, null)
        };

        /// <summary>
        /// 取得胶囊轴向对应的世界缩放绝对值。
        /// </summary>
        /// <param name="scale">Origin 的 lossyScale。</param>
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
        /// <param name="scale">Origin 的 lossyScale。</param>
        /// <param name="axis">胶囊局部轴向。</param>
        /// <returns>径向缩放。</returns>
        private static float GetRadialScale(Vector3 scale, CapsuleAxis axis) => axis switch
        {
            CapsuleAxis.X => Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)),
            CapsuleAxis.Y => Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)),
            CapsuleAxis.Z => Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y)),
            _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, null)
        };

        #endregion
    }
}
