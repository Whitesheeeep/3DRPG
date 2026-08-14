using UnityEngine;
using WS_Modules.FSM;

namespace RPG.Character.DirectionalLocomotion
{
    /// <summary>播放 Walk 并仅测试 CharacterController 前向水平位移。</summary>
    public sealed class DirectionalMoveState : StateBase<DirectionalLocomotionStateId, DirectionalLocomotionController>
    {
        /// <summary>创建持续方向移动状态。</summary>
        public DirectionalMoveState() : base(DirectionalLocomotionStateId.Move) { }

        /// <summary>进入状态时播放前行循环动画。</summary>
        public override void OnEnter()
        {
            Owner.Animancer.Play(Owner.Setting.walkForward);
        }

        /// <summary>根据当前移动意图计算水平位移，并交给共享移动驱动应用。</summary>
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

            Owner.HorizontalBeforeY = Owner.transform.position.y;
            Owner.MotionDriver.FixedUpdateMove(Owner.HorizontalMovement);
            Owner.HorizontalAfterY = Owner.transform.position.y;
            Owner.HorizontalActualDeltaY = Owner.HorizontalAfterY - Owner.HorizontalBeforeY;
        }
    }
}
