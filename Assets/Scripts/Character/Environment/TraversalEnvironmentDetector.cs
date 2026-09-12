using System;
using UnityEngine;
using WS_Modules.Utilities;

namespace RPG.Character
{
    /// <summary>
    /// 按一次输入采集静态环境中的 Vault/Mantle 候选。
    /// 检测器只返回冻结的世界空间几何事实，不读取输入、不消费 Request，也不切换状态。
    /// </summary>
    [Serializable]
    public sealed class TraversalEnvironmentDetector
    {
        #region 常量与依赖

        private const float Epsilon = 0.0001f;
        private const int RaycastBufferCapacity = 32;
        private const int OverlapBufferCapacity = 64;

        // 普通检测线与最终采用结果使用不同颜色，后绘制的结果线会覆盖普通线。
        private static readonly Color FrontRayColor = new(1f, 0.75f, 0f, 1f);
        private static readonly Color TopRayColor = new(0f, 0.9f, 1f, 1f);
        private static readonly Color SupportedDepthProbeColor = new(0.1f, 0.45f, 1f, 1f);
        private static readonly Color UnsupportedDepthProbeColor = new(1f, 0.15f, 0.15f, 1f);
        private static readonly Color RefinementProbeColor = new(0.75f, 0.25f, 1f, 1f);
        private static readonly Color SelectedColor = new(0.1f, 1f, 0.15f, 1f);
        private static readonly Color FarEdgeColor = new(1f, 0.05f, 0.85f, 1f);
        private static readonly Color TargetColor = new(1f, 0.95f, 0.1f, 1f);
        private static readonly Color RejectedColor = new(1f, 0.1f, 0.05f, 1f);
        private static readonly Color CapsuleColor = new(0f, 0.85f, 1f, 1f);

        private CharacterActor character;
        private Transform characterRoot;
        private CharacterController characterController;
        private TraversalDetectionSettings settings;
        private readonly RaycastHit[] raycastHits = new RaycastHit[RaycastBufferCapacity];
        private readonly Collider[] overlapResults = new Collider[OverlapBufferCapacity];

        #endregion

        #region 生命周期

        /// <summary>
        /// 绑定角色和本次项目使用的 Traversal 几何配置。
        /// </summary>
        /// <param name="sourceCharacter">用于忽略角色自身碰撞体的角色。</param>
        /// <param name="detectionSettings">由 PlayerFSMTransition 持有的检测配置。</param>
        /// <exception cref="ArgumentNullException">角色或配置为空时抛出。</exception>
        public void Initialize(CharacterActor sourceCharacter,
            TraversalDetectionSettings detectionSettings)
        {
            character = sourceCharacter ?? throw new ArgumentNullException(nameof(sourceCharacter));
            settings = detectionSettings ?? throw new ArgumentNullException(nameof(detectionSettings));
            settings.Validate();
            characterRoot = sourceCharacter.RootTransform ?? throw new InvalidOperationException(
                $"角色 '{sourceCharacter.name}' 没有可用 CharacterRoot。");
            characterController = characterRoot.GetComponent<CharacterController>() ??
                throw new InvalidOperationException(
                    $"角色 '{sourceCharacter.name}' 的 CharacterRoot 缺少 CharacterController。");
        }

        #endregion

        #region 检测入口

        /// <summary>
        /// 执行一次前向环境检测并返回 Vault/Mantle 候选。
        /// </summary>
        /// <param name="candidate">检测成功时输出冻结的世界空间候选。</param>
        /// <returns>存在可执行候选时返回 true。</returns>
        public bool TryDetect(out TraversalCandidate candidate)
        {
            EnsureInitialized();

            // 角色前向必须在水平面上，否则无法判断墙面和顶部。
            Vector3 up = characterRoot.up;
            Vector3 forward = Vector3.ProjectOnPlane(characterRoot.forward, up);
            if (forward.sqrMagnitude <= Epsilon)
            {
                candidate = default;
                return false;
            }

            // 从角色中心底部采样前墙，避免角色胶囊碰撞体挡住射线。
            forward.Normalize();
            Vector3 basePoint = characterRoot.TransformPoint(
                new Vector3(characterController.center.x,
                    characterController.center.y - characterController.height * 0.5f,
                    characterController.center.z));
            // 前墙射线起点稍微抬高，避免角色胶囊底部挡住射线。
            Vector3 wallOrigin = basePoint + up * 0.05f;

            // 前墙检测失败时直接返回，避免后续顶部和后沿检测浪费性能。
            if (!TryFindFrontWall(wallOrigin, forward, up, out RaycastHit wallHit))
            {
                candidate = default;
                return false;
            }

            // 顶部检测起点在前墙命中点上方，避免角色胶囊挡住射线。
            Vector3 topCastOrigin = wallHit.point + forward * settings.TopCastForwardOffset +
                up * settings.MaxObstacleHeight;
            // 顶部检测距离稍微加大，避免墙面顶点挡住射线。
            float topCastDistance = settings.MaxObstacleHeight + 0.5f;
            DrawRay(topCastOrigin, -up * topCastDistance, TopRayColor);
            if (!TryFindTopSurface(topCastOrigin, -up, topCastDistance, out RaycastHit topHit))
            {
                candidate = default;
                return false;
            }

            // 找到顶部顶点后，检查顶部表面坡度是否过陡，过陡时直接拒绝。
            if (Vector3.Dot(topHit.normal, up) < settings.MinTopNormalUpDot)
            {
                DrawRay(topCastOrigin, -up * topHit.distance, RejectedColor);
                DrawCross(topHit.point, RejectedColor, up);
                DrawLine(topHit.point, topHit.point + topHit.normal * 0.25f, RejectedColor);
                candidate = default;
                return false;
            }

            // 顶部命中点与角色底部的高度差，作为障碍物高度。
            float obstacleHeight = Vector3.Dot(topHit.point - basePoint, up);
            if (obstacleHeight < settings.MinObstacleHeight ||
                obstacleHeight > settings.MaxObstacleHeight)
            {
                DrawRay(topCastOrigin, -up * topHit.distance, RejectedColor);
                DrawCross(topHit.point, RejectedColor, up);
                DrawLine(topHit.point, topHit.point + topHit.normal * 0.25f, RejectedColor);
                candidate = default;
                return false;
            }

            // 检查是否有远端边缘，用于判断是否可以进行翻越或攀爬。
            bool hasFarEdge = TryFindFarEdge(topHit.point, forward, up,
                out float obstacleDepth, out Vector3 farEdgePoint);
            bool isVault = obstacleHeight <= settings.VaultMaxObstacleHeight && hasFarEdge &&
                obstacleDepth <= settings.VaultMaxDepth;

            Vector3 targetFootPoint;
            TraversalCandidateKind kind;
            if (isVault)
            {
                // Vault 使用真实后沿，而不是角色半径或固定距离推算的近似点。
                targetFootPoint = farEdgePoint + forward * settings.VaultTargetForwardClearance;
                kind = TraversalCandidateKind.Vault;
            }
            else
            {
                // Mantle 目标位于顶部内缩位置，不复用 Vault 的后沿余量。
                targetFootPoint = topHit.point + forward * settings.MantleStandingInset;
                kind = TraversalCandidateKind.Mantle;
            }

            if (!IsCapsuleSpaceClear(targetFootPoint, up) ||
                !HasSurfaceSupport(targetFootPoint, up, out _))
            {
                candidate = default;
                return false;
            }

            Vector3 entryPosition = characterRoot.position + forward *
                Mathf.Max(0.05f, characterController.radius * 0.5f);
            Vector3 finalPosition = CalculateRootPosition(targetFootPoint, up);
            Quaternion targetRotation = Quaternion.LookRotation(-wallHit.normal, up);
            candidate = new TraversalCandidate(
                kind,
                obstacleHeight,
                hasFarEdge ? obstacleDepth : float.PositiveInfinity,
                wallHit.point,
                wallHit.normal,
                topHit.point,
                hasFarEdge ? farEdgePoint : Vector3.zero,
                entryPosition,
                finalPosition,
                targetRotation);

            DrawLine(topHit.point,
                hasFarEdge ? farEdgePoint : topHit.point,
                FarEdgeColor);
            if (hasFarEdge)
                DrawCross(farEdgePoint, FarEdgeColor, up);
            DrawLine(hasFarEdge ? farEdgePoint : topHit.point, targetFootPoint, TargetColor);
            DrawCross(targetFootPoint, TargetColor, up);
            DrawCross(topHit.point, SelectedColor, up);
            DrawLine(topCastOrigin, topHit.point, SelectedColor);
            DrawLine(topHit.point, topHit.point + topHit.normal * 0.25f, SelectedColor);
            return true;
        }

