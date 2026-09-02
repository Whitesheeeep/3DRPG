namespace RPG.Character
{
    /// <summary>使用根运动完成大角度转向，再返回普通 Move。</summary>
    public sealed class RootMotionSharpTurnState : RootMotionLocomotionState
    {
        /// <summary>创建急转向状态。</summary>
        public RootMotionSharpTurnState() : base(CharacterLocomotionStateId.RootMotionSharpTurn) { }
        /// <inheritdoc />
        protected override Animancer.AnimancerState PlayRootMotion() => Owner.PlaySharpTurnAnimation();
        /// <inheritdoc />
        public override void OnFixedUpdate()
        {
            if (Owner.ShouldCorrectSharpTurnRotation) Owner.SubmitRootRotationCorrection(ControlHandle);
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
