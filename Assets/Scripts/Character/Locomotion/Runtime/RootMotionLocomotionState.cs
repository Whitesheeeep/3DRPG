using Animancer;
using UnityEngine;

namespace RPG.Character
{
    /// <summary>起步、停止和急转向共用的根运动状态基类。</summary>
    public abstract class RootMotionLocomotionState : CharacterLocomotionState
    {
        #region 运行时状态

        /// <summary>获取该根运动状态当前播放的动画状态。</summary>
        protected AnimancerState AnimationState { get; private set; }

        #endregion

        #region 生命周期

        /// <summary>创建指定根运动状态。</summary>
        /// <param name="stateId">根运动状态标识。</param>
        protected RootMotionLocomotionState(CharacterLocomotionStateId stateId)
            : base(stateId) { }

        /// <summary>播放具体子状态选择的根运动动画。</summary>
        /// <returns>实际播放的 Animancer 状态；无可用动画时返回 null。</returns>
        protected abstract AnimancerState PlayRootMotionAnimation();

        /// <inheritdoc />
        public override void OnEnter()
        {
            AcquireControl(MotionChannels.Horizontal | MotionChannels.Rotation);
            AnimationState = PlayRootMotionAnimation();
            if (AnimationState != null)
                AnimationState.Events(this).OnEnd = OnAnimationFinished;
        }

        // 根增量由 PlayerController 收集并由 MotionDriver 统一结算；Locomotion 只接收阶段通知。
        /// <inheritdoc />
        public override void OnAnimationMove()
        {
            if (AnimationState == null || ControlHandle == null) return;
            Driver.SubmitAnimatorMotion(ControlHandle,
                new AnimatorMotionSubmission(AnimatorDeltaPosition, AnimatorDeltaRotation));
        }

        /// <summary>在动画完整播放后决定进入下一个业务状态。</summary>
        protected abstract void OnAnimationFinished();

        /// <inheritdoc />
        public override void OnExit()
        {
            if (AnimationState != null)
                AnimationState.Events(this).OnEnd = null;
            ReleaseControl();
            AnimationState = null;
        }

        /// <inheritdoc />
        internal override void ResetForActivation()
        {
            AnimationState = null;
            ReleaseControl();
        }

        #endregion

    }
}
