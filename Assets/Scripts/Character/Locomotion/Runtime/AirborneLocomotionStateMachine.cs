using RPG.PlayerInputSystem;
using WS_Modules.FSM;

namespace RPG.Character
{
    /// <summary>组合 Jump、ExternalLaunch、Fall 和 FallLand 空中叶状态。</summary>
    public sealed class AirborneLocomotionStateMachine : StateMachine<CharacterLocomotionStateId, CharacterLocomotionStateMachine>
    {
        /// <summary>创建 Airborne 子状态机并注册全部直接子状态。</summary>
        public AirborneLocomotionStateMachine() : base(CharacterLocomotionStateId.Airborne)
        {
            AddState(new JumpMotionState());
            AddState(new ExternalLaunchState());
            AddState(new FallState());
            AddState(new FallLandState());
            SetDefaultState(CharacterLocomotionStateId.Fall);
        }

        /// <summary>判断 FallLand 当前是否已开放输入并存在可执行的主动跳跃。</summary>
        /// <returns>落地输入窗口开放、Jump Press 有效且头顶无阻挡时返回 true。</returns>
        internal bool CanEnterBufferedJumpFromLanding()
        {
            return CurrentState is FallLandState fallLand &&
                fallLand.IsInputOpen &&
                Owner.Blackboard.InputRequests.TryGetRequest(
                    PlayerInputType.Jump,
                    out IReadOnlyPlayerInputRequest request) &&
                request.HasBufferedPress &&
                Owner.Blackboard.TimeSinceGrounded <= Owner.Transition.CoyoteTime &&
                !Owner.Blackboard.IsCeilingBlocked;
        }

        /// <summary>判断 FallLand 是否可以在接地后按 Move 进入指定起步状态。</summary>
        /// <param name="run">为 true 时要求 Sprint Held，为 false 时要求未持有 Sprint。</param>
        /// <returns>当前 FallLand 输入窗口开放且满足接地移动条件时返回 true。</returns>
        internal bool CanEnterLandingMoveStart(bool run)
        {
            return CurrentState is FallLandState fallLand &&
                fallLand.IsInputOpen &&
                Owner.Blackboard.IsGrounded &&
                Owner.Blackboard.HasMovement &&
                Owner.Blackboard.IsSprintHeld == run;
        }
    }
}
