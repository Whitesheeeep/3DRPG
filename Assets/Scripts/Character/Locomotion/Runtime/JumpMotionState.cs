using Animancer;
using RPG.Character.Animation;

namespace RPG.Character
{
    /// <summary>主动跳跃状态；水平与垂直运动均在 Update 阶段提交。</summary>
    public sealed class JumpMotionState : AirborneMotionState
    {
        private AnimancerState animationState;

        /// <summary>创建主动跳跃状态。</summary>
        public JumpMotionState() : base(CharacterLocomotionStateId.JumpMotion) { }

        /// <inheritdoc />
        protected override WS_Modules.GAS.TAG.GameplayTag StateTag => Transition.JumpStateTag;
        /// <inheritdoc />
        protected override WS_Modules.GAS.TAG.GameplayTagQuery StateCanEnterQuery => Transition.JumpCanEnterQuery;

        /// <summary>主动跳跃同时控制水平惯性、朝向和垂直速度。</summary>
        protected override MotionChannels ControlledChannels =>
            MotionChannels.Horizontal | MotionChannels.Vertical | MotionChannels.Rotation;

        /// <inheritdoc />
        public override void OnEnter(bool suppressDefaultState = false)
        {
            base.OnEnter(suppressDefaultState);
            Owner.SetVerticalSpeed(Transition.JumpInitialSpeed);

            // 起跳动画只在进入状态时选片；这里依据实际水平位移，避免只有输入但尚未移动时误播前跳。
            ClipTransition selectedTransition = Owner.Blackboard.ObservedPlanarVelocity.magnitude >=
                Transition.ForwardJumpMinSpeed
                ? Transition.ForwardJumpTransition
                : Transition.JumpTransition;
            if (selectedTransition == null || !selectedTransition.IsValid)
                selectedTransition = Transition.JumpTransition;
            if (selectedTransition != null && selectedTransition.IsValid)
                animationState = Character.AnimationPlayer.Play(AnimationLayerType.Base, selectedTransition);
        }

        /// <inheritdoc />
        public override void OnUpdate()
        {
            // 先提交本帧水平惯性，再切换到 Fall；否则临界帧会丢掉一次水平位移，表现为空中顿挫。
            base.OnUpdate();
            if (Owner.VerticalSpeed <= 0f)
            {
                Owner.ChangeState(CharacterLocomotionStateId.Fall);
                return;
            }

            Driver.SubmitUpdate(
                ControlHandle,
                UpdateMotionSubmission.TranslationOnly(
                    UnityEngine.Vector3.up * Owner.VerticalSpeed * DeltaTime));
        }

        /// <inheritdoc />
        public override void OnExit()
        {
            animationState = null;
            base.OnExit();
        }
    }
}
