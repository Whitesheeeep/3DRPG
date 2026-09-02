namespace RPG.Character
{
    /// <summary>起步、停止和急转向共用的根运动状态基类。</summary>
    public abstract class RootMotionLocomotionState : CharacterLocomotionState
    {
        private Animancer.AnimancerState animationState;
        /// <summary>创建指定根运动状态。</summary>
        /// <param name="stateId">根运动状态标识。</param>
        protected RootMotionLocomotionState(CharacterLocomotionStateId stateId) : base(stateId) { }
        /// <summary>播放状态动画并申请根运动消费权。</summary>
        protected abstract Animancer.AnimancerState PlayRootMotion();
        /// <inheritdoc />
        public override void OnEnter()
        {
            AcquireControl(MotionChannels.Horizontal | MotionChannels.Rotation, true);
            animationState = PlayRootMotion();
        }
        /// <inheritdoc />
        public override void OnUpdate()
        {
            if (!HasMovement && StateId != CharacterLocomotionStateId.RootMotionStop)
            {
                Owner.ChangeState(Owner.HasStopAnimation
                    ? CharacterLocomotionStateId.RootMotionStop
                    : CharacterLocomotionStateId.Idle);
                return;
            }
            if (animationState == null || animationState.Length <= 0f || animationState.Time >= animationState.Length)
                OnAnimationFinished();
        }
        /// <inheritdoc />
        // 根增量由 CharacterLocomotionStateMachine 先采样，再由 PlayerController/MotionDriver 统一结算。
        public override void OnAnimationMove() { }
        /// <summary>在动画完整播放后决定进入下一个业务状态。</summary>
        protected abstract void OnAnimationFinished();
        /// <inheritdoc />
        public override void OnExit() => ReleaseControl();
    }
}
