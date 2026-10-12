using System;
using RPG.Character;
using RPG.PlayerInputSystem;
using WS_Modules.FSM;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.Generated;

namespace RPG.Character.State
{
    /// <summary>标识角色生命状态机中的顶层状态。</summary>
    internal enum E_CharacterLifeStateId
    {
        /// <summary>角色处于存活状态，内部由 AliveCharacterState 驱动 Locomotion 子状态机。</summary>
        Alive,
        /// <summary>角色处于死亡状态，等待死亡动画交接或显式复活。</summary>
        Dead
    }

    /// <summary>统一管理角色生命状态，并将当前角色阶段转交给对应状态。</summary>
    internal sealed class CharacterLifeStateMachine : IDisposable
    {
        #region 依赖字段

        private readonly CharacterActor actor;
        private readonly PlayerController playerController;
        private readonly CharacterEquipmentEffectBinding equipmentEffectBinding;
        private readonly StateMachine<E_CharacterLifeStateId, CharacterLifeStateMachine> stateMachine;
        private readonly AliveCharacterState aliveState;
        private readonly DeadCharacterState deadState;

        #endregion

        #region 生命周期与阶段上下文

        private IPlayerInputRequestBuffer inputRequests;
        private float updateDeltaTime;
        private float fixedDeltaTime;
        private float lateDeltaTime;
        private bool actorControlEntered;
        private bool controlAllowed;
        private bool fixedStepAllowsMotionCommit;
        private bool initialized;
        private bool disposing;
        private bool disposed;

        #endregion

        #region 查询属性

        /// <summary>获取生命状态机所属的 CharacterActor。</summary>
        internal CharacterActor Actor => actor;

        /// <summary>获取由 Alive 状态持有的 Locomotion 子状态机。</summary>
        internal CharacterLocomotionStateMachine Locomotion => aliveState.Locomotion;

        /// <summary>获取当前生命状态是否为 Dead。</summary>
        internal bool IsDead => stateMachine.CurrentState?.StateId == E_CharacterLifeStateId.Dead;

        /// <summary>获取角色是否已由 Actor 生命周期正式进入操控状态。</summary>
        internal bool IsActorControlEntered => actorControlEntered;

        /// <summary>获取 Player 当前是否允许 Alive 状态推进 Locomotion。</summary>
        internal bool IsControlAllowed => controlAllowed;

        /// <summary>获取共享 MotionDriver 的玩家接地查询和 Owner 协调入口。</summary>
        internal PlayerController PlayerController => playerController;

        /// <summary>获取生命状态机是否正在销毁，供 Dead 状态区分复活退出和生命周期清理。</summary>
        internal bool IsDisposing => disposing;

        #endregion

        #region 构造与释放

        /// <summary>创建生命状态根节点，并让 Alive 状态拥有并初始化 Locomotion 子状态机。</summary>
        /// <param name="sourceActor">被管理的角色 Actor。</param>
        /// <param name="sourcePlayerController">负责共享角色运动与接地查询的 PlayerController。</param>
        /// <param name="sourceEquipmentEffectBinding">复活时需要恢复的装备效果绑定。</param>
        /// <exception cref="ArgumentNullException">任一必要依赖为空时抛出。</exception>
        internal CharacterLifeStateMachine(
            CharacterActor sourceActor,
            PlayerController sourcePlayerController,
            CharacterEquipmentEffectBinding sourceEquipmentEffectBinding)
        {
            actor = sourceActor ?? throw new ArgumentNullException(nameof(sourceActor));
            playerController = sourcePlayerController ?? throw new ArgumentNullException(nameof(sourcePlayerController));
            equipmentEffectBinding = sourceEquipmentEffectBinding ??
                                     throw new ArgumentNullException(nameof(sourceEquipmentEffectBinding));

            stateMachine = new StateMachine<E_CharacterLifeStateId, CharacterLifeStateMachine>(
                E_CharacterLifeStateId.Alive);
            stateMachine.Init(this, null);
            aliveState = new AliveCharacterState(this);
            deadState = new DeadCharacterState(this);
            stateMachine.AddState(aliveState);
            stateMachine.AddState(deadState);
            DebugLifeStateMachineCreated();
        }

