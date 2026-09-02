using WS_Modules.FSM;

namespace RPG.Character
{
    /// <summary>UnifiedFSM 使用的角色 Locomotion 状态基类。</summary>
    public abstract class CharacterLocomotionState : StateBase<CharacterLocomotionStateId, CharacterLocomotionStateMachine>
    {
        /// <summary>创建绑定稳定状态标识的 Locomotion 状态。</summary>
        /// <param name="stateId">状态机协议中的状态标识。</param>
        protected CharacterLocomotionState(CharacterLocomotionStateId stateId) : base(stateId) { }
        /// <summary>获取状态所属角色。</summary>
        protected CharacterActor Character => Owner.Owner;
        /// <summary>获取本帧是否有有效世界移动意图。</summary>
        protected bool HasMovement => Owner.HasMovementInput;
        /// <summary>获取当前状态的控制请求句柄。</summary>
        protected MotionControlHandle ControlHandle { get; set; }
        /// <summary>释放状态拥有的控制请求。</summary>
        protected void ReleaseControl()
        {
            ControlHandle?.Dispose();
            ControlHandle = null;
        }
        /// <summary>为当前状态建立控制请求。</summary>
        protected void AcquireControl(MotionChannels channels, bool consumeRootMotion) =>
            ControlHandle = Owner.Driver.RequestControl(new MotionControlRequest(
                Character, MotionPriority.Locomotion, channels, consumeRootMotion));
    }
}
