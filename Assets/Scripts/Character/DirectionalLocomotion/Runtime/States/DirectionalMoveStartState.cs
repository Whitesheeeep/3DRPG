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

        /// <summary>创建带起步动画根运动的方向移动状态。</summary>
        public DirectionalMoveStartState() : base(DirectionalLocomotionStateId.MoveStart) { }

        /// <summary>进入状态时启动前行起步动画并注册自然结束回调。</summary>
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

        /// <summary>同步起步动画进度，并在移动许可或输入消失时返回 Idle。</summary>
        public override void OnUpdate()
        {
            Owner.StartNormalizedTime = _animationState != null ? _animationState.NormalizedTime : 0f;

            if (!Owner.IsMoving)
                Machine.ChangeState(DirectionalLocomotionStateId.Idle);
        }

        /// <summary>提取当前动画帧水平根位移，并交给共享移动驱动应用 Tag 限制。</summary>
        public override void OnAnimationMove()
        {
            Owner.RawRootDelta = Owner.Animator.deltaPosition;
            Owner.AppliedRootMovement = Owner.MotionDriver.CanMoveHorizontally
                ? new Vector3(Owner.RawRootDelta.x, 0f, Owner.RawRootDelta.z)
                : Vector3.zero;

            Owner.RootMotionBeforeY = Owner.transform.position.y;
            Owner.MotionDriver.FixedUpdateMove(Owner.AppliedRootMovement);
            Owner.RootMotionAfterY = Owner.transform.position.y;
            Owner.RootMotionActualDeltaY = Owner.RootMotionAfterY - Owner.RootMotionBeforeY;

            Owner.RootVelocity = Time.deltaTime > 0f
                ? Owner.AppliedRootMovement / Time.deltaTime
                : Vector3.zero;
            Owner.CurrentSpeed = Owner.RootVelocity.magnitude;
        }
        /// <summary>退出状态时解除当前起步动画运行标记。</summary>
        public override void OnExit()
        {
            _active = false;
            _animationState = null;
        }

        /// <summary>起步动画自然结束且状态仍有效时切换到持续移动。</summary>
        private void OnAnimationEnd()
        {
            if (_active)
                Machine.ChangeState(DirectionalLocomotionStateId.Move);
        }
    }
}
