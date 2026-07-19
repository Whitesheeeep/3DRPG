using Animancer;
using UnityEngine;
using WS_Modules.FSM;

namespace RPG.Character.DirectionalLocomotion
{
    /// <summary>纯动画 MoveStart 状态，播放结束后进入 Move。</summary>
    public sealed class DirectionalMoveStartState : StateBase<DirectionalLocomotionStateId, DirectionalLocomotionController>
    {
        private AnimancerState _animationState;
        private bool _active;

        public DirectionalMoveStartState() : base(DirectionalLocomotionStateId.MoveStart) { }

        public override void OnEnter()
        {
            _active = true;
            Owner.SelectedStartAngle = 0f;
            Owner.SelectedStartClipName = Owner.Setting.startForward.name;
            Owner.StartNormalizedTime = 0f;

            _animationState = Owner.Animancer.Play(Owner.Setting.startForward);
            _animationState.Time = 0f;
            _animationState.Events(this).OnEnd = OnAnimationEnd;
        }

        public override void OnUpdate()
        {
            Owner.StartNormalizedTime = _animationState != null ? _animationState.NormalizedTime : 0f;

            if (!Owner.IsMoving)
                Machine.ChangeState(DirectionalLocomotionStateId.Idle);
        }

        public override void OnAnimationMove()
        {
            CharacterController characterController = Owner.CharacterController;
            if (!characterController.enabled)
                return;

            Owner.RawRootDelta = Owner.Animator.deltaPosition;
            Owner.AppliedRootMovement = Owner.RawRootDelta;
            Owner.AppliedRootMovement = new Vector3(
                Owner.AppliedRootMovement.x,
                0f,
                Owner.AppliedRootMovement.z);

            Owner.RootMotionBeforeY = Owner.transform.position.y;
            Owner.RootMotionCollisionFlags = characterController.Move(Owner.AppliedRootMovement);
            Owner.RootMotionAfterY = Owner.transform.position.y;
            Owner.RootMotionActualDeltaY = Owner.RootMotionAfterY - Owner.RootMotionBeforeY;

            Owner.RootVelocity = Time.deltaTime > 0f
                ? Owner.AppliedRootMovement / Time.deltaTime
                : Vector3.zero;
            Owner.CurrentSpeed = Owner.RootVelocity.magnitude;
        }
        public override void OnExit()
        {
            _active = false;
            _animationState = null;
        }

        private void OnAnimationEnd()
        {
            if (_active)
                Machine.ChangeState(DirectionalLocomotionStateId.Move);
        }
    }
}