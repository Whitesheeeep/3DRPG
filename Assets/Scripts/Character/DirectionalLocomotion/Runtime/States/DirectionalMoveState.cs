using UnityEngine;
using WS_Modules.FSM;

namespace RPG.Character.DirectionalLocomotion
{
    /// <summary>播放 Walk 并仅测试 CharacterController 前向水平位移。</summary>
    public sealed class DirectionalMoveState : StateBase<DirectionalLocomotionStateId, DirectionalLocomotionController>
    {
        private MotionControlHandle _handle;
        /// <summary>创建持续方向移动状态。</summary>
        public DirectionalMoveState() : base(DirectionalLocomotionStateId.Move) { }

        /// <summary>进入状态时播放前行循环动画。</summary>
        public override void OnEnter()
        {
            _handle = Owner.MotionDriver.RequestControl(new MotionControlRequest(
                Owner.GetComponentInParent<CharacterActor>(), MotionPriority.Locomotion,
                MotionChannels.Horizontal | MotionChannels.Rotation));
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

        }

        /// <summary>在物理阶段提交本步水平代码位移。</summary>
        public override void OnFixedUpdate()
        {
            Owner.HorizontalBeforeY = Owner.transform.position.y;
            Owner.HorizontalMovement = Owner.WalkVelocity * Time.fixedDeltaTime;
            Owner.MotionDriver.SubmitFixed(_handle,
                FixedMotionRequest.TranslationOnly(Owner.HorizontalMovement));
            Owner.HorizontalAfterY = Owner.transform.position.y;
            Owner.HorizontalActualDeltaY = 0f;
        }

        /// <summary>离开移动状态时释放持续控制权。</summary>
        public override void OnExit()
        {
            _handle?.Dispose();
            _handle = null;
        }
    }
}
