using Animancer;
using UnityEngine;

namespace RPG.Character
{
    /// <summary>起步和停止状态共用的根运动状态基类。</summary>
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
        public override void OnEnter(bool suppressDefaultState = false)
        {
            base.OnEnter(suppressDefaultState);
            AcquireControl(MotionChannels.Horizontal | MotionChannels.Rotation);
            AnimationState = PlayRootMotionAnimation();
            if (AnimationState != null)
                AnimationState.Events(this).OnEnd += OnAnimationFinished;
        }

        // 根增量由 PlayerController 收集并由 MotionDriver 统一结算；Locomotion 只接收阶段通知。
        /// <inheritdoc />
        public override void OnAnimationMove()
        {
            SubmitAnimatorMotionWithDirectionCorrection(false);
        }

        /// <summary>提交原始根运动，并按状态要求组合动画旋转与最新输入方向。</summary>
        /// <param name="correctDirection">是否在本次动画阶段执行方向修正。</param>
        protected void SubmitAnimatorMotionWithDirectionCorrection(bool correctDirection)
        {
            if (AnimationState == null || ControlHandle == null)
                return;

            Quaternion rotation = AnimatorDeltaRotation;
            if (correctDirection && HasMovement)
            {
                Vector3 planarForward = Vector3.ProjectOnPlane(Character.RootTransform.forward, Vector3.up);
                Vector3 targetDirection = Vector3.ProjectOnPlane(MovementInput, Vector3.up);
                if (planarForward.sqrMagnitude > 0.0001f && targetDirection.sqrMagnitude > 0.0001f)
                {
                    // 获取根运动的动画旋转在世界空间的朝向，并与目标输入方向做插值修正，最后再转换回角色本地空间。
                    Quaternion animatedRotation = Character.RootTransform.rotation * rotation;
                    Quaternion targetRotation = Quaternion.LookRotation(targetDirection.normalized, Vector3.up);
                    Quaternion correctedRotation = Quaternion.Slerp(
                        animatedRotation,
                        targetRotation,
                        Mathf.Clamp01(Transition.CorrectionSpeed * AnimatorEvaluationDeltaTime));
                    rotation = Quaternion.Inverse(Character.RootTransform.rotation) * correctedRotation;
                }
            }

            // 根位移已经是世界空间，不因朝向修正再次旋转本次位移轨迹。
            Driver.SubmitAnimatorMotion(ControlHandle,
                new AnimatorMotionSubmission(AnimatorDeltaPosition, rotation));
        }

        /// <summary>在动画完整播放后决定进入下一个业务状态。</summary>
        protected abstract void OnAnimationFinished();

        /// <inheritdoc />
        public override void OnExit()
        {
            if (AnimationState != null)
                AnimationState.Events(this).OnEnd -= OnAnimationFinished;
            ReleaseControl();
            AnimationState = null;
            base.OnExit();
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
