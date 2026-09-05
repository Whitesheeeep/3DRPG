using System;
using System.Collections;
using RPG.Character.State;
using RPG.DialogueSystemModule;
using RPG.PlayerInputSystem;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.Generated;
using WS_Modules;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.TAG;

namespace RPG.Character
{
    /// <summary>稳定编排玩家输入、当前角色能力、Locomotion 与最终运动结算。</summary>
    [DefaultExecutionOrder(-800), DisallowMultipleComponent]
    [InfoBox(
        "依赖 Player 上的 PlayerInputController、DialogueParticipant，以及 CharacterRoot 上的 CharacterManager 和唯一 CharacterController；cameraTransform 可由常驻摄像机系统注入。")]
    public sealed class PlayerController : MonoBehaviour, ILooseGameplayTagEventTarget
    {
        #region 配置与运行时状态
        // 稳定 Player 依赖：这些引用只在 Awake 解析一次，角色切换不重建控制器。
        [SerializeField]
        private PlayerInputController inputController;
        [SerializeField]
        private DialogueParticipant dialogueParticipant;
        [SerializeField]
        private Transform characterRoot;
        [SerializeField]
        private CharacterManager characterManager;
        [SerializeField]
        private CharacterController characterController;
        [SerializeField]
        private MotionDriver motionDriver = new();
        // 可选常驻摄像机基准；为空时输入仲裁 Manager 使用世界 X/Z 作为回退。
        [SerializeField] private Transform cameraTransform;
        private LooseGameplayTagEventBridge looseGameplayTagEventBridge;
        private Coroutine frameIntentCleanupCoroutine;
        private bool dialogueSwitchLocked;
        private bool runtimeStarted;
        private int lastAnimatorMoveFrame = -1;
        #endregion

        #region 事件与属性
#if UNITY_EDITOR
        /// <summary>报告输入请求消费转交结果。</summary>
        internal event Action<GameplayTag, InputRequestHandle, bool> InputRequestConsumptionForwarded;
#endif
        /// <summary>获取稳定 Player 持有的状态黑板。</summary>
        public PlayerStateBlackboard StateBlackboard { get; private set; }
        /// <summary>获取玩家输入 Intent Tag 仲裁器。</summary>
        public GameplayInputIntentArbiterManager InputIntentArbiterManager { get; private set; }
        /// <summary>获取当前角色管理器。</summary>
        public CharacterManager CharacterManager => characterManager;
        /// <summary>向 GAS 与 Locomotion 暴露同一个运动请求接口。</summary>
        public IMotionDriver MotionDriver => motionDriver;
        /// <inheritdoc />
        GameplayAbilitySystemComponent ILooseGameplayTagEventTarget.AbilitySystemComponent =>
            characterManager.ActiveCharacter?.AbilitySystemComponent;
        /// <inheritdoc />
        GameObject ILooseGameplayTagEventTarget.TagEventTarget => gameObject;
        #endregion

