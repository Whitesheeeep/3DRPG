using System;
using Animancer;
using RPG.Character.State;
using RPG.PlayerInputSystem;
using UnityEngine;
using WS_Modules.FSM;

namespace RPG.Character
{
    /// <summary>角色 Locomotion HFSM 的稳定状态标识。</summary>
    public enum CharacterLocomotionStateId
    {
        /// <summary>未启用状态。</summary>
        Disable = 0,
        /// <summary>接地状态机。</summary>
        Grounded = 1,
        /// <summary>空中状态机。</summary>
        Airborne = 2,
        /// <summary>接地待机。</summary>
        Idle = 3,
        /// <summary>Walk 起步根运动。</summary>
        RootMotionWalkStart = 4,
        /// <summary>Walk 代码移动。</summary>
        Walk = 5,
        /// <summary>Run 代码移动。</summary>
        Run = 6,
        // 原第 7 个状态整数已退役，不能复用，避免旧序列化数据错映射。
        /// <summary>停止根运动。</summary>
        RootMotionStop = 8,
        /// <summary>主动跳跃。</summary>
        JumpMotion = 9,
        /// <summary>外力弹射。</summary>
        ExternalLaunch = 10,
        /// <summary>普通下落。</summary>
        Fall = 11,
        /// <summary>落地缓冲表现。</summary>
        FallLand = 12,
        /// <summary>Run 起步根运动。</summary>
        RootMotionRunStart = 13
    }

    /// <summary>
    /// 组装角色 Locomotion 的 Grounded/Airborne HFSM，并提供 PlayerController 的显式阶段入口。
    /// </summary>
    [Serializable]
    public sealed class CharacterLocomotionStateMachine
    {
        #region 依赖字段

        // 配置与角色依赖由 CharacterActor 注入；状态通过 Owner 读取，不复制这些依赖。
        private PlayerFSMTransition transition;
        private CharacterActor owner;
        private IMotionDriver driver;
        private float gravity;

        #endregion

        #region HFSM 与运行时上下文

        private StateMachine<CharacterLocomotionStateId, CharacterLocomotionStateMachine> stateMachine;
        private GroundedLocomotionStateMachine groundedMachine;
        private AirborneLocomotionStateMachine airborneMachine;

        // Locomotion 状态机的阶段上下文由 PlayerController 传入，状态机不缓存 Update/Fixed/Late 的 deltaTime。
        private float verticalSpeed;
        private float deltaTime;
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
        /// <summary>获取角色 Locomotion 配置。</summary>
        internal PlayerFSMTransition Transition => transition;
        /// <summary>获取首次姿态预热使用的 Idle Transition。</summary>
        internal ITransition IdleTransition => transition?.IdleTransition;
        /// <summary>获取当前活动叶状态的标识。</summary>
        public CharacterLocomotionStateId CurrentState =>
            stateMachine?.CurrentLeafState?.StateId ?? CharacterLocomotionStateId.Disable;
        /// <summary>获取当前是否处于一次重新激活入口。</summary>
        internal bool IsActivationEntry => activationEntryPending;
        /// <summary>获取本次普通 Tick 的外部时间。</summary>
        internal float DeltaTime => deltaTime;
        /// <summary>获取本次 AnimatorMove 转发的世界位移。</summary>
        internal Vector3 AnimatorDeltaPosition => currentAnimatorDeltaPosition;
        /// <summary>获取本次 AnimatorMove 转发的根旋转。</summary>
        internal Quaternion AnimatorDeltaRotation => currentAnimatorDeltaRotation;
        /// <summary>获取本次 Animator 求值使用的时间。</summary>
        internal float AnimatorEvaluationDeltaTime => animatorEvaluationDeltaTime;
        /// <summary>获取当前角色共享的输入与环境黑板。</summary>
        internal PlayerStateBlackboard Blackboard => owner.StateBlackboard;
        /// <summary>获取当前垂直运动速度。</summary>
        internal float VerticalSpeed => verticalSpeed;
        /// <summary>由主动跳跃或外力状态设置本次空中运动初速度。</summary>
        internal void SetVerticalSpeed(float speed) => verticalSpeed = speed;
        #endregion

