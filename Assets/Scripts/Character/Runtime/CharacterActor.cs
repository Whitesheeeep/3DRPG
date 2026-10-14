using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using RPG.Character.Animation;
using RPG.ItemSystem;
using RPG.Character.State;
using RPG.Combat;
using RPG.PlayerInputSystem;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.GameplayAbilitySystem;

namespace RPG.Character
{
    /// <summary>封装单个角色的战斗与能力运行时，并将生命状态和 Locomotion 阶段交给生命 FSM。</summary>
    [RequireComponent(typeof(Animator))]
    [InfoBox("依赖 CharacterConfig、同节点 Humanoid Animator、AnimationController 和 WeaponRigController，以及同节点或子节点中的 ASC、MarkerProvider、SkillRuntimeHost、CharacterWeaponPresentationController；武器表现由 CharacterManager 注入 Roster/WeaponInventory，武器姿态由技能 Task 通过 Owner.WeaponSwitch 驱动；Config 提供角色属性、战斗与 Locomotion 参数，子树 Renderer 用于隐藏后台角色。")]
    public sealed class CharacterActor : CharacterAbilityActor, IGameplayAbilityTargetProvider,
        ICombatDamageReceiver, IPlayerGameplayAbilitySystemOwner
    {
        #region 配置与运行时状态

        // 玩家角色配置与 Humanoid 足部缓存只服务于玩家战斗和队伍切换。
        [SerializeField, Required] private CharacterConfig config;
        [SerializeField, Required] private CharacterWeaponPresentationController weaponPresentationController;
        [NonSerialized] private Transform leftFoot;
        [NonSerialized] private Transform rightFoot;
        [NonSerialized] private bool footBonesCached;

        // 战斗、实例和成长运行时在 CharacterManager 绑定 CharacterInstance 后初始化。
        private readonly CharacterCombatSystem combatSystem = new();
        private CharacterActionArbiter actionArbiter;
        private CharacterHitReactionSystem hitReactionSystem;
        private CharacterInstance instance;
        private CharacterRuntimeAttributeBinding runtimeAttributeBinding;
        private CharacterEquipmentEffectBinding equipmentEffectBinding;
        private CharacterLifeStateMachine lifeStateMachine;
        private readonly CharacterAttributeProgressionResolver progressionResolver = new();
        // 该句柄让 Actor 在离队、初始化回滚或销毁时释放自己的 ASC 伤害路由。
        private IDisposable damageReceiverRegistration;
        private bool runtimeStopped = true;
        private bool isActiveCharacter;

        // Player 注入的稳定运行时依赖，不随角色切换重新创建。
        private Transform characterRoot;
        private IMotionDriver motionDriver;
        private PlayerController playerController;
        // 所有队伍角色共享同一个 PlayerStateBlackboard，Locomotion 通过 Owner 直接读取当前玩家输入事实。
        private PlayerStateBlackboard stateBlackboard;
        // 预热期间 Animator 可能被临时启用；该标记阻止初始化求值误进入玩家运动结算。
        private bool suppressAnimatorMotion;
        // 角色表现 Renderer 缓存，由上场与退场生命周期统一切换可见状态。
        private Renderer[] presentationRenderers;
        // PlayerController 在 Start 阶段统一初始化队伍；CharacterActor.Start 作为独立实例启用时的幂等兜底。
        private bool runtimeConfigurationInitialized;

        #endregion

        #region 属性

        /// <summary>获取角色稳定标识。</summary>
        public CharacterId CharacterId => Config != null ? Config.CharacterId : default;
        /// <summary>获取角色配置；Manager 在加载阶段校验该引用。</summary>
        public CharacterConfig Config => instance != null ? instance.Config : config;
        /// <summary>获取当前 Actor 绑定的稳定角色实例。</summary>
        public CharacterInstance Instance => instance;
        /// <summary>判断左脚是否位于右脚前方；仅适用于已初始化的 Humanoid Avatar。</summary>
        public bool IsLeftFootAhead
        {
            get
            {
                EnsureDependencies();
                if (leftFoot == null || rightFoot == null)
                    throw new InvalidOperationException($"角色 '{name}' 未提供左右脚骨骼。");
                return RootTransform.InverseTransformPoint(leftFoot.position).z >
                       RootTransform.InverseTransformPoint(rightFoot.position).z;
            }
        }
        /// <summary>获取 Alive 状态持有的 Locomotion 子状态机。</summary>
        public CharacterLocomotionStateMachine Locomotion => LifeStateMachine.Locomotion;
        /// <summary>获取当前 Actor 的生命状态机。</summary>
        internal CharacterLifeStateMachine LifeStateMachine => lifeStateMachine ??
            throw new InvalidOperationException($"角色 '{name}' 尚未初始化生命状态机。");
        /// <summary>向 Dead 状态提供角色动画控制器，不将动画生命周期逻辑放回 Actor。</summary>
        internal AnimationController LifeAnimationController => animationController;
        /// <summary>获取当前 Actor 是否正在场上并代表玩家接受操控。</summary>
        public bool IsActiveCharacter => isActiveCharacter;
        /// <summary>获取当前角色是否已进入死亡层。</summary>
        public bool IsDead => lifeStateMachine?.IsDead ?? false;
        /// <summary>获取稳定 Player 共享的输入状态黑板。</summary>
        public PlayerStateBlackboard StateBlackboard => stateBlackboard;
        /// <inheritdoc />
        public IPlayerInputRequestBuffer InputRequestBuffer => PlayerInputController.Instance;
        /// <inheritdoc />
        public Vector3 MoveWorldInput => StateBlackboard.MoveWorldInput;
        /// <summary>按当前角色战斗配置查找技能输入对应的 GA Handle。</summary>
        /// <param name="inputType">玩家技能输入类型。</param>
        /// <param name="handle">找到时返回 ASC 授予的 Handle。</param>
        /// <returns>当前角色配置了该输入槽位时返回 true。</returns>
        public bool TryGetGameplayAbilityHandle(
            E_PlayerInputType inputType,
            out GameplayAbilityHandle handle) => combatSystem.TryGetSkillAbilityHandle(inputType, out handle);
        /// <inheritdoc />
        public bool TryGetCurrentAbilityInputSource(
            out E_PlayerInputType inputType,
            out InputRequestHandle inputHandle) =>
            combatSystem.TryGetCurrentAbilityInputSource(out inputType, out inputHandle);
        /// <summary>当角色仍有活动能力时，角色切换应直接拒绝。</summary>
        public bool IsBusy => IsDead || AbilitySystemComponent.ActiveAbilities.Count > 0;
        /// <inheritdoc />
        public override Transform RootTransform => characterRoot != null ? characterRoot : transform;
        /// <inheritdoc />
        public override IMotionDriver MotionDriver => motionDriver;
        /// <inheritdoc />
        public override IFullBodyActionArbiter FullBodyActionArbiter => actionArbiter ??
            throw new InvalidOperationException($"角色 '{name}' 尚未完成 Action Arbiter 初始化。");
        /// <summary>获取当前 FullBody Runtime，供同步受击解析读取当前 Phase 的取消窗口。</summary>
        internal GameplayAbilityRuntime CurrentFullBodyRuntime => actionArbiter?.CurrentFullBodyRuntime;

        /// <summary>当死亡状态变化后通知 HUD 和其他角色表现订阅者。</summary>
        public event Action<CharacterActor, bool> LifeStateChanged;

        /// <summary>当前 Active 角色的死亡动画播放结束后通知队伍管理器。</summary>
        public event Action<CharacterActor> DeathAnimationCompleted;

        /// <inheritdoc />
        public GameplayAbilitySystemComponent TryGetTargetASC()
        {
            if (!isActiveAndEnabled || !runtimeConfigurationInitialized || !isActiveCharacter || IsDead)
                return null;

            return abilitySystemComponent;
        }

        #endregion

        #region Unity 生命周期

        /// <summary>在角色包装 Prefab 的真实序列化边界解析并校验必需依赖。</summary>
        private void Awake() => EnsureDependencies();

        /// <summary>在 Player 已完成运行时绑定时，作为幂等兜底导入属性并组装角色业务。</summary>
        private void Start()
        {
            // 正式入口由 PlayerController 初始化流程调用；Player 初始化失败时不能再制造缺少 Locomotion 依赖的次生异常。
            if (stateBlackboard != null && !runtimeStopped)
                InitializeFromInstance();
        }

        /// <summary>先释放 Actor 自有运行时，再清理基类 Ability 依赖。</summary>
        protected override void OnDestroy()
        {
            StopRuntime();
            base.OnDestroy();
        }

        /// <summary>在父级 PlayerController 先于子角色 Awake 时也能同步解析依赖。</summary>
        private void EnsureDependencies()
        {
            EnsureAbilityDependencies();
            if (!footBonesCached)
            {
                if (animator.avatar == null || !animator.isHuman)
                    throw new InvalidOperationException($"角色 '{name}' 的 Animator 必须配置 Humanoid Avatar 才能判断左右脚。");
                leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                footBonesCached = true;
            }
            if (config == null)
                throw new InvalidOperationException($"CharacterActor '{name}' 未配置 CharacterConfig。");
            if (weaponRigController == null)
                throw new InvalidOperationException($"CharacterActor '{name}' 未配置 WeaponRigController。");
            if (weaponPresentationController == null)
                throw new InvalidOperationException($"CharacterActor '{name}' 未配置 CharacterWeaponPresentationController。");
        }

        /// <summary>仅在配置了 AttributeSet 且 ASC 尚未初始化时执行一次初始化。</summary>
        private void InitializeAttributesFromInstance()
        {
            EnsureDependencies();
            IReadOnlyList<GameplayAttributeValue> baseValues = progressionResolver.ResolveBaseValues(instance.Config, instance.Level);
            // 即使其他启动路径提前导入了 AttributeSet，也必须把稳定实例等级的烘焙 BaseValue 覆盖进去。
            if (abilitySystemComponent.IsInitialized)
            {
                if (!abilitySystemComponent.TryApplyBaseValues(baseValues))
                    throw new InvalidOperationException($"CharacterActor '{name}' 的 ASC 已初始化，但无法应用角色等级 BaseValue。");
                return;
            }
            // 如果 ASC 未初始化，则需要进行初始化。
            if (!abilitySystemComponent.TryInitialize(instance.Config.InitialAttributeSets, baseValues))
                throw new InvalidOperationException($"CharacterActor '{name}' 的 ASC 等级属性初始化失败。");
        }

        #endregion

        #region 运行时绑定与阶段推进

        /// <summary>
        /// 在角色进入第一帧控制前完成 AttributeSet 导入与初始 Ability 授予。
        /// </summary>
        /// <remarks>
        /// PlayerController 在自己的 Start 中显式调用该方法，保证 ASC 完成初始化，
        /// 再组装可能读取 GAS Speed 的生命与 Locomotion 状态；CharacterActor.Start 会幂等兜底。
        /// </remarks>
        internal void InitializeFromInstance()
        {
            if (runtimeConfigurationInitialized) return;
            if (instance == null) throw new InvalidOperationException($"CharacterActor '{name}' 尚未绑定 CharacterInstance。");
            instance.Config.Validate();
            InitializeAttributesFromInstance();
            runtimeAttributeBinding = new CharacterRuntimeAttributeBinding(instance, abilitySystemComponent, progressionResolver);
            combatSystem.Initialize(abilitySystemComponent, instance.Config.CombatConfig, stateBlackboard);
            equipmentEffectBinding = new CharacterEquipmentEffectBinding(instance, abilitySystemComponent);
            lifeStateMachine = new CharacterLifeStateMachine(this, playerController, equipmentEffectBinding);
            actionArbiter = new CharacterActionArbiter(
                this,
                stateBlackboard,
                combatSystem,
                abilitySystemComponent,
                lifeStateMachine.Locomotion.GroundedJumpTransitionQueryService);
            hitReactionSystem = new CharacterHitReactionSystem(this, combatSystem);
            damageReceiverRegistration = CombatDamagePipeline.RegisterReceiver(abilitySystemComponent, this);
            runtimeConfigurationInitialized = true;
            lifeStateMachine.Initialize();
        }

        /// <summary>注销伤害接收并释放当前 Actor 的实例绑定、战斗运行时与表现资源。</summary>
        internal void StopRuntime()
        {
            if (runtimeStopped)
                return;

            // 先封闭外部伤害回调，再退出上场状态并逐个释放依赖，防止清理期间再次进入受击流程。
            runtimeStopped = true;
            damageReceiverRegistration?.Dispose();
            damageReceiverRegistration = null;
            if (isActiveCharacter)
                OnExit();

            lifeStateMachine?.Dispose();
            lifeStateMachine = null;

            weaponPresentationController.PresentationModelChanged -= HandleWeaponPresentationModelChanged;
            weaponPresentationController.StopRuntime();
            hitReactionSystem?.Dispose();
            hitReactionSystem = null;
            equipmentEffectBinding?.Dispose();
            equipmentEffectBinding = null;
            runtimeAttributeBinding?.Dispose();
            runtimeAttributeBinding = null;
            actionArbiter?.Dispose();
            actionArbiter = null;
            runtimeConfigurationInitialized = false;
            instance = null;
            if (animator != null) animator.enabled = false;
            if (presentationRenderers == null) presentationRenderers = GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < presentationRenderers.Length; index++)
                if (presentationRenderers[index] != null) presentationRenderers[index].forceRenderingOff = true;
            Debug.Log($"[CharacterActor] 已停止角色运行时并解除实例绑定，actor={name}。");
        }

        /// <summary>接收伤害管线按本角色 ASC 直接路由的伤害结算结果。</summary>
        /// <param name="result">含 GE 输出与可选本次命中快照的 Gameplay 结果。</param>
        public void ReceiveDamage(CombatDamageResult result)
        {
            if (!runtimeConfigurationInitialized || hitReactionSystem == null)
                throw new InvalidOperationException($"CharacterActor '{name}' 尚未准备好接收伤害反馈。");
            if (result == null || result.EffectResult.Target != abilitySystemComponent)
                throw new InvalidOperationException($"CharacterActor '{name}' 收到其他 ASC 的伤害结果。");

            hitReactionSystem.ReceiveDamage(result);
        }

        /// <summary>将该 Actor 设置为当前操控角色，并把进入生命周期交给生命 FSM。</summary>
        /// <exception cref="InvalidOperationException">角色运行时尚未初始化或切换顺序违反 Manager 契约时抛出。</exception>
        internal void OnEnter()
        {
            if (isActiveCharacter)
                return;
            if (!runtimeConfigurationInitialized)
                throw new InvalidOperationException($"CharacterActor '{name}' 尚未初始化，不能进入操控状态。");

            // 先切换共享 MotionDriver Owner，再由当前生命状态决定是否启动其子状态。
            bool allowControl = playerController.PrepareCharacterEntry(this);
            isActiveCharacter = true;
            try
            {
                SetActivePresentation(true);
                lifeStateMachine.OnActorEntered(allowControl);
                Debug.Log(
                    $"[CharacterActor] 角色进入操控状态，character={CharacterId}, isDead={lifeStateMachine.IsDead}, controlAllowed={allowControl}。",
                    this);
            }
            catch
            {
                // 进入中途失败时归还刚切换的 Owner 与表现，避免 Manager 留下半激活角色。
                OnExit();
                throw;
            }
        }

        /// <summary>退出操控状态并释放本 Actor 持有的表现、Locomotion 与运动请求。</summary>
        internal void OnExit()
        {
            if (!isActiveCharacter)
                return;

            // 先让当前生命状态释放子状态资源，再清理 Owner 的剩余运动请求并隐藏角色表现。
            lifeStateMachine?.OnActorExited();
            isActiveCharacter = false;
            if (playerController != null)
                playerController.ReleaseCharacterControl(this);
            combatSystem.ResetCombo();
            SetActivePresentation(false);
            Debug.Log($"[CharacterActor] 角色退出操控状态，character={CharacterId}。", this);
        }

        /// <summary>绑定角色实例和运动依赖，并初始化武器表现所需的数据源。</summary>
        /// <param name="root">所有队伍角色共享的空间根节点。</param>
        /// <param name="driver">GAS 与 Locomotion 共用的请求接口。</param>
        /// <param name="controller">负责驱动当前角色 AnimatorMove 的稳定 PlayerController。</param>
        /// <param name="blackboard">所有队伍角色共享的 PlayerStateBlackboard。</param>
        /// <param name="characterInstance">角色进度和装备关系的唯一权威实例。</param>
        /// <param name="rosterManager">提供最新角色实例的 Roster Manager。</param>
        /// <param name="weaponInventoryManager">按装备实例 ID 查询武器定义的库存 Manager。</param>
        internal void BindRuntime(
            Transform root,
            IMotionDriver driver,
            PlayerController controller,
            PlayerStateBlackboard blackboard,
            CharacterInstance characterInstance,
            CharacterRosterManager rosterManager,
            WeaponInventoryManager weaponInventoryManager)
        {
            characterRoot = root;
            motionDriver = driver;
            playerController = controller ?? throw new ArgumentNullException(nameof(controller));
            stateBlackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
            if (config == null) throw new InvalidOperationException($"CharacterActor '{name}' 未配置 CharacterConfig。");
            instance = characterInstance ?? throw new ArgumentNullException(nameof(characterInstance));
            if (instance.Config.CharacterId != config.CharacterId)
                throw new InvalidOperationException($"CharacterActor '{name}' 的 Prefab Config 与 CharacterInstance 不一致。");
            if (rosterManager == null) throw new ArgumentNullException(nameof(rosterManager));
            if (weaponInventoryManager == null) throw new ArgumentNullException(nameof(weaponInventoryManager));
            runtimeStopped = false;
            weaponPresentationController.PresentationModelChanged += HandleWeaponPresentationModelChanged;
            weaponPresentationController.BindRuntime(instance, rosterManager, weaponInventoryManager);
            SetActivePresentation(false);
            Debug.Log($"[CharacterActor] 已绑定角色运行时依赖，character={instance.CharacterId}。");
        }

        /// <summary>等待装备权威状态对应的武器模型和技能 Marker 完成首次同步。</summary>
        /// <returns>武器表现初始化任务。</returns>
        internal UniTask InitializeWeaponPresentationAsync() =>
            weaponPresentationController.InitializeWeaponPresentationAsync();

        /// <summary>在当前表现状态不变的隐藏窗口内求值 Idle 初始姿态。</summary>
        internal void PrimeIdlePose()
        {
            EnsureDependencies();
            bool previousAnimatorEnabled = animator.enabled;
            Renderer[] renderers = presentationRenderers ??= GetComponentsInChildren<Renderer>(true);
            bool[] previousRendererVisibility = new bool[renderers.Length];
            for (int index = 0; index < renderers.Length; index++)
            {
                previousRendererVisibility[index] = renderers[index].forceRenderingOff;
                renderers[index].forceRenderingOff = true;
            }

            suppressAnimatorMotion = true;
            try
            {
                animator.enabled = true;
                // 姿态预热只求值 Idle Transition，不调用 FSM Enter，因此不会建立 Locomotion 控制请求。
                animationController.PrimeInitialPose(LifeStateMachine.Locomotion.IdleTransition);
            }
            finally
            {
                animator.enabled = previousAnimatorEnabled;
                for (int index = 0; index < renderers.Length; index++)
                    renderers[index].forceRenderingOff = previousRendererVisibility[index];
                suppressAnimatorMotion = false;
            }
        }

        /// <summary>接收同节点 Animator 的根运动增量并交给稳定 PlayerController 统一结算。</summary>
        private void OnAnimatorMove()
        {
            if (suppressAnimatorMotion || playerController == null) return;
            playerController.ProcessAnimatorMotion(this, animator.deltaPosition, animator.deltaRotation);
        }

        /// <summary>只切换角色视觉表现，不停用承载 ASC 的节点。</summary>
        /// <param name="active">该角色是否成为玩家当前操控对象。</param>
        private void SetActivePresentation(bool active)
        {
            EnsureDependencies();
            // ASC 所在对象不能 SetActive(false)，否则后台能力与事件生命周期会被截断。
            presentationRenderers ??= GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in presentationRenderers)
                if (renderer != null) renderer.forceRenderingOff = !active;
            animator.enabled = active;
        }

        /// <summary>武器层级替换后重建渲染器缓存，避免后台角色的新模型闪现。</summary>
        private void HandleWeaponPresentationModelChanged()
        {
            presentationRenderers = GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in presentationRenderers)
                if (renderer != null) renderer.forceRenderingOff = !isActiveCharacter;
        }

        /// <summary>推进普通 ASC 阶段；后台角色也会执行。</summary>
        /// <param name="deltaTime">本帧缩放时间。</param>
        internal void TickAbility(float deltaTime) => abilitySystemComponent.Tick(deltaTime);

        /// <summary>先推进连段时间，再按技能槽、普通攻击、Jump、Move 顺序仲裁输入。</summary>
        /// <param name="inputRequests">提供战斗 Request 的输入缓冲区。</param>
        /// <param name="deltaTime">本帧缩放时间。</param>
        internal void AdvanceActionFrame(IPlayerInputRequestBuffer inputRequests, float deltaTime)
        {
            combatSystem.AdvanceFrame(deltaTime);
            actionArbiter.ArbitrateFrame(inputRequests);
        }

        /// <summary>推进当前角色 ASC 物理阶段。</summary>
        /// <param name="fixedDeltaTime">本物理步时长。</param>
        internal void FixedTickAbility(float fixedDeltaTime) => abilitySystemComponent.FixedTick(fixedDeltaTime);

        /// <summary>推进 Animator 求值后的能力阶段。</summary>
        internal void UpdateAnimationMoveAbility(Vector3 deltaPosition, Quaternion deltaRotation) =>
            abilitySystemComponent.UpdateAnimationMove(deltaPosition, deltaRotation);

        /// <summary>推进 ASC 延迟阶段。</summary>
        /// <param name="deltaTime">本帧缩放时间。</param>
        internal void LateTickAbility(float deltaTime) => abilitySystemComponent.LateTick(deltaTime);

        /// <summary>由生命状态机请求更新 Alive Locomotion 控制门禁。</summary>
        /// <param name="allowControl">是否允许角色运动状态推进。</param>
        internal void SetLifeStateControlAllowed(bool allowControl) => LifeStateMachine.SetControlAllowed(allowControl);

        /// <summary>转发生命状态变化事件，不在 Actor 中实现死亡状态业务。</summary>
        /// <param name="isDead">角色当前是否死亡。</param>
        internal void NotifyLifeStateChanged(bool isDead) => LifeStateChanged?.Invoke(this, isDead);

        /// <summary>转发死亡动画完成事件，供 CharacterManager 在安全帧阶段切换角色。</summary>
        internal void NotifyDeathAnimationCompleted() => DeathAnimationCompleted?.Invoke(this);

        #endregion
    }
}
