using UnityEngine;
using WS_Modules.FSM;

namespace RPG.Character.DirectionalLocomotion
{
    /// <summary>播放 Walk 并仅测试 CharacterController 前向水平位移。</summary>
    public sealed class DirectionalMoveState : StateBase<DirectionalLocomotionStateId, DirectionalLocomotionController>
    {
        public DirectionalMoveState() : base(DirectionalLocomotionStateId.Move) { }

        public override void OnEnter()
        {
            Owner.Animancer.Play(Owner.Setting.walkForward);
        }

        public override void OnUpdate()
        {
            if (!Owner.IsMoving)
            {
                Machine.ChangeState(DirectionalLocomotionStateId.Idle);
                return;
            }

            DirectionalLocomotionSetting setting = Owner.Setting;
            Owner.CurrentSpeed = setting.walkSpeed;
            Owner.WalkVelocity = Owner.transform.forward * Owner.CurrentSpeed;
            Owner.HorizontalMovement = Owner.WalkVelocity * Time.deltaTime;

            CharacterController characterController = Owner.CharacterController;
            if (!characterController.enabled)
                return;

            Owner.HorizontalBeforeY = Owner.transform.position.y;
            Owner.HorizontalCollisionFlags = characterController.Move(Owner.HorizontalMovement);
            Owner.HorizontalAfterY = Owner.transform.position.y;
            Owner.HorizontalActualDeltaY = Owner.HorizontalAfterY - Owner.HorizontalBeforeY;
        }
    }
}