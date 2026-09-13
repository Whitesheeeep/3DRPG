using Animancer;
using RPG.Character.Animation;
using UnityEngine;
using WS_Modules.GAS.TAG;

namespace RPG.Character
{
    /// <summary>Vault 与 Mantle 共用的根运动 Traversal 状态行为。</summary>
    public abstract class TraversalLocomotionState : RootMotionLocomotionState
    {
        #region 运行时数据

        private TraversalCandidate candidate;
        private TraversalAnimationSettings animationSettings;
        private float previousEntryCorrectionWeight;
        private float previousTargetCorrectionWeight;

        #endregion

        #region 查询与配置

        /// <summary>获取该状态期待的候选类型。</summary>
        protected abstract TraversalCandidateKind CandidateKind { get; }

        /// <summary>获取该状态使用的 GameplayTag。</summary>
        protected abstract override GameplayTag StateTag { get; }

        /// <summary>获取该状态使用的 TagQuery。</summary>
        protected abstract override GameplayTagQuery StateCanEnterQuery { get; }

        /// <summary>获取当前动画是否已经开放连续输入。</summary>
        internal bool IsExitInputOpen => AnimationState != null &&
            AnimationState.NormalizedTime >= animationSettings.InputOpenNormalizedTime;

        /// <summary>获取当前动画是否已经开放下落检测。</summary>
        internal bool IsFallDetectionOpen => AnimationState != null &&
            AnimationState.NormalizedTime >= animationSettings.FallDetectionNormalizedTime;

        /// <summary>创建一个 Traversal 状态。</summary>
        /// <param name="stateId">Traversal 叶状态标识。</param>
        protected TraversalLocomotionState(CharacterLocomotionStateId stateId) : base(stateId) { }

        /// <summary>按候选数据选择实际动画配置。</summary>
        /// <param name="traversalCandidate">冻结的环境候选。</param>
        /// <returns>实际播放设置。</returns>
        protected abstract TraversalAnimationSettings SelectAnimation(TraversalCandidate traversalCandidate);

        /// <inheritdoc />
        public override bool CanEnter()
        {
            return base.CanEnter() && Owner.TryGetPreparedTraversalCandidate(CandidateKind, out _);
        }

        /// <inheritdoc />
        public override void OnEnter(bool suppressDefaultState = false)
        {
            if (!Owner.TryGetPreparedTraversalCandidate(CandidateKind, out candidate))
                throw new System.InvalidOperationException(
                    $"角色 '{Character.name}' 进入 Traversal 状态 '{StateId}' 时缺少已准备候选。 ");
            animationSettings = SelectAnimation(candidate);
            if (animationSettings == null)
                throw new System.InvalidOperationException(
                    $"角色 '{Character.name}' 的 Traversal 状态 '{StateId}' 没有匹配的动画设置。 ");
            animationSettings.Validate(StateId.ToString());
            previousEntryCorrectionWeight = 0f;
            previousTargetCorrectionWeight = 0f;
            ApplyAnimationOffsets();
            base.OnEnter(suppressDefaultState);
        }

        /// <inheritdoc />
        protected override MotionChannels RootMotionChannels =>
            MotionChannels.Horizontal | MotionChannels.Vertical | MotionChannels.Rotation;

        /// <inheritdoc />
        protected override MotionCollisionMode RootMotionCollisionMode => MotionCollisionMode.BypassCollision;

        /// <inheritdoc />
        protected override AnimancerState PlayRootMotionAnimation() =>
            Character.AnimationPlayer.Play(AnimationLayerType.Base, animationSettings.Transition);

        /// <inheritdoc />
        protected override void OnAnimationFinished()
        {
            Owner.CompleteTraversal();
        }

        /// <inheritdoc />
        public override void OnAnimationMove()
        {
            SubmitTraversalAnimatorMotion();
        }

        /// <summary>退出 Traversal 时清理冻结候选与两个修正窗口的累计进度。</summary>
        public override void OnExit()
        {
            base.OnExit();
            candidate = default;
            animationSettings = null;
            previousEntryCorrectionWeight = 0f;
            previousTargetCorrectionWeight = 0f;
        }

        /// <summary>重新激活角色时清理 Traversal 候选和位置修正进度。</summary>
        internal override void ResetForActivation()
        {
            base.ResetForActivation();
            candidate = default;
            animationSettings = null;
            previousEntryCorrectionWeight = 0f;
            previousTargetCorrectionWeight = 0f;
        }

        /// <summary>按当前动画修正窗口提交根位移和目标旋转。</summary>
        private void SubmitTraversalAnimatorMotion()
        {
            if (AnimationState == null || ControlHandle == null)
                return;

            Vector3 translation = AnimatorDeltaPosition;
            Quaternion rotation = AnimatorDeltaRotation;
            float normalizedTime = AnimationState.NormalizedTime;
            Vector3 predictedPosition = Character.RootTransform.position + AnimatorDeltaPosition;
            translation += CalculatePositionCorrection(normalizedTime, predictedPosition);

            Quaternion animatedRotation = Character.RootTransform.rotation * rotation;
            Vector3 targetForward = Vector3.ProjectOnPlane(candidate.TargetRotation * Vector3.forward, Vector3.up);
            if (targetForward.sqrMagnitude > 0.0001f &&
                IsInCorrectionRange(normalizedTime, animationSettings.TargetCorrectionNormalizedRange))
            {
                Quaternion corrected = Quaternion.Slerp(
                    animatedRotation,
                    candidate.TargetRotation,
                    Mathf.Clamp01(Transition.CorrectionSpeed * AnimatorEvaluationDeltaTime));
                rotation = Quaternion.Inverse(Character.RootTransform.rotation) * corrected;
            }

            Driver.SubmitAnimatorMotion(ControlHandle,
                new AnimatorMotionSubmission(translation, rotation));
        }

        /// <summary>
        /// 按当前修正窗口的曲线增量消费位置误差；入口阶段只修正水平位置，
        /// 目标阶段修正完整世界位置，避免把同一累计权重在每帧重复应用。
        /// </summary>
        private Vector3 CalculatePositionCorrection(
            float normalizedTime,
            Vector3 predictedPosition)
        {
            if (IsInCorrectionRange(normalizedTime, animationSettings.EntryCorrectionNormalizedRange))
            {
                Vector3 error = Vector3.ProjectOnPlane(
                    candidate.EntryPosition - predictedPosition,
                    Vector3.up);
                return ConsumeCorrectionError(
                    normalizedTime,
                    animationSettings.EntryCorrectionNormalizedRange,
                    error,
                    ref previousEntryCorrectionWeight);
            }

            if (IsInCorrectionRange(normalizedTime, animationSettings.TargetCorrectionNormalizedRange))
            {
                Vector3 error = candidate.FinalPosition - predictedPosition;
                return ConsumeCorrectionError(
                    normalizedTime,
                    animationSettings.TargetCorrectionNormalizedRange,
                    error,
                    ref previousTargetCorrectionWeight);
            }

            return Vector3.zero;
        }

        /// <summary>按曲线本帧新增的累计比例消费指定位置误差。</summary>
        /// <param name="normalizedTime">当前动画归一化时间。</param>
        /// <param name="range">当前修正窗口。</param>
        /// <param name="error">应用原始根位移后的剩余世界空间误差。</param>
        /// <param name="previousWeight">该窗口上一帧已完成的累计比例。</param>
        /// <returns>本帧应附加到 Animator 位移的世界空间修正增量。</returns>
        private Vector3 ConsumeCorrectionError(
            float normalizedTime,
            Vector2 range,
            Vector3 error,
            ref float previousWeight)
        {
            float localProgress = Mathf.InverseLerp(range.x, range.y, normalizedTime);
            float sampledWeight = Mathf.Clamp01(
                animationSettings.PositionCorrectionWeightCurve.Evaluate(localProgress));
            float currentWeight = Mathf.Max(previousWeight, sampledWeight);
            float deltaWeight = currentWeight - previousWeight;
            float remainingWeight = 1f - previousWeight;
            previousWeight = currentWeight;
            if (remainingWeight <= Mathf.Epsilon || deltaWeight <= 0f)
                return Vector3.zero;

            float consumeRatio = Mathf.Clamp01(deltaWeight / remainingWeight);
            return error * consumeRatio;
        }

        /// <summary>把动画设置中的入口距离和目标高度偏移应用到本次冻结候选。</summary>
        private void ApplyAnimationOffsets()
        {
            Vector3 targetForward = Vector3.ProjectOnPlane(candidate.TargetRotation * Vector3.forward, Vector3.up);
            if (targetForward.sqrMagnitude > 0.0001f)
            {
                targetForward.Normalize();
                candidate = new TraversalCandidate(
                    candidate.Kind,
                    candidate.ObstacleHeight,
                    candidate.ObstacleDepth,
                    candidate.WallPoint,
                    candidate.WallNormal,
                    candidate.TopPoint,
                    candidate.FarEdgePoint,
                    candidate.EntryPosition + targetForward * animationSettings.EntryDistanceOffset,
                    candidate.FinalPosition + Vector3.up * animationSettings.TargetHeightOffset,
                    candidate.TargetRotation);
            }
        }

        /// <summary>判断归一化播放时间是否处在一个有效的修正区间内。</summary>
        private static bool IsInCorrectionRange(float normalizedTime, Vector2 range)
        {
            return normalizedTime >= range.x && normalizedTime <= range.y && range.y >= range.x;
        }

        #endregion
    }
}
