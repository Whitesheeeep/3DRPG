using Animancer;
using RPG.Character.Animation;

namespace RPG.Character
{
    /// <summary>使用根运动完成松开输入后的停止状态。</summary>
    public sealed class RootMotionStopState : RootMotionLocomotionState
    {
        #region 运行时状态

        // 当前项目停止配置没有左右脚判定，沿用已有优先级选择右脚再左脚。
        private ITransition selectedTransition;

        #endregion

        #region 生命周期

        /// <summary>创建停止状态。</summary>
        public RootMotionStopState() : base(CharacterLocomotionStateId.RootMotionStop) { }

        /// <inheritdoc />
        public override void OnEnter()
        {
            selectedTransition = Character.IsLeftFootAhead ? Transition.StopLeft : Transition.StopRight;
            if (selectedTransition == null || !selectedTransition.IsValid)
                selectedTransition = Character.IsLeftFootAhead ? Transition.StopRight : Transition.StopLeft;
            if (selectedTransition != null && !selectedTransition.IsValid)
                selectedTransition = null;
            if (selectedTransition == null)
            {
                Owner.ChangeState(CharacterLocomotionStateId.Idle);
                return;
            }

            base.OnEnter();
        }

        /// <inheritdoc />
        public override void OnUpdate()
        {
            if (HasMovement)
            {
                Owner.ChangeState(CharacterLocomotionStateId.RootMotionStart);
                return;
            }
        }

        /// <inheritdoc />
        protected override AnimancerState PlayRootMotionAnimation() =>
            Character.AnimationPlayer.Play(AnimationLayerType.Base, selectedTransition);

        /// <inheritdoc />
        protected override void OnAnimationFinished()
        {
            selectedTransition = null;
            // 停止动画期间重新按下 Move 时，交给起步状态重新选择九方向动画。
            Owner.ChangeState(HasMovement
                ? CharacterLocomotionStateId.RootMotionStart
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