        #region 初始化与生命周期

        /// <summary>注入角色重力与 Locomotion 状态过渡配置。</summary>
        /// <param name="configuredGravity">非负重力值。</param>
        /// <param name="configuredTransition">状态过渡配置。</param>
        internal void Configure(float configuredGravity, PlayerFSMTransition configuredTransition)
        {
            if (float.IsNaN(configuredGravity) || float.IsInfinity(configuredGravity) || configuredGravity < 0f)
                throw new ArgumentOutOfRangeException(nameof(configuredGravity));
            transition = configuredTransition ?? throw new ArgumentNullException(nameof(configuredTransition));
            gravity = configuredGravity;
        }

        /// <summary>绑定角色和 MotionDriver，并组装唯一的 Grounded/Airborne 状态树。</summary>
        /// <param name="sourceOwner">状态所属角色。</param>
        /// <param name="sourceDriver">统一运动请求出口。</param>
        internal void Initialize(CharacterActor sourceOwner, IMotionDriver sourceDriver)
        {
            owner = sourceOwner ?? throw new ArgumentNullException(nameof(sourceOwner));
            driver = sourceDriver ?? throw new ArgumentNullException(nameof(sourceDriver));
            if (transition == null)
                throw new InvalidOperationException($"角色 '{owner.name}' 未配置 PlayerFSMTransition。");

            stateMachine = new StateMachine<CharacterLocomotionStateId, CharacterLocomotionStateMachine>(
                CharacterLocomotionStateId.Disable);
            groundedMachine = new GroundedLocomotionStateMachine();
            airborneMachine = new AirborneLocomotionStateMachine();
            stateMachine.AddState(groundedMachine);
            stateMachine.AddState(airborneMachine);
            stateMachine.Init(this, null);
        }

        /// <summary>根据共享环境和输入直接进入 Grounded 的目标叶状态。</summary>
        internal void Activate()
        {
            if (active)
                return;

            active = true;
            verticalSpeed = 0f;
            deltaTime = 0f;
            activationEntryPending = true;
            // 激活时重置所有叶状态的启用周期缓存，避免上次退出时残留的状态数据影响本次激活。
            ResetStatesForActivation();
            gravityHandle = driver.RequestControl(new MotionControlRequest(
                owner, MotionPriority.Gravity, MotionChannels.Vertical));

            // 切换角色的路由逻辑：如果当前有 Move 输入，则直接进入 Walk/Run；否则进入 Idle。
            // 后续如果还有其他逻辑，比如攀爬、贴墙或边缘检测，也可以在这里根据环境事实选择对应的初始叶状态。
            // 角色切换属于持续输入续接，不是 Idle 再次起步：有 Move 时直接进入对应代码移动状态。
            CharacterLocomotionStateId initialState = Blackboard.HasMovement
                ? Blackboard.IsSprintHeld ? CharacterLocomotionStateId.Run : CharacterLocomotionStateId.Walk
                : CharacterLocomotionStateId.Idle;
            try
            {
                if (!ChangeStatePath(CharacterLocomotionStateId.Grounded, initialState))
                    throw new InvalidOperationException(
                        $"角色 '{owner.name}' 的初始 Locomotion 状态 '{initialState}' 未通过 CanEnter。 ");
            }
            catch
            {
                stateMachine.OnExit();
                gravityHandle?.Dispose();
                gravityHandle = null;
                active = false;
                throw;
            }
            finally
            {
                activationEntryPending = false;
            }
        }

        /// <summary>退出当前状态并释放跨状态共享的重力控制请求。</summary>
        internal void Deactivate()
        {
            if (!active)
                return;

            active = false;
            activationEntryPending = false;
            stateMachine.OnExit();
            gravityHandle?.Dispose();
            gravityHandle = null;
            verticalSpeed = 0f;
            deltaTime = 0f;
        }

