using System;
using System.Collections.Generic;
using RPG.Character.State;
using RPG.PlayerInputSystem;
using WS_Modules.GAS.GameplayAbilitySystem;
using RPG.Character.Animation;
using RPG.Markers;
using RPG.SkillSystem;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.AttributeSystem;

namespace RPG.Character
{
    /// <summary>封装一个角色独立的战斗、能力、动画、挂点和 Locomotion 状态。</summary>
    [RequireComponent(typeof(Animator))]
    [InfoBox("依赖 CharacterConfig、同节点 Humanoid Animator、AnimationController，以及同节点或子节点中的 ASC、MarkerProvider 与 SkillRuntimeHost；Config 提供初始属性、战斗配置和 Locomotion 参数；子树 Renderer 用于隐藏后台角色。")]
    public sealed class CharacterActor : CharacterAbilityActor
    {
        #region 配置与运行时状态

        // 玩家角色配置与 Humanoid 足部缓存只服务于玩家战斗和队伍切换。
        [SerializeField, Required] private CharacterConfig config;
        [NonSerialized] private Transform leftFoot;
        [NonSerialized] private Transform rightFoot;
        [NonSerialized] private bool footBonesCached;
        [SerializeField] private CharacterLocomotionStateMachine locomotion = new();

        // 战斗、实例和成长运行时在 CharacterManager 绑定 CharacterInstance 后初始化。
        private readonly CharacterCombatSystem combatSystem = new();
        private CharacterActionArbiter actionArbiter;
        private CharacterInstance instance;
        private CharacterRuntimeAttributeBinding runtimeAttributeBinding;
        private readonly CharacterAttributeProgressionResolver progressionResolver = new();

        // Player 注入的稳定运行时依赖，不随角色切换重新创建。
        private Transform characterRoot;
        private IMotionDriver motionDriver;
        private PlayerController playerController;
        // 所有队伍角色共享同一个 PlayerStateBlackboard，Locomotion 通过 Owner 直接读取当前玩家输入事实。
        private PlayerStateBlackboard stateBlackboard;
        // 预热期间 Animator 可能被临时启用；该标记阻止初始化求值误进入玩家运动结算。
        private bool suppressAnimatorMotion;
        // 角色表现 Renderer 缓存，SetActivePresentation 时用于隐藏后台角色。
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
        /// <summary>获取当前角色 Locomotion FSM。</summary>
        public CharacterLocomotionStateMachine Locomotion => locomotion;
        /// <summary>获取稳定 Player 共享的输入状态黑板。</summary>
        public PlayerStateBlackboard StateBlackboard => stateBlackboard;
        /// <summary>当角色仍有活动能力时，角色切换应直接拒绝。</summary>
        public bool IsBusy => AbilitySystemComponent.ActiveAbilities.Count > 0;
        /// <inheritdoc />
        public override Transform RootTransform => characterRoot != null ? characterRoot : transform;
        /// <inheritdoc />
        public override IMotionDriver MotionDriver => motionDriver;
        /// <inheritdoc />
        public override IFullBodyActionArbiter FullBodyActionArbiter => actionArbiter ??
            throw new InvalidOperationException($"角色 '{name}' 尚未完成 Action Arbiter 初始化。");

        #endregion

        #region Unity 生命周期

        /// <summary>在角色包装 Prefab 的真实序列化边界解析并校验必需依赖。</summary>
        private void Awake() => EnsureDependencies();

        /// <summary>在 Player 已完成运行时绑定时，作为幂等兜底导入属性并组装角色业务。</summary>
        private void Start()
        {
            // 正式入口由 PlayerController 初始化流程调用；Player 初始化失败时不能再制造缺少 Locomotion 依赖的次生异常。
            if (stateBlackboard != null)
                InitializeFromInstance();
        }

        /// <summary>销毁角色时释放 FullBody Action 注册并归还共享 Blackboard 占据。</summary>
        private void OnDestroy()
        {
            StopRuntime();
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
        /// PlayerController 在自己的 Start 中显式调用该方法，保证所有 ASC 已完成 Awake，
        /// 再激活可能读取 GAS Speed 的 Locomotion；CharacterActor.Start 会再次调用但不会重复授予。
        /// </remarks>
        internal void InitializeFromInstance()
        {
            if (runtimeConfigurationInitialized) return;
            if (instance == null) throw new InvalidOperationException($"CharacterActor '{name}' 尚未绑定 CharacterInstance。");
            instance.Config.Validate();
            InitializeAttributesFromInstance();
            runtimeAttributeBinding = new CharacterRuntimeAttributeBinding(instance, abilitySystemComponent, progressionResolver);
            combatSystem.Initialize(abilitySystemComponent, instance.Config.CombatConfig, stateBlackboard);
            actionArbiter = new CharacterActionArbiter(
                this,
                stateBlackboard,
                combatSystem,
                abilitySystemComponent,
                locomotion.GroundedJumpTransitionQueryService);
            runtimeConfigurationInitialized = true;
        }

        /// <summary>停止当前 Actor 的实例绑定和战斗运行时，防止存档移除角色后继续持有孤立实例。</summary>
        internal void StopRuntime()
        {
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

        /// <summary>把稳定 CharacterRoot、输入黑板与 Player 持有的运动请求接口注入角色。</summary>
        /// <param name="root">所有队伍角色共享的空间根节点。</param>
        /// <param name="driver">GAS 与 Locomotion 共用的请求接口。</param>
        /// <param name="controller">负责驱动当前角色 AnimatorMove 的稳定 PlayerController。</param>
        /// <param name="blackboard">所有队伍角色共享的 PlayerStateBlackboard。</param>
        internal void BindRuntime(
            Transform root,
            IMotionDriver driver,
            PlayerController controller,
            PlayerStateBlackboard blackboard,
            CharacterInstance characterInstance)
        {
            characterRoot = root;
            motionDriver = driver;
            playerController = controller ?? throw new ArgumentNullException(nameof(controller));
            stateBlackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
            if (config == null) throw new InvalidOperationException($"CharacterActor '{name}' 未配置 CharacterConfig。");
            instance = characterInstance ?? throw new ArgumentNullException(nameof(characterInstance));
            if (instance.Config.CharacterId != config.CharacterId)
                throw new InvalidOperationException($"CharacterActor '{name}' 的 Prefab Config 与 CharacterInstance 不一致。");
            locomotion.Configure(instance.Config.Gravity, instance.Config.LocomotionTransition);
            locomotion.Initialize(this, driver);
        }

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
                animationController.PrimeInitialPose(locomotion.IdleTransition);
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

        /// <summary>只切换角色表现，不停用承载 ASC 的节点，也不驱动 Locomotion。</summary>
        /// <param name="active">该角色是否成为玩家当前操控对象。</param>
        internal void SetActivePresentation(bool active)
        {
            EnsureDependencies();
            // ASC 所在对象不能 SetActive(false)，否则后台能力与事件生命周期会被截断。
            if (!active) combatSystem.ResetCombo();
            presentationRenderers ??= GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in presentationRenderers) renderer.forceRenderingOff = !active;
            animator.enabled = active;
        }

        /// <summary>推进普通 ASC 阶段；后台角色也会执行。</summary>
        /// <param name="deltaTime">本帧缩放时间。</param>
        internal void TickAbility(float deltaTime) => abilitySystemComponent.Tick(deltaTime);

        /// <summary>先推进连段时间，再由角色 Action Arbiter 按 Ability、Jump、Move 顺序处理输入。</summary>
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

        #endregion
    }
}
