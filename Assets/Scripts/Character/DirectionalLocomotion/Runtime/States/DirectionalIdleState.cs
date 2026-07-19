using WS_Modules.FSM;

namespace RPG.Character.DirectionalLocomotion
{
    /// <summary>纯动画 Idle 状态。</summary>
    public sealed class DirectionalIdleState : StateBase<DirectionalLocomotionStateId, DirectionalLocomotionController>
    {
        public DirectionalIdleState() : base(DirectionalLocomotionStateId.Idle) { }

        public override void OnEnter()
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