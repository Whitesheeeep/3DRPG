namespace RPG.Character
{
    /// <summary>使用根运动完成从 Idle 到 Move 的起步状态。</summary>
    public sealed class RootMotionStartState : RootMotionLocomotionState
    {
        /// <summary>创建起步状态。</summary>
        public RootMotionStartState() : base(CharacterLocomotionStateId.RootMotionStart) { }
        /// <inheritdoc />
        public override void OnEnter()
        {
            Owner.BeginRootSpeedSample();
            base.OnEnter();
        }

        /// <inheritdoc />
        protected override Animancer.AnimancerState PlayRootMotion() => Owner.PlayStartAnimation();
        /// <inheritdoc />
        public override void OnFixedUpdate()
        {
            if (Owner.ShouldCorrectStartRotation) Owner.SubmitRootRotationCorrection(ControlHandle);
        }
        /// <inheritdoc />
        protected override void OnAnimationFinished() => Owner.ChangeState(
            HasMovement
                ? CharacterLocomotionStateId.CodeLocomotion
                : Owner.HasStopAnimation
                    ? CharacterLocomotionStateId.RootMotionStop
                    : CharacterLocomotionStateId.Idle);
    }
}
