using System;
using System.Collections.Generic;
using RPG.Character.State;
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
    [InfoBox("依赖同节点 Animator、AnimationController，以及同节点或子节点中的 ASC、MarkerProvider 与 SkillRuntimeHost；可选 initialAttributeSets 在 Start 导入角色初始数值；子树 Renderer 用于隐藏后台角色。")]
    public sealed class CharacterActor : MonoBehaviour, IGameplayAbilitySystemOwner
    {
        #region 配置与运行时状态

        // 角色能力与表现依赖：ASC 保持启用供后台 Tick，Animator 仅由当前表现状态启用。
        [SerializeField] private CharacterId characterId;
        [SerializeField] private GameplayAbilitySystemComponent abilitySystemComponent;
        [SerializeField] private Animator animator;
        [SerializeField] private MarkerProvider markerProvider;
        [SerializeField] private SkillRuntimeHost skillRuntimeHost;
        [SerializeField] private AnimationController animationController;
        // 角色专属数值模板；Start 在 ASC 完成 Awake 后导入，避免与外部测试初始化竞态。
        [SerializeField] private GameplayAttributeSet[] initialAttributeSets = Array.Empty<GameplayAttributeSet>();
        [SerializeField] private CharacterLocomotionStateMachine locomotion = new();
        [SerializeField] private CharacterAbilityInputBinding[] abilityInputBindings = Array.Empty<CharacterAbilityInputBinding>();

        // Player 注入的稳定运行时依赖，不随角色切换重新创建。
        private Transform characterRoot;
        private IMotionDriver motionDriver;
        private PlayerController playerController;
        // 角色表现 Renderer 缓存，SetActivePresentation 时用于隐藏后台角色。
        private Renderer[] presentationRenderers;

        #endregion

        #region 属性

        /// <summary>获取角色稳定标识。</summary>
        public CharacterId CharacterId => characterId;
        /// <summary>获取该角色离散技能输入配置。</summary>
        public IReadOnlyList<CharacterAbilityInputBinding> AbilityInputBindings => abilityInputBindings;
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
        /// <summary>获取当前角色 Locomotion FSM。</summary>
        public CharacterLocomotionStateMachine Locomotion => locomotion;
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

        /// <summary>在所有 ASC Awake 完成后导入角色配置的初始 AttributeSet。</summary>
        private void Start() => InitializeConfiguredAttributes();

        /// <summary>在父级 PlayerController 先于子角色 Awake 时也能同步解析依赖。</summary>
        private void EnsureDependencies()
        {
            if (abilitySystemComponent == null) abilitySystemComponent = GetComponentInChildren<GameplayAbilitySystemComponent>(true);
            if (animator == null) animator = GetComponent<Animator>();
            if (markerProvider == null) markerProvider = GetComponentInChildren<MarkerProvider>(true);
            if (skillRuntimeHost == null) skillRuntimeHost = GetComponentInChildren<SkillRuntimeHost>(true);
            if (animationController == null) animationController = GetComponentInChildren<AnimationController>(true);
            if (abilitySystemComponent == null || animator == null || markerProvider == null || skillRuntimeHost == null ||
                animationController == null)
                throw new InvalidOperationException($"CharacterActor '{name}' 缺少同节点 Animator、AnimationController 或角色能力组件。 ");
            animator.applyRootMotion = true;
        }

        /// <summary>仅在配置了 AttributeSet 且 ASC 尚未初始化时执行一次初始化。</summary>
        private void InitializeConfiguredAttributes()
        {
            EnsureDependencies();
            if (abilitySystemComponent.IsInitialized || initialAttributeSets == null || initialAttributeSets.Length == 0)
                return;
            abilitySystemComponent.Initialize(initialAttributeSets);
        }

        #endregion

        #region 运行时绑定与阶段推进

        /// <summary>把稳定 CharacterRoot 与 Player 持有的运动请求接口注入角色。</summary>
        /// <param name="root">所有队伍角色共享的空间根节点。</param>
        /// <param name="driver">GAS 与 Locomotion 共用的请求接口。</param>
        internal void BindRuntime(Transform root, IMotionDriver driver, PlayerController controller)
        {
            characterRoot = root;
            motionDriver = driver;
            playerController = controller ?? throw new ArgumentNullException(nameof(controller));
            locomotion.Initialize(this, driver);
        }

        /// <summary>接收同节点 Animator 的根运动增量并交给稳定 PlayerController 统一结算。</summary>
        private void OnAnimatorMove()
        {
            if (playerController == null) return;
            playerController.ProcessAnimatorMotion(this, animator.deltaPosition, animator.deltaRotation);
        }

        /// <summary>应用队伍 Definition 的实例身份，允许同一包装 Prefab 用于多个配置。</summary>
        /// <param name="identity">经过队伍初始化校验的稳定角色键。</param>
        internal void AssignIdentity(CharacterId identity) => characterId = identity;

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

        /// <summary>在输入仲裁之后尝试激活已授予 GA，只有激活成功才确认来源消费。</summary>
        /// <param name="blackboard">玩家稳定黑板。</param>
        internal void ConsumeAbilityIntents(PlayerStateBlackboard blackboard)
        {
            foreach (CharacterAbilityInputBinding binding in abilityInputBindings)
            {
                if (!blackboard.HasIntent(binding.IntentTag)) continue;
                foreach (GameplayAbilitySpec spec in abilitySystemComponent.GrantedAbilities)
                {
                    if (spec.Data != binding.Ability) continue;
                    if (abilitySystemComponent.TryActivateAbility(spec.Handle, out _))
                        blackboard.TryConfirmIntentConsumed(binding.IntentTag);
                    break;
                }
            }
        }

        /// <summary>推进当前角色 ASC 物理阶段。</summary>
        /// <param name="fixedDeltaTime">本物理步时长。</param>
        internal void FixedTickAbility(float fixedDeltaTime) => abilitySystemComponent.FixedTick(fixedDeltaTime);

        /// <summary>推进 Animator 求值后的能力阶段。</summary>
        internal void UpdateAnimationMoveAbility() => abilitySystemComponent.UpdateAnimationMove();

        /// <summary>推进 ASC 延迟阶段。</summary>
        /// <param name="deltaTime">本帧缩放时间。</param>
        internal void LateTickAbility(float deltaTime) => abilitySystemComponent.LateTick(deltaTime);

        #endregion
    }
}
