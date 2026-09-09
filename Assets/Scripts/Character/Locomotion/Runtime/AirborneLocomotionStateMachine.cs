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
    }
}
