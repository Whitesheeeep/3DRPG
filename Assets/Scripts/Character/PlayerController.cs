using System;
using System.Collections;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.TAG;
using RPG.Character.State;
using RPG.Markers;
using RPG.PlayerInputSystem;
using RPG.SkillSystem;
using WS_Modules;

namespace RPG.Character
{
    /// <summary>统一编排角色 ASC 的 Unity 更新时序，并持有角色移动驱动。</summary>
    [DefaultExecutionOrder(-800)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GameplayAbilitySystemComponent), typeof(Animator), typeof(CharacterController))]
    [RequireComponent(typeof(PlayerInputController))]
    [RequireComponent(typeof(SkillRuntimeHost))]
    public sealed class PlayerController : MonoBehaviour, IGameplayAbilitySystemOwner, IGameplayAbilitySystemTagBridge
    {
        #region 序列化引用与属性

        [SerializeField] private GameplayAbilitySystemComponent abilitySystemComponent;
        [SerializeField] private Animator animator;
        [SerializeField] private CharacterController characterController;
        [SerializeField] private PlayerInputController inputController;
        [SerializeField] private SkillRuntimeHost skillRuntimeHost;
        [SerializeField] private MotionDriver motionDriver = new();
        private IMarkerProvider markerProvider;
        private Coroutine frameIntentCleanupCoroutine;
        // 由 PlayerController 持有事件桥接生命周期，不让 ASC 反向订阅全局事件。
        private LooseGameplayTagEventBridge looseGameplayTagEventBridge;

        /// <summary>获取由当前角色长期持有的移动驱动。</summary>
        public IMotionDriver MotionDriver => motionDriver;

        /// <summary>获取由当前 PlayerController 独占并管理生命周期的玩家状态黑板。</summary>
        public PlayerStateBlackboard StateBlackboard { get; private set; }

        /// <inheritdoc />
        public Transform RootTransform => transform;

        /// <inheritdoc />
        public IMarkerProvider MarkerProvider => markerProvider;

        /// <inheritdoc />
        public ISkillRuntimeHost SkillRuntimeHost => skillRuntimeHost;

        /// <inheritdoc />
        public GameplayAbilitySystemComponent AbilitySystemComponent => abilitySystemComponent;

        /// <inheritdoc />
        public GameObject TagEventTarget => gameObject;

        /// <summary>获取当前角色拥有的输入 Intent 仲裁管理器。</summary>
        public GameplayInputIntentArbiterManager InputIntentArbiterManager { get; private set; }

        #endregion

        #if UNITY_EDITOR
        #region 输入消费事件
        /// <summary>报告黑板来源句柄转交给输入 Controller 后的实际接受结果。</summary>
        internal event Action<GameplayTag, InputRequestHandle, bool> InputRequestConsumptionForwarded;
        #endregion
        #endif

        #region Unity 生命周期

        /// <summary>校验角色序列化依赖并构造不参与 Unity 生命周期的移动驱动。</summary>
        /// <exception cref="InvalidOperationException">任一必需角色依赖未配置时抛出。</exception>
        private void Awake()
        {
            // 同节点组件是 PlayerController 的稳定契约；序列化引用为空时按该契约自动取得。
            if (abilitySystemComponent == null)
                abilitySystemComponent = GetComponent<GameplayAbilitySystemComponent>();
            if (animator == null) animator = GetComponent<Animator>();
            if (characterController == null) characterController = GetComponent<CharacterController>();
            if (inputController == null) inputController = GetComponent<PlayerInputController>();
            if (skillRuntimeHost == null) skillRuntimeHost = GetComponent<SkillRuntimeHost>();
            markerProvider = GetComponent<IMarkerProvider>();
            if (abilitySystemComponent == null || animator == null || characterController == null || inputController == null)
                throw new InvalidOperationException(
                    $"PlayerController '{name}' 必须配置 ASC、Animator、CharacterController 和 PlayerInputController。");
            if (skillRuntimeHost == null)
                throw new InvalidOperationException(
                    $"PlayerController '{name}' 必须配置 SkillRuntimeHost，才能作为 Skill Gameplay Ability 宿主。");

            StateBlackboard = new PlayerStateBlackboard(abilitySystemComponent);
            InputIntentArbiterManager = new GameplayInputIntentArbiterManager(inputController, StateBlackboard);
            InputIntentArbiterManager.RegisterDefaultArbiters();
            motionDriver.Initialize(animator, characterController, abilitySystemComponent);
            looseGameplayTagEventBridge = new LooseGameplayTagEventBridge(this);
        }

        /// <summary>启动真正帧末执行的 Intent 清理循环。</summary>
        private void OnEnable()
        {
            looseGameplayTagEventBridge?.Enable();
            // Controller 统一协调自己持有的黑板与输入组件，消费回传不属于仲裁器职责。
            StateBlackboard.IntentSourceConsumed += OnIntentSourceConsumed;
            frameIntentCleanupCoroutine = StartCoroutine(ClearFrameIntentsAtFrameEnd());
        }

        /// <summary>停止帧末循环并清空当前角色持有的全部临时黑板状态。</summary>
        private void OnDisable()
        {
            looseGameplayTagEventBridge?.Disable();
            // 先切断消费回传，再重置帧级状态，避免禁用期间继续改变 Request。
            StateBlackboard.IntentSourceConsumed -= OnIntentSourceConsumed;
            if (frameIntentCleanupCoroutine != null)
            {
                StopCoroutine(frameIntentCleanupCoroutine);
                frameIntentCleanupCoroutine = null;
            }

            StateBlackboard?.Reset();
        }

        /// <summary>释放玩家专属的 LooseTag 事件桥接器。</summary>
        private void OnDestroy() => looseGameplayTagEventBridge?.Dispose();

        /// <summary>按角色控制器定义的顺序推进 ASC 普通阶段。</summary>
        private void Update()
        {
            abilitySystemComponent.Tick(Time.deltaTime);
            // 输入 Controller 已在更早执行序推进 Request，此处基于最新 ASC 与黑板状态统一仲裁。
            InputIntentArbiterManager.ArbitrateFrame();
        }

        /// <summary>先推进 ASC 固定状态，再为后续 Locomotion 推进移动驱动固定阶段。</summary>
        private void FixedUpdate()
        {
            abilitySystemComponent.FixedTick(Time.fixedDeltaTime);
        }

        /// <summary>在 Animator 求值完成后让当前 Active Ability 决定是否消费根运动。</summary>
        private void OnAnimatorMove() => abilitySystemComponent.UpdateAnimationMove();

        /// <summary>在普通更新和 Animator 求值后推进 ASC 延迟阶段。</summary>
        private void LateUpdate() => abilitySystemComponent.LateTick(Time.deltaTime);

        /// <summary>在每帧渲染提交前的末端清除未消费 Intent，不向 RequestBuffer 发送消费确认。</summary>
        /// <returns>持续等待帧末的 Unity 协程迭代器。</returns>
        private IEnumerator ClearFrameIntentsAtFrameEnd()
        {
            while (true)
            {
                yield return CoroutineTool.WaitForEndOfFrame();
                StateBlackboard.ClearFrameIntents();
            }
            // ReSharper disable once IteratorNeverReturns
        }

        #endregion

        #region 输入消费协调

        /// <summary>把黑板确认的 Intent 来源阶段提交给所属输入 Controller。</summary>
        /// <param name="intentTag">已确认消费的帧级 Intent Tag。</param>
        /// <param name="handle">业务成功消费 Intent 后由黑板返回的来源句柄。</param>
        private void OnIntentSourceConsumed(GameplayTag intentTag, InputRequestHandle handle)
        {
            // 只有 Request Controller 接受匹配的版本与阶段，才算完成输入阶段消费。
            bool accepted = inputController.TryConfirmConsumed(handle);
            InputRequestConsumptionForwarded?.Invoke(intentTag, handle, accepted);
        }

        #endregion
    }
}
