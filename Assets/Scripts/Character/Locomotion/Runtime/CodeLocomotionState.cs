using UnityEngine;

namespace RPG.Character
{
    /// <summary>使用 GAS Speed 驱动普通 Move Loop 和世界空间代码位移。</summary>
    public sealed class CodeLocomotionState : CharacterLocomotionState
    {
        private Animancer.AnimancerState animationState;
        private float currentSpeed;
        /// <summary>创建普通代码移动状态。</summary>
        public CodeLocomotionState() : base(CharacterLocomotionStateId.CodeLocomotion) { }
        /// <inheritdoc />
        public override void OnEnter()
        {
            AcquireControl(MotionChannels.Horizontal | MotionChannels.Rotation, false);
            animationState = Owner.PlayMoveLoop();
            currentSpeed = Owner.EntrySpeed;
        }
        /// <inheritdoc />
        public override void OnUpdate()
        {
            if (!HasMovement)
            {
                Owner.ChangeState(Owner.HasStopAnimation
                    ? CharacterLocomotionStateId.RootMotionStop
                    : CharacterLocomotionStateId.Idle);
                return;
            }
            if (Owner.HasSharpTurnAnimation && Owner.ShouldEnterSharpTurn)
            {
                Owner.ChangeState(CharacterLocomotionStateId.RootMotionSharpTurn);
                return;
            }
            Owner.UpdateMoveAnimationSpeed(animationState, currentSpeed);
        }
        /// <inheritdoc />
        public override void OnFixedUpdate()
        {
            currentSpeed = Mathf.MoveTowards(currentSpeed, Owner.TargetSpeed,
                Owner.Transition.MovementAcceleration * Owner.FixedDeltaTime);
            Owner.CalculateCodeMotion(currentSpeed, Owner.Transition.TurnSpeed, out Vector3 displacement,
                out Quaternion rotation);
            Owner.Driver.SubmitFixed(ControlHandle, new FixedMotionRequest(displacement, rotation));
        }
        /// <inheritdoc />
        public override void OnExit() => ReleaseControl();
    }
}
