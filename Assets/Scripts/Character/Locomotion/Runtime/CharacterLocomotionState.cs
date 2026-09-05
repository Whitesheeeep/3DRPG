using UnityEngine;
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

        /// <summary>获取状态当前读取的共享世界空间移动输入。</summary>
        protected Vector3 MovementInput => Character.StateBlackboard.MoveWorldInput;

        /// <summary>获取当前是否存在有效移动输入。</summary>
        protected bool HasMovement => MovementInput.sqrMagnitude > 0.0001f;

        /// <summary>获取当前状态使用的角色运动配置。</summary>
        protected PlayerFSMTransition Transition => Owner.Transition;

        /// <summary>获取当前状态使用的统一运动请求出口。</summary>
        protected IMotionDriver Driver => Owner.Driver;

        /// <summary>获取本次固定阶段由外部传入的物理步长。</summary>
        protected float FixedDeltaTime => Owner.FixedDeltaTime;

        /// <summary>获取本次普通 Tick 由外部传入的帧时长。</summary>
        protected float DeltaTime => Owner.DeltaTime;
        /// <summary>获取本次 Animator 阶段的世界空间位移增量。</summary>
        protected Vector3 AnimatorDeltaPosition => Owner.AnimatorDeltaPosition;
        /// <summary>获取本次 Animator 阶段的根旋转增量。</summary>
        protected Quaternion AnimatorDeltaRotation => Owner.AnimatorDeltaRotation;
        /// <summary>获取本次 Animator 阶段的求值时间。</summary>
        protected float AnimatorEvaluationDeltaTime => Owner.AnimatorEvaluationDeltaTime;

        /// <summary>获取当前角色 GAS Speed；属性未初始化时立即暴露配置契约错误。</summary>
        protected float TargetSpeed
        {
            get
            {
                if (!Character.AbilitySystemComponent.TryGetCurrentValue(
                        WS_Modules.GAS.Generated.GameplayAttributes.Attribute_Speed,
                        out float value))
                    throw new System.InvalidOperationException(
                        $"角色 '{Character.name}' 未初始化 GameplayAttributes.Speed。");
                return Mathf.Max(0f, value);
            }
        }

        /// <summary>获取当前状态的控制请求句柄。</summary>
        protected MotionControlHandle ControlHandle { get; private set; }

        /// <summary>为当前状态建立指定通道的持续控制请求。</summary>
        /// <param name="channels">需要竞争的运动通道。</param>
        protected void AcquireControl(MotionChannels channels)
        {
            ControlHandle = Driver.RequestControl(new MotionControlRequest(
                Character,
                MotionPriority.Locomotion,
                channels));
        }

        /// <summary>释放状态拥有的控制请求，保证状态退出不遗留运动控制权。</summary>
        protected void ReleaseControl()
        {
            ControlHandle?.Dispose();
            ControlHandle = null;
        }

        /// <summary>在一次新的角色启用周期开始时清理状态自己的运行时数据。</summary>
        /// <remarks>状态机只负责发送通知，不理解各个状态需要重置的具体业务字段。</remarks>
        internal virtual void ResetForActivation() { }
    }
}
