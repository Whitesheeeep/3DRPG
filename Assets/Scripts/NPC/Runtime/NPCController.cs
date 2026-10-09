using System;
using System.Collections.Generic;
using RPG.Character;
using RPG.Character.Animation;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.CustomEventSystem;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.Generated;
using WS_Modules.GAS.GameplayAbilitySystem;

namespace RPG.NPC
{
    /// <summary>独立驱动 NPC 的 ASC、Alive/Dead 状态与 CharacterController 位移结算。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController), typeof(NPCActor))]
    [InfoBox("依赖同节点 CharacterController、NPCActor 与可选 NPCIdentity，以及子级碰撞体；NPCConfig 提供等级成长、属性集、资源规则、技能、技能激活规则和 Idle/Move/Death 动画。身份缺失时不会发布可供任务匹配的击败事件。")]
    public sealed class NPCController : MonoBehaviour
    {
        #region 配置与依赖字段

        // 配置与同节点控制依赖
        [SerializeField, Required] private NPCConfig config;
        [SerializeField, MinValue(1), LabelText("生成等级")] private int generationLevel = 1;
        [SerializeField] private CharacterController characterController;
        [SerializeField] private NPCActor actor;
        [SerializeField] private NPCIdentity npcIdentity;
        [SerializeField] private MotionDriver motionDriver = new();
        [SerializeField] private BossLocomotionStateMachine locomotion = new();

        #endregion

        #region 运行时依赖

        // ASC、Action 注册与初始化状态由本组件在 Awake/Start 生命周期中建立和释放。
        private GameplayAbilitySystemComponent abilitySystemComponent;
        private NPCActionArbiter actionArbiter;
        private bool initialized;
        private int lastAnimatorMoveFrame = -1;
        private bool deathEventPublished;

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

        /// <summary>获取当前 NPC 唯一的 FullBody Action 注册器。</summary>
        public IFullBodyActionArbiter FullBodyActionArbiter => actionArbiter ??
            throw new InvalidOperationException($"NPCController '{name}' 尚未创建 Action Arbiter。");

        /// <summary>获取 NPC Alive/Dead 与 Idle/Move 分层状态机。</summary>
        public BossLocomotionStateMachine Locomotion => locomotion;

        /// <summary>获取该 NPC 是否被 FullBody Ability 占据。</summary>
        public bool IsFullBodyActionOccupied => actionArbiter != null && actionArbiter.IsOccupied;

        /// <summary>获取 NPC 运行时是否已完成属性、技能和状态机初始化。</summary>
        public bool IsInitialized => initialized;

        /// <summary>通过 ASC 精确查询该 NPC 是否持有不可逆死亡 Tag。</summary>
        public bool IsDead =>
            abilitySystemComponent != null &&
            abilitySystemComponent.HasTagExact(GameplayTags.Tag_State_Dead);

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
            if (characterController == null || actor == null)
            {
                Debug.LogError(
                    $"[NPCController] '{name}' 缺少同节点 CharacterController 或 NPCActor，无法建立运行时依赖。",
                    this);
                throw new InvalidOperationException(
                    $"NPCController '{name}' 必须与 CharacterController 和 NPCActor 挂在同一 GameObject。");
            }

            motionDriver.Initialize(characterController);
            actionArbiter = new NPCActionArbiter(this);
            abilitySystemComponent = actor.AbilitySystemComponent;
            motionDriver.SetActiveOwner(actor, abilitySystemComponent);
            motionDriver.Resume();
        }

        /// <summary>导入 NPC 属性、授予配置技能并进入 Idle 动画状态。</summary>
        private void Start() => InitializeRuntime();

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
                if (IsDead || actor.IsActionPaused)
                    motionDriver.ClearTransientRequests();
                else
                    motionDriver.ResolveUpdateMotion();
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

        /// <summary>释放动作占据与仍属于 NPC 的运动控制权。</summary>
        private void OnDestroy()
        {
            locomotion?.Deactivate();
            if (abilitySystemComponent != null)
                abilitySystemComponent.AttributeChanged -= OnAttributeChanged;
            actionArbiter?.Dispose();
            if (actor != null)
                motionDriver.ReleaseAll(actor);
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
            Debug.Log(
                $"[NPCController] NPC '{name}' 初始化完成，AttributeSets={config.InitialAttributeSets.Count}，" +
                $"Abilities={config.GrantedAbilities.Count}，State={locomotion.CurrentState}。",
                this);
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
            IReadOnlyList<GameplayAbilityData> grantedAbilities = config.GrantedAbilities;
            for (int abilityIndex = 0; abilityIndex < grantedAbilities.Count; abilityIndex++)
            {
                GameplayAbilityData ability = grantedAbilities[abilityIndex];
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
                actor.AnimationPlayer.SetFloatParameter(config.MoveParameterX, 1f);

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