        /// <summary>订阅 Health 变化并按初始化后的实际 Health 进入 Alive 或 Dead。</summary>
        /// <exception cref="InvalidOperationException">ASC 缺少 Health，或初始状态无法进入时抛出。</exception>
        internal void Initialize()
        {
            if (initialized)
                return;

            if (!actor.AbilitySystemComponent.TryGetCurrentValue(GameplayAttributes.Attribute_Health, out float health))
                throw new InvalidOperationException($"角色 {actor.CharacterId} 的 ASC 缺少 Health Attribute。");

            // 先注册变化监听，再提交初始生命状态，避免初始化与首次伤害之间存在观察空窗。
            actor.AbilitySystemComponent.AttributeChanged += HandleAttributeChanged;
            initialized = true;
            E_CharacterLifeStateId initialState = health <= 0f
                ? E_CharacterLifeStateId.Dead
                : E_CharacterLifeStateId.Alive;
            if (!stateMachine.ChangeState(initialState))
                throw new InvalidOperationException($"角色 {actor.CharacterId} 无法初始化生命状态 {initialState}。");

            UnityEngine.Debug.Log(
                $"[CharacterLifeStateMachine] 生命状态机初始化完成，character={actor.CharacterId}, state={initialState}, health={health:F1}。",
                actor);
        }

        /// <summary>注销 Health 监听并退出当前状态，释放 Locomotion、死亡动画与死亡重力资源。</summary>
        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            disposing = true;
            if (initialized)
                actor.AbilitySystemComponent.AttributeChanged -= HandleAttributeChanged;

            // 状态退出负责释放本状态所拥有的子 FSM 与异步动画事件，不在此处伪造复活状态变化。
            stateMachine.OnExit();
            actorControlEntered = false;
            controlAllowed = false;
            inputRequests = null;
            initialized = false;
            UnityEngine.Debug.Log(
                $"[CharacterLifeStateMachine] 生命状态机已释放，character={actor.CharacterId}。",
                actor);
        }

        #endregion

        #region Actor 生命周期与状态切换

        /// <summary>由 Health 归零或初始化血量触发 Dead 顶层状态。</summary>
        /// <returns>本次实际完成状态切换时返回 true。</returns>
        internal bool EnterDead()
        {
            if (!initialized || disposed)
                throw new InvalidOperationException($"角色 {actor.CharacterId} 的生命状态机尚未初始化或已经释放。");
            if (IsDead)
                return false;

            bool changed = stateMachine.ChangeState(E_CharacterLifeStateId.Dead);
            UnityEngine.Debug.Log(
                $"[CharacterLifeStateMachine] 请求进入 Dead 状态，character={actor.CharacterId}, changed={changed}。",
                actor);
            return changed;
        }

        /// <summary>在 Active Actor 生命周期开始时通知当前状态接管表现与控制。</summary>
        /// <param name="allowControl">Player 当前是否允许驱动 Alive Locomotion。</param>
        internal void OnActorEntered(bool allowControl)
        {
            if (actorControlEntered)
                return;

            actorControlEntered = true;
            controlAllowed = allowControl;
            if (IsDead)
                deadState.OnActorEntered();
            else
                aliveState.OnActorEntered();

            UnityEngine.Debug.Log(
                $"[CharacterLifeStateMachine] Actor 已进入生命状态控制，character={actor.CharacterId}, state={CurrentStateName}, controlAllowed={controlAllowed}。",
                actor);
        }

        /// <summary>在 Actor 退场时由当前状态释放本状态拥有的运动和动画资源。</summary>
        internal void OnActorExited()
        {
            if (!actorControlEntered)
                return;

            if (IsDead)
                deadState.OnActorExited();
            else
                aliveState.OnActorExited();

            actorControlEntered = false;
            controlAllowed = false;
            UnityEngine.Debug.Log(
                $"[CharacterLifeStateMachine] Actor 已退出生命状态控制，character={actor.CharacterId}, state={CurrentStateName}。",
                actor);
        }

