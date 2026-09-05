using Animancer;
using RPG.Character.Animation;
using UnityEngine;

namespace RPG.Character
{
    /// <summary>使用九方向根运动完成从 Idle 到 Move 的起步状态。</summary>
    public sealed class RootMotionStartState : RootMotionLocomotionState
    {
        #region 运行时状态

        // 起步方向只在进入本状态时选择一次，播放过程中改变输入不会重播另一段动画。
        private ITransition selectedTransition;
        private bool isForwardStart;

        #endregion

        #region 生命周期

        /// <summary>创建起步状态。</summary>
        public RootMotionStartState() : base(CharacterLocomotionStateId.RootMotionStart) { }

        /// <inheritdoc />
        public override void OnEnter()
        {
            selectedTransition = Transition.SelectStartTransition(
                Character.RootTransform.forward,
                MovementInput);
            if (selectedTransition == null || !selectedTransition.IsValid)
            {
                // 当前方向没有配置起步动画时直接交给代码移动，不申请无效的根运动控制权。
                Owner.ChangeState(CharacterLocomotionStateId.CodeLocomotion);
                return;
            }
            isForwardStart = ReferenceEquals(selectedTransition, Transition.ForwardStart);

            base.OnEnter();
        }

        /// <inheritdoc />
        public override void OnUpdate()
        {
            if (!HasMovement)
            {
                selectedTransition = null;
                Owner.ChangeState(HasStopAnimation
                    ? CharacterLocomotionStateId.RootMotionStop
                    : CharacterLocomotionStateId.Idle);
                return;
            }

            base.OnUpdate();
        }

        /// <inheritdoc />
        public override void OnFixedUpdate()
        {
        }

        /// <inheritdoc />
        public override void OnAnimationMove()
        {
            if (ControlHandle == null) return;
            Quaternion rotation = AnimatorDeltaRotation;
            // 只有在起步动画播放到一定时间后才进行方向修正，否则会导致动画还没播放到一半就被强行旋转。
            bool shouldCorrect = isForwardStart ||
                AnimationState != null &&
                AnimationState.NormalizedTime >= Transition.StartDirectionCorrectionNormalizedTime;
            if (shouldCorrect && HasMovement)
            {
                Vector3 target = MovementInput.normalized;
                Vector3 planarForward = Vector3.ProjectOnPlane(Character.RootTransform.forward, Vector3.up);
                float angle = Vector3.SignedAngle(planarForward, target, Vector3.up);
                Quaternion targetRotation = Quaternion.AngleAxis(angle, Vector3.up) *
                    Character.RootTransform.rotation;
                Quaternion animatedRotation = Character.RootTransform.rotation * rotation;
                Quaternion correctedRotation = Quaternion.Slerp(
                    animatedRotation,
                    targetRotation,
                    Mathf.Clamp01(Transition.CorrectionSpeed * AnimatorEvaluationDeltaTime));
                rotation = Quaternion.Inverse(Character.RootTransform.rotation) * correctedRotation;
            }
            Driver.SubmitAnimatorMotion(ControlHandle,
                new AnimatorMotionSubmission(AnimatorDeltaPosition, rotation));
        }

        /// <inheritdoc />
        protected override AnimancerState PlayRootMotionAnimation() =>
            Character.AnimationPlayer.Play(AnimationLayerType.Base, selectedTransition);

        /// <inheritdoc />
        protected override void OnAnimationFinished()
        {
            selectedTransition = null;
            isForwardStart = false;
            Owner.ChangeState(HasMovement
                ? CharacterLocomotionStateId.CodeLocomotion
                : HasStopAnimation
                    ? CharacterLocomotionStateId.RootMotionStop
                    : CharacterLocomotionStateId.Idle);
        }

        /// <inheritdoc />
        public override void OnExit()
        {
            selectedTransition = null;
            isForwardStart = false;
            base.OnExit();
        }

        /// <inheritdoc />
        internal override void ResetForActivation()
        {
            selectedTransition = null;
            isForwardStart = false;
            base.ResetForActivation();
        }

        #endregion

        #region 状态判断

        /// <summary>获取当前是否配置了停止根运动。</summary>
        private bool HasStopAnimation =>
            Transition.StopLeft != null && Transition.StopLeft.IsValid ||
            Transition.StopRight != null && Transition.StopRight.IsValid;

        #endregion
    }
}
