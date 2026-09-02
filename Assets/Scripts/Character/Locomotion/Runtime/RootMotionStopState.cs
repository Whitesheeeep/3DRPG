namespace RPG.Character
{
    /// <summary>使用根运动完成松开输入后的停止状态。</summary>
    public sealed class RootMotionStopState : RootMotionLocomotionState
    {
        /// <summary>创建停止状态。</summary>
        public RootMotionStopState() : base(CharacterLocomotionStateId.RootMotionStop) { }
        /// <inheritdoc />
        protected override Animancer.AnimancerState PlayRootMotion() => Owner.PlayStopAnimation();
        /// <inheritdoc />
        protected override void OnAnimationFinished() => Owner.ChangeState(
            HasMovement
                ? Owner.HasStartAnimation
                    ? CharacterLocomotionStateId.RootMotionStart
                    : CharacterLocomotionStateId.CodeLocomotion
                : CharacterLocomotionStateId.Idle);
    }
}