        /// <summary>响应 Player 禁用、场景准备或恢复流程对 Alive Locomotion 的控制门禁。</summary>
        /// <param name="allowControl">是否允许 Alive 子状态机推进运动。</param>
        internal void SetControlAllowed(bool allowControl)
        {
            if (controlAllowed == allowControl)
                return;

            controlAllowed = allowControl;
            if (actorControlEntered && !IsDead)
                aliveState.OnControlPermissionChanged();

            UnityEngine.Debug.Log(
                $"[CharacterLifeStateMachine] Player 控制门禁已更新，character={actor.CharacterId}, state={CurrentStateName}, allowed={allowControl}。",
                actor);
        }

        /// <summary>保存 Alive 子状态退出前的垂直速度，供 Dead 状态接续重力积分。</summary>
        /// <param name="sourceVerticalSpeed">Locomotion 最后一次更新的垂直速度。</param>
        internal void RecordVerticalSpeedBeforeDeath(float sourceVerticalSpeed) =>
            deadState.SetEntryVerticalSpeed(sourceVerticalSpeed);

        /// <summary>显式复活时恢复当前装备 GE、按最终 MaxHealth 回满，并切回 Alive。</summary>
        internal void ReviveToFullHealth()
        {
            if (!IsDead)
                return;

            // 装备贡献先恢复，随后读取最终上限，避免复活生命值低于装备加成后的 MaxHealth。
            equipmentEffectBinding.ResumeAndRefresh();
            if (!actor.AbilitySystemComponent.TryGetCurrentValue(GameplayAttributes.Attribute_MaxHealth, out float maxHealth))
                throw new InvalidOperationException($"角色 {actor.CharacterId} 复活时缺少 MaxHealth Attribute。");
            if (!actor.AbilitySystemComponent.TrySetResourceCurrentValues(new[]
                {
                    new GameplayAttributeValue(GameplayAttributes.Attribute_Health, maxHealth)
                }))
                throw new InvalidOperationException($"角色 {actor.CharacterId} 复活时无法将 Health 恢复到 MaxHealth。");
            if (actor.IsActiveCharacter)
                SetControlAllowed(playerController.PrepareCharacterEntry(actor));
            if (!stateMachine.ChangeState(E_CharacterLifeStateId.Alive))
                throw new InvalidOperationException($"角色 {actor.CharacterId} 复活时无法切换到 Alive 状态。");

            UnityEngine.Debug.Log(
                $"[CharacterLifeStateMachine] 角色已显式复活并按最终 MaxHealth 回满，character={actor.CharacterId}, maxHealth={maxHealth:F1}。",
                actor);
        }

        #endregion

        #region 当前角色阶段推进

        /// <summary>由 CharacterManager 推进当前生命状态的 Update 阶段。</summary>
        /// <param name="sourceInputRequests">当前帧的输入请求缓冲区。</param>
        /// <param name="deltaTime">当前 Update 时间步长。</param>
        internal void Tick(IPlayerInputRequestBuffer sourceInputRequests, float deltaTime)
        {
            inputRequests = sourceInputRequests ?? throw new ArgumentNullException(nameof(sourceInputRequests));
            updateDeltaTime = deltaTime;
            try
            {
                stateMachine.OnUpdate();
            }
            finally
            {
                inputRequests = null;
                updateDeltaTime = 0f;
            }
        }

        /// <summary>由 CharacterManager 推进当前生命状态的 Fixed 阶段并返回运动结算门禁。</summary>
        /// <param name="deltaTime">当前 Fixed 时间步长。</param>
        /// <returns>当前状态允许 MotionDriver 完成物理结算时返回 true。</returns>
        internal bool FixedTick(float deltaTime)
        {
            fixedDeltaTime = deltaTime;
            fixedStepAllowsMotionCommit = false;
            try
            {
                stateMachine.OnFixedUpdate();
                return fixedStepAllowsMotionCommit;
            }
            finally
            {
                fixedDeltaTime = 0f;
                fixedStepAllowsMotionCommit = false;
            }
        }

        /// <summary>由 CharacterManager 推进当前生命状态的 Late 阶段。</summary>
        /// <param name="deltaTime">当前 LateUpdate 时间步长。</param>
        internal void LateTick(float deltaTime)
        {
            lateDeltaTime = deltaTime;
            try
            {
                stateMachine.OnLateUpdate();
            }
            finally
            {
                lateDeltaTime = 0f;
            }
        }

