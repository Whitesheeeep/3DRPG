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
        RootMotionRunStart = 13,
        /// <summary>Traversal 分支状态机。</summary>
        Traversal = 14,
        /// <summary>低矮障碍 Vault。</summary>
        Vault = 15,
        /// <summary>平台 Mantle。</summary>
        Mantle = 16
    }

    /// <summary>
    /// 组装角色 Locomotion 的 Grounded/Traversal/Airborne HFSM，并提供 PlayerController 的显式阶段入口。
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
        private TraversalEnvironmentDetector traversalDetector;
        #endregion

        #region HFSM 与运行时上下文
        private StateMachine<CharacterLocomotionStateId, CharacterLocomotionStateMachine> stateMachine;
        private GroundedLocomotionStateMachine groundedMachine;
        private TraversalLocomotionStateMachine traversalMachine;
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

        /// <summary>绑定角色和 MotionDriver，并组装唯一的 Grounded/Traversal/Airborne 状态树。</summary>
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
            traversalDetector = new TraversalEnvironmentDetector();
            traversalDetector.Initialize(owner, transition.TraversalDetectionSettings);
            groundedMachine = new GroundedLocomotionStateMachine(traversalDetector);
            traversalMachine = new TraversalLocomotionStateMachine();
            airborneMachine = new AirborneLocomotionStateMachine();
            stateMachine.AddState(groundedMachine);
            stateMachine.AddState(traversalMachine);
            stateMachine.AddState(airborneMachine);
            stateMachine.Init(this, null);
            AddRootTransitions();
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
                groundedMachine.ClearPreparedJump();
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
            groundedMachine.ClearPreparedJump();
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

            // Grounded/Traversal/Airborne 状态机是唯一的根节点，直接切换到叶状态时必须补齐路径。
            if (stateId == CharacterLocomotionStateId.Grounded || stateId == CharacterLocomotionStateId.Airborne)
                return stateMachine.ChangeState(stateId);
            if (stateId == CharacterLocomotionStateId.Traversal)
                return stateMachine.ChangeState(stateId);
            return ChangeStatePath(
                IsTraversalState(stateId)
                    ? CharacterLocomotionStateId.Traversal
                    : IsAirborneState(stateId)
                        ? CharacterLocomotionStateId.Airborne
                        : CharacterLocomotionStateId.Grounded,
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
            // 先让根级 Transition 完成跨 Grounded/Traversal/Airborne 的路径预检与切换。
            // 新状态不会在本帧继续 OnUpdate；随后共享重力使用刚进入 Jump 的初速度提交。
            ClampVerticalSpeedByCeiling();
            stateMachine.OnUpdate();
            ClampVerticalSpeedByCeiling();
            IntegrateAndSubmitGravity();
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
        /// <summary>组合所有根级分支转换；条件只查询来源分支，成功副作用延迟到完整路径提交后。</summary>
        private void AddRootTransitions()
        {
            // 添加从 Grounded 分支到 Traversal/Airborne 的首次 Vault/Mantle/Jump/Fall 路径。
            AddBranchTransitions(CharacterLocomotionStateId.Grounded,
                () => groundedMachine.CanEnterTraversal(TraversalCandidateKind.Vault),
                () => groundedMachine.CanEnterTraversal(TraversalCandidateKind.Mantle),
                groundedMachine.CanEnterBufferedJump);

            stateMachine.AddTransition(new Transition<CharacterLocomotionStateId, CharacterLocomotionStateMachine>(
                    CharacterLocomotionStateId.Grounded,
                    new[] { CharacterLocomotionStateId.Airborne, CharacterLocomotionStateId.ExternalLaunch }, 500)
                .AddCondition(owner => owner.HasExternalLaunchCause)
                .OnCommitted(owner => owner.groundedMachine.ClearPreparedJump()));

            // Traversal
            stateMachine.AddTransition(new Transition<CharacterLocomotionStateId, CharacterLocomotionStateMachine>(
                    CharacterLocomotionStateId.Traversal,
                    new[] { CharacterLocomotionStateId.Airborne, CharacterLocomotionStateId.ExternalLaunch }, 500)
                .AddCondition(owner => owner.HasExternalLaunchCause));

            stateMachine.AddTransition(new Transition<CharacterLocomotionStateId, CharacterLocomotionStateMachine>(
                    CharacterLocomotionStateId.Traversal,
                    new[] { CharacterLocomotionStateId.Airborne, CharacterLocomotionStateId.JumpMotion }, 300)
                .AddCondition(owner => owner.traversalMachine.CanEnterBufferedJump())
                .OnCommitted(owner => owner.traversalMachine.CommitBufferedJump()));

            stateMachine.AddTransition(new Transition<CharacterLocomotionStateId, CharacterLocomotionStateMachine>(
                    CharacterLocomotionStateId.Traversal,
                    new[] { CharacterLocomotionStateId.Grounded, CharacterLocomotionStateId.RootMotionRunStart }, 250)
                .AddCondition(owner => owner.traversalMachine.CanExitByInput &&
                                       owner.Blackboard.IsGrounded && owner.Blackboard.HasMovement &&
                                       owner.Blackboard.IsSprintHeld));

            stateMachine.AddTransition(new Transition<CharacterLocomotionStateId, CharacterLocomotionStateMachine>(
                    CharacterLocomotionStateId.Traversal,
                    new[] { CharacterLocomotionStateId.Grounded, CharacterLocomotionStateId.RootMotionWalkStart }, 240)
                .AddCondition(owner => owner.traversalMachine.CanExitByInput &&
                                       owner.Blackboard.IsGrounded && owner.Blackboard.HasMovement &&
                                       !owner.Blackboard.IsSprintHeld));

            stateMachine.AddTransition(new Transition<CharacterLocomotionStateId, CharacterLocomotionStateMachine>(
                    CharacterLocomotionStateId.Traversal,
                    new[] { CharacterLocomotionStateId.Airborne, CharacterLocomotionStateId.Fall }, 100)
                .AddCondition(owner => owner.traversalMachine.CanDetectFall &&
                                       !owner.Blackboard.IsGrounded && owner.Blackboard.ObservedVerticalSpeed <= 0f));

            // Airbone
            stateMachine.AddTransition(new Transition<CharacterLocomotionStateId, CharacterLocomotionStateMachine>(
                    CharacterLocomotionStateId.Airborne,
                    new[] { CharacterLocomotionStateId.Airborne, CharacterLocomotionStateId.ExternalLaunch }, 500)
                .AddCondition(owner =>
                    owner.HasExternalLaunchCause && owner.CurrentState != CharacterLocomotionStateId.ExternalLaunch));
            stateMachine.AddTransition(new Transition<CharacterLocomotionStateId, CharacterLocomotionStateMachine>(
                    CharacterLocomotionStateId.Airborne,
                    new[] { CharacterLocomotionStateId.Airborne, CharacterLocomotionStateId.JumpMotion }, 400)
                .AddCondition(owner => owner.airborneMachine.CanEnterBufferedJumpFromLanding())
                .OnCommitted(owner => owner.ConfirmJumpPress()));
            stateMachine.AddTransition(new Transition<CharacterLocomotionStateId, CharacterLocomotionStateMachine>(
                    CharacterLocomotionStateId.Airborne,
                    new[] { CharacterLocomotionStateId.Grounded, CharacterLocomotionStateId.RootMotionRunStart }, 350)
                .AddCondition(owner => owner.airborneMachine.CanEnterLandingMoveStart(true)));
            stateMachine.AddTransition(new Transition<CharacterLocomotionStateId, CharacterLocomotionStateMachine>(
                    CharacterLocomotionStateId.Airborne,
                    new[] { CharacterLocomotionStateId.Grounded, CharacterLocomotionStateId.RootMotionWalkStart }, 340)
                .AddCondition(owner => owner.airborneMachine.CanEnterLandingMoveStart(false)));
            stateMachine.AddTransition(new Transition<CharacterLocomotionStateId, CharacterLocomotionStateMachine>(
                    CharacterLocomotionStateId.Airborne,
                    new[] { CharacterLocomotionStateId.Airborne, CharacterLocomotionStateId.Fall }, 200)
                .AddCondition(owner => !owner.Blackboard.IsGrounded && owner.Blackboard.ObservedVerticalSpeed <= 0f &&
                                       owner.CurrentState != CharacterLocomotionStateId.Fall));

            // 添加从 Airborne 分支到 FallLand 的路径。
            stateMachine.AddTransition(new Transition<CharacterLocomotionStateId, CharacterLocomotionStateMachine>(
                    CharacterLocomotionStateId.Airborne,
                    new[] { CharacterLocomotionStateId.Airborne, CharacterLocomotionStateId.FallLand }, 100)
                .AddCondition(owner => owner.Blackboard.IsGrounded &&
                                       owner.CurrentState != CharacterLocomotionStateId.FallLand &&
                                       owner.CurrentState != CharacterLocomotionStateId.JumpMotion &&
                                       owner.CurrentState != CharacterLocomotionStateId.ExternalLaunch));
        }

        /// <summary>为 Grounded 来源分支添加首次 Vault、Mantle、Jump 和 Fall 路径。</summary>
        /// <param name="sourceState">根状态机中的来源分支。</param>
        /// <param name="canVault">来源分支的 Vault 资格判断。</param>
        /// <param name="canMantle">来源分支的 Mantle 资格判断。</param>
        /// <param name="canJump">来源分支的普通 Jump 资格判断。</param>
        private void AddBranchTransitions(
            CharacterLocomotionStateId sourceState,
            Func<bool> canVault,
            Func<bool> canMantle,
            Func<bool> canJump)
        {
            // 通过闭包引用来源分支的具体 Commit/Clear，避免根状态机重新解释输入来源。
            // 由于 Vault/Mantle/Jump 都是一次性消耗的输入，Commit 后必须清理来源分支的准备状态。
            Action<CharacterLocomotionStateMachine> commit = owner => owner.groundedMachine.CommitPreparedJump();
            Action<CharacterLocomotionStateMachine> clear = owner => owner.groundedMachine.ClearPreparedJump();


            Transition<CharacterLocomotionStateId, CharacterLocomotionStateMachine> vaultTransition =
                new Transition<CharacterLocomotionStateId, CharacterLocomotionStateMachine>(
                        sourceState,
                        new[] { CharacterLocomotionStateId.Traversal, CharacterLocomotionStateId.Vault }, 400)
                    .AddCondition(_ => canVault())
                    .OnCommitted(commit);
            stateMachine.AddTransition(vaultTransition);

            Transition<CharacterLocomotionStateId, CharacterLocomotionStateMachine> mantleTransition =
                new Transition<CharacterLocomotionStateId, CharacterLocomotionStateMachine>(
                    sourceState,
                    new[] { CharacterLocomotionStateId.Traversal, CharacterLocomotionStateId.Mantle }, 390);
            stateMachine.AddTransition(mantleTransition.AddCondition(_ => canMantle()).OnCommitted(commit));

            stateMachine.AddTransition(new Transition<CharacterLocomotionStateId, CharacterLocomotionStateMachine>(
                    sourceState,
                    new[] { CharacterLocomotionStateId.Airborne, CharacterLocomotionStateId.JumpMotion }, 300)
                .AddCondition(_ => canJump())
                .OnCommitted(commit));

            stateMachine.AddTransition(new Transition<CharacterLocomotionStateId, CharacterLocomotionStateMachine>(
                    sourceState,
                    new[] { CharacterLocomotionStateId.Airborne, CharacterLocomotionStateId.Fall }, 100)
                .AddCondition(owner => !owner.Blackboard.IsGrounded &&
                                       owner.Blackboard.ObservedVerticalSpeed <= 0f)
                .OnCommitted(clear));
        }

        /// <summary>遍历唯一状态树并清理各叶状态的启用周期缓存。</summary>
        private void ResetStatesForActivation()
        {
            // UnifiedFSM 的子状态机不参与 CharacterLocomotionState 的虚拟重置协议，
            // 因此由外层在遍历叶状态前显式通知 Grounded 的候选缓存和共享运行时。
            groundedMachine.ResetRuntimeForActivation();
            ResetMachine(stateMachine);
        }

        /// <summary>递归重置状态机和其直接子状态。</summary>
        private static void ResetMachine(
            StateMachine<CharacterLocomotionStateId, CharacterLocomotionStateMachine> machine)
        {
            foreach (IState<CharacterLocomotionStateId, CharacterLocomotionStateMachine> state in machine.States.Values)
            {
                // 根据状态树约定，所有叶状态都是 CharacterLocomotionState，非叶节点都是 StateMachine。
                if (state is CharacterLocomotionState locomotionState)
                    locomotionState.ResetForActivation();
                else if
                    (state is StateMachine<CharacterLocomotionStateId, CharacterLocomotionStateMachine> childMachine)
                    ResetMachine(childMachine);
                else
                    throw new InvalidOperationException(
                        $"Locomotion 状态树中的 '{state.StateId}' 不是 CharacterLocomotionState 或嵌套 StateMachine。 ");
            }
        }

        /// <summary>判断当前 ASC 是否声明外力弹射原因。</summary>
        private bool HasExternalLaunchCause =>
            Transition.ExternalLaunchCauseTag.IsValid &&
            owner.AbilitySystemComponent.HasTag(Transition.ExternalLaunchCauseTag) &&
            Blackboard.ObservedVerticalSpeed >= Transition.ExternalLaunchMinVerticalSpeed;

        /// <summary>获取 Traversal 分支准备好的指定候选，供目标叶状态冻结动画设置。</summary>
        /// <param name="candidateKind">目标候选类型。</param>
        /// <param name="candidate">准备好的世界空间候选。</param>
        /// <returns>存在匹配候选时返回 true。</returns>
        internal bool TryGetPreparedTraversalCandidate(
            TraversalCandidateKind candidateKind,
            out TraversalCandidate candidate)
        {
            // 根级路径预检的目标叶状态并不知道来源分支；根据当前根节点选择唯一来源缓存，
            // 避免使用另一分支上一次生命周期留下的候选或 Jump Press。
            return groundedMachine.TryGetPreparedCandidate(candidateKind, out candidate);
        }

        /// <summary>在 Traversal 动画自然结束时按当前输入回到 Grounded。</summary>
        internal void CompleteTraversal()
        {
            // Jump 资格成立但目标 TagQuery/路径预检失败时，不能吞掉输入窗口；继续尝试接地 MoveStart。
            if (traversalMachine.CanEnterBufferedJump() &&
                ChangeStatePath(
                    CharacterLocomotionStateId.Airborne,
                    CharacterLocomotionStateId.JumpMotion))
            {
                traversalMachine.CommitBufferedJump();
                return;
            }

            if (!Blackboard.IsGrounded)
            {
                ChangeStatePath(
                    CharacterLocomotionStateId.Airborne,
                    CharacterLocomotionStateId.Fall);
            }
            else if (Blackboard.HasMovement)
            {
                ChangeStatePath(
                    CharacterLocomotionStateId.Grounded,
                    Blackboard.IsSprintHeld
                        ? CharacterLocomotionStateId.RootMotionRunStart
                        : CharacterLocomotionStateId.RootMotionWalkStart);
            }
            else
            {
                ChangeStatePath(
                    CharacterLocomotionStateId.Grounded,
                    CharacterLocomotionStateId.RootMotionStop);
            }
        }

        /// <summary>
        /// 在 FallLand 动画自然结束时按 Jump、接地 Move 和 Idle 的顺序选择后续路径。
        /// </summary>
        internal void CompleteLanding()
        {
            // OnEnd 可能发生在根状态机本帧自动 Transition 之后，因此这里再次执行同一优先级。
            if (TryEnterBufferedJump())
                return;

            if (!Blackboard.IsGrounded)
            {
                ChangeStatePath(
                    CharacterLocomotionStateId.Airborne,
                    CharacterLocomotionStateId.Fall);
                return;
            }

            ChangeStatePath(
                CharacterLocomotionStateId.Grounded,
                Blackboard.HasMovement
                    ? Blackboard.IsSprintHeld
                        ? CharacterLocomotionStateId.RootMotionRunStart
                        : CharacterLocomotionStateId.RootMotionWalkStart
                    : CharacterLocomotionStateId.Idle);
        }

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

        /// <summary>判断状态标识是否属于 Traversal 子状态。</summary>
        private static bool IsTraversalState(CharacterLocomotionStateId stateId) =>
            stateId == CharacterLocomotionStateId.Vault ||
            stateId == CharacterLocomotionStateId.Mantle;
        #endregion
    }
}