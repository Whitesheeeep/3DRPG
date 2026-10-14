using System;
using System.Collections.Generic;
using BehaviorDesigner.Runtime;
using RPG.Character;
using RPG.Character.Animation;
using RPG.Combat;
using RPG.Game.JumpText;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.AI;
using WS_Modules.CustomEventSystem;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.Generated;
using WS_Modules.GAS.GameplayAbilitySystem;
using WS_Modules.GAS.TAG;

namespace RPG.NPC
{
    /// <summary>独立驱动 NPC 的 ASC、Alive/Dead 状态与 CharacterController 位移结算。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController), typeof(NPCActor))]
    [InfoBox("依赖同节点 CharacterController、NPCActor、可选 NPCIdentity；启用行为树的 NPC 还必须配置同节点 NavMeshAgent、BehaviorTree/External Behavior 和已烘焙 NavMesh。安全瞬移与死亡清理会查询子级碰撞体。身份缺失时不会发布可供任务匹配的击败事件。")]
    public sealed class NPCController : MonoBehaviour, IFullBodyActionArbiter,
        IFullBodyActionRegistrationOwner, IGameplayAbilityTargetProvider, ICombatDamageReceiver, IDisposable
    {
        #region 配置与依赖字段

        // NavMeshAgent 会在 stoppingDistance 之外提前停下；路径目标略微进入攻击区以吸收该容差。
        private const float NavigationAttackRangeMargin = 0.15f;

        // 配置与同节点控制依赖
        [SerializeField, Required] private NPCConfig config;
        [SerializeField, MinValue(1), LabelText("生成等级")] private int generationLevel = 1;
        [SerializeField] private CharacterController characterController;
        [SerializeField] private NPCActor actor;
        [SerializeField] private NPCIdentity npcIdentity;
        [SerializeField] private NavMeshAgent navigationAgent;
        [SerializeField] private BehaviorTree behaviorTree;
        [SerializeField] private MotionDriver motionDriver = new();
        [SerializeField] private BossLocomotionStateMachine locomotion = new();

        #endregion

        #region 运行时依赖

        // ASC、Action 注册和 AI 运行系统由本组件在 Awake/Start 生命周期中建立和释放。
        private GameplayAbilitySystemComponent abilitySystemComponent;
        private IDisposable damageReceiverRegistration;
        private readonly NPCCombatSystem combatSystem = new();
        private NavMeshPath navigationPath;
        private Collider[] physicsQueryBuffer = new Collider[32];
        private RaycastHit[] raycastHitBuffer = new RaycastHit[32];
        private GameplayAbilityRuntime fullBodyRuntime;
        private MotionControlHandle navigationMotionHandle;
        private Transform navigationTarget;
        private float navigationStopDistance;
        private float nextNavigationRefreshTime;
        private Transform teleportRetryTarget;
        private float teleportRetryUntilTime;
        private bool hasTeleportPrecheckResult;
        private bool lastTeleportPrecheckAllowed;
        private float lastTeleportPrecheckPathDistance = float.PositiveInfinity;
        private string lastTeleportPrecheckReason = "尚未执行预检。";
        private Vector3 pendingTeleportPosition;
        private Quaternion pendingTeleportRotation;
        private Transform pendingTeleportTarget;
        private bool hasPendingTeleport;
        private int lastTeleportActivationId = -1;
        private bool lastTeleportSucceeded;
        private int nextRegistrationId;
        private int currentRegistrationId;
        private bool initialized;
        private bool disposed;
        private bool destroying;
        private int lastAnimatorMoveFrame = -1;
        private bool deathEventPublished;
        private bool deathCompletionProcessed;
        private bool missingJumpTextWarningIssued;
        private Transform lastAttackReadinessTarget;
        private string lastAttackReadinessReason = "尚未判定。";
        private float lastAttackReadinessDistance = float.PositiveInfinity;
        private string lastNormalAttackActivationBlock = "尚未尝试。";

        #endregion

        #region 查询属性

        /// <summary>获取 Controller 所属 NPC。</summary>
        public NPCActor Actor => actor;

        /// <summary>获取该 NPC 初始化时使用的 NPCConfig。</summary>
        public NPCConfig Config => config;

        /// <summary>获取该 NPC 实例在生成时选定的等级。</summary>
        public int Level => generationLevel;

        /// <summary>获取驱动所有 NPC 能力和移动状态的 MotionDriver。</summary>
        public IMotionDriver MotionDriver => motionDriver;

        /// <summary>获取所属 NPC 的动画播放器，供状态机播放基础移动动画。</summary>
        public IAnimationPlayer AnimationPlayer => actor.AnimationPlayer;

        /// <summary>获取由本组件直接维护的 FullBody Action 占据契约。</summary>
        public IFullBodyActionArbiter FullBodyActionArbiter => this;

        /// <summary>获取 NPC Alive/Dead 与 Idle/Move 分层状态机。</summary>
        public BossLocomotionStateMachine Locomotion => locomotion;

        /// <summary>获取该 NPC 是否被 FullBody Ability 占据。</summary>
        public bool IsFullBodyActionOccupied => fullBodyRuntime != null &&
            fullBodyRuntime.State == GameplayAbilityRuntimeState.Active;

        /// <summary>获取 NPC 普通攻击连段缓存和当前攻击 Runtime。</summary>
        public NPCCombatSystem CombatSystem => combatSystem;

        /// <summary>获取 NPC 运行时是否已完成属性、技能和状态机初始化。</summary>
        public bool IsInitialized => initialized;

        /// <inheritdoc />
        public GameplayAbilitySystemComponent TryGetTargetASC()
        {
            if (disposed || !initialized || !isActiveAndEnabled || abilitySystemComponent == null ||
                !abilitySystemComponent.IsInitialized)
                return null;

            return abilitySystemComponent;
        }

        /// <summary>通过 ASC 精确查询该 NPC 是否持有不可逆死亡 Tag。</summary>
        public bool IsDead =>
            abilitySystemComponent != null &&
            abilitySystemComponent.HasTagExact(GameplayTags.Tag_State_Dead);

        /// <summary>获取最近一次瞬移预检采用的完整 NavMesh 路径距离。</summary>
        public float LastTeleportPrecheckPathDistance => lastTeleportPrecheckPathDistance;

        /// <summary>获取最近一次瞬移预检的通过状态。</summary>
        public bool LastTeleportPrecheckAllowed => lastTeleportPrecheckAllowed;

        /// <summary>获取最近一次瞬移预检的诊断原因。</summary>
        public string LastTeleportPrecheckReason => lastTeleportPrecheckReason;

        /// <summary>获取当前瞬移失败退避的剩余缩放时间。</summary>
        public float TeleportRetryRemaining => Mathf.Max(0f, teleportRetryUntilTime - Time.time);

        /// <summary>获取攻击准入最近一次变化原因，供 Odin 观察停顿发生在哪个条件。</summary>
        public string LastAttackReadinessReason => lastAttackReadinessReason;

        /// <summary>获取攻击准入最近一次计算的水平距离。</summary>
        public float LastAttackReadinessDistance => lastAttackReadinessDistance;

        /// <summary>获取最近一次普通攻击激活失败的阻断快照。</summary>
        public string LastNormalAttackActivationBlock => lastNormalAttackActivationBlock;

        #endregion

        #region 初始化事件

        /// <summary>等级属性、资源、技能及运行系统全部初始化后同步发送。</summary>
        public event Action<NPCController> Initialized;

        #endregion

        #region Unity 生命周期与初始化

        // Unity 生命周期：NPC Controller 先建立运行依赖，再在 Start 导入资产配置。
        /// <summary>解析同节点角色与运动依赖，并先建立 FullBody Action 所有者。</summary>
        private void Awake()
        {
            if (characterController == null)
                characterController = GetComponent<CharacterController>();
            if (actor == null)
                actor = GetComponent<NPCActor>();
            if (npcIdentity == null)
                npcIdentity = GetComponent<NPCIdentity>();
            if (navigationAgent == null)
                navigationAgent = GetComponent<NavMeshAgent>();
            if (behaviorTree == null)
                behaviorTree = GetComponent<BehaviorTree>();
            if (characterController == null || actor == null)
            {
                Debug.LogError(
                    $"[NPCController] '{name}' 缺少同节点 CharacterController 或 NPCActor，无法建立运行时依赖。",
                    this);
                throw new InvalidOperationException(
                    $"NPCController '{name}' 必须与 CharacterController 和 NPCActor 挂在同一 GameObject。");
            }

            // NavMeshPath 包含 Unity 原生状态，必须在 MonoBehaviour 生命周期内创建，不能放在字段初始化器中。
            navigationPath = new NavMeshPath();
            Debug.Log($"[NPCController] '{name}' 已初始化 NavMeshPath 路径缓存。", this);

            motionDriver.Initialize(characterController);
            abilitySystemComponent = actor.AbilitySystemComponent;
            motionDriver.SetActiveOwner(actor, abilitySystemComponent);
            motionDriver.Resume();
            if (behaviorTree != null)
            {
                behaviorTree.StartWhenEnabled = false;
                behaviorTree.RestartWhenComplete = true;
                behaviorTree.ResetValuesOnRestart = false;
                behaviorTree.DisableBehavior(false);
            }
        }

        /// <summary>导入 NPC 属性、授予配置技能并进入 Idle 动画状态。</summary>
        private void Start()
        {
            InitializeRuntime();
            if (config.UseBehaviorTree && isActiveAndEnabled)
                StartBehaviorTree();
        }

        /// <summary>恢复已初始化 NPC 的行为树；重新启用不会重建连段缓存。</summary>
        private void OnEnable()
        {
            if (initialized && !IsDead && config != null && config.UseBehaviorTree)
                StartBehaviorTree();
        }

        /// <summary>停用时立即停止行为树导航，避免后台实例继续提交运动。</summary>
        private void OnDisable()
        {
            if (!initialized)
                return;
            StopBehaviorTree("NPC disabled");
            StopMove();
        }

        /// <summary>推进 NPC Ability、状态机及 Update 位移结算。</summary>
        private void Update()
        {
            if (!initialized)
                return;

            try
            {
                if (!IsDead)
                    abilitySystemComponent.Tick(Time.deltaTime);
                // 根状态机每帧检查死亡 Transition；死亡 Tag 会立即停止活体 Ability 更新。
                locomotion.Tick();
                combatSystem.AdvanceFrame(Time.deltaTime);
                if (IsDead || actor.IsActionPaused)
                    motionDriver.ClearTransientRequests();
                else
                {
                    UpdateNavigationMotion(Time.deltaTime);
                    motionDriver.ResolveUpdateMotion();
                    SynchronizeNavigationPosition();
                }
            }
            catch
            {
                motionDriver.ClearTransientRequests();
                throw;
            }
        }

        /// <summary>推进 NPC Ability 物理阶段并统一结算物理位移。</summary>
        private void FixedUpdate()
        {
            if (!initialized)
                return;
            if (IsDead || actor.IsActionPaused) return;

            try
            {
                abilitySystemComponent.FixedTick(Time.fixedDeltaTime);
                locomotion.FixedTick();
                motionDriver.ResolveFixedMotion();
            }
            catch
            {
                motionDriver.ClearTransientRequests();
                throw;
            }
        }

        /// <summary>推进 NPC Ability 与 Boss 状态机的延迟阶段。</summary>
        private void LateUpdate()
        {
            if (!initialized)
                return;
            if (!IsDead)
                abilitySystemComponent.LateTick(Time.deltaTime);
            if (!IsDead && !actor.IsActionPaused)
                locomotion.LateTick();
        }

        /// <summary>释放动作占据、导航控制权和可能位于未激活子层级中的动画 Graph。</summary>
        private void OnDestroy()
        {
            destroying = true;
            locomotion?.Deactivate();
            if (abilitySystemComponent != null)
                abilitySystemComponent.AttributeChanged -= OnAttributeChanged;
            StopBehaviorTree("NPC destroyed");
            StopMove();
            Dispose();
            if (actor != null)
            {
                motionDriver.ReleaseAll(actor);
                actor.ReleaseAnimationRuntime();
            }
            motionDriver.Suspend();
            Debug.Log($"[NPCController] NPC '{name}' 已释放控制器运行时资源。", this);
        }

        // 配置初始化
        /// <summary>校验等级烘焙输入，依序初始化 ASC 属性、Resource、Ability 与 Locomotion。</summary>
        private void InitializeRuntime()
        {
            if (initialized)
                return;
            if (config == null)
                throw CreateInitializationFailure("未配置 NPCConfig。");

            Debug.Log($"[NPCController] 开始初始化 NPC '{name}'，level={generationLevel}。", this);
            try
            {
                config.Validate();
            }
            catch (InvalidOperationException exception)
            {
                throw CreateInitializationFailure(
                    $"NPCConfig '{config.name}' 校验失败：{exception.Message}",
                    exception);
            }
            if (generationLevel < 1 || generationLevel > config.GrowthProfile.MaxLevel)
                throw CreateInitializationFailure(
                    $"生成等级 {generationLevel} 超出 NPCGrowthProfile 范围 1–{config.GrowthProfile.MaxLevel}。");
            if (!ReferenceEquals(abilitySystemComponent.Owner, actor))
                throw CreateInitializationFailure("ASC Owner 不是同节点 NPCActor。");

            // 在导入 AttributeSet 前取出固定等级快照，避免初始化过程隐式读取曲线或等级状态。
            IReadOnlyList<GameplayAttributeValue> initialBaseValues = config.ResolveBaseValues(generationLevel);
            if (!abilitySystemComponent.IsInitialized)
            {
                abilitySystemComponent.Initialize(config.InitialAttributeSets, config.ActivationRules);
                if (!abilitySystemComponent.IsInitialized)
                    throw CreateInitializationFailure(
                        $"无法导入 NPCConfig '{config.name}' 的 AttributeSet。");
            }
            if (!abilitySystemComponent.TryApplyBaseValues(initialBaseValues))
                throw CreateInitializationFailure(
                    $"无法将 level={generationLevel} 的全部烘焙 BaseValue 应用到 ASC。");

            InitializeResourceCurrentValues();
            abilitySystemComponent.AttributeChanged += OnAttributeChanged;

            GrantConfiguredAbilities();
            locomotion.Initialize(this, config.IdleTransition, config.MoveTransition, new NPCDeadState());
            initialized = true;
            if (config.UseBehaviorTree)
            {
                InitializeNavigation();
                combatSystem.Initialize(
                    this,
                    config.NormalAttackAbilities,
                    config.NormalAttackComboRetention);
            }
            damageReceiverRegistration = CombatDamagePipeline.RegisterReceiver(abilitySystemComponent, this);
            Debug.Log(
                $"[NPCController] NPC '{name}' 初始化完成，AttributeSets={config.InitialAttributeSets.Count}，" +
                $"Abilities={config.GrantedAbilities.Count}，State={locomotion.CurrentState}。",
                this);
            // UI 绑定者可能比 NPC 晚启用；事件只在所有 ASC、技能与运行系统就绪后发布。
            Initialized?.Invoke(this);
        }

        /// <summary>在等级 BaseValue 应用后按资源规则设置全部 Resource CurrentValue。</summary>
        private void InitializeResourceCurrentValues()
        {
            IReadOnlyList<CharacterResourceRule> resourceRules = config.ResourceRules;
            var initialResourceValues = new List<GameplayAttributeValue>();
            for (int setIndex = 0; setIndex < config.InitialAttributeSets.Count; setIndex++)
            {
                IReadOnlyList<GameplayAttributeDefinition> definitions =
                    config.InitialAttributeSets[setIndex].Definitions;
                for (int definitionIndex = 0; definitionIndex < definitions.Count; definitionIndex++)
                {
                    GameplayAttributeDefinition definition = definitions[definitionIndex];
                    if (definition.Type != GameplayAttributeType.Resource)
                        continue;

                    CharacterResourceRule rule = null;
                    for (int ruleIndex = 0; ruleIndex < resourceRules.Count; ruleIndex++)
                    {
                        if (resourceRules[ruleIndex].ResourceAttribute.Id != definition.Attribute.Id)
                            continue;
                        rule = resourceRules[ruleIndex];
                        break;
                    }

                    float currentValue;
                    if (rule != null && rule.InitialValueMode == CharacterResourceInitialValueMode.FullCapacity)
                    {
                        if (!abilitySystemComponent.TryGetCurrentValue(rule.CapacityAttribute, out currentValue))
                            throw CreateInitializationFailure(
                                $"找不到资源 {definition.Attribute} 的容量 Attribute {rule.CapacityAttribute}。");
                    }
                    else
                    {
                        // 未配置专用规则或选用 DefinitionDefault 时，恢复资源定义自身的生成默认值。
                        currentValue = definition.DefaultValue;
                    }

                    if (rule != null && rule.CapacityAttribute.IsValid)
                    {
                        if (!abilitySystemComponent.TryGetCurrentValue(rule.CapacityAttribute, out float capacity))
                            throw CreateInitializationFailure(
                                $"找不到资源 {definition.Attribute} 的容量 Attribute {rule.CapacityAttribute}。");
                        currentValue = Mathf.Min(currentValue, capacity);
                    }

                    initialResourceValues.Add(new GameplayAttributeValue(definition.Attribute, currentValue));
                }
            }

            if (initialResourceValues.Count > 0 &&
                !abilitySystemComponent.TrySetResourceCurrentValues(initialResourceValues))
                throw CreateInitializationFailure(
                    $"无法提交 {initialResourceValues.Count} 个 Resource CurrentValue。");

            Debug.Log(
                $"[NPCController] NPC '{name}' 完成等级属性与资源初始化，level={generationLevel}, resources={initialResourceValues.Count}。",
                this);
        }

        /// <summary>为 NPC 授予 NPCConfig 中尚未存在的 Ability Spec。</summary>
        private void GrantConfiguredAbilities()
        {
            var grantCandidates = new List<GameplayAbilityData>(
                config.GrantedAbilities.Count + config.NormalAttackAbilities.Count + 1);
            var uniqueAbilities = new HashSet<GameplayAbilityData>();
            AddConfiguredAbilities(config.GrantedAbilities, grantCandidates, uniqueAbilities);
            AddConfiguredAbilities(config.NormalAttackAbilities, grantCandidates, uniqueAbilities);
            if (config.TeleportAbility != null && uniqueAbilities.Add(config.TeleportAbility))
                grantCandidates.Add(config.TeleportAbility);

            for (int abilityIndex = 0; abilityIndex < grantCandidates.Count; abilityIndex++)
            {
                GameplayAbilityData ability = grantCandidates[abilityIndex];
                bool alreadyGranted = false;
                IReadOnlyList<GameplayAbilitySpec> currentSpecs = abilitySystemComponent.GrantedAbilities;
                for (int specIndex = 0; specIndex < currentSpecs.Count; specIndex++)
                {
                    if (!ReferenceEquals(currentSpecs[specIndex].Data, ability))
                        continue;
                    alreadyGranted = true;
                    break;
                }

                if (alreadyGranted)
                    continue;

                GameplayAbilityHandle handle = abilitySystemComponent.GiveAbility(ability, 1);
                if (!handle.IsValid)
                    throw CreateInitializationFailure(
                        $"无法授予配置 Ability '{ability.name}'。");
                Debug.Log(
                    $"[NPCController] NPC '{name}' 授予 Ability '{ability.Name}'，Handle={handle}。",
                    this);
            }
        }

        /// <summary>将尚未登记的配置技能追加到唯一授予候选表。</summary>
        /// <param name="sourceAbilities">来自一个配置来源的技能列表。</param>
        /// <param name="grantCandidates">最终授予列表。</param>
        /// <param name="uniqueAbilities">用于避免同一资产重复授予的集合。</param>
        private static void AddConfiguredAbilities(
            IReadOnlyList<GameplayAbilityData> sourceAbilities,
            List<GameplayAbilityData> grantCandidates,
            HashSet<GameplayAbilityData> uniqueAbilities)
        {
            for (int index = 0; index < sourceAbilities.Count; index++)
            {
                GameplayAbilityData ability = sourceAbilities[index];
                if (ability != null && uniqueAbilities.Add(ability))
                    grantCandidates.Add(ability);
            }
        }

        /// <summary>记录初始化失败的 NPC 与输入上下文，并创建保留原始异常的契约错误。</summary>
        /// <param name="reason">导致初始化无法继续的具体原因。</param>
        /// <param name="innerException">底层配置校验异常；直接失败时为空。</param>
        /// <returns>包含 NPC 上下文的初始化异常。</returns>
        private InvalidOperationException CreateInitializationFailure(
            string reason,
            Exception innerException = null)
        {
            string message = $"NPCController '{name}' 初始化失败：{reason}";
            Debug.LogError($"[NPCController] {message}", this);
            return new InvalidOperationException(message, innerException);
        }

        #endregion

        #region 测试与技能入口

        // Locomotion 状态入口
        /// <summary>请求进入 Idle 或 Move 状态；被 FullBody 技能占据时拒绝切换。</summary>
        /// <param name="stateId">测试或后续 AI 选择的目标状态。</param>
        /// <returns>状态成功切换时返回 true。</returns>
        public bool TrySetLocomotionState(E_NPCStateId stateId)
        {
            if (!initialized)
                throw new InvalidOperationException($"NPCController '{name}' 尚未初始化。");
            if (IsDead)
            {
                Debug.Log($"[NPCController] NPC '{name}' 已死亡，拒绝切换到 {stateId}。", this);
                return false;
            }
            if (IsFullBodyActionOccupied)
            {
                Debug.Log(
                    $"[NPCController] NPC '{name}' 仍被 FullBody Ability 占据，拒绝切换到 {stateId}。",
                    this);
                return false;
            }

            // Move Mixer 的采样位置要在进入 Transition 前写入，避免首帧先播放 Idle 混合点。
            if (stateId == E_NPCStateId.Move)
                SetMoveBlend(Vector3.forward);

            bool changed = locomotion.TryChangeState(stateId);
            if (changed)
                Debug.Log($"[NPCController] NPC '{name}' 状态切换为 {stateId}。", this);
            return changed;
        }

        // Ability 入口与取消
        /// <summary>激活 NPCConfig 已授予的指定 Ability。</summary>
        /// <param name="ability">NPCConfig 中的技能资产。</param>
        /// <param name="runtime">成功时返回新建 Ability Runtime。</param>
        /// <returns>技能成功激活时返回 true。</returns>
        public bool TryActivateAbility(GameplayAbilityData ability, out GameplayAbilityRuntime runtime)
        {
            if (!initialized)
                throw new InvalidOperationException($"NPCController '{name}' 尚未初始化。");
            if (ability == null)
                throw new ArgumentNullException(nameof(ability));
            if (IsDead)
            {
                runtime = null;
                Debug.Log($"[NPCController] NPC '{name}' 已死亡，拒绝激活 Ability '{ability.Name}'。", this);
                return false;
            }
            if (IsFullBodyActionOccupied)
            {
                runtime = null;
                Debug.Log(
                    $"[NPCController] NPC '{name}' 已被 FullBody Ability 占据，拒绝激活 '{ability.Name}'。",
                    this);
                return false;
            }

            IReadOnlyList<GameplayAbilitySpec> specs = abilitySystemComponent.GrantedAbilities;
            for (int index = 0; index < specs.Count; index++)
            {
                GameplayAbilitySpec spec = specs[index];
                if (!ReferenceEquals(spec.Data, ability))
                    continue;

                bool activated = abilitySystemComponent.TryActivateAbility(spec.Handle, out runtime);
                Debug.Log(
                    $"[NPCController] NPC '{name}' 激活 Ability '{ability.Name}'，成功={activated}。",
                    this);
                return activated;
            }

            runtime = null;
            Debug.LogWarning(
                $"[NPCController] NPC '{name}' 未授予 Ability '{ability.Name}'，无法激活。",
                this);
            return false;
        }

        /// <summary>取消该 NPC 当前所有 Active Ability，供场景 OdinTester 验证清理。</summary>
        public void CancelActiveAbilities()
        {
            IReadOnlyList<GameplayAbilityRuntime> activeAbilities = abilitySystemComponent.ActiveAbilities;
            int cancelledCount = 0;
            for (int index = activeAbilities.Count - 1; index >= 0; index--)
            {
                if (abilitySystemComponent.TryCancelAbility(activeAbilities[index]))
                    cancelledCount++;
            }
            Debug.Log(
                $"[NPCController] NPC '{name}' 批量取消 Active Ability，成功数量={cancelledCount}。",
                this);
        }

        #endregion

        #region 死亡事实与清理

        /// <inheritdoc />
        public void ReceiveDamage(CombatDamageResult result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            if (!ReferenceEquals(result.EffectResult.Target, abilitySystemComponent))
                throw new InvalidOperationException(
                    $"NPCController '{name}' 收到不属于自身 ASC 的 Gameplay 伤害结果。");

            JumpTextManager manager = JumpTextManager.Instance;
            if (manager == null)
            {
                if (!missingJumpTextWarningIssued)
                {
                    missingJumpTextWarningIssued = true;
                    Debug.LogWarning(
                        $"[NPCController] NPC '{name}' 已结算伤害，但场景中没有 JumpTextManager，跳过伤害跳字。",
                        this);
                }
                return;
            }

            Vector3 worldPosition = result.HitSnapshot.HasValue
                ? result.HitSnapshot.Value.HitPoint
                : actor.RootTransform.position + Vector3.up;
            manager.ShowDamage(result.FinalDamage, worldPosition, result.IsCritical);
            Debug.Log(
                $"[NPCController] NPC '{name}' 已发布伤害跳字，damage={result.FinalDamage:F1}，" +
                $"critical={result.IsCritical}，hasHitPoint={result.HitSnapshot.HasValue}，position={worldPosition}。",
                this);
        }

        /// <summary>只在 Health 从正数降至非正数时为 ASC 添加死亡 Tag。</summary>
        /// <param name="attribute">变化的属性。</param>
        /// <param name="oldValue">变化前的当前值。</param>
        /// <param name="newValue">变化后的当前值。</param>
        private void OnAttributeChanged(GameplayAttribute attribute, float oldValue, float newValue)
        {
            if (attribute != GameplayAttributes.Attribute_Health || oldValue <= 0f || newValue > 0f || IsDead)
                return;

            if (abilitySystemComponent.AddLooseGameplayTag(GameplayTags.Tag_State_Dead))
                Debug.Log($"[NPCController] NPC '{name}' Health 归零，已添加 State.Dead，等待根状态机进入 Dead。", this);
        }

        /// <summary>执行死亡入口战斗清理、击败事件发布和碰撞关闭。</summary>
        internal void BeginDeath()
        {
            if (!IsDead)
                throw new InvalidOperationException($"NPCController '{name}' 在持有 State.Dead 前进入 Dead。");

            StopBehaviorTree("death");
            abilitySystemComponent.AddLooseGameplayTag(GameplayTags.Tag_State_Block_AbilityActivation);
            int cancelledAbilityCount = abilitySystemComponent.ForceCancelAllAbilities();
            motionDriver.ClearTransientRequests();
            motionDriver.ReleaseAll(actor);
            motionDriver.Suspend();
            DisableDeathColliders();
            actor.AnimationPlayer.StopLayer(AnimationLayerType.Action);
            actor.AnimationPlayer.StopLayer(AnimationLayerType.UpperBody);
            actor.AnimationPlayer.StopLayer(AnimationLayerType.Additive);

            NPCId defeatedNpcId = default;
            if (npcIdentity != null)
            {
                defeatedNpcId = npcIdentity.Id;
                npcIdentity.UnregisterForDeath();
                if (!deathEventPublished)
                {
                    deathEventPublished = true;
                    try
                    {
                        EventSystem.EventTrigger_Type(
                            typeof(NPCDefeatedEventArgs),
                            new NPCDefeatedEventArgs(defeatedNpcId));
                    }
                    catch (Exception exception)
                    {
                        // 外部任务订阅者异常不能阻断死亡动画与 NPC 清理；击败事实只尝试发布一次。
                        Debug.LogError(
                            $"[NPCController] NPC '{name}' 已死亡，但 NPCDefeatedEventArgs 发布失败，npcId={defeatedNpcId}。",
                            this);
                        Debug.LogException(exception, this);
                    }
                }
            }

            Debug.Log(
                $"[NPCController] NPC '{name}' 进入 Dead，npcId={(npcIdentity != null ? defeatedNpcId.ToString() : "<none>")}，" +
                $"cancelledAbilities={cancelledAbilityCount}。",
                this);
        }

        /// <summary>在死亡动画自然结束后生成一次配置出口；不负责切换场景或销毁 Boss。</summary>
        internal void CompleteDeath()
        {
            if (deathCompletionProcessed)
                return;
            deathCompletionProcessed = true;
            if (config.DeathCompletionPrefab == null)
            {
                Debug.Log($"[NPCController] NPC '{name}' 死亡动画完成，无死亡出口 Prefab 配置。", this);
                return;
            }

            GameObject entry = Instantiate(
                config.DeathCompletionPrefab,
                transform.position,
                Quaternion.identity);
            Debug.Log(
                $"[NPCController] NPC '{name}' 死亡动画完成，在位置 {transform.position} 生成返回入口 '{entry.name}'。",
                entry);
        }

        /// <summary>关闭 NPC 根层级下所有碰撞体，阻止死亡期间继续参与交互与物理碰撞。</summary>
        private void DisableDeathColliders()
        {
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            int disabledCount = 0;
            for (int index = 0; index < colliders.Length; index++)
            {
                if (!colliders[index].enabled)
                    continue;
                colliders[index].enabled = false;
                disabledCount++;
            }
            Debug.Log($"[NPCController] NPC '{name}' 死亡碰撞体已关闭，count={disabledCount}。", this);
        }

        #endregion

        #region FullBody Action 注册与生命周期

        /// <summary>为新启动的 FullBody Runtime 建立唯一占据并立即让出导航控制。</summary>
        /// <param name="runtime">已成功启动表现的 Ability Runtime。</param>
        /// <returns>用于幂等释放占据的生命周期 Handle。</returns>
        public FullBodyActionHandle RegisterFullBodyAction(GameplayAbilityRuntime runtime)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(NPCController));
            if (runtime == null)
                throw new ArgumentNullException(nameof(runtime));
            if (fullBodyRuntime != null && fullBodyRuntime.State == GameplayAbilityRuntimeState.Active)
                throw new InvalidOperationException(
                    $"NPC '{name}' 已被 FullBody Ability ActivationId={fullBodyRuntime.ActivationId} 占据。");

