using System;
using System.Collections.Generic;
using Animancer;
using UnityEngine;
using WS_Modules.FSM;

namespace RPG.Character
{
    /// <summary>角色 Locomotion UnifiedFSM 的稳定状态标识。</summary>
    public enum CharacterLocomotionStateId
    {
        /// <summary>未启用状态。</summary>
        Disable,
        /// <summary>无水平位移的待机状态。</summary>
        Idle,
        /// <summary>按 GAS Speed 进行代码移动。</summary>
        CodeLocomotion,
        /// <summary>使用起步根运动进入普通移动。</summary>
        RootMotionStart,
        /// <summary>使用停止根运动结束移动。</summary>
        RootMotionStop,
    }

    /// <summary>
    /// 封装一个 CharacterActor 的 UnifiedFSM，并提供共享依赖、生命周期和阶段转发。
    /// 具体运动、动画和状态转换规则由各个 Locomotion 状态实现。
    /// </summary>
    [Serializable]
    public sealed class CharacterLocomotionStateMachine
    {
        #region 配置与依赖字段

        // 序列化配置只描述该角色的状态树参数，不保存某个具体状态的运行时运动数据。
        [SerializeField] private PlayerFSMTransition transition;
        [SerializeField, Min(0f)] private float gravity = 9.81f;

        // PlayerController 注入角色与统一运动请求出口；状态通过 Owner 访问这些依赖。
        private CharacterActor owner;
        private IMotionDriver driver;

        #endregion

        #region FSM 与运行时状态

        private StateMachine<CharacterLocomotionStateId, CharacterLocomotionStateMachine> stateMachine;
        // 用于在启用周期内通知全部状态清理运行时数据，避免状态机持有业务数据。
        private readonly List<CharacterLocomotionState> registeredStates = new();

        private float verticalSpeed;
        private float fixedDeltaTime;
        private float deltaTime;
        private float activationElapsedTime;
        private Vector3 currentAnimatorDeltaPosition;
        private Quaternion currentAnimatorDeltaRotation = Quaternion.identity;
        private float animatorEvaluationDeltaTime;
        private bool active;
        private bool activationEntryPending;
        private MotionControlHandle gravityHandle;

        #endregion

        #region 查询

        /// <summary>获取状态机所属角色。</summary>
        internal CharacterActor Owner => owner;

        /// <summary>获取统一运动请求接口。</summary>
        internal IMotionDriver Driver => driver;

        /// <summary>获取当前角色的 Locomotion 配置。</summary>
        internal PlayerFSMTransition Transition => transition;

        /// <summary>获取用于首次姿态预热的 Idle Transition；该查询不改变 FSM 状态。</summary>
        internal ITransition IdleTransition => transition?.IdleTransition;

        /// <summary>获取当前 FSM 状态。</summary>
        public CharacterLocomotionStateId CurrentState =>
            stateMachine?.CurrentState?.StateId ?? CharacterLocomotionStateId.Disable;

        /// <summary>获取状态是否正在响应本次启用的首次入口。</summary>
        /// <remarks>该标记区分重新启用与普通状态切换，避免复用 UnifiedFSM 上次退出时的 PreviousState。</remarks>
        internal bool IsActivationEntry => activationEntryPending;

        /// <summary>获取当前是否存在共享 Blackboard 中的世界空间移动输入。</summary>
        internal bool HasMovementInput => owner.StateBlackboard.MoveWorldInput.sqrMagnitude > 0.0001f;

        /// <summary>获取当前 FixedTick 同步调用区间内缓存的物理步长。</summary>
        /// <remarks>
        /// PlayerController 传入物理步长后由 CharacterManager 转发给本状态机，只有 UnifiedFSM 的
        /// OnFixedUpdate 调用区间允许状态读取该值；普通帧和 AnimatorMove 使用各自的阶段时序。
        /// </remarks>
        internal float FixedDeltaTime => fixedDeltaTime;

        /// <summary>获取当前 Tick 同步调用区间内缓存的普通帧时长。</summary>
        /// <remarks>状态使用该值平滑表现参数，不直接读取 Unity 的静态时间源。</remarks>
        internal float DeltaTime => deltaTime;

        /// <summary>获取本次启用周期已经推进的普通帧时间。</summary>
        /// <remarks>该时间由外部 Tick 的 deltaTime 累计，供状态保存跨状态仍有效的时间边界。</remarks>
        internal float ActivationElapsedTime => activationElapsedTime;
        internal Vector3 AnimatorDeltaPosition => currentAnimatorDeltaPosition;
        internal Quaternion AnimatorDeltaRotation => currentAnimatorDeltaRotation;
        internal float AnimatorEvaluationDeltaTime => animatorEvaluationDeltaTime;

        #endregion

        #region 生命周期与阶段驱动

        /// <summary>绑定角色、MotionDriver 并组装正式 UnifiedFSM 状态树。</summary>
        /// <param name="sourceOwner">角色包装对象。</param>
        /// <param name="sourceDriver">PlayerController 持有的运动接口。</param>
        /// <exception cref="ArgumentNullException">角色或运动接口为空。</exception>
        /// <exception cref="InvalidOperationException">角色未配置 PlayerFSMTransition。</exception>
        internal void Initialize(CharacterActor sourceOwner, IMotionDriver sourceDriver)
        {
            owner = sourceOwner ?? throw new ArgumentNullException(nameof(sourceOwner));
            driver = sourceDriver ?? throw new ArgumentNullException(nameof(sourceDriver));
            if (transition == null)
                throw new InvalidOperationException($"角色 '{owner.name}' 未配置 PlayerFSMTransition。 ");

            stateMachine = new StateMachine<CharacterLocomotionStateId, CharacterLocomotionStateMachine>(
                CharacterLocomotionStateId.Disable);
            registeredStates.Clear();

            // 状态实例在这里统一组装；状态之间的必要协作通过明确引用注入，不由状态机承载业务判断。
            var idleState = new IdleLocomotionState();
            var startState = new RootMotionStartState();
            var codeState = new CodeLocomotionState();
            var stopState = new RootMotionStopState();

            RegisterState(idleState);
            RegisterState(startState);
            RegisterState(codeState);
            RegisterState(stopState);
            stateMachine.SetDefaultState(CharacterLocomotionStateId.Idle);
            stateMachine.Init(this, null);
        }