        /// <summary>按叶状态标识切换当前 Locomotion，自动补齐 Grounded/Airborne 路径。</summary>
        /// <param name="stateId">目标叶状态。</param>
        /// <returns>切换成功时返回 true。</returns>
        public bool ChangeState(CharacterLocomotionStateId stateId)
        {
            if (!active && stateId != CharacterLocomotionStateId.Disable)
                return false;
            if (stateId == CharacterLocomotionStateId.Disable)
            {
                Deactivate();
                return true;
            }
            // Grounded/Airborne 状态机是唯一的根节点，直接切换到叶状态时必须补齐路径。
            if (stateId == CharacterLocomotionStateId.Grounded || stateId == CharacterLocomotionStateId.Airborne)
                return stateMachine.ChangeState(stateId);
            return ChangeStatePath(
                IsAirborneState(stateId) ? CharacterLocomotionStateId.Airborne : CharacterLocomotionStateId.Grounded,
                stateId);
        }

        /// <summary>转发从根节点开始的完整 HFSM 路径切换。</summary>
        /// <param name="statePath">根状态机到目标叶状态的路径。</param>
        /// <returns>路径通过预检并完成切换时返回 true。</returns>
        public bool ChangeStatePath(params CharacterLocomotionStateId[] statePath) =>
            stateMachine.ChangeStatePath(statePath);

        #endregion

        #region 外部阶段驱动

        /// <summary>推进普通帧并让根节点先处理跨 Grounded/Airborne 路由。</summary>
        /// <param name="frameDeltaTime">PlayerController 传入的 Update 时间。</param>
        internal void Tick(float frameDeltaTime)
        {
            if (!active)
                return;
            deltaTime = Mathf.Max(0f, frameDeltaTime);
            // 头顶检测是独立环境事实；主动上升遇到阻挡时先截断垂直速度，避免继续向上提交。
            ClampVerticalSpeedByCeiling();
            TryRouteEnvironment();
            // Jump/ExternalLaunch 可能在路由阶段刚刚设置上升速度，因此在状态 Tick 前再次应用头顶门禁。
            ClampVerticalSpeedByCeiling();
            IntegrateAndSubmitGravity();
            stateMachine.OnUpdate();
        }

        /// <summary>推进物理阶段，仅转发明确使用固定步长的状态逻辑。</summary>
        /// <param name="fixedDeltaTime">PlayerController 传入的 FixedUpdate 时间。</param>
        internal void FixedTick(float fixedDeltaTime)
        {
            if (!active)
                return;

            // Locomotion 的重力、跳跃和空中水平运动统一在 Update 提交，避免 CharacterRoot
            // 同时由渲染帧和固定物理帧驱动而产生阶梯式抖动。这里仍保留状态机固定阶段，
            // 供未来明确使用 Fixed 的状态扩展；当前叶状态不在此阶段移动 CharacterRoot。
            stateMachine.OnFixedUpdate();
        }

        /// <summary>推进当前 Locomotion 的 Late 阶段。</summary>
        /// <param name="frameDeltaTime">PlayerController 传入的 LateUpdate 时间。</param>
        internal void LateTick(float frameDeltaTime)
        {
            if (active)
                stateMachine.OnLateUpdate();
        }

        /// <summary>在一次 AnimatorMove 回调期间转发根运动数据，结束后清空阶段上下文。</summary>
        /// <param name="deltaPosition">Animator 根位移。</param>
        /// <param name="deltaRotation">Animator 根旋转。</param>
        /// <param name="evaluationDeltaTime">本次 Animator 求值时间。</param>
        internal void UpdateAnimationMove(Vector3 deltaPosition, Quaternion deltaRotation, float evaluationDeltaTime)
        {
            if (!active)
                return;
            currentAnimatorDeltaPosition = deltaPosition;
            currentAnimatorDeltaRotation = deltaRotation;
            animatorEvaluationDeltaTime = Mathf.Max(0f, evaluationDeltaTime);
            try
            {
                stateMachine.OnAnimationMove();
            }
            finally
            {
                currentAnimatorDeltaPosition = Vector3.zero;
                currentAnimatorDeltaRotation = Quaternion.identity;
                animatorEvaluationDeltaTime = 0f;
            }
        }

