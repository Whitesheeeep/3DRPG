using UnityEngine;

namespace RPG.Character
{
    /// <summary>
    /// Jump、ExternalLaunch 与 Fall 共用的空中水平运动基类。
    /// 它只负责继承实际水平惯性和提交空中转向，不决定垂直阶段或动画。
    /// </summary>
    public abstract class AirborneMotionState : CharacterLocomotionState
    {
        #region 运行时状态

        private Vector3 planarVelocity;

        #endregion

        #region 构造与入口

        /// <summary>创建指定的空中叶状态。</summary>
        /// <param name="stateId">空中状态标识。</param>
        protected AirborneMotionState(CharacterLocomotionStateId stateId)
            : base(stateId)
        {
        }

        /// <summary>获取本状态需要竞争的运动通道。</summary>
        protected virtual MotionChannels ControlledChannels =>
            MotionChannels.Horizontal | MotionChannels.Rotation;

        /// <summary>获取当前实际水平速度，供派生状态调试和生命周期判断使用。</summary>
        protected Vector3 PlanarVelocity => planarVelocity;

        /// <inheritdoc />
        public override void OnEnter(bool suppressDefaultState = false)
        {
            base.OnEnter(suppressDefaultState);
            planarVelocity = Character.StateBlackboard.ObservedPlanarVelocity;
            planarVelocity.y = 0f;
            AcquireControl(ControlledChannels);
        }

        /// <inheritdoc />
        public override void OnUpdate()
        {
            UpdateAirbornePlanarVelocity();
            SubmitAirborneMotion();
        }

        /// <inheritdoc />
        public override void OnExit()
        {
            planarVelocity = Vector3.zero;
            base.OnExit();
        }

        /// <inheritdoc />
        internal override void ResetForActivation()
        {
            planarVelocity = Vector3.zero;
            base.ResetForActivation();
        }

        #endregion

        #region 空中运动

        /// <summary>
        /// 根据最新 Move 输入逐步修正水平速度；无输入时保留当前惯性，不主动清零。
        /// </summary>
        private void UpdateAirbornePlanarVelocity()
        {
            if (!HasMovement)
                return;

            float targetSpeed = TargetSpeed *
                (IsSprintHeld ? Transition.RunSpeedMultiplier : 1f);
            Vector3 targetVelocity = MovementInput.normalized * targetSpeed;
            planarVelocity = Vector3.MoveTowards(
                planarVelocity,
                targetVelocity,
                Transition.AirMovementAcceleration * DeltaTime);
            planarVelocity.y = 0f;
        }

        /// <summary>
        /// 将空中水平位移和朝向修正提交到 Update 阶段。
        /// </summary>
        private void SubmitAirborneMotion()
        {
            if (ControlHandle == null || DeltaTime <= 0f)
                return;

            Quaternion relativeRotation = Quaternion.identity;
            Vector3 planarDirection = planarVelocity;
            planarDirection.y = 0f;
            if (planarDirection.sqrMagnitude > 0.0001f)
            {
                Vector3 currentForward = Vector3.ProjectOnPlane(
                    Character.RootTransform.forward,
                    Vector3.up);
                if (currentForward.sqrMagnitude > 0.0001f)
                {
                    float signedAngle = Vector3.SignedAngle(
                        currentForward.normalized,
                        planarDirection.normalized,
                        Vector3.up);
                    float rotationStep = Mathf.Clamp(
                        signedAngle,
                        -Transition.AirTurnSpeed * DeltaTime,
                        Transition.AirTurnSpeed * DeltaTime);
                    Quaternion nextRotation =
                        Quaternion.AngleAxis(rotationStep, Vector3.up) *
                        Character.RootTransform.rotation;
                    relativeRotation = Quaternion.Inverse(
                        Character.RootTransform.rotation) * nextRotation;
                }
            }

            Driver.SubmitUpdate(
                ControlHandle,
                new UpdateMotionSubmission(
                    planarVelocity * DeltaTime,
                    relativeRotation));
        }

        #endregion
    }
}
