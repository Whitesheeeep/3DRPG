using WS_Modules.FSM;

namespace RPG.Character.DirectionalLocomotion
{
    /// <summary>纯动画 Idle 状态。</summary>
    public sealed class DirectionalIdleState : StateBase<DirectionalLocomotionStateId, DirectionalLocomotionController>
    {
        /// <summary>创建临时方向移动待机状态。</summary>
        public DirectionalIdleState() : base(DirectionalLocomotionStateId.Idle) { }

        /// <summary>进入临时方向移动待机状态并播放待机动画。</summary>
        /// <param name="suppressDefaultState">该叶状态忽略父状态机默认子状态参数。</param>
        public override void OnEnter(bool suppressDefaultState = false)
        {
            Owner.StartNormalizedTime = 0f;
            Owner.Animancer.Play(Owner.Setting.idle);
        }

        public override void OnUpdate()
        {
            if (Owner.IsMoving)
                Machine.ChangeState(DirectionalLocomotionStateId.MoveStart);
        }
    }
}
