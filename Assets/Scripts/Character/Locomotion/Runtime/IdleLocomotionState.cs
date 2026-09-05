using RPG.Character.Animation;

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
            AcquireControl(MotionChannels.Rotation);
            // Idle 只更新基础表现，水平位移由检测到输入后的目标状态负责。
            if (Transition.IdleTransition != null)
                Character.AnimationPlayer.Play(AnimationLayerType.Base, Transition.IdleTransition);
        }

        /// <inheritdoc />
        public override void OnUpdate()
        {
            if (!HasMovement) return;

            // 真正从 Idle 起步时由 RootMotionStartState 自己选择九方向动画；缺失时由该状态直接回退代码移动。
            Owner.ChangeState(CharacterLocomotionStateId.RootMotionStart);
        }

        /// <inheritdoc />
        public override void OnExit() => ReleaseControl();
    }
}