        /// <summary>按当前 Update 时间积分共享重力，并提交本帧的垂直候选位移。</summary>
        private void IntegrateAndSubmitGravity()
        {
            if (Blackboard.IsGrounded && verticalSpeed < 0f)
                verticalSpeed = -2f;
            else
                verticalSpeed -= gravity * deltaTime;

            if (gravityHandle != null && deltaTime > 0f)
            {
                // Jump/ExternalLaunch 随后以更高优先级提交 Vertical；没有更高优先级提交时，
                // 本次重力结果才成为最终 Update 位移。
                driver.SubmitUpdate(
                    gravityHandle,
                    UpdateMotionSubmission.TranslationOnly(
                        Vector3.up * (verticalSpeed * deltaTime)));
            }
        }

        #endregion

        #region 内部状态路由

        /// <summary>遍历唯一状态树并清理各叶状态的启用周期缓存。</summary>
        private void ResetStatesForActivation() => ResetMachine(stateMachine);

        /// <summary>递归重置状态机和其直接子状态。</summary>
        private static void ResetMachine(StateMachine<CharacterLocomotionStateId, CharacterLocomotionStateMachine> machine)
        {
            foreach (IState<CharacterLocomotionStateId, CharacterLocomotionStateMachine> state in machine.States.Values)
            {
                // 根据状态树约定，所有叶状态都是 CharacterLocomotionState，非叶节点都是 StateMachine。
                if (state is CharacterLocomotionState locomotionState)
                    locomotionState.ResetForActivation();
                else if (state is StateMachine<CharacterLocomotionStateId, CharacterLocomotionStateMachine> childMachine)
                    ResetMachine(childMachine);
                else
                    throw new InvalidOperationException(
                        $"Locomotion 状态树中的 '{state.StateId}' 不是 CharacterLocomotionState 或嵌套 StateMachine。 ");
            }
        }

        /// <summary>根据环境事实选择跨 Grounded/Airborne 的完整路径。</summary>
        private void TryRouteEnvironment()
        {
            bool airborne = !Blackboard.IsGrounded;
            bool currentlyAirborne = stateMachine.CurrentState == airborneMachine;

            // 判断优先级：外力弹射 > 主动跳跃 > 落地 > 起跳/落地切换。
            // 外力弹射是高优先级环境事实：即使同帧存在 Jump Press，也不能把跳板/击飞误判成主动跳跃。
            if (!currentlyAirborne && HasExternalLaunchCause)
            {
                ChangeStatePath(CharacterLocomotionStateId.Airborne,
                    CharacterLocomotionStateId.ExternalLaunch);
                return;
            }
            // 主动跳跃是高优先级环境事实：即使同帧落地，也要先切入 JumpMotion。
            if (!airborne && !currentlyAirborne && HasBufferedJump)
            {
                TryEnterBufferedJump();
                return;
            }
            // 落地判断在 Update 阶段执行，避免 FixedUpdate 采样的 Grounded 过早切换到 Airborne。
            if (!airborne && currentlyAirborne)
            {
                // 跳跃刚在 Update 成功切入时，Fixed 环境采样仍可能保留上一帧 grounded；
                // 只有主动上升阶段才延迟一次落地判断，避免 JumpMotion 被同一物理步立即打断。
                CharacterLocomotionStateId leafState = CurrentState;
                if ((leafState == CharacterLocomotionStateId.JumpMotion ||
                     leafState == CharacterLocomotionStateId.ExternalLaunch) &&
                    (VerticalSpeed > 0f || Blackboard.ObservedVerticalSpeed > 0f))
                    return;
                // FallLand 仍属于 Airborne 分支；动画完成后由叶状态自己切回 Grounded。
                ChangeStatePath(CharacterLocomotionStateId.Airborne, CharacterLocomotionStateId.FallLand);
                return;
            }
            // 落地后仍有 Jump Press 缓冲时，允许直接从 FallLand 切入 JumpMotion。
            if (airborne && !currentlyAirborne)
            {
                CharacterLocomotionStateId airborneState = HasExternalLaunchCause
                    ? CharacterLocomotionStateId.ExternalLaunch
                    : HasBufferedJump
                        ? CharacterLocomotionStateId.JumpMotion
                        : CharacterLocomotionStateId.Fall;
                if (airborneState == CharacterLocomotionStateId.JumpMotion)
                    TryEnterBufferedJump();
                else
                    ChangeStatePath(CharacterLocomotionStateId.Airborne, airborneState);
                return;
            }

            // 外力原因可能在已经处于 Fall 时才由 GAS 添加；外力优先于后续主动跳跃判断。
            if (airborne && currentlyAirborne &&
                CurrentState == CharacterLocomotionStateId.Fall && HasExternalLaunchCause)
            {
                ChangeStatePath(CharacterLocomotionStateId.Airborne,
                    CharacterLocomotionStateId.ExternalLaunch);
            }
        }