        /// <summary>将 Animator 求值增量交给 Alive 状态，由其协调 ASC 和 Locomotion 动画阶段。</summary>
        /// <param name="deltaPosition">Animator 本次求值产生的根位移。</param>
        /// <param name="deltaRotation">Animator 本次求值产生的根旋转。</param>
        /// <param name="evaluationDeltaTime">本次 Animator 求值时长。</param>
        /// <returns>Alive 状态实际消费了 Animator 阶段时返回 true。</returns>
        internal bool UpdateAnimationMove(
            UnityEngine.Vector3 deltaPosition,
            UnityEngine.Quaternion deltaRotation,
            float evaluationDeltaTime)
        {
            if (!CanAdvanceAliveState)
                return false;

            return aliveState.UpdateAnimationMove(deltaPosition, deltaRotation, evaluationDeltaTime);
        }

        /// <summary>在存活状态允许时依次推进输入仲裁和 Locomotion Update。</summary>
        internal void AdvanceAliveUpdate()
        {
            if (!CanAdvanceAliveState)
                return;

            // Action Arbiter 先提交技能切换，保证后续 Locomotion 读取本帧最新 FullBody 占据。
            actor.AdvanceActionFrame(inputRequests, updateDeltaTime);
            if (!IsDead && !actor.IsActionPaused)
                aliveState.Locomotion.Tick(updateDeltaTime);
        }

        /// <summary>在 Alive 状态推进 ASC 与 Locomotion Fixed 阶段。</summary>
        internal void AdvanceAliveFixed()
        {
            if (!CanAdvanceAliveState)
                return;

            actor.FixedTickAbility(fixedDeltaTime);
            if (!IsDead && !actor.IsActionPaused)
                aliveState.Locomotion.FixedTick(fixedDeltaTime);
            fixedStepAllowsMotionCommit = true;
        }

        /// <summary>在存活且未局部暂停时推进 Locomotion Late 阶段。</summary>
        internal void AdvanceAliveLate()
        {
            if (CanAdvanceAliveState)
                aliveState.Locomotion.LateTick(lateDeltaTime);
        }

        /// <summary>在 Dead 状态提交只包含垂直通道的重力候选，并允许 MotionDriver 结算。</summary>
        internal void AdvanceDeadFixed()
        {
            if (!actorControlEntered || !actor.IsActiveCharacter)
                return;

            deadState.AdvanceFixed(fixedDeltaTime);
            fixedStepAllowsMotionCommit = true;
        }

        #endregion

        #region Health 事件与内部辅助

        /// <summary>仅在 Health 低于或等于零时请求进入 Dead，避免监听其他属性的无关变化。</summary>
        /// <param name="attribute">发生变化的 Gameplay Attribute。</param>
        /// <param name="oldValue">变化前的 CurrentValue。</param>
        /// <param name="newValue">变化后的 CurrentValue。</param>
        private void HandleAttributeChanged(GameplayAttribute attribute, float oldValue, float newValue)
        {
            if (attribute != GameplayAttributes.Attribute_Health || newValue > 0f || IsDead)
                return;

            EnterDead();
        }

        /// <summary>判断当前 Alive 子状态是否具备推进玩家控制阶段的全部门禁。</summary>
        private bool CanAdvanceAliveState =>
            actorControlEntered && controlAllowed && !IsDead && !actor.IsActionPaused;

        /// <summary>获取当前顶层状态名称，用于一次性生命周期日志。</summary>
        private string CurrentStateName => stateMachine.CurrentState?.StateId.ToString() ?? "None";

        /// <summary>记录根状态机及 Alive Locomotion 子状态机的装配完成。</summary>
        private void DebugLifeStateMachineCreated()
        {
            UnityEngine.Debug.Log(
                $"[CharacterLifeStateMachine] 已组装 Alive/Dead 状态层与 Alive Locomotion 子状态机，character={actor.CharacterId}。",
                actor);
        }

        #endregion
    }
}