        #endregion

        #region 前墙与顶部查询

        /// <summary>
        /// 完整执行所有高度的前墙射线，再从已采集结果中选择最终墙面。
        /// </summary>
        /// <param name="origin">最低前墙采样点。</param>
        /// <param name="forward">世界水平前向。</param>
        /// <param name="up">角色世界上方向。</param>
        /// <param name="hit">最终采用的前墙命中。</param>
        /// <returns>至少有一条射线命中非角色碰撞体时返回 true。</returns>
        private bool TryFindFrontWall(Vector3 origin, Vector3 forward, Vector3 up,
            out RaycastHit hit)
        {
            float height = Mathf.Max(0.1f,
                characterController.height - characterController.radius * 2f);
            float bestDistance = float.PositiveInfinity;
            float bestCenterDistance = float.PositiveInfinity;
            bool found = false;
            hit = default;
            Vector3 selectedOrigin = origin;

            for (int index = 0; index < settings.FrontWallRayCount; index++)
            {
                // 按照高度比例均匀分布射线，避免角色胶囊底部挡住射线。
                float normalized = index / (settings.FrontWallRayCount - 1f);
                Vector3 rayOrigin = origin + up * (height * normalized);
                DrawRay(rayOrigin, forward * settings.FrontWallCastDistance, FrontRayColor);
                if (!TryGetNearestHit(rayOrigin, forward, settings.FrontWallCastDistance,
                        out RaycastHit current))
                    continue;

                DrawCross(current.point, FrontRayColor, up);
                // 墙面坡度过陡时仍保留已执行的检测线，但不能参与最终墙面竞争。
                if (Vector3.Dot(current.normal, up) > settings.MaxWallNormalUpDot)
                {
                    // 不合格命中仍保留已执行的检测线，但不能参与最终墙面竞争。
                    DrawRay(rayOrigin, forward * current.distance, RejectedColor);
                    DrawCross(current.point, RejectedColor, up);
                    continue;
                }
                float centerDistance = Mathf.Abs(normalized - 0.5f);
                bool isCloser = current.distance < bestDistance - Epsilon;
                // 当距离相等时，优先选择靠近中心的射线命中。
                bool isTie = Mathf.Abs(current.distance - bestDistance) <= Epsilon;
                if (!isCloser && (!isTie || centerDistance >= bestCenterDistance))
                    continue;

                bestDistance = current.distance;
                bestCenterDistance = centerDistance;
                hit = current;
                selectedOrigin = rayOrigin;
                found = true;
            }

            if (!found)
                return false;

            DrawRay(selectedOrigin, forward * hit.distance, SelectedColor);
            DrawCross(hit.point, SelectedColor, up);
            DrawLine(hit.point, hit.point + hit.normal * 0.25f, SelectedColor);
            return true;
        }

        /// <summary>从向下命中集合中选出最近的非角色顶部表面。</summary>
        /// <param name="origin">射线起点。</param>
        /// <param name="direction">射线方向。</param>
        /// <param name="distance">最大检测距离。</param>
        /// <param name="hit">最近的非角色命中。</param>
        /// <returns>存在有效命中时返回 true。</returns>
        private bool TryFindTopSurface(Vector3 origin, Vector3 direction, float distance, out RaycastHit hit)
        {
            return TryGetNearestHit(origin, direction, distance,
                out hit);
        }