        #region Unity 生命周期与阶段编排
        /// <summary>解析稳定依赖并初始化队伍与唯一运动出口。</summary>
        private void Awake()
        {
            // 依赖解析只发生在稳定 Player 上；角色切换或普通场景切换不会重新寻找这些对象。
            if (inputController == null) inputController = GetComponent<PlayerInputController>();
            if (dialogueParticipant == null) dialogueParticipant = GetComponent<DialogueParticipant>();
            if (characterManager == null) characterManager = GetComponentInChildren<CharacterManager>(true);
            if (characterController == null) characterController = GetComponentInChildren<CharacterController>(true);
            if (characterRoot == null && characterManager != null) characterRoot = characterManager.transform;
            if (inputController == null || characterManager == null || characterController == null ||
                characterRoot == null)
                throw new InvalidOperationException(
                    $"PlayerController '{name}' 缺少输入、CharacterRoot、CharacterManager 或 CharacterController。");
            DontDestroyOnLoad(gameObject);

            // MotionDriver 只绑定共享 CharacterController；Tag 来源稍后随 ActiveCharacter 注入。
            motionDriver.Initialize(characterController);

            // Blackboard 由稳定 Player 创建，随后以同一个引用注入全部 CharacterActor。
            StateBlackboard = new PlayerStateBlackboard();

            // CharacterManager 只负责构建队伍和选出当前角色；阶段推进仍由 PlayerController 主动调用。
            characterManager.Initialize();
            // 每个角色获得同一个 IMotionDriver、PlayerController 回调入口和 Blackboard，保留独立 ASC、Animator 与 FSM。
            foreach (CharacterActor actor in characterManager.Characters)
            {
                actor.BindRuntime(characterRoot, motionDriver, this, StateBlackboard);
                // 在首次可能显示前预热 Idle；预热期间由 CharacterActor 屏蔽 AnimatorMove，避免产生移动副作用。
                actor.PrimeIdlePose();
            }
            characterManager.ActiveCharacterChanged += OnActiveCharacterChanged;
            CharacterActor active = characterManager.ActiveCharacter ??
                                    throw new InvalidOperationException("CharacterManager 初始化后没有可用 ActiveCharacter。");

            // ActiveOwner 决定本帧可获胜的请求；Tag 读取也从当前 ASC 开始。
            motionDriver.SetActiveOwner(active, active.AbilitySystemComponent);

            // 输入仲裁必须在 GAS 消费前完成；默认策略由 Manager 统一装配。
            InputIntentArbiterManager = new GameplayInputIntentArbiterManager(
                inputController,
                StateBlackboard);
            InputIntentArbiterManager.RegisterDefaultArbiters();
            looseGameplayTagEventBridge = new LooseGameplayTagEventBridge(this);
            dialogueParticipant?.SetAnimationPlayer(active.AnimationPlayer);
        }

        /// <summary>
        /// 在所有角色和 ASC 完成 Awake 后初始化配置，并激活当前角色 Locomotion。
        /// </summary>
        private void Start()
        {
            // PlayerController 的执行顺序早于角色 Start；这里集中完成 ASC 初始化，避免 Awake 阶段读取未建好的 Attribute 容器。
            foreach (CharacterActor actor in characterManager.Characters)
                actor.InitializeRuntimeConfiguration();

            CharacterActor active = characterManager.ActiveCharacter ??
                                    throw new InvalidOperationException("CharacterManager 初始化后没有可用 ActiveCharacter。 ");
            runtimeStarted = true;
            // 运行时配置就绪后再直接选择 Idle/CodeLocomotion，持续输入切人仍由 Activate 内部决定目标状态。
            active.Locomotion.Activate();
        }

        /// <summary>恢复输入消费回传、LooseTag 桥接和帧末清理。</summary>
        private void OnEnable()
        {
            looseGameplayTagEventBridge?.Enable();
            if (StateBlackboard == null) return;
            lastAnimatorMoveFrame = -1;
            motionDriver.Resume();
            if (runtimeStarted)
                characterManager.ActiveCharacter?.Locomotion.Activate();
            StateBlackboard.IntentSourceConsumed += OnIntentSourceConsumed;
            frameIntentCleanupCoroutine = StartCoroutine(ClearFrameIntentsAtFrameEnd());
        }

        /// <summary>停止帧级协调并丢弃未结算的瞬时状态。</summary>
        private void OnDisable()
        {
            looseGameplayTagEventBridge?.Disable();
            lastAnimatorMoveFrame = -1;
            inputController?.ClearMoveInput();
            if (StateBlackboard != null) StateBlackboard.IntentSourceConsumed -= OnIntentSourceConsumed;
            if (frameIntentCleanupCoroutine != null) StopCoroutine(frameIntentCleanupCoroutine);
            frameIntentCleanupCoroutine = null;
            StateBlackboard?.ClearMoveInput();
            StateBlackboard?.ClearFrameIntents();
            characterManager?.ActiveCharacter?.Locomotion.Deactivate();
            motionDriver.Suspend();
        }

        /// <summary>释放事件订阅与全局事件桥。</summary>
        private void OnDestroy()
        {
            if (characterManager != null) characterManager.ActiveCharacterChanged -= OnActiveCharacterChanged;
            looseGameplayTagEventBridge?.Dispose();
        }

        /// <summary>依次推进全队 ASC、输入分析、角色切换和当前角色普通阶段。</summary>
        private void Update()
        {
            // CharacterManager 负责遍历角色，但只由此处显式推进；后台角色的冷却和持续 GE 不因切人停止。
            characterManager.TickCharacters(Time.deltaTime);
            // 输入控制器已完成本帧采样；Manager 当前默认只调度需要镜头转换的 Move Arbiter。
            InputIntentArbiterManager.ArbitrateFrame(cameraTransform);

            // 切人 Request 的映射和消费由 CharacterManager 处理；玩家级对话锁仍在 PlayerController 门禁。
            if (CanProcessCharacterSwitchInput())
                characterManager.ProcessSwitchInputRequests(inputController);

            // 切换后由 Manager 重新读取 ActiveCharacter，确保同帧技能和 Locomotion 使用新角色。
            characterManager.TickActiveCharacter(inputController, Time.deltaTime);
        }

        /// <summary>请求 CharacterManager 收集当前角色物理运动，然后由 MotionDriver 统一移动一次。</summary>
        private void FixedUpdate()
        {
            // Manager 只收集当前角色 GAS 与 Locomotion 请求，不执行最终 CharacterController.Move。
            characterManager.FixedTickActiveCharacter(Time.fixedDeltaTime);
            // 所有候选请求在同一物理边界统一仲裁；Resolve 自己清空瞬时提交，不跨步复用。
            motionDriver.ResolveFixedMotion();
        }

        /// <summary>请求 CharacterManager 推进全队能力与当前 Locomotion 延迟阶段。</summary>
        private void LateUpdate()
        {
            // Late 阶段只处理能力和 FSM 的延迟逻辑，避免同一帧出现第二次 CharacterController.Move。
            characterManager.LateTickCharacters(Time.deltaTime);
        }

        /// <summary>接收当前 Character Animator 的增量并在业务阶段之后统一结算。</summary>
        /// <param name="source">产生回调的角色。</param>
        /// <param name="deltaPosition">Animator 根位移增量。</param>
        /// <param name="deltaRotation">Animator 根旋转增量。</param>
        /// <remarks>
        /// 该方法是 CharacterActor 对 PlayerController 的普通调用入口，不能命名为 Unity 保留的
        /// <c>OnAnimatorMove</c>，否则 Unity 会按消息方法校验参数并输出签名错误。
        /// </remarks>
        internal void ProcessAnimatorMotion(CharacterActor source, Vector3 deltaPosition, Quaternion deltaRotation)
        {
            if (!isActiveAndEnabled) return;
            // 同一渲染帧只允许当前角色结算一次，避免重复 Animator 求值导致根运动被重复消费。
            if (lastAnimatorMoveFrame == Time.frameCount) return;
            // CharacterManager 验证来源并推进当前角色动画阶段；MotionDriver 仍由 PlayerController 最后结算。
            if (!characterManager.TryUpdateAnimationMove(source, deltaPosition, deltaRotation, Time.deltaTime)) return;
            lastAnimatorMoveFrame = Time.frameCount;
            motionDriver.ResolveAnimatorMotion();
        }

        /// <summary>在渲染帧末清理未消费 Intent。</summary>
        /// <returns>持续等待帧末的协程。</returns>
        private IEnumerator ClearFrameIntentsAtFrameEnd()
        {
            while (true)
            {
                yield return CoroutineTool.WaitForEndOfFrame();
                StateBlackboard.ClearFrameIntents();
            }
        }
        #endregion

