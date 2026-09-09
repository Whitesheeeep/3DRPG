using Animancer;
using RPG.Character.Animation;

namespace RPG.Character
{
    /// <summary>使用左右脚停止根运动结束接地移动。</summary>
    public sealed class RootMotionStopState : RootMotionLocomotionState
    {
        #region 运行时状态

        private ITransition selectedTransition;

        #endregion

        #region 生命周期

        /// <summary>创建停止状态。</summary>
        public RootMotionStopState() : base(CharacterLocomotionStateId.RootMotionStop) { }

        /// <inheritdoc />
        protected override WS_Modules.GAS.TAG.GameplayTag StateTag => Transition.StopStateTag;
        /// <inheritdoc />
        protected override WS_Modules.GAS.TAG.GameplayTagQuery StateCanEnterQuery => Transition.StopCanEnterQuery;

        /// <inheritdoc />
        public override void OnEnter(bool suppressDefaultState = false)
        {
            selectedTransition = Character.IsLeftFootAhead ? Transition.StopLeft : Transition.StopRight;
            if (selectedTransition == null || !selectedTransition.IsValid)
                selectedTransition = Character.IsLeftFootAhead ? Transition.StopRight : Transition.StopLeft;

            if (selectedTransition == null || !selectedTransition.IsValid)
            {
                Owner.ChangeState(CharacterLocomotionStateId.Idle);
                return;
            }
            base.OnEnter(suppressDefaultState);
        }

        /// <inheritdoc />
        public override void OnUpdate()
        {
            // Stop 可被移动输入立即打断；根据当前 Sprint 选择对应的起步状态，起步期间不再改投另一种动画。
            if (HasMovement)
                Owner.ChangeState(IsSprintHeld
                    ? CharacterLocomotionStateId.RootMotionRunStart
                    : CharacterLocomotionStateId.RootMotionWalkStart);
        }

        /// <inheritdoc />
        protected override AnimancerState PlayRootMotionAnimation() =>
            Character.AnimationPlayer.Play(AnimationLayerType.Base, selectedTransition);

        /// <inheritdoc />
        protected override void OnAnimationFinished()
        {
            selectedTransition = null;
            Owner.ChangeState(HasMovement
                ? IsSprintHeld
                    ? CharacterLocomotionStateId.RootMotionRunStart
                    : CharacterLocomotionStateId.RootMotionWalkStart
                : CharacterLocomotionStateId.Idle);
        }

        /// <inheritdoc />
        public override void OnExit()
        {
            selectedTransition = null;
            base.OnExit();
        }

        /// <inheritdoc />
        internal override void ResetForActivation()
        {
            selectedTransition = null;
            base.ResetForActivation();
        }

        #endregion
    }
}
