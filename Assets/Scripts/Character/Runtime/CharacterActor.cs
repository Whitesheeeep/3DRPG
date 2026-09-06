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
    /// <summary>封装一个角色独立的能力、动画、挂点和 Locomotion 状态。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    [InfoBox("依赖 CharacterConfig、同节点 Humanoid Animator、AnimationController，以及同节点或子节点中的 ASC、MarkerProvider 与 SkillRuntimeHost；Config 提供初始属性、输入绑定和 Locomotion 参数；子树 Renderer 用于隐藏后台角色。")]
    public sealed class CharacterActor : MonoBehaviour, IGameplayAbilitySystemOwner
    {
        #region 配置与运行时状态

        // 角色能力与表现依赖：ASC 保持启用供后台 Tick，Animator 仅由当前表现状态启用。
        [SerializeField, Required] private CharacterConfig config;
        [SerializeField] private GameplayAbilitySystemComponent abilitySystemComponent;
        [SerializeField] private Animator animator;
        [NonSerialized] private Transform leftFoot;
        [NonSerialized] private Transform rightFoot;
        [NonSerialized] private bool footBonesCached;
        [SerializeField] private MarkerProvider markerProvider;
        [SerializeField] private SkillRuntimeHost skillRuntimeHost;
        [SerializeField] private AnimationController animationController;
        [SerializeField] private CharacterLocomotionStateMachine locomotion = new();

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
        // 角色技能槽位到 ASC Spec 的稳定索引；初始化时授予一次，输入阶段只查询该索引。
        // key：战斗输入槽位；value：Config 中对应 Ability 授予后的 ASC Handle。
        private readonly Dictionary<PlayerInputType, GameplayAbilityHandle> abilityHandleByInputMap = new();
        // PlayerController 在 Start 阶段统一初始化队伍；CharacterActor.Start 作为独立实例启用时的幂等兜底。
        private bool runtimeConfigurationInitialized;

        #endregion

        #region 属性

        /// <summary>获取角色稳定标识。</summary>
        public CharacterId CharacterId => config != null ? config.CharacterId : default;
        /// <summary>获取角色配置；Manager 在加载阶段校验该引用。</summary>
        public CharacterConfig Config => config;
        /// <summary>获取角色独立 ASC。</summary>
        public GameplayAbilitySystemComponent AbilitySystemComponent
        {
            get
            {
                EnsureDependencies();
                return abilitySystemComponent;
            }
        }
        /// <summary>获取角色 Animator。</summary>
        public Animator Animator
        {
            get
            {
                EnsureDependencies();
                return animator;
            }
        }
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
        /// <summary>获取对话系统可使用的当前动画播放器。</summary>
        public IAnimationPlayer AnimationPlayer
        {
            get
            {
                EnsureDependencies();
                return animationController;
            }
        }
        /// <summary>当角色仍有活动能力时，角色切换应直接拒绝。</summary>
        public bool IsBusy => AbilitySystemComponent.ActiveAbilities.Count > 0;
        /// <inheritdoc />
        public Transform RootTransform => characterRoot != null ? characterRoot : transform;
        /// <inheritdoc />
        public IMarkerProvider MarkerProvider
        {
            get
            {
                EnsureDependencies();
                return markerProvider;
            }
        }
        /// <inheritdoc />
        public ISkillRuntimeHost SkillRuntimeHost
        {
            get
            {
                EnsureDependencies();
                return skillRuntimeHost;
            }
        }
        /// <inheritdoc />
        public IMotionDriver MotionDriver => motionDriver;

        #endregion

        #region Unity 生命周期

        /// <summary>在角色包装 Prefab 的真实序列化边界解析并校验必需依赖。</summary>
        private void Awake() => EnsureDependencies();

        /// <summary>在所有 ASC Awake 完成后导入属性并授予角色配置的初始技能。</summary>
        private void Start() => InitializeFromConfig();

        /// <summary>在父级 PlayerController 先于子角色 Awake 时也能同步解析依赖。</summary>
        private void EnsureDependencies()
        {
            if (abilitySystemComponent == null) abilitySystemComponent = GetComponentInChildren<GameplayAbilitySystemComponent>(true);
            if (animator == null) animator = GetComponent<Animator>();
            if (animator == null)
                throw new InvalidOperationException($"角色 '{name}' 缺少 Animator。");
            if (!footBonesCached)
            {
                if (animator.avatar == null || !animator.isHuman)
                    throw new InvalidOperationException($"角色 '{name}' 的 Animator 必须配置 Humanoid Avatar 才能判断左右脚。");
                leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                footBonesCached = true;
            }
            if (markerProvider == null) markerProvider = GetComponentInChildren<MarkerProvider>(true);
            if (skillRuntimeHost == null) skillRuntimeHost = GetComponentInChildren<SkillRuntimeHost>(true);
            if (animationController == null) animationController = GetComponentInChildren<AnimationController>(true);
            if (abilitySystemComponent == null || animator == null || markerProvider == null || skillRuntimeHost == null ||
                animationController == null)
                throw new InvalidOperationException($"CharacterActor '{name}' 缺少同节点 Animator、AnimationController 或角色能力组件。 ");
            if (config == null)
                throw new InvalidOperationException($"CharacterActor '{name}' 未配置 CharacterConfig。");
            ValidateAbilityInputBindings();
            animator.applyRootMotion = true;
        }

        /// <summary>校验角色能力装配只使用固定六个战斗输入且不重复。</summary>
        private void ValidateAbilityInputBindings()
        {
            IReadOnlyList<CharacterAbilityInputBinding> bindings = config.AbilityInputBindings;
            if (bindings == null) return;
            var inputTypes = new HashSet<PlayerInputType>();
            var abilities = new HashSet<GameplayAbilityData>();
            foreach (CharacterAbilityInputBinding binding in bindings)
            {
                if (binding == null)
                    throw new InvalidOperationException($"CharacterActor '{name}' 的能力绑定列表包含空元素。 ");
                if (!IsAbilityInputType(binding.InputType))
                    throw new InvalidOperationException(
                        $"CharacterActor '{name}' 的能力绑定使用了非战斗输入 {binding.InputType}。 ");
                if (binding.Ability == null)
                    throw new InvalidOperationException(
                        $"CharacterActor '{name}' 的 {binding.InputType} 未配置 GameplayAbilityData。 ");
                if (!inputTypes.Add(binding.InputType))
                    throw new InvalidOperationException(
                        $"CharacterActor '{name}' 的能力输入 {binding.InputType} 被重复配置。 ");
                if (!abilities.Add(binding.Ability))
                    throw new InvalidOperationException(
                        $"CharacterActor '{name}' 的 Ability '{binding.Ability.name}' 被重复配置。 ");
            }
        }

        /// <summary>判断角色能力绑定是否使用固定的战斗输入槽位。</summary>
        /// <param name="inputType">待校验的输入类型。</param>
        /// <returns>属于 Primary、Secondary 或 Skill1-4 时返回 true。</returns>
        private static bool IsAbilityInputType(PlayerInputType inputType) => inputType == PlayerInputType.Primary ||
            inputType == PlayerInputType.Secondary ||
            inputType == PlayerInputType.Skill1 ||
            inputType == PlayerInputType.Skill2 ||
            inputType == PlayerInputType.Skill3 ||
            inputType == PlayerInputType.Skill4;

        /// <summary>仅在配置了 AttributeSet 且 ASC 尚未初始化时执行一次初始化。</summary>
        private void InitializeConfiguredAttributes()
        {
            EnsureDependencies();
            IReadOnlyList<GameplayAttributeSet> attributeSets = config.InitialAttributeSets;
            if (abilitySystemComponent.IsInitialized || attributeSets == null || attributeSets.Count == 0)
                return;
            abilitySystemComponent.Initialize(attributeSets);
        }

        /// <summary>把角色能力绑定中的 Ability 授予自身 ASC，并建立输入到 Spec Handle 的索引。</summary>
        private void GrantConfiguredAbilities()
        {
            IReadOnlyList<CharacterAbilityInputBinding> bindings = config.AbilityInputBindings;
            if (bindings == null || bindings.Count == 0)
                return;
            if (!abilitySystemComponent.IsInitialized)
                throw new InvalidOperationException(
                    $"CharacterActor '{name}' 的 ASC 尚未完成属性初始化，无法授予能力绑定。 ");

            abilityHandleByInputMap.Clear();
            foreach (CharacterAbilityInputBinding binding in bindings)
            {
                GameplayAbilityHandle handle = abilitySystemComponent.GiveAbility(binding.Ability, 1);
                if (!handle.IsValid)
                    throw new InvalidOperationException(
                        $"CharacterActor '{name}' 无法授予 {binding.InputType} 的 Ability '{binding.Ability.name}'。 ");
                abilityHandleByInputMap.Add(binding.InputType, handle);
            }
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
        internal void InitializeFromConfig()
        {
            if (runtimeConfigurationInitialized) return;
            if (config == null) throw new InvalidOperationException($"CharacterActor '{name}' 未配置 CharacterConfig。");
            config.Validate();
            InitializeConfiguredAttributes();
            GrantConfiguredAbilities();
            runtimeConfigurationInitialized = true;
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
            PlayerStateBlackboard blackboard)
        {
            characterRoot = root;
            motionDriver = driver;
            playerController = controller ?? throw new ArgumentNullException(nameof(controller));
            stateBlackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
            if (config == null) throw new InvalidOperationException($"CharacterActor '{name}' 未配置 CharacterConfig。");
            locomotion.Configure(config.Gravity, config.LocomotionTransition);
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
            presentationRenderers ??= GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in presentationRenderers) renderer.forceRenderingOff = !active;
            animator.enabled = active;
        }

        /// <summary>推进普通 ASC 阶段；后台角色也会执行。</summary>
        /// <param name="deltaTime">本帧缩放时间。</param>
        internal void TickAbility(float deltaTime) => abilitySystemComponent.Tick(deltaTime);

        /// <summary>直接查询角色能力绑定的 Request，激活成功后确认 Press 阶段。</summary>
        /// <param name="inputRequests">提供技能 Request 的输入缓冲区。</param>
        internal void ProcessAbilityInputRequests(IPlayerInputRequestBuffer inputRequests)
        {
            if (inputRequests == null) throw new ArgumentNullException(nameof(inputRequests));
            IReadOnlyList<CharacterAbilityInputBinding> bindings = config.AbilityInputBindings;
            if (bindings == null) return;
            foreach (CharacterAbilityInputBinding binding in bindings)
            {
                if (binding == null || binding.Ability == null ||
                    !inputRequests.TryGetRequest(binding.InputType,
                        out IReadOnlyPlayerInputRequest request) ||
                    !request.HasBufferedPress) continue;
                if (!abilityHandleByInputMap.TryGetValue(binding.InputType,
                        out GameplayAbilityHandle handle))
                    throw new InvalidOperationException(
                        $"CharacterActor '{name}' 的 {binding.InputType} 未建立 Ability Handle。 ");
                if (abilitySystemComponent.TryActivateAbility(handle, out _))
                    inputRequests.TryConfirmConsumed(request.PressHandle);
            }
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