        /// <summary>启用 FSM，并根据共享 Blackboard 直接进入 Idle 或 CodeLocomotion。</summary>
        internal void Activate()
        {
            if (active) return;

            active = true;
            verticalSpeed = 0f;
            fixedDeltaTime = 0f;
            deltaTime = 0f;
            activationElapsedTime = 0f;
            ResetStatesForActivation();
            gravityHandle = driver.RequestControl(new MotionControlRequest(
                owner,
                MotionPriority.Gravity,
                MotionChannels.Vertical));

            // 激活上下文决定初始状态，不能通过修改 UnifiedFSM 默认状态或先进入 Idle 再二次切换。
            activationEntryPending = true;
            try
            {
                stateMachine.ChangeState(HasMovementInput
                    ? CharacterLocomotionStateId.CodeLocomotion
                    : CharacterLocomotionStateId.Idle);
            }
            finally
            {
                activationEntryPending = false;
            }
        }

        /// <summary>停用 FSM 并释放重力控制请求和当前状态资源。</summary>
        internal void Deactivate()
        {
            if (!active) return;

            active = false;
            activationEntryPending = false;
            stateMachine.OnExit();
            gravityHandle?.Dispose();
            gravityHandle = null;
            verticalSpeed = 0f;
            fixedDeltaTime = 0f;
            deltaTime = 0f;
            activationElapsedTime = 0f;
        }

        /// <summary>主动切换正式 UnifiedFSM 状态。</summary>
        /// <param name="stateId">目标状态标识。</param>
        /// <returns>状态切换成功时返回 true。</returns>
        public bool ChangeState(CharacterLocomotionStateId stateId)
        {
            if (!active && stateId != CharacterLocomotionStateId.Disable) return false;
            if (stateId == CharacterLocomotionStateId.Disable)
            {
                Deactivate();
                return true;
            }

            // 入口速度、起步选择和延迟等业务数据由目标状态在 OnEnter 中按来源自行准备。
            return stateMachine.ChangeState(stateId);
        }

        /// <summary>推进普通帧并累计本次启用周期时间。</summary>
        /// <param name="deltaTime">PlayerController 从 Unity Update 传入的普通帧时长。</param>
        internal void Tick(float deltaTime)
        {
            if (!active) return;

            activationElapsedTime += Mathf.Max(0f, deltaTime);
            this.deltaTime = Mathf.Max(0f, deltaTime);
            stateMachine.OnUpdate();
        }

        /// <summary>推进物理阶段并提交跨全部状态共用的重力。</summary>
        /// <param name="deltaTime">PlayerController 从 Unity FixedUpdate 传入的物理步长。</param>
        internal void FixedTick(float deltaTime)
        {
            if (!active) return;

            fixedDeltaTime = deltaTime;
            verticalSpeed = driver.IsGrounded && verticalSpeed < 0f
                ? -2f
                : verticalSpeed - gravity * deltaTime;
            stateMachine.OnFixedUpdate();

            // 重力始终是世界 Y，不参与水平输入和角色朝向转换；提交仍由 MotionDriver 统一结算。
            driver.SubmitFixed(gravityHandle,
                FixedMotionRequest.TranslationOnly(Vector3.up * (verticalSpeed * deltaTime)));
        }

        /// <summary>推进当前 Locomotion 的延迟阶段，不在该阶段产生 CharacterController 移动。</summary>
        /// <param name="deltaTime">PlayerController 从 Unity LateUpdate 传入的普通帧时长。</param>
        internal void LateTick(float deltaTime)
        {
            if (!active) return;
            stateMachine.OnLateUpdate();
        }

        /// <summary>转发当前 Animator 求值阶段，不读取或累计根运动速度。</summary>
        internal void UpdateAnimationMove(Vector3 deltaPosition, Quaternion deltaRotation, float evaluationDeltaTime)
        {
            if (!active) return;
            currentAnimatorDeltaPosition = deltaPosition;
            currentAnimatorDeltaRotation = deltaRotation;
            animatorEvaluationDeltaTime = Mathf.Max(0f, evaluationDeltaTime);
            try { stateMachine.OnAnimationMove(); }
            finally
            {
                currentAnimatorDeltaPosition = Vector3.zero;
                currentAnimatorDeltaRotation = Quaternion.identity;
                animatorEvaluationDeltaTime = 0f;
            }
        }

        #endregion

        #region 内部组装

        /// <summary>注册一个需要接收 Owner 和阶段回调的 Locomotion 状态。</summary>
        /// <param name="state">待注册的具体状态。</param>
        private void RegisterState(CharacterLocomotionState state)
        {
            registeredStates.Add(state);
            stateMachine.AddState(state);
        }

        /// <summary>通知全部状态清理属于上一次启用周期的运行时数据。</summary>
        private void ResetStatesForActivation()
        {
            for (int index = 0; index < registeredStates.Count; index++)
                registeredStates[index].ResetForActivation();
        }

        #endregion
    }
}