            fullBodyRuntime = runtime;
            currentRegistrationId = ++nextRegistrationId;
            StopMove();
            if (!IsDead && locomotion.CurrentState == E_NPCStateId.Move)
                locomotion.TryChangeState(E_NPCStateId.Idle);
            Debug.Log(
                $"[NPCController] NPC '{name}' 注册 FullBody Ability，ActivationId={runtime.ActivationId}，导航已停止。",
                this);
            return new FullBodyActionHandle(this, currentRegistrationId);
        }

        /// <summary>按注册编号释放 FullBody 占据，忽略已失效或过期 Handle。</summary>
        /// <param name="registrationId">创建 Handle 时分配的单调编号。</param>
        void IFullBodyActionRegistrationOwner.UnregisterFullBodyAction(int registrationId)
        {
            if (fullBodyRuntime == null || currentRegistrationId != registrationId)
                return;

            int activationId = fullBodyRuntime.ActivationId;
            fullBodyRuntime = null;
            currentRegistrationId = 0;
            Debug.Log(
                $"[NPCController] NPC '{name}' 释放 FullBody Ability 占据，ActivationId={activationId}。",
                this);
        }

        /// <summary>清除导航、普攻 Phase 订阅与 FullBody Action 注册状态。</summary>
        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            damageReceiverRegistration?.Dispose();
            damageReceiverRegistration = null;
            StopMove();
            combatSystem.Dispose();
            fullBodyRuntime = null;
            currentRegistrationId = 0;
            Debug.Log($"[NPCController] NPC '{name}' 已注销伤害接收并释放 FullBody 注册与导航状态。", this);
        }

        #endregion

        #region Behavior Designer 生命周期

        /// <summary>只有属性、技能和导航依赖均就绪后才启动外部行为树。</summary>
        private void StartBehaviorTree()
        {
            if (!initialized || IsDead || !config.UseBehaviorTree || behaviorTree == null)
                return;
            if (behaviorTree.ExternalBehavior == null)
                throw CreateInitializationFailure("启用了行为树但 BehaviorTree 未绑定 External Behavior。");

            behaviorTree.RestartWhenComplete = true;
            behaviorTree.ResetValuesOnRestart = false;
            behaviorTree.StartWhenEnabled = false;
            behaviorTree.EnableBehavior();
            Debug.Log($"[NPCController] Boss '{name}' 已启动 Behavior Designer 外部行为树。", this);
        }

        /// <summary>停止行为树并清理导航命令；结束原因用于区分死亡、停用与销毁。</summary>
        /// <param name="reason">触发停止的生命周期原因。</param>
        private void StopBehaviorTree(string reason)
        {
            if (behaviorTree != null)
                behaviorTree.DisableBehavior(false);
            StopMove();
            if (initialized)
                Debug.Log($"[NPCController] NPC '{name}' 已停止 Behavior Designer，reason={reason}。", this);
        }

        #endregion

        #region NavMesh 导航与移动提交

        /// <summary>校验 Boss 现有 NavMeshAgent、External Behavior 和出生点烘焙位置。</summary>
        private void InitializeNavigation()
        {
            if (navigationAgent == null)
                throw CreateInitializationFailure("NPCConfig 启用了行为树，但同节点缺少 NavMeshAgent。");
            if (behaviorTree == null || behaviorTree.ExternalBehavior == null)
                throw CreateInitializationFailure("NPCConfig 启用了行为树，但同节点 BehaviorTree 未绑定外部树。");

            navigationAgent.updatePosition = false;
            navigationAgent.updateRotation = false;
            navigationAgent.autoRepath = false;
            navigationAgent.speed = config.MoveSpeed;
            navigationAgent.stoppingDistance = 0.05f;
            if (!navigationAgent.isOnNavMesh)
            {
                float sampleRadius = Mathf.Max(1f, navigationAgent.radius * 2f);
                if (!NavMesh.SamplePosition(transform.position, out NavMeshHit hit, sampleRadius,
                        navigationAgent.areaMask) || !navigationAgent.Warp(hit.position))
                    throw CreateInitializationFailure(
                        $"NavMeshAgent '{navigationAgent.name}' 不在已烘焙 NavMesh 上，且出生点附近无法定位导航位置。");
                Debug.Log(
                    $"[NPCController] Boss '{name}' 出生点吸附到烘焙 NavMesh，position={hit.position}。",
                    this);
            }
            navigationAgent.nextPosition = transform.position;
            navigationAgent.isStopped = true;
            Debug.Log(
                $"[NPCController] Boss '{name}' 导航已初始化，updatePosition=false，updateRotation=false，speed={config.MoveSpeed:F2}。",
                this);
        }

        /// <summary>保存行为树提交的目标与攻击停止距离，导航路径由 Controller 逐帧结算。</summary>
        /// <param name="target">共享黑板中的目标空间根。</param>
        /// <param name="stopDistance">进入普通攻击的水平停止距离。</param>
        /// <returns>命令有效时返回 true；当前移动受阻时返回 false 但保留目标以便重试。</returns>
        public bool TryMoveTo(Transform target, float stopDistance)
        {
            if (!config.UseBehaviorTree || !IsValidPlayerTarget(target))
            {
                StopMove();
                return false;
            }

            if (!ReferenceEquals(navigationTarget, target) || !Mathf.Approximately(navigationStopDistance, stopDistance))
            {
                navigationTarget = target;
                navigationStopDistance = stopDistance;
                nextNavigationRefreshTime = 0f;
            }

            if (!CanNavigate())
            {
                StopNavigationMotion(false);
                return false;
            }

            if (Time.time >= nextNavigationRefreshTime)
            {
                nextNavigationRefreshTime = Time.time + config.NavigationRefreshInterval;
                float approachStopDistance = Mathf.Max(0f, stopDistance - NavigationAttackRangeMargin);
                if (!TryBuildCompletePath(target, approachStopDistance, out _, out _))
                {
                    StopNavigationMotion(false);
                    return true;
                }
                if (!navigationAgent.SetPath(navigationPath))
                {
                    StopNavigationMotion(false);
                    Debug.LogWarning($"[NPCController] Boss '{name}' 设置完整 NavMesh 路径失败，将在下一间隔重试。", this);
                    return true;
                }
                navigationAgent.speed = config.MoveSpeed;
                navigationAgent.isStopped = false;
            }

            return true;
        }

        /// <summary>清除目标导航、运动 Handle 与移动动画参数，返回 Idle 状态。</summary>
        public void StopMove()
        {
            navigationTarget = null;
            nextNavigationRefreshTime = 0f;
            StopNavigationMotion(true);
        }

        /// <summary>通过 NavMeshAgent 完整路径计算追击距离，失败时禁止直线距离兜底。</summary>
        /// <param name="target">目标世界空间根。</param>
        /// <param name="stopDistance">路径终点距目标的期望水平间隔。</param>
        /// <param name="pathDistance">完整路径上的剩余总距离。</param>
        /// <returns>导航 Agent 已落在 NavMesh 且路径完整时返回 true。</returns>
        public bool TryGetCompletePathDistance(Transform target, float stopDistance, out float pathDistance)
        {
            pathDistance = float.PositiveInfinity;
            return config != null && config.UseBehaviorTree && navigationAgent != null &&
                   navigationAgent.isOnNavMesh && IsValidPlayerTarget(target) &&
                   TryBuildCompletePath(target, stopDistance, out _, out pathDistance);
        }

        /// <summary>用 NavMesh 路径完整性与 remainingDistance 判断是否已到攻击区。</summary>
        /// <param name="target">共享黑板目标。</param>
        /// <param name="attackDistance">当前行为树攻击节点快照的水平距离。</param>
        /// <returns>水平距离与视线均满足攻击条件时返回 true。</returns>
        public bool IsReadyToAttack(Transform target, float attackDistance)
        {
            string reason;
            float planarDistance = float.PositiveInfinity;
            if (!IsFinitePositive(attackDistance))
                reason = "攻击距离配置无效";
            else if (!IsValidPlayerTarget(target))
                reason = "攻击目标失效";
            else if (actor.IsActionPaused)
                reason = "角色动作暂停";
            else
            {
                Vector3 planarOffset = Vector3.ProjectOnPlane(target.position - transform.position, Vector3.up);
                planarDistance = planarOffset.magnitude;
                if (planarDistance > attackDistance)
                    reason = "超出攻击距离";
                else if (!HasLineOfSight(target))
                    reason = "攻击视线被遮挡";
                // Agent 路径未完成时不启动攻击，避免一边寻路一边释放 FullBody GA。
                else if (navigationAgent != null && navigationAgent.isOnNavMesh && navigationAgent.pathPending)
                    reason = "NavMesh 路径仍在计算";
                else if (navigationAgent != null && navigationAgent.isOnNavMesh && navigationAgent.hasPath &&
                         navigationAgent.remainingDistance > 0.1f)
                    reason = "NavMesh 路径尚未到达";
                else
                    reason = "可以攻击";
            }

            UpdateAttackReadinessDiagnostic(target, reason, planarDistance);
            return reason == "可以攻击";
        }

        /// <summary>仅在攻击准入原因或目标变化时记录一次状态，便于定位追击后停顿。</summary>
        /// <param name="target">本次被检查的行为树目标。</param>
        /// <param name="reason">阻止攻击或允许攻击的稳定原因名称。</param>
        /// <param name="planarDistance">当前水平距离；目标无效时为正无穷。</param>
        private void UpdateAttackReadinessDiagnostic(Transform target, string reason, float planarDistance)
        {
            bool changed = !ReferenceEquals(lastAttackReadinessTarget, target) ||
                           lastAttackReadinessReason != reason;
            lastAttackReadinessTarget = target;
            lastAttackReadinessReason = reason;
            lastAttackReadinessDistance = planarDistance;
            if (changed)
                Debug.Log(
                    $"[NPCController] Boss '{name}' 攻击准入状态变化，target='{target?.name ?? "<none>"}'，" +
                    $"reason={reason}，horizontalDistance={planarDistance:F2}m。",
                    this);
        }

        /// <summary>记录普攻激活被拒绝时的主要阻断状态，并在条件相同期间抑制重复日志。</summary>
        /// <param name="attackIndex">CombatSystem 当前准备启动的零基段位。</param>
        /// <param name="activated">ASC 是否成功创建新 Runtime。</param>
        internal void ReportNormalAttackActivationResult(int attackIndex, bool activated)
        {
            if (activated)
            {
                lastNormalAttackActivationBlock = string.Empty;
                return;
            }

            string block = $"Attack{attackIndex + 1}: FullBody={IsFullBodyActionOccupied}, " +
                           $"Paused={actor.IsActionPaused}, Dead={IsDead}, " +
                           $"HorizontalMoveAllowed={motionDriver.CanMoveHorizontally}; ASC 未接受激活";
            if (lastNormalAttackActivationBlock == block)
                return;

            lastNormalAttackActivationBlock = block;
            Debug.LogWarning($"[NPCController] Boss '{name}' 普攻激活受阻，{block}。", this);
        }

        /// <summary>通过目标根所属的 CharacterManager 验证当前 Active 角色仍可被攻击。</summary>
        /// <param name="target">当前树黑板保存的目标根。</param>
        /// <returns>目标是 Manager 当前 Active 角色的共享 Root，且 Manager 提供有效 ASC 时返回 true。</returns>
        public bool IsValidPlayerTarget(Transform target)
        {
            if (target == null || !target.gameObject.activeInHierarchy)
                return false;

            // CharacterRoot 在切人期间保持不变；角色身份和死亡状态由其所属 Manager 解析。
            CharacterManager playerManager = target.GetComponentInParent<CharacterManager>();
            if (playerManager == null || !playerManager.isActiveAndEnabled || !playerManager.IsReady)
                return false;

            CharacterActor activeActor = playerManager.ActiveCharacter;
            return activeActor != null && activeActor.isActiveAndEnabled &&
                   ReferenceEquals(activeActor.RootTransform, target) && playerManager.TryGetTargetASC() != null;
        }

        /// <summary>获取目标在三段攻击列表中的稳定 Spec Handle。</summary>
        /// <param name="ability">已配置并应已授予的 Ability 资产。</param>
        /// <returns>当前 ASC 中匹配资产的唯一 Handle。</returns>
        internal GameplayAbilityHandle FindGrantedAbilityHandle(GameplayAbilityData ability)
        {
            IReadOnlyList<GameplayAbilitySpec> specs = abilitySystemComponent.GrantedAbilities;
            for (int index = 0; index < specs.Count; index++)
                if (ReferenceEquals(specs[index].Data, ability))
                    return specs[index].Handle;
            throw new InvalidOperationException(
                $"NPCController '{name}' 未授予配置 Ability '{ability.name}'，无法建立 NPCCombatSystem。");
        }

        /// <summary>按普通启动或有效分类阻断区间交接方式激活指定普通攻击 Spec。</summary>
        /// <param name="handle">NPCCombatSystem 缓存的 Attack Spec Handle。</param>
        /// <param name="previousRuntime">实时交接时当前正在播放的普通攻击 Runtime；普通启动时为空。</param>
        /// <param name="runtime">成功时返回本次新建 Runtime。</param>
        /// <returns>ASC 完成新 Runtime 激活时返回 true。</returns>
        internal bool TryActivateNormalAttack(
            GameplayAbilityHandle handle,
            GameplayAbilityRuntime previousRuntime,
            out GameplayAbilityRuntime runtime)
        {
            if (!initialized || IsDead || !handle.IsValid)
            {
                runtime = null;
                return false;
            }
            if (actor.IsActionPaused)
            {
                runtime = null;
                return false;
            }

            IReadOnlyDictionary<GameplayTag, float> setByCallerMagnitudeByTagMap = null;
            if (previousRuntime == null)
            {
                if (IsFullBodyActionOccupied)
                {
                    runtime = null;
                    return false;
                }
            }
            else
            {
                // 只有当前普攻允许普通取消且未阻断普攻分类时，才把 FullBody 控制权交给下一段。
                if (!IsFullBodyActionOccupied || !ReferenceEquals(fullBodyRuntime, previousRuntime) ||
                    previousRuntime.State != GameplayAbilityRuntimeState.Active ||
                    !previousRuntime.AbilityTags.HasTag(GameplayTags.Tag_Skill_NormalAttack) ||
                    !previousRuntime.IsCancelable ||
                    NPCCombatSystem.IsAbilityCategoryBlocked(
                        previousRuntime.BlockAbilityTags.Tags,
                        GameplayTags.Tag_Skill_NormalAttack))
                {
                    runtime = null;
                    return false;
                }

                setByCallerMagnitudeByTagMap = new Dictionary<GameplayTag, float>
                {
                    [GameplayTags.Tag_Skill_Entry_Active] = 1f
                };
            }

            bool activated = abilitySystemComponent.TryActivateAbility(
                handle,
                setByCallerMagnitudeByTagMap,
                out runtime);
            if (activated)
                Debug.Log(
                    $"[NPCController] Boss '{name}' 激活普通攻击 Handle={handle}，入口={(previousRuntime == null ? "TimelineStart" : "FirstActivePhase")}，ActivationId={runtime?.ActivationId ?? -1}。",
                    this);
            return activated;
        }

        /// <summary>无副作用地判断目标是否满足远距瞬移条件，并记录供 Odin 查看的一次诊断快照。</summary>
        /// <param name="target">当前玩家目标根。</param>
        /// <param name="pathDistanceThreshold">完整 NavMesh 路径必须严格超过的距离阈值。</param>
        /// <param name="attackDistance">计算到玩家攻击位置的路径终点距离。</param>
        /// <param name="pathDistance">成功计算时返回完整路径距离；否则为正无穷。</param>
        /// <returns>角色、目标、ASC 激活前置、冷却和安全落点均允许瞬移时返回 true。</returns>
        public bool CanTeleportTo(
            Transform target,
            float pathDistanceThreshold,
            float attackDistance,
            out float pathDistance)
        {
            pathDistance = float.PositiveInfinity;
            string reason;
            bool allowed = false;

            if (!initialized || !isActiveAndEnabled || config == null || !config.UseBehaviorTree)
                reason = "NPC 尚未就绪或未启用行为树。";
            else if (teleportRetryTarget == target && Time.time < teleportRetryUntilTime)
                reason = "瞬移失败退避尚未结束。";
            else if (!CanNavigate())
                reason = "NPC 当前死亡、暂停、被 FullBody 占据或移动受阻。";
            else if (!IsValidPlayerTarget(target))
                reason = "玩家目标失效。";
            else if (!IsFinitePositive(pathDistanceThreshold) || !IsFinitePositive(attackDistance))
                reason = "行为树路径阈值或攻击距离配置非法。";
            else if (!TryGetCompletePathDistance(target, attackDistance, out pathDistance))
                reason = "无法计算到攻击位置的完整 NavMesh 路径。";
            else if (pathDistance <= pathDistanceThreshold)
                reason = "完整 NavMesh 路径未超过瞬移阈值。";
            else if (!CanActivateTeleportAbility())
                reason = "瞬移 GA 未授予、Tag 条件未满足、处于冷却或被 Ability 阻断。";
            else if (!TryResolveSafeTeleportDestination(target, out _, out _))
                reason = "玩家前方没有安全 NavMesh 落点。";
            else
            {
                allowed = true;
                reason = "所有瞬移前置条件满足。";
            }

            RecordTeleportPrecheck(allowed, pathDistance, reason);
            return allowed;
        }

        /// <summary>判断当前目标是否处于瞬移失败后的按目标退避窗口。</summary>
        /// <param name="target">查询目标；不同目标不继承旧目标的失败退避。</param>
        /// <returns>目标与退避快照相同且缩放时间尚未到期时返回 true。</returns>
        public bool IsTeleportRetryPending(Transform target) =>
            target != null && teleportRetryTarget == target && Time.time < teleportRetryUntilTime;

        /// <summary>以当前有效目标和安全落点快照尝试激活独立瞬移 GA。</summary>
        /// <param name="target">当前玩家目标根。</param>
        /// <param name="pathDistanceThreshold">完整 NavMesh 路径必须严格超过的距离阈值。</param>
        /// <param name="attackDistance">计算到玩家攻击位置的路径终点距离。</param>
        /// <param name="retryInterval">失败后抑制立即重复尝试的缩放时间。</param>
        /// <returns>瞬移 Task 实际完成安全重定位时返回 true。</returns>
        public bool TryActivateTeleport(
            Transform target,
            float pathDistanceThreshold,
            float attackDistance,
            float retryInterval)
        {
            if (IsTeleportRetryPending(target))
                return false;
            if (!CanTeleportTo(target, pathDistanceThreshold, attackDistance, out _))
            {
                ScheduleTeleportRetry(target, retryInterval);
                return false;
            }
            if (!TryResolveSafeTeleportDestination(target, out Vector3 destination, out Quaternion rotation))
            {
                ScheduleTeleportRetry(target, retryInterval);
                RecordTeleportPrecheck(false, lastTeleportPrecheckPathDistance, "激活前安全落点再次解析失败。");
                return false;
            }

            GameplayAbilityHandle handle = FindGrantedAbilityHandle(config.TeleportAbility);
            // 瞬移前撤销旧导航路径；如果 GA 冷却拒绝，行为树随后会在 Walk 分支重新发出目标命令。
            StopMove();
            hasPendingTeleport = true;
            pendingTeleportTarget = target;
            pendingTeleportPosition = destination;
            pendingTeleportRotation = rotation;
            lastTeleportActivationId = -1;
            lastTeleportSucceeded = false;
            try
            {
                bool activated = abilitySystemComponent.TryActivateAbility(handle, out GameplayAbilityRuntime runtime);
                bool succeeded = activated && runtime != null &&
                                 lastTeleportActivationId == runtime.ActivationId && lastTeleportSucceeded;
                Debug.Log(
                    $"[NPCController] Boss '{name}' 瞬移 GA 结果，activated={activated}，succeeded={succeeded}，" +
                    $"ActivationId={runtime?.ActivationId ?? -1}，destination={pendingTeleportPosition}。",
                    this);
                if (succeeded)
                {
                    teleportRetryTarget = null;
                    teleportRetryUntilTime = 0f;
                }
                else
                {
                    ScheduleTeleportRetry(target, retryInterval);
                }
                return succeeded;
            }
            finally
            {
                // 未被 Task 消费的请求不跨越本次同步 GA 激活边界。
                hasPendingTeleport = false;
                pendingTeleportTarget = null;
            }
        }

        /// <summary>检查瞬移 Spec 的无副作用激活前置条件，不创建 Runtime 或提交 GE。</summary>
        /// <returns>Spec 存在、Tag 查询通过、冷却已结束且没有活动 Block 时返回 true。</returns>
        private bool CanActivateTeleportAbility()
        {
            GameplayAbilityData ability = config.TeleportAbility;
            if (ability == null || !ability.IsRuntimeConfigurationValid)
                return false;

            GameplayAbilitySpec teleportSpec = null;
            IReadOnlyList<GameplayAbilitySpec> abilitySpecs = abilitySystemComponent.GrantedAbilities;
            for (int index = 0; index < abilitySpecs.Count; index++)
            {
                GameplayAbilitySpec candidate = abilitySpecs[index];
                if (!ReferenceEquals(candidate.Data, ability))
                    continue;
                teleportSpec = candidate;
                break;
            }
            if (teleportSpec == null || teleportSpec.Level < 1 ||
                !ability.ActivationTagQuery.Matches(abilitySystemComponent.Tags))
                return false;

            if (abilitySystemComponent.IsAbilityActivationBlockedByOwnerTags)
                return false;

            IReadOnlyList<GameplayAbilityRuntime> activeAbilities = abilitySystemComponent.ActiveAbilities;
            for (int runtimeIndex = 0; runtimeIndex < activeAbilities.Count; runtimeIndex++)
            {
                GameplayAbilityRuntime activeRuntime = activeAbilities[runtimeIndex];
                if (activeRuntime.State != GameplayAbilityRuntimeState.Active ||
                    activeRuntime.BlockAbilityTags.IsEmpty)
                    continue;
                foreach (GameplayTag abilityTag in ability.AbilityTags)
                    foreach (GameplayTag blockTag in activeRuntime.BlockAbilityTags.Tags)
                        if (abilityTag.MatchesTag(blockTag))
                            return false;
            }

            if (ability.CooldownEffect != null)
                foreach (GameplayTag cooldownTag in ability.CooldownEffect.GrantedTags)
                    if (abilitySystemComponent.HasTag(cooldownTag))
                        return false;

            return true;
        }

        /// <summary>记录瞬移预检快照，并仅在结果或原因变化时输出诊断日志。</summary>
        /// <param name="allowed">预检是否允许尝试激活。</param>
        /// <param name="pathDistance">当前完整路径距离或正无穷。</param>
        /// <param name="reason">预检结果的业务原因。</param>
        private void RecordTeleportPrecheck(bool allowed, float pathDistance, string reason)
        {
            bool changed = !hasTeleportPrecheckResult || lastTeleportPrecheckAllowed != allowed ||
                           lastTeleportPrecheckReason != reason;
            hasTeleportPrecheckResult = true;
            lastTeleportPrecheckAllowed = allowed;
            lastTeleportPrecheckPathDistance = pathDistance;
            lastTeleportPrecheckReason = reason;
            if (changed)
                Debug.Log(
                    $"[NPCController] Boss '{name}' 瞬移条件={allowed}，路径距离={pathDistance:F2}m，原因={reason}",
                    this);
        }

        /// <summary>对本目标登记一次失败退避，避免 Conditional Abort 立即重复打断 Walk。</summary>
        /// <param name="target">失败时的玩家目标。</param>
        /// <param name="retryInterval">行为树节点配置的缩放时间退避间隔。</param>
        private void ScheduleTeleportRetry(Transform target, float retryInterval)
        {
            float backoffDuration = Mathf.Max(0.01f, retryInterval);
            teleportRetryTarget = target;
            teleportRetryUntilTime = Time.time + backoffDuration;
            Debug.Log(
                $"[NPCController] Boss '{name}' 瞬移尝试未完成，目标='{(target != null ? target.name : "<none>")}'，" +
                $"{backoffDuration:F2}s 后重新评估。",
                this);
        }

        /// <summary>判断用于行为树策略的浮点数是否为有限正值。</summary>
        /// <param name="value">待检查的距离或时间。</param>
        /// <returns>大于零且非 NaN、Infinity 时返回 true。</returns>
        private static bool IsFinitePositive(float value) =>
            value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);

        /// <summary>由瞬移 Task 二次检查目标与落点后执行一次安全 Warp。</summary>
        /// <param name="activationId">执行该请求的异步 GA 激活标识。</param>
        /// <returns>Agent 与角色根均成功重定位时返回 true。</returns>
        internal bool TryCompletePendingTeleport(int activationId)
        {
            Transform target = pendingTeleportTarget;
            bool success = false;
            Vector3 destination = pendingTeleportPosition;
            Quaternion rotation = pendingTeleportRotation;
            NavMeshHit sampledPosition = default;
            bool locationStillValid = hasPendingTeleport && IsValidPlayerTarget(target) &&
                                      NavMesh.SamplePosition(
                                          pendingTeleportPosition,
                                          out sampledPosition,
                                          0.12f,
                                          navigationAgent.areaMask) &&
                                      Vector3.Distance(sampledPosition.position, pendingTeleportPosition) <= 0.12f &&
                                      Mathf.Abs(sampledPosition.position.y - target.position.y) <= 1.5f &&
                                      IsTeleportCapsuleClear(sampledPosition.position, pendingTeleportRotation) &&
                                      HasClearTeleportSight(target, sampledPosition.position);
            if (locationStillValid)
            {
                destination = sampledPosition.position;
                Vector3 previousPosition = transform.position;
                Quaternion previousRotation = transform.rotation;
                bool controllerWasEnabled = characterController.enabled;
                characterController.enabled = false;
                try
                {
                    transform.SetPositionAndRotation(destination, rotation);
                    success = navigationAgent.Warp(destination);
                    if (success)
                    {
                        navigationAgent.nextPosition = transform.position;
                        navigationAgent.ResetPath();
                        navigationAgent.isStopped = true;
                        Debug.Log(
                            $"[NPCController] Boss '{name}' 瞬移 Task 完成安全重定位，ActivationId={activationId}，" +
                            $"from={previousPosition}，to={destination}。",
                            this);
                    }
                    else
                    {
                        transform.SetPositionAndRotation(previousPosition, previousRotation);
                        Debug.LogWarning(
                            $"[NPCController] Boss '{name}' NavMeshAgent.Warp 失败，已恢复原位，ActivationId={activationId}。",
                            this);
                    }
                }
                finally
                {
                    characterController.enabled = controllerWasEnabled;
                }
            }

            hasPendingTeleport = false;
            pendingTeleportTarget = null;
            lastTeleportActivationId = activationId;
            lastTeleportSucceeded = success;
            return success;
        }

        #endregion

        #region 安全落点与路径解析

        /// <summary>在玩家水平前方附近搜索 NavMesh 落点，并排除墙体、环境遮挡和 Boss 胶囊重叠。</summary>
        /// <param name="target">经有效性校验的玩家空间根。</param>
        /// <param name="destination">找到时返回 NavMesh 落点。</param>
        /// <param name="rotation">落点朝向玩家的旋转。</param>
        /// <returns>0.5 米配置半径内存在安全落点时返回 true。</returns>
        private bool TryResolveSafeTeleportDestination(
            Transform target,
            out Vector3 destination,
            out Quaternion rotation)
        {
            destination = default;
            rotation = transform.rotation;
            if (navigationAgent == null || !navigationAgent.isOnNavMesh || !IsValidPlayerTarget(target))
                return false;

            Vector3 playerForward = Vector3.ProjectOnPlane(target.forward, Vector3.up);
            if (playerForward.sqrMagnitude <= 0.0001f)
                return false;
            playerForward.Normalize();
            Vector3 idealPosition = target.position + playerForward * config.TeleportForwardOffset;
            Vector3 facingDirection = Vector3.ProjectOnPlane(target.position - idealPosition, Vector3.up);
            if (facingDirection.sqrMagnitude <= 0.0001f)
                return false;
            Quaternion facingRotation = Quaternion.LookRotation(facingDirection.normalized, Vector3.up);

            float closestSquaredDistance = config.TeleportLandingRadius * config.TeleportLandingRadius;
            bool found = false;
            for (int ring = 0; ring <= 2; ring++)
            {
                float radius = ring * (config.TeleportLandingRadius * 0.5f);
                int sampleCount = ring == 0 ? 1 : 8;
                for (int sample = 0; sample < sampleCount; sample++)
                {
                    float angle = sample * Mathf.PI * 2f / sampleCount;
                    Vector3 offset = ring == 0
                        ? Vector3.zero
                        : new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                    Vector3 candidate = idealPosition + offset;
                    if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 0.12f, navigationAgent.areaMask))
                        continue;
                    float offsetSquared = (hit.position - idealPosition).sqrMagnitude;
                    if (offsetSquared > closestSquaredDistance ||
                        Mathf.Abs(hit.position.y - target.position.y) > 1.5f ||
                        offsetSquared >= closestSquaredDistance && found ||
                        !IsTeleportCapsuleClear(hit.position, facingRotation) ||
                        !HasClearTeleportSight(target, hit.position))
                        continue;

                    closestSquaredDistance = offsetSquared;
                    destination = hit.position;
                    rotation = facingRotation;
                    found = true;
                }
            }
            return found;
        }

        /// <summary>确保候选位置不会与其他碰撞体重叠；只排除 Boss 自身层级。</summary>
        /// <param name="candidatePosition">NavMesh 上的候选根位置。</param>
        /// <param name="candidateRotation">候选朝向，用于旋转 CharacterController 中心。</param>
        /// <returns>胶囊体空间无其他碰撞体时返回 true。</returns>
        private bool IsTeleportCapsuleClear(Vector3 candidatePosition, Quaternion candidateRotation)
        {
            float radius = Mathf.Max(0.05f, characterController.radius - 0.03f);
            float height = Mathf.Max(characterController.height, radius * 2f);
            Vector3 center = candidatePosition + candidateRotation * characterController.center;
            float halfSegment = Mathf.Max(0f, height * 0.5f - radius);
            Vector3 lower = center - Vector3.up * halfSegment + Vector3.up * 0.05f;
            Vector3 upper = center + Vector3.up * halfSegment;
            int overlapCount = Physics.OverlapCapsuleNonAlloc(
                lower, upper, radius, physicsQueryBuffer, ~0, QueryTriggerInteraction.Ignore);
            while (overlapCount == physicsQueryBuffer.Length)
            {
                Array.Resize(ref physicsQueryBuffer, checked(physicsQueryBuffer.Length * 2));
                overlapCount = Physics.OverlapCapsuleNonAlloc(
                    lower, upper, radius, physicsQueryBuffer, ~0, QueryTriggerInteraction.Ignore);
            }

            bool clear = true;
            for (int index = 0; index < overlapCount; index++)
            {
                Collider hit = physicsQueryBuffer[index];
                if (hit == null || hit.transform == transform || hit.transform.IsChildOf(transform))
                    continue;
                clear = false;
                break;
            }
            Array.Clear(physicsQueryBuffer, 0, overlapCount);
            return clear;
        }

        /// <summary>候选落点至玩家的水平视线不得穿过墙体或其他环境阻挡物。</summary>
        /// <param name="target">需要到达的玩家根。</param>
        /// <param name="candidatePosition">候选 NavMesh 落点。</param>
        /// <returns>两点之间没有非 Boss、非玩家碰撞体时返回 true。</returns>
        private bool HasClearTeleportSight(Transform target, Vector3 candidatePosition)
        {
            Vector3 start = target.position + Vector3.up;
            Vector3 end = candidatePosition + Vector3.up;
            Vector3 direction = end - start;
            float distance = direction.magnitude;
            if (distance <= 0.01f)
                return false;
            int hitCount = Physics.RaycastNonAlloc(
                start, direction / distance, raycastHitBuffer, distance,
                config.AttackObstructionMask, QueryTriggerInteraction.Ignore);
            while (hitCount == raycastHitBuffer.Length)
            {
                Array.Resize(ref raycastHitBuffer, checked(raycastHitBuffer.Length * 2));
                hitCount = Physics.RaycastNonAlloc(
                    start, direction / distance, raycastHitBuffer, distance,
                    config.AttackObstructionMask, QueryTriggerInteraction.Ignore);
            }

            bool clear = true;
            for (int index = 0; index < hitCount; index++)
            {
                Transform hit = raycastHitBuffer[index].collider != null
                    ? raycastHitBuffer[index].collider.transform
                    : null;
                if (hit == null || hit == target || hit.IsChildOf(target) ||
                    hit == transform || hit.IsChildOf(transform))
                    continue;
                clear = false;
                break;
            }
            Array.Clear(raycastHitBuffer, 0, hitCount);
            return clear;
        }

        /// <summary>构造到攻击距离外的完整路径，并计算所有 NavMesh 拐点长度。</summary>
        /// <param name="target">目标玩家根。</param>
        /// <param name="stopDistance">目标周边的追击停止距离。</param>
        /// <param name="destination">完整路径的终点。</param>
        /// <param name="pathDistance">路径所有折线段的长度。</param>
        /// <returns>Agent、起点与终点均可通行且路径完整时返回 true。</returns>
        private bool TryBuildCompletePath(
            Transform target,
            float stopDistance,
            out Vector3 destination,
            out float pathDistance)
        {
            destination = default;
            pathDistance = float.PositiveInfinity;
            if (navigationAgent == null || !navigationAgent.isOnNavMesh || !IsValidPlayerTarget(target))
                return false;

            Vector3 toTarget = Vector3.ProjectOnPlane(target.position - transform.position, Vector3.up);
            Vector3 attackApproachPosition = toTarget.sqrMagnitude > 0.0001f
                ? target.position - toTarget.normalized * stopDistance
                : transform.position;
            float sampleRadius = Mathf.Max(0.5f, navigationAgent.radius * 2f);
            if (!NavMesh.SamplePosition(
                    attackApproachPosition, out NavMeshHit hit, sampleRadius, navigationAgent.areaMask))
                return false;
            if (!navigationAgent.CalculatePath(hit.position, navigationPath) ||
                navigationPath.status != NavMeshPathStatus.PathComplete)
                return false;

            Vector3[] corners = navigationPath.corners;
            float totalDistance = 0f;
            for (int index = 1; index < corners.Length; index++)
                totalDistance += Vector3.Distance(corners[index - 1], corners[index]);
            destination = hit.position;
            pathDistance = totalDistance;
            return true;
        }

        /// <summary>以同一 Physics Query 规则判断玩家与 Boss 之间是否被环境遮挡。</summary>
        /// <param name="target">玩家空间根。</param>
        /// <returns>攻击视线无遮挡时返回 true。</returns>
        private bool HasLineOfSight(Transform target)
        {
            Vector3 start = transform.position + Vector3.up;
            Vector3 end = target.position + Vector3.up;
            Vector3 direction = end - start;
            float distance = direction.magnitude;
            if (distance <= 0.01f)
                return true;
            int hitCount = Physics.RaycastNonAlloc(
                start, direction / distance, raycastHitBuffer, distance,
                config.AttackObstructionMask, QueryTriggerInteraction.Ignore);
            while (hitCount == raycastHitBuffer.Length)
            {
                Array.Resize(ref raycastHitBuffer, checked(raycastHitBuffer.Length * 2));
                hitCount = Physics.RaycastNonAlloc(
                    start, direction / distance, raycastHitBuffer, distance,
                    config.AttackObstructionMask, QueryTriggerInteraction.Ignore);
            }

            bool visible = true;
            for (int index = 0; index < hitCount; index++)
            {
                Transform hit = raycastHitBuffer[index].collider != null
                    ? raycastHitBuffer[index].collider.transform
                    : null;
                if (hit == null || hit == target || hit.IsChildOf(target) ||
                    hit == transform || hit.IsChildOf(transform))
                    continue;
                visible = false;
                break;
            }
            Array.Clear(raycastHitBuffer, 0, hitCount);
            return visible;
        }

        #endregion

        #region 移动阶段与导航资源

        /// <summary>仅在移动可用时把 Agent 意图提交给 MotionDriver，保持 CharacterController 唯一移动出口。</summary>
        /// <param name="deltaTime">本次普通帧时长。</param>
        private void UpdateNavigationMotion(float deltaTime)
        {
            if (!config.UseBehaviorTree || navigationTarget == null)
                return;
            if (!CanNavigate() || !navigationAgent.isOnNavMesh)
            {
                StopNavigationMotion(false);
                return;
            }
            if (navigationAgent.pathPending || navigationAgent.pathStatus != NavMeshPathStatus.PathComplete ||
                !navigationAgent.hasPath)
                return;

            Vector3 desiredDirection = Vector3.ProjectOnPlane(navigationAgent.desiredVelocity, Vector3.up);
            if (desiredDirection.sqrMagnitude <= 0.0001f)
            {
                SetMoveBlend(Vector3.zero);
                return;
            }

            Quaternion targetRotation = Quaternion.LookRotation(
                Vector3.ProjectOnPlane(navigationTarget.position - transform.position, Vector3.up).normalized,
                Vector3.up);
            Vector3 localDirection = transform.InverseTransformDirection(desiredDirection.normalized);
            SetMoveBlend(localDirection);

            float speed = Mathf.Min(config.MoveSpeed, desiredDirection.magnitude);
            float maxStep = Mathf.Max(0f, navigationAgent.remainingDistance);
            Vector3 translation = desiredDirection.normalized * Mathf.Min(speed * deltaTime, maxStep);
            Quaternion relativeRotation = Quaternion.Inverse(transform.rotation) * targetRotation;
            EnsureNavigationMotionHandle();
            if (!navigationMotionHandle.IsValid)
                return;

            if (locomotion.CurrentState != E_NPCStateId.Move)
                locomotion.TryChangeState(E_NPCStateId.Move);
            motionDriver.SubmitUpdate(
                navigationMotionHandle,
                new UpdateMotionSubmission(translation, relativeRotation));
        }

        /// <summary>同步 NavMesh 内部位置到 MotionDriver 与碰撞结算后的真实角色根位置。</summary>
        private void SynchronizeNavigationPosition()
        {
            if (navigationAgent != null && navigationAgent.isOnNavMesh)
                navigationAgent.nextPosition = transform.position;
        }

        /// <summary>保证普通 Walk 在当前帧拥有 Locomotion 优先级的水平与旋转通道。</summary>
        private void EnsureNavigationMotionHandle()
        {
            if (navigationMotionHandle != null && navigationMotionHandle.IsValid)
                return;
            navigationMotionHandle = motionDriver.RequestControl(new MotionControlRequest(
                actor,
                MotionPriority.Locomotion,
                MotionChannels.Horizontal | MotionChannels.Rotation));
        }

        /// <summary>暂停 Agent 当前意图并按需释放 Handle；可保留目标等待阻断状态解除。</summary>
        /// <param name="releaseHandle">是否归还 MotionDriver 控制权。</param>
        private void StopNavigationMotion(bool releaseHandle)
        {
            if (navigationAgent != null && navigationAgent.isOnNavMesh)
            {
                navigationAgent.isStopped = true;
                navigationAgent.ResetPath();
            }
            // 这里只撤回导航意图；不可清空 MotionDriver 全局瞬时提交，以免抹掉同帧 GA 根运动。
            if (releaseHandle)
            {
                navigationMotionHandle?.Dispose();
                navigationMotionHandle = null;
            }
            // 结束或销毁时只释放导航控制权；动画子对象可能从未激活，写参数会惰性创建新 Graph。
            if (initialized && !disposed && !destroying && isActiveAndEnabled && gameObject.activeInHierarchy)
            {
                SetMoveBlend(Vector3.zero);
                if (!IsDead && locomotion.CurrentState == E_NPCStateId.Move)
                    locomotion.TryChangeState(E_NPCStateId.Idle);
            }
        }

        /// <summary>判断导航是否被死亡、HitStop、FullBody、停用或 GAS 移动阻断条件暂停。</summary>
        /// <returns>所有导航前置条件满足时返回 true。</returns>
        private bool CanNavigate() => initialized && isActiveAndEnabled && !IsDead &&
                                      !actor.IsActionPaused && !IsFullBodyActionOccupied &&
                                      motionDriver.CanMoveHorizontally;

        /// <summary>按二维 Move Mixer 约定写入 X 向右、Y 向前的角色局部移动方向。</summary>
        /// <param name="localDirection">Move Mixer 的角色局部方向。</param>
        private void SetMoveBlend(Vector3 localDirection)
        {
            if (actor?.AnimationPlayer == null || config == null)
                return;
            if (config.MoveParameterX != null)
                actor.AnimationPlayer.SetFloatParameter(config.MoveParameterX, localDirection.x);
            if (config.MoveParameterY != null)
                actor.AnimationPlayer.SetFloatParameter(config.MoveParameterY, localDirection.z);
        }

        #endregion

        #region Animator 运动结算

        /// <summary>接收 Animator 阶段增量并按 GAS 提交结果执行一次运动结算。</summary>
        /// <param name="deltaPosition">Animator 本次根位移。</param>
        /// <param name="deltaRotation">Animator 本次根旋转。</param>
        internal void ProcessAnimatorMotion(Vector3 deltaPosition, Quaternion deltaRotation)
        {
            if (!initialized || !isActiveAndEnabled || actor.IsActionPaused ||
                IsDead ||
                lastAnimatorMoveFrame == Time.frameCount)
                return;

            try
            {
                abilitySystemComponent.UpdateAnimationMove(deltaPosition, deltaRotation);
                locomotion.AnimatorMove();
                motionDriver.ResolveAnimatorMotion();
                SynchronizeNavigationPosition();
                lastAnimatorMoveFrame = Time.frameCount;
            }
            catch
            {
                motionDriver.ClearTransientRequests();
                throw;
            }
        }

        #endregion

    }
}
