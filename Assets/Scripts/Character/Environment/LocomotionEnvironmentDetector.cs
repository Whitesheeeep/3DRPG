using System;
using RPG.Character.State;
using UnityEngine;
using WS_Modules.Utilities;

namespace RPG.Character
{
    /// <summary>
    /// 采集 CharacterRoot 共享的地面、头顶和垂直运动事实，并写入 PlayerStateBlackboard。
    /// 该检测器不依赖 CharacterController.isGrounded，也不决定 Locomotion 状态。
    /// </summary>
    [Serializable]
    public sealed class LocomotionEnvironmentDetector
    {
        #region 依赖与检测配置

        // 依赖字段：所有形状都以 CharacterRoot 为局部坐标宿主，避免切换角色时重新绑定。
        [SerializeField]
        private PhysicsShapeData groundDetectionShape = new();
        [SerializeField]
        private PhysicsShapeData ceilingDetectionShape = new();
        [SerializeField]
        private LayerMask environmentLayerMask = Physics.DefaultRaycastLayers;
        [SerializeField]
        private QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.Ignore;

        private readonly RaycastHit[] castHitBuffer = new RaycastHit[16];
        private Transform characterRoot;
        private Vector3 previousRootPosition;
        private bool previousGrounded;
        private float airbornePeakWorldY;
        private bool initialized;

        #endregion

        #region 生命周期与采样

        /// <summary>
        /// 绑定 CharacterRoot 并建立第一份环境快照。
        /// 初始化快照只建立位移基线，不推进跳跃、重力或任何状态机。
        /// </summary>
        /// <param name="sourceCharacterRoot">承载共享 CharacterController 与角色队伍的根节点。</param>
        /// <param name="blackboard">需要写入初始环境事实的共享黑板。</param>
        internal void Initialize(Transform sourceCharacterRoot, PlayerStateBlackboard blackboard)
        {
            characterRoot = sourceCharacterRoot ??
                throw new ArgumentNullException(nameof(sourceCharacterRoot));
            if (blackboard == null)
                throw new ArgumentNullException(nameof(blackboard));
            ValidateShape(groundDetectionShape, "地面检测形状");
            ValidateShape(ceilingDetectionShape, "头顶检测形状");

            previousRootPosition = characterRoot.position;
            SampleEnvironment(0f, blackboard, true);
            initialized = true;
        }

        /// <summary>
        /// 在 PlayerController 的 Update 开始阶段采集最新环境事实。
        /// Update 采样可以让同一渲染帧内的 Locomotion 读取当前根节点位置，避免固定物理频率造成起跳/起步视觉滞后。
        /// </summary>
        /// <param name="deltaTime">当前渲染帧的时间步长。</param>
        /// <param name="blackboard">稳定 Player 共享的状态黑板。</param>
        internal void TickUpdate(float deltaTime, PlayerStateBlackboard blackboard)
        {
            EnsureInitialized();
            if (blackboard == null)
                throw new ArgumentNullException(nameof(blackboard));
            SampleEnvironment(Mathf.Max(0f, deltaTime), blackboard, false);
        }

        /// <summary>在局部采样完成后将所有环境事实一次性写入黑板。</summary>
        /// <param name="deltaTime">本次 Update 时间步长。</param>
        /// <param name="blackboard">环境事实写入目标。</param>
        /// <param name="initialSample">是否为初始化边界；初始化不产生垂直速度和土狼时间。</param>
        private void SampleEnvironment(float deltaTime, PlayerStateBlackboard blackboard, bool initialSample)
        {
            Vector3 currentRootPosition = characterRoot.position;
            Vector3 displacement = currentRootPosition - previousRootPosition;
            previousRootPosition = currentRootPosition;

            bool grounded = TryGetNearestHit(groundDetectionShape, out RaycastHit groundHit);
            bool ceilingBlocked = TryGetNearestHit(ceilingDetectionShape, out RaycastHit ceilingHit);
            Vector3 groundNormal = grounded ? groundHit.normal : characterRoot.up;
            Vector3 ceilingNormal = ceilingBlocked ? ceilingHit.normal : -characterRoot.up;
            float groundDistance = grounded ? groundHit.distance : float.PositiveInfinity;
            float ceilingDistance = ceilingBlocked ? ceilingHit.distance : float.PositiveInfinity;
            float observedVerticalSpeed = initialSample || deltaTime <= 0f
                ? 0f
                : displacement.y / deltaTime;
            Vector3 observedPlanarVelocity = initialSample || deltaTime <= 0f
                ? Vector3.zero
                : Vector3.ProjectOnPlane(displacement, Vector3.up) / deltaTime;

            float timeSinceGrounded;
            if (grounded)
            {
                timeSinceGrounded = 0f;
            }
            else if (initialSample ||
                     (!previousGrounded && float.IsPositiveInfinity(blackboard.TimeSinceGrounded)))
            {
                timeSinceGrounded = float.PositiveInfinity;
            }
            else if (previousGrounded)
            {
                // 本帧刚离地时从一个完整的渲染采样步开始计时，保留土狼时间窗口。
                timeSinceGrounded = deltaTime;
            }
            else
            {
                timeSinceGrounded = blackboard.TimeSinceGrounded + deltaTime;
            }

            float fallHeight = UpdateFallHeight(grounded, currentRootPosition.y, initialSample);

            // 只有完成本次所有查询和派生量计算后才写入 Blackboard，避免状态机读取半更新事实。
            blackboard.IsGrounded = grounded;
            blackboard.GroundNormal = groundNormal;
            blackboard.GroundDistance = groundDistance;
            blackboard.IsCeilingBlocked = ceilingBlocked;
            blackboard.CeilingNormal = ceilingNormal;
            blackboard.CeilingDistance = ceilingDistance;
            blackboard.ObservedVerticalSpeed = observedVerticalSpeed;
            blackboard.ObservedPlanarVelocity = observedPlanarVelocity;
            blackboard.TimeSinceGrounded = timeSinceGrounded;
            blackboard.CurrentFallHeight = fallHeight;
            previousGrounded = grounded;
        }