        /// <summary>
        /// 执行一次非角色 Raycast，并从结果中选最近命中。
        /// </summary>
        /// <param name="origin">射线起点。</param>
        /// <param name="direction">单位方向。</param>
        /// <param name="distance">最大检测距离。</param>
        /// <param name="hit">最近的非角色命中。</param>
        /// <returns>存在有效命中时返回 true。</returns>
        private bool TryGetNearestHit(Vector3 origin, Vector3 direction, float distance,
            out RaycastHit hit)
        {
            int count = Physics.RaycastNonAlloc(origin, direction, raycastHits, distance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float nearestDistance = float.PositiveInfinity;
            bool found = false;
            hit = default;
            for (int index = 0; index < count; index++)
            {
                RaycastHit current = raycastHits[index];
                if (IsSelfCollider(current.collider) || current.distance >= nearestDistance)
                    continue;
                nearestDistance = current.distance;
                hit = current;
                found = true;
            }
            return found;
        }

        #endregion

        #region 后沿与空间检查

        /// <summary>
        /// 沿顶部逐步探测并用二分法得到真实后沿，不使用角色半径伪造厚度。
        /// </summary>
        /// <param name="topPoint">近端顶部命中点。</param>
        /// <param name="forward">角色世界前向。</param>
        /// <param name="up">角色世界上方向。</param>
        /// <param name="obstacleDepth">输出真实后沿距离。</param>
        /// <param name="farEdgePoint">输出真实后沿世界点。</param>
        /// <returns>在 Vault 最大厚度内找到支撑中断时返回 true。</returns>
        private bool TryFindFarEdge(Vector3 topPoint, Vector3 forward, Vector3 up,
            out float obstacleDepth, out Vector3 farEdgePoint)
        {
            // 存在两种情况：1. 在 Vault 最大厚度内找到支撑中断；2. 在 Vault 最大厚度内仍有支撑。
            obstacleDepth = float.PositiveInfinity;
            farEdgePoint = Vector3.zero;

            // 先采样近端顶部支撑，避免二分法在起点就失败。
            float previousDistance = 0f;
            bool previousSupported = TrySampleTopSupport(topPoint, forward, up,
                previousDistance, false, out _);
            if (!previousSupported)
                return false;

            // 再采样远端顶部支撑，以确定后沿。
            for (float distance = settings.DepthProbeStep;
                 distance <= settings.VaultMaxDepth + Epsilon;
                 distance += settings.DepthProbeStep)
            {
                float clampedDistance = Mathf.Min(distance, settings.VaultMaxDepth);
                bool supported = TrySampleTopSupport(topPoint, forward, up,
                    clampedDistance, false, out _);
                if (supported)
                {
                    previousDistance = clampedDistance;
                    previousSupported = true;
                    if (clampedDistance >= settings.VaultMaxDepth - Epsilon)
                    {
                        // 到达最大厚度仍有支撑，不伪造后沿，交给 Mantle 分支。
                        DrawLine(topPoint, topPoint + forward * settings.VaultMaxDepth,
                            RejectedColor);
                        return false;
                    }

                    continue;
                }

                // 在 Vault 最大厚度内找到支撑中断，使用二分法精确后沿。
                float low = previousDistance;
                float high = clampedDistance;
                for (int iteration = 0;
                     iteration < settings.FarEdgeRefinementIterations;
                     iteration++)
                {
                    float middle = (low + high) * 0.5f;
                    bool middleSupported = TrySampleTopSupport(topPoint, forward, up,
                        middle, true, out _);
                    if (middleSupported)
                        low = middle;
                    else
                        high = middle;
                }

                obstacleDepth = (low + high) * 0.5f;
                farEdgePoint = topPoint + forward * obstacleDepth;
                DrawDepthBoundaryProbe(topPoint, forward, up, low,
                    SupportedDepthProbeColor);
                DrawDepthBoundaryProbe(topPoint, forward, up, high,
                    RejectedColor);
                DrawLine(topPoint, farEdgePoint, new Color(0.1f, 0.8f, 1f, 1f));
                DrawCross(farEdgePoint, FarEdgeColor, up);
                return true;
            }

            return false;
        }

        /// <summary>执行一个顶部支撑探针并保留完整探测线。</summary>
        /// <param name="topPoint">近端顶部点。</param>
        /// <param name="forward">角色世界前向。</param>
        /// <param name="up">角色世界上方向。</param>
        /// <param name="distance">相对近端的前向距离。</param>
        /// <param name="refinement">是否为二分细化探针。</param>
        /// <param name="supportHit">存在有效支撑时输出命中。</param>
        /// <returns>探针命中且表面坡度合格时返回 true。</returns>
        private bool TrySampleTopSupport(Vector3 topPoint, Vector3 forward, Vector3 up,
            float distance, bool refinement, out RaycastHit supportHit)
        {
            Vector3 origin = topPoint + forward * distance + up * settings.DepthProbeHeight;
            Vector3 direction = -up;
            DrawRay(origin, direction * settings.DepthProbeDistance,
                refinement ? RefinementProbeColor : SupportedDepthProbeColor);
            if (!TryGetNearestHit(origin, direction, settings.DepthProbeDistance,
                    out supportHit))
            {
                if (!refinement)
                    DrawRay(origin, direction * settings.DepthProbeDistance,
                        UnsupportedDepthProbeColor);
                return false;
            }

            bool supported = Vector3.Dot(supportHit.normal, up) >= settings.MinTopNormalUpDot;
            DrawRay(origin, direction * supportHit.distance,
                refinement ? RefinementProbeColor :
                (supported ? SupportedDepthProbeColor : UnsupportedDepthProbeColor));
            DrawCross(supportHit.point,
                refinement ? RefinementProbeColor :
                (supported ? SupportedDepthProbeColor : UnsupportedDepthProbeColor), up);
            return supported;
        }

        /// <summary>以最终颜色覆盖一条后沿边界探针。</summary>
        /// <param name="topPoint">近端顶部点。</param>
        /// <param name="forward">角色世界前向。</param>
        /// <param name="up">角色世界上方向。</param>
        /// <param name="distance">探针前向距离。</param>
        /// <param name="color">边界颜色。</param>
        private void DrawDepthBoundaryProbe(Vector3 topPoint, Vector3 forward, Vector3 up,
            float distance, Color color)
        {
            Vector3 origin = topPoint + forward * distance + up * settings.DepthProbeHeight;
            DrawRay(origin, -up * settings.DepthProbeDistance, color);
        }

        /// <summary>检查目标胶囊空间，不在此方法中判断地面支撑。</summary>
        /// <param name="footPoint">角色胶囊底部的目标点。</param>
        /// <param name="up">角色世界上方向。</param>
        /// <returns>目标胶囊内没有第三方碰撞体时返回 true。</returns>
        private bool IsCapsuleSpaceClear(Vector3 footPoint, Vector3 up)
        {
            float radius = Mathf.Max(0.05f, characterController.radius - 0.02f);
            float half = Mathf.Max(radius, characterController.height * 0.5f - radius);
            Vector3 center = footPoint + up * (half + radius);
            Vector3 bottom = center - up * half;
            Vector3 top = center + up * half;
            int count = Physics.OverlapCapsuleNonAlloc(
                bottom, top, radius, overlapResults, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            DrawCapsule(center, radius, characterController.height,
                Quaternion.FromToRotation(Vector3.up, up), CapsuleColor);
            for (int index = 0; index < count; index++)
                if (!IsSelfCollider(overlapResults[index]))
                    return false;
            return true;
        }

        /// <summary>检查目标点下方支撑，并绘制完整支撑探针。</summary>
        /// <param name="footPoint">目标脚点世界坐标。</param>
        /// <param name="up">角色世界上方向。</param>
        /// <param name="supportHit">有效支撑命中。</param>
        /// <returns>目标下方存在坡度合格的非角色表面时返回 true。</returns>
        private bool HasSurfaceSupport(Vector3 footPoint, Vector3 up,
            out RaycastHit supportHit)
        {
            Vector3 origin = footPoint + up * settings.SupportProbeHeight;
            if (!TryGetNearestHit(origin, -up, settings.SupportProbeDistance,
                    out supportHit))
            {
                DrawRay(origin, -up * settings.SupportProbeDistance, RejectedColor);
                return false;
            }

            bool supported = Vector3.Dot(supportHit.normal, up) >= settings.MinTopNormalUpDot;
            Color color = supported ? SelectedColor : RejectedColor;
            DrawRay(origin, -up * supportHit.distance, color);
            DrawCross(supportHit.point, color, up);
            return supported;
        }

        #endregion

        #region 绘制辅助

        /// <summary>绘制世界空间射线；关闭 Debug 时不创建任何线段。</summary>
        /// <param name="origin">射线起点。</param>
        /// <param name="direction">带长度的射线向量。</param>
        /// <param name="color">线段颜色。</param>
        private void DrawRay(Vector3 origin, Vector3 direction, Color color)
        {
            if (settings.DrawDebug)
                DebugUtility.DrawRay(origin, direction, color, settings.DebugDuration);
        }

        /// <summary>绘制世界空间线段。</summary>
        /// <param name="start">起点。</param>
        /// <param name="end">终点。</param>
        /// <param name="color">线段颜色。</param>
        private void DrawLine(Vector3 start, Vector3 end, Color color)
        {
            if (settings.DrawDebug)
                DebugUtility.DrawLine(start, end, color, settings.DebugDuration);
        }

        /// <summary>绘制命中点十字，帮助区分命中点与投射线。</summary>
        /// <param name="point">十字中心。</param>
        /// <param name="color">线段颜色。</param>
        /// <param name="up">角色世界上方向。</param>
        private void DrawCross(Vector3 point, Color color, Vector3 up)
        {
            if (!settings.DrawDebug)
                return;
            Vector3 right = Vector3.Cross(up, characterRoot.forward).normalized;
            if (right.sqrMagnitude <= Epsilon)
                right = characterRoot.right;
            Vector3 forward = Vector3.Cross(right, up).normalized;
            const float size = 0.035f;
            DebugUtility.DrawLine(point - right * size, point + right * size,
                color, settings.DebugDuration);
            DebugUtility.DrawLine(point - forward * size, point + forward * size,
                color, settings.DebugDuration);
            DebugUtility.DrawLine(point - up * size, point + up * size,
                color, settings.DebugDuration);
        }

        /// <summary>绘制目标胶囊空间。</summary>
        /// <param name="center">胶囊中心。</param>
        /// <param name="radius">胶囊半径。</param>
        /// <param name="height">胶囊总高度。</param>
        /// <param name="rotation">胶囊局部 Y 到世界上方向的旋转。</param>
        /// <param name="color">线框颜色。</param>
        private void DrawCapsule(Vector3 center, float radius, float height,
            Quaternion rotation, Color color)
        {
            if (settings.DrawDebug)
                DebugUtility.DrawCapsule(center, radius, height, rotation,
                    color, settings.DebugDuration);
        }

        #endregion

        #region 内部辅助

        /// <summary>把平台脚点转换为 CharacterRoot 的世界坐标。</summary>
        /// <param name="footPoint">平台脚点。</param>
        /// <param name="up">角色世界上方向。</param>
        /// <returns>与脚点高度和水平位置对齐的 CharacterRoot 位置。</returns>
        private Vector3 CalculateRootPosition(Vector3 footPoint, Vector3 up)
        {
            Vector3 position = characterRoot.position;
            float localBottom = characterController.center.y - characterController.height * 0.5f;
            position += up * (Vector3.Dot(footPoint - characterRoot.position, up) - localBottom);
            Vector3 planarOffset = Vector3.ProjectOnPlane(footPoint - position, up);
            return position + planarOffset;
        }

        /// <summary>确认检测器已经绑定角色、配置和碰撞依赖。</summary>
        private void EnsureInitialized()
        {
            if (characterRoot == null || characterController == null || settings == null)
                throw new InvalidOperationException("TraversalEnvironmentDetector 尚未 Initialize。 ");
        }

        /// <summary>判断碰撞体是否属于当前角色自身。</summary>
        /// <param name="collider">待判断的碰撞体。</param>
        /// <returns>碰撞体为空或位于角色层级内时返回 true。</returns>
        private bool IsSelfCollider(Collider collider)
        {
            return collider == null || collider.transform == characterRoot ||
                collider.transform.IsChildOf(characterRoot) ||
                collider.transform.IsChildOf(character.transform);
        }

        #endregion
    }
}