        #region 角色切换
        /// <summary>在玩家级阻断通过后切换角色。</summary>
        /// <param name="characterId">目标角色标识。</param>
        /// <returns>明确的切换状态。</returns>
        public CharacterSwitchStatus TrySwitchCharacter(CharacterId characterId)
        {
            if (!characterManager.IsInitialized) return CharacterSwitchStatus.NotInitialized;
            if (dialogueSwitchLocked ||
                characterManager.ActiveCharacter == null ||
                characterManager.ActiveCharacter.AbilitySystemComponent.Tags.HasTag(
                    GameplayTags.Tag_State_Block_AbilityActivation))
                return CharacterSwitchStatus.CharacterBusy;
            return characterManager.TrySwitch(characterId);
        }

        /// <summary>处理一个玩家级队伍槽位切换入口，并保留角色管理器的明确结果。</summary>
        /// <param name="slotIndex">从零开始的队伍槽位下标。</param>
        /// <returns>玩家级检查或队伍切换的结果。</returns>
        public CharacterSwitchStatus TrySwitchCharacterSlot(int slotIndex)
        {
            if (!characterManager.IsInitialized) return CharacterSwitchStatus.NotInitialized;
            if (dialogueSwitchLocked ||
                characterManager.ActiveCharacter == null ||
                characterManager.ActiveCharacter.AbilitySystemComponent.Tags.HasTag(
                    GameplayTags.Tag_State_Block_AbilityActivation))
                return CharacterSwitchStatus.CharacterBusy;
            return characterManager.TrySwitchSlot(slotIndex);
        }

        /// <summary>设置对话是否阻止角色切换。</summary>
        /// <param name="locked">是否正在占用角色表现。</param>
        public void SetDialogueSwitchLocked(bool locked) => dialogueSwitchLocked = locked;

        /// <summary>同步切换 MotionDriver Owner 与对话动画目标。</summary>
        /// <param name="previous">旧当前角色。</param>
        /// <param name="current">新当前角色。</param>
        private void OnActiveCharacterChanged(CharacterActor previous, CharacterActor current)
        {
            if (previous != null)
            {
                previous.Locomotion.Deactivate();
                motionDriver.ReleaseAll(previous);
            }

            motionDriver.SetActiveOwner(current, current.AbilitySystemComponent);
            looseGameplayTagEventBridge?.RebindActiveAbilitySystem();
            // IntentTag 和连续 Move 表示稳定 Player 的输入意图；切人不清理本帧输入事实。
            current.Locomotion.Activate();
            dialogueParticipant?.SetAnimationPlayer(current.AnimationPlayer);
        }
        #endregion

        #region 切人输入门禁
        /// <summary>判断当前玩家级条件是否允许 CharacterManager 处理切人 Request。</summary>
        /// <returns>允许处理时返回 true。</returns>
        private bool CanProcessCharacterSwitchInput()
        {
            if (!characterManager.IsInitialized || dialogueSwitchLocked) return false;
            CharacterActor active = characterManager.ActiveCharacter;
            return active != null && !active.AbilitySystemComponent.Tags.HasTag(
                GameplayTags.Tag_State_Block_AbilityActivation);
        }
        #endregion

        #region 输入消费协调
        /// <summary>把业务确认的 Intent 来源提交给输入 Controller。</summary>
        /// <param name="intentTag">已消费 Intent。</param>
        /// <param name="handle">来源请求句柄。</param>
        private void OnIntentSourceConsumed(GameplayTag intentTag, InputRequestHandle handle)
        {
            bool accepted = inputController.TryConfirmConsumed(handle);
#if UNITY_EDITOR
            InputRequestConsumptionForwarded?.Invoke(intentTag, handle, accepted);
#endif
        }

        #endregion
    }
}
