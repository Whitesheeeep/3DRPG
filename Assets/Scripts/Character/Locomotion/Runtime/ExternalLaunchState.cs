using RPG.Character.Animation;
using UnityEngine;

namespace RPG.Character
{
    /// <summary>外部弹射空中状态；水平与垂直运动均在 Update 阶段提交。</summary>
    public sealed class ExternalLaunchState : AirborneMotionState
    {
        private Animancer.AnimancerState launchStartState;
        private Animancer.AnimancerState launchLoopState;
        /// <summary>创建外部弹射状态。</summary>
        public ExternalLaunchState() : base(CharacterLocomotionStateId.ExternalLaunch) { }

        /// <inheritdoc />
        protected override WS_Modules.GAS.TAG.GameplayTag StateTag => Transition.ExternalLaunchStateTag;
        /// <inheritdoc />
        protected override WS_Modules.GAS.TAG.GameplayTagQuery StateCanEnterQuery => Transition.ExternalLaunchCanEnterQuery;

        /// <summary>外力弹射同时允许自身处理水平惯性、旋转和垂直运动。</summary>
        protected override MotionChannels ControlledChannels =>
            MotionChannels.Horizontal | MotionChannels.Vertical | MotionChannels.Rotation;

        /// <inheritdoc />
        public override void OnEnter(bool suppressDefaultState = false)
        {
            base.OnEnter(suppressDefaultState);
            // 外力状态使用环境检测到的观测上升速度，不能复用 Locomotion 重力缓存中的旧值。
            Owner.SetVerticalSpeed(Mathf.Max(0f, Owner.Blackboard.ObservedVerticalSpeed));
            PlayLaunchStartOrLoop();
        }

        /// <inheritdoc />
        public override void OnUpdate()
        {
            // 外力上升结束的临界帧仍要保留水平惯性，再释放当前状态的控制权进入 Fall。
            base.OnUpdate();
            if (Owner.VerticalSpeed <= 0f && Owner.Blackboard.ObservedVerticalSpeed <= 0f)
            {
                Owner.ChangeState(CharacterLocomotionStateId.Fall);
                return;
            }

            Driver.SubmitUpdate(
                ControlHandle,
                UpdateMotionSubmission.TranslationOnly(
                    UnityEngine.Vector3.up * Owner.VerticalSpeed * DeltaTime));
        }

        /// <summary>播放外力弹射起始动画，并为自然结束注册 Start→Loop 回调。</summary>
        private void PlayLaunchStartOrLoop()
        {
            if (Transition.ExternalLaunchStartTransition != null &&
                Transition.ExternalLaunchStartTransition.IsValid)
            {
                launchStartState = Character.AnimationPlayer.Play(
                    AnimationLayerType.Base,
                    Transition.ExternalLaunchStartTransition);
                launchStartState.Events(this).OnEnd += OnLaunchStartFinished;
                return;
            }

            PlayLaunchLoop();
        }

        /// <summary>在 Start 结束且仍处于上升阶段时切换到外力弹射循环。</summary>
        private void OnLaunchStartFinished()
        {
            if (launchStartState != null)
                launchStartState.Events(this).OnEnd -= OnLaunchStartFinished;
            launchStartState = null;
            if (Owner.CurrentState != CharacterLocomotionStateId.ExternalLaunch ||
                (Owner.VerticalSpeed <= 0f && Owner.Blackboard.ObservedVerticalSpeed <= 0f))
                return;
            PlayLaunchLoop();
        }

        /// <summary>播放外力弹射上升循环；循环动画不注册结束回调。</summary>
        private void PlayLaunchLoop()
        {
            if (Transition.ExternalLaunchLoopTransition == null ||
                !Transition.ExternalLaunchLoopTransition.IsValid)
                return;
            launchLoopState = Character.AnimationPlayer.Play(
                AnimationLayerType.Base,
                Transition.ExternalLaunchLoopTransition);
        }

        /// <inheritdoc />
        public override void OnExit()
        {
            if (launchStartState != null)
                launchStartState.Events(this).OnEnd -= OnLaunchStartFinished;
            launchStartState = null;
            launchLoopState = null;
            base.OnExit();
        }

        /// <inheritdoc />
        internal override void ResetForActivation()
        {
            launchStartState = null;
            launchLoopState = null;
            base.ResetForActivation();
        }
    }
}
