namespace RPG.Character
{
    /// <summary>无水平位移的待机状态，保留玩家旋转控制权。</summary>
    public sealed class IdleLocomotionState : CharacterLocomotionState
    {
        /// <summary>创建待机状态。</summary>
        public IdleLocomotionState() : base(CharacterLocomotionStateId.Idle) { }
        /// <inheritdoc />
        public override void OnEnter()
        {
            AcquireControl(MotionChannels.Rotation, false);
            Owner.PlayIdleAnimation();
        }
        /// <inheritdoc />
        public override void OnUpdate()
        {
            if (HasMovement) Owner.ChangeState(Owner.HasStartAnimation
                ? CharacterLocomotionStateId.RootMotionStart
                : CharacterLocomotionStateId.CodeLocomotion);
        }
        /// <inheritdoc />
        public override void OnExit() => ReleaseControl();
    }
}