        /// <summary>
        /// 根据接地边界累计本次离地高度；重新接地时保留刚刚完成的下落高度供 FallLand 选片。
        /// </summary>
        /// <param name="grounded">本次查询是否接地。</param>
        /// <param name="currentWorldY">CharacterRoot 当前世界 Y。</param>
        /// <param name="initialSample">是否为初始化采样。</param>
        /// <returns>本次离地过程的最大下降高度。</returns>
        private float UpdateFallHeight(bool grounded, float currentWorldY, bool initialSample)
        {
            if (initialSample)
            {
                airbornePeakWorldY = currentWorldY;
                return 0f;
            }

            if (!previousGrounded && grounded)
                return Mathf.Max(0f, airbornePeakWorldY - currentWorldY);

            if (grounded)
            {
                airbornePeakWorldY = currentWorldY;
                return 0f;
            }

            if (previousGrounded)
                airbornePeakWorldY = currentWorldY;
            else
                airbornePeakWorldY = Mathf.Max(airbornePeakWorldY, currentWorldY);
            return Mathf.Max(0f, airbornePeakWorldY - currentWorldY);
        }

        #endregion

        #region 查询与校验

        /// <summary>使用 PhysicsShapeData 查询离 CharacterRoot 最近的有效环境命中。</summary>
        /// <param name="shape">以 CharacterRoot 为宿主的 SphereCast 形状。</param>
        /// <param name="nearestHit">最近的非角色层级命中。</param>
        /// <returns>存在有效环境命中时返回 true。</returns>
        private bool TryGetNearestHit(PhysicsShapeData shape, out RaycastHit nearestHit)
        {
            int hitCount = PhysicsUtility.SphereCastNonAlloc(
                characterRoot,
                shape,
                castHitBuffer,
                environmentLayerMask,
                queryTriggerInteraction);
            float nearestDistance = float.PositiveInfinity;
            nearestHit = default;

            for (int index = 0; index < hitCount; index++)
            {
                RaycastHit hit = castHitBuffer[index];
                Collider collider = hit.collider;
                if (collider == null || collider.transform == characterRoot ||
                    collider.transform.IsChildOf(characterRoot))
                    continue;
                if (hit.distance < nearestDistance)
                {
                    nearestDistance = hit.distance;
                    nearestHit = hit;
                }
            }

            return nearestDistance < float.PositiveInfinity;
        }

        /// <summary>确认检测形状已配置为可由 PhysicsUtility 执行的 SphereCast。</summary>
        /// <param name="shape">待检查的形状。</param>
        /// <param name="label">Inspector 中使用的形状名称。</param>
        private static void ValidateShape(PhysicsShapeData shape, string label)
        {
            if (shape == null)
                throw new InvalidOperationException($"LocomotionEnvironmentDetector 缺少{label}配置。");
            if (shape.Type != PhysicsShapeType.Sphere)
                throw new InvalidOperationException($"LocomotionEnvironmentDetector 的{label}必须使用 Sphere 类型。");
        }

        /// <summary>确认检测器已经完成 CharacterRoot 绑定。</summary>
        private void EnsureInitialized()
        {
            if (!initialized)
                throw new InvalidOperationException("LocomotionEnvironmentDetector 尚未初始化。");
        }

        /// <summary>
        /// 绘制与实际 SphereCast 相同的球心、半径和投射方向，避免 Inspector 中看到的形状与运行时查询不一致。
        /// </summary>
        public void OnGizmosDraw()
        {
            if (characterRoot == null)
                return;

            DrawSphereCastGizmo(groundDetectionShape, Color.green);
            DrawSphereCastGizmo(ceilingDetectionShape, Color.red);
        }

        /// <summary>按 PhysicsUtility 的缩放规则绘制 SphereCast 起点、终点与连接线。</summary>
        /// <param name="shape">以 CharacterRoot 为局部坐标宿主的球体投射配置。</param>
        /// <param name="color">该检测用途对应的 Gizmo 颜色。</param>
        private void DrawSphereCastGizmo(PhysicsShapeData shape, Color color)
        {
            if (shape == null || !shape.CanDrawGizmos || shape.Type != PhysicsShapeType.Sphere)
                return;

            Vector3 start = characterRoot.TransformPoint(shape.LocalPosition);
            Vector3 scaledDirection = characterRoot.TransformVector(
                Quaternion.Euler(shape.LocalEulerAngles) * Vector3.forward);
            float directionScale = scaledDirection.magnitude;
            if (directionScale <= Mathf.Epsilon)
                return;

            Vector3 direction = scaledDirection / directionScale;
            float length = shape.Length * directionScale;
            Vector3 end = start + direction * length;
            Vector3 scale = characterRoot.lossyScale;
            float radius = shape.Radius * Mathf.Max(
                Mathf.Abs(scale.x),
                Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));

            Color previousColor = Gizmos.color;
            Gizmos.color = color;
            Gizmos.DrawWireSphere(start, radius);
            Gizmos.DrawWireSphere(end, radius);
            Gizmos.DrawLine(start, end);
            Gizmos.color = previousColor;
        }
        #endregion
    }
}