        /// <summary>判断当前 ASC 是否声明外力弹射原因。</summary>
        private bool HasExternalLaunchCause =>
            Transition.ExternalLaunchCauseTag.IsValid &&
            owner.AbilitySystemComponent.HasTag(Transition.ExternalLaunchCauseTag) &&
            Blackboard.ObservedVerticalSpeed >= Transition.ExternalLaunchMinVerticalSpeed;

        /// <summary>判断跳跃输入是否仍在缓冲窗口内。</summary>
        private bool HasBufferedJump =>
            Blackboard.InputRequests.TryGetRequest(PlayerInputType.Jump,
                out IReadOnlyPlayerInputRequest request) && request.HasBufferedPress &&
            Blackboard.TimeSinceGrounded <= Transition.CoyoteTime &&
            !Blackboard.IsCeilingBlocked;

        /// <summary>
        /// 尝试从当前环境进入主动跳跃，并只在完整路径成功后确认 Jump Press。
        /// FallLand 与普通环境路由共用该事务，避免重复实现输入消费边界。
        /// </summary>
        internal bool TryEnterBufferedJump()
        {
            if (!HasBufferedJump)
                return false;

            if (!ChangeStatePath(
                    CharacterLocomotionStateId.Airborne,
                    CharacterLocomotionStateId.JumpMotion))
                return false;

            ConfirmJumpPress();
            return true;
        }

        /// <summary>
        /// 根据环境检测器提供的头顶阻挡事实限制主动上升速度。
        /// 头顶检测不替代重力或跳跃状态，只在本帧最终状态更新前阻止继续向上。
        /// </summary>
        private void ClampVerticalSpeedByCeiling()
        {
            if (Blackboard.IsCeilingBlocked && verticalSpeed > 0f)
                verticalSpeed = 0f;
        }

        /// <summary>主动跳跃路径成功进入后确认输入句柄。</summary>
        private void ConfirmJumpPress()
        {
            if (Blackboard.InputRequests.TryGetRequest(PlayerInputType.Jump,
                    out IReadOnlyPlayerInputRequest request) && request.HasBufferedPress)
                Blackboard.InputRequests.TryConfirmConsumed(request.PressHandle);
        }

        /// <summary>判断状态标识是否属于 Airborne 子状态。</summary>
        private static bool IsAirborneState(CharacterLocomotionStateId stateId) =>
            stateId == CharacterLocomotionStateId.JumpMotion ||
            stateId == CharacterLocomotionStateId.ExternalLaunch ||
            stateId == CharacterLocomotionStateId.Fall ||
            stateId == CharacterLocomotionStateId.FallLand;

        #endregion
    }
}
