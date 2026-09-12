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

        /// <summary>获取当前落地动画是否已经开放 Jump 或接地 Move 输入。</summary>
        internal bool IsInputOpen => AnimationState != null &&
            AnimationState.NormalizedTime >= inputOpenNormalizedTime;

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
                Owner.CompleteLanding();
                return;
            }
            base.OnEnter(suppressDefaultState);
        }

        /// <inheritdoc />
        public override void OnUpdate()
        {
            // 输入窗口由根状态机优先处理；本状态不在同一帧再次推进新叶状态。
        }

        /// <inheritdoc />
        protected override AnimancerState PlayRootMotionAnimation()
        {
            return selectedTransition == null || !selectedTransition.IsValid
                ? null
                : Character.AnimationPlayer.Play(AnimationLayerType.Base, selectedTransition);
        }

        /// <inheritdoc />
        protected override void OnAnimationFinished() => Owner.CompleteLanding();

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
