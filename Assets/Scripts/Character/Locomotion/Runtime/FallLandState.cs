using Animancer;
using RPG.Character.Animation;

namespace RPG.Character
{
    /// <summary>落地表现状态；根据本次离地高度选择落地动画并在结束后回到 Grounded。</summary>
    public sealed class FallLandState : RootMotionLocomotionState
    {
        #region 运行时状态

        private TransitionAsset selectedTransition;
        private float inputOpenNormalizedTime;

        #endregion

        #region 生命周期

        /// <summary>创建落地状态。</summary>
        public FallLandState() : base(CharacterLocomotionStateId.FallLand) { }

        /// <inheritdoc />
        protected override WS_Modules.GAS.TAG.GameplayTag StateTag => Transition.FallLandStateTag;
        /// <inheritdoc />
        protected override WS_Modules.GAS.TAG.GameplayTagQuery StateCanEnterQuery => Transition.FallLandCanEnterQuery;

        /// <inheritdoc />
        public override void OnEnter(bool suppressDefaultState = false)
        {
            // 只在落地瞬间读取一次离地高度，后续检测器更新不会改变本次已选动画。
            selectedTransition = SelectLandingTransition(
                Owner.Blackboard.CurrentFallHeight,
                out inputOpenNormalizedTime);
            if (selectedTransition == null || !selectedTransition.IsValid)
            {
                HandleLandingInput(true);
                return;
            }
            base.OnEnter(suppressDefaultState);
        }

        /// <inheritdoc />
        public override void OnUpdate()
        {
            if (AnimationState == null ||
                AnimationState.NormalizedTime < inputOpenNormalizedTime)
                return;

            HandleLandingInput(false);
        }

        /// <inheritdoc />
        protected override AnimancerState PlayRootMotionAnimation()
        {
            return selectedTransition == null || !selectedTransition.IsValid
                ? null
                : Character.AnimationPlayer.Play(AnimationLayerType.Base, selectedTransition);
        }

        /// <inheritdoc />
        protected override void OnAnimationFinished() => HandleLandingInput(true);

        /// <summary>按高度阈值选择落地动画，并向较低档位回退缺失资源。</summary>
        /// <param name="fallHeight">环境检测器记录的本次离地下降高度。</param>
        /// <returns>可播放的落地动画；全部缺失时返回 null。</returns>
        private TransitionAsset SelectLandingTransition(
            float fallHeight,
            out float selectedInputOpenNormalizedTime)
        {
            selectedInputOpenNormalizedTime = 1f;
            if (fallHeight >= Transition.Landing3HeightThreshold &&
                Transition.Landing3HeightTransition != null &&
                Transition.Landing3HeightTransition.IsValid)
            {
                selectedInputOpenNormalizedTime = Transition.Landing3InputNormalizedTime;
                return Transition.Landing3HeightTransition;
            }

            if (fallHeight >= Transition.Landing2HeightThreshold &&
                Transition.Landing2HeightTransition != null &&
                Transition.Landing2HeightTransition.IsValid)
            {
                selectedInputOpenNormalizedTime = Transition.Landing2InputNormalizedTime;
                return Transition.Landing2HeightTransition;
            }

            if (Transition.Landing1HeightTransition != null &&
                Transition.Landing1HeightTransition.IsValid)
            {
                selectedInputOpenNormalizedTime = Transition.Landing1InputNormalizedTime;
                return Transition.Landing1HeightTransition;
            }

            return null;
        }

        /// <summary>
        /// 在落地输入窗口开放后按 Jump→Move→Idle 的优先级决定后续状态。
        /// </summary>
        private void HandleLandingInput(bool animationFinished)
        {
            // Jump 只有完整路径切换成功后才确认 Press，门禁失败时保留缓冲请求。
            if (Owner.TryEnterBufferedJump())
                return;

            // 输入窗口开放后没有新输入时继续播放落地动画；只有自然结束才回到 Idle。
            if (!Owner.Blackboard.HasMovement && !animationFinished)
                return;

            Owner.ChangeState(Owner.Blackboard.HasMovement
                ? Owner.Blackboard.IsSprintHeld ? CharacterLocomotionStateId.Run : CharacterLocomotionStateId.Walk
                : CharacterLocomotionStateId.Idle);
        }

        /// <inheritdoc />
        public override void OnExit()
        {
            base.OnExit();
            selectedTransition = null;
            inputOpenNormalizedTime = 0f;
        }

        /// <inheritdoc />
        internal override void ResetForActivation()
        {
            base.ResetForActivation();
            selectedTransition = null;
            inputOpenNormalizedTime = 0f;
        }

        #endregion
    }
}
