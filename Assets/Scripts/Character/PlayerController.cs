using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using RPG.Character.State;
using RPG.DialogueSystemModule;
using RPG.PlayerInputSystem;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.Generated;
using WS_Modules;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.TAG;
using WS_Modules.LogModule;
using WS_Modules.Singleton;

namespace RPG.Character
{
    /// <summary>稳定编排玩家输入、当前角色能力、Locomotion 与最终运动结算。</summary>
    [DefaultExecutionOrder(-800), DisallowMultipleComponent]
    [InfoBox(
        "依赖 Player 上的 PlayerInputController、DialogueParticipant，以及 CharacterRoot 上的 CharacterManager 和唯一 CharacterController；cameraTransform 可在 Inspector 指定，未绑定时低频查找 MainCamera。")]
    public sealed class PlayerController : SingletonMonoBase<PlayerController>, ILooseGameplayTagEventTarget
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
        [SerializeField]
        private CharacterEnvironmentDetector environmentDetector = new();
        // 缓存已绑定的 MainCamera；引用暂缺或被销毁时暂停移动输入并低频恢复。
        [SerializeField] private Transform cameraTransform;
        private LooseGameplayTagEventBridge looseGameplayTagEventBridge;
        private Coroutine frameIntentCleanupCoroutine;
        private Coroutine cameraResolveCoroutine;
        private bool dialogueSwitchLocked;
        private bool runtimeStarted;
        private bool hasLoggedMissingMainCamera;
        private int lastAnimatorMoveFrame = -1;
        private CancellationTokenSource initializationCancellationSource;
        #endregion

        #region 事件与属性
#if UNITY_EDITOR
        /// <summary>报告输入请求消费转交结果。</summary>
        internal event Action<GameplayTag, InputRequestHandle, bool> InputRequestConsumptionForwarded;
#endif
        /// <summary>角色队伍完成异步初始化后发送一次。</summary>
        public event Action Initialized;
        /// <summary>角色队伍初始化失败后发送一次。</summary>
        public event Action<Exception> InitializationFailed;
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
        /// <summary>解析 Player 依赖并建立运行时协调器，依赖完整后再注册单例。</summary>
        protected override void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError($"[PlayerController] 检测到重复 Player 控制器，销毁对象 '{name}'。", this);
                gameObject.SetActive(false);
                // 基类发现已有实例后负责安排重复 Player 根对象销毁。
                base.Awake();
                return;
            }

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
            characterManager.InitializationFailed += OnCharacterInitializationFailed;

            // MotionDriver 只绑定共享 CharacterController；Tag 来源稍后随 ActiveCharacter 注入。
            motionDriver.Initialize(characterController);
            // 角色配置尚未完成时保持 Suspended，避免外部误提交的请求在加载期结算。
            motionDriver.Suspend();

            // Blackboard 由稳定 Player 创建，随后以同一个引用注入全部 CharacterActor。
            StateBlackboard = new PlayerStateBlackboard(inputController);
            // 环境检测由一个总协调器统一推进；PlayerController 不直接依赖具体的 Locomotion 子检测器。
            environmentDetector.Initialize(characterRoot, StateBlackboard);

            // 输入仲裁必须在 GAS 消费前完成；默认策略由 Manager 统一装配。
            InputIntentArbiterManager = new GameplayInputIntentArbiterManager(
                inputController,
                StateBlackboard);
            InputIntentArbiterManager.RegisterDefaultArbiters();
            looseGameplayTagEventBridge = new LooseGameplayTagEventBridge(this);

            // 基类在硬依赖和协调器均初始化后发布单例并保留 Player 根对象。
            base.Awake();
            Debug.Log($"[PlayerController] 已注册并常驻 Player 单例，player={gameObject.name}。", this);
        }

        /// <summary>启动角色配置的异步加载；输入和 Blackboard 不等待该任务。</summary>
        private void Start() => InitializePlayerAsync().Forget(HandleInitializationException);

        /// <summary>异步等待角色队伍完成原子提交，并在 Ready 后恢复 MotionDriver。</summary>
        private async UniTask InitializePlayerAsync()
        {
            initializationCancellationSource ??= new CancellationTokenSource();
            try
            {
                await characterManager.InitializeAsync(
                    characterRoot,
                    motionDriver,
                    this,
                    StateBlackboard,
                    initializationCancellationSource.Token);
                if (!characterManager.IsReady) return;
                characterManager.ActiveCharacterChanged += OnActiveCharacterChanged;
                CharacterActor active = characterManager.ActiveCharacter ??
                                        throw new InvalidOperationException("CharacterManager Ready 后没有 ActiveCharacter。");
                motionDriver.SetActiveOwner(active, active.AbilitySystemComponent);
                motionDriver.Resume();
                runtimeStarted = true;
                active.Locomotion.Activate();
                dialogueParticipant?.SetAnimationPlayer(active.AnimationPlayer);
                Initialized?.Invoke();
            }
            catch (Exception exception)
            {
                InitializationFailed?.Invoke(exception);
                throw;
            }
        }

        /// <summary>集中接收 Forget 的异常，避免无观察者的 async void 继续传播。</summary>
        /// <param name="exception">异步初始化异常。</param>
        private void HandleInitializationException(Exception exception)
        {
            if (exception != null) Debug.LogException(exception, this);
        }

        /// <summary>转发 CharacterManager 的整批加载失败，不影响玩家级输入生命周期。</summary>
        /// <param name="exception">角色加载异常。</param>
        private void OnCharacterInitializationFailed(Exception exception)
        {
            WSLog.LogError("[PlayerController] 角色队伍初始化失败：" + exception.Message);
            InitializationFailed?.Invoke(exception);
        }

        /// <summary>等待角色队伍进入 Ready 或 Failed。</summary>
        /// <param name="cancellationToken">调用方取消令牌。</param>
        public async UniTask WaitUntilInitializedAsync(CancellationToken cancellationToken)
        {
            while (!characterManager.IsInitialized && characterManager.InitializationState != CharacterInitializationState.Failed)
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            if (characterManager.InitializationState == CharacterInitializationState.Failed)
                throw new InvalidOperationException("[PlayerController] 角色队伍初始化失败。");
        }

        /// <summary>恢复输入消费回传、LooseTag 桥接和帧末清理。</summary>
        private void OnEnable()
        {
            if (Instance != this) return;

            looseGameplayTagEventBridge?.Enable();
            if (cameraTransform == null && !ResolveMainCameraTransform("OnEnable"))
                BeginMainCameraResolution();
            if (StateBlackboard == null) return;
            lastAnimatorMoveFrame = -1;
            if (characterManager.IsReady)
                motionDriver.Resume();
            if (runtimeStarted && characterManager.IsReady)
                characterManager.ActiveCharacter?.Locomotion.Activate();
            StateBlackboard.IntentSourceConsumed += OnIntentSourceConsumed;
            frameIntentCleanupCoroutine = StartCoroutine(ClearFrameIntentsAtFrameEnd());
        }

        /// <summary>停止帧级协调并丢弃未结算的瞬时状态。</summary>
        private void OnDisable()
        {
            if (Instance != this) return;

            looseGameplayTagEventBridge?.Disable();
            if (cameraResolveCoroutine != null) StopCoroutine(cameraResolveCoroutine);
            cameraResolveCoroutine = null;
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

        /// <summary>释放 Player 事件资源并由基类注销静态单例引用。</summary>
        protected override void OnDestroy()
        {
            if (characterManager != null) characterManager.ActiveCharacterChanged -= OnActiveCharacterChanged;
            if (characterManager != null) characterManager.InitializationFailed -= OnCharacterInitializationFailed;
            initializationCancellationSource?.Cancel();
            initializationCancellationSource?.Dispose();
            characterManager?.CancelInitialization();
            looseGameplayTagEventBridge?.Dispose();
            if (Instance == this)
                Debug.Log($"[PlayerController] 已注销 Player 单例，player={gameObject.name}。", this);
            base.OnDestroy();
        }

        private void OnDrawGizmosSelected()
        {
            Transform drawTrans = GetComponentInChildren<CharacterManager>().transform;
            environmentDetector.OnGizmosDraw(drawTrans);
        }

        /// <summary>依次推进全队 ASC、输入分析、角色切换和当前角色普通阶段。</summary>
        private void Update()
        {
            try
            {
                // 环境事实在本帧最前面采样；Locomotion、技能与运动结算随后读取同一份完整快照。
                environmentDetector.TickUpdate(Time.deltaTime, StateBlackboard);
                // CharacterManager 负责遍历角色，但只由此处显式推进；后台角色的冷却和持续 GE 不因切人停止。
                characterManager.AdvanceAbilityFrame(Time.deltaTime);
                // 输入控制器已完成本帧采样；Manager 当前默认只调度需要镜头转换的 Move Arbiter。
                if (cameraTransform == null)
                {
                    // MainCamera 暂时缺失时必须丢弃移动意图，不能让仲裁器按世界轴继续移动。
                    inputController.ClearMoveInput();
                    StateBlackboard.ClearMoveInput();
                    BeginMainCameraResolution();
                }
                else
                {
                    InputIntentArbiterManager.ArbitrateFrame(cameraTransform);
                }

                // 切人 Request 的映射和消费由 CharacterManager 处理；玩家级对话锁仍在 PlayerController 门禁。
                // TODO: 优化切人门禁
                if (CanProcessCharacterSwitchInput())
                    characterManager.ProcessSwitchInputRequests(inputController);

                // 切换后由 Manager 重新读取 ActiveCharacter，确保同帧技能和 Locomotion 使用新角色。
                characterManager.AdvanceActiveFrame(inputController, Time.deltaTime);

                // 普通 Locomotion 与需要渲染帧同步的 GAS 运动在此统一仲裁并结算一次。
                motionDriver.ResolveUpdateMotion();
            }
            catch
            {
                // 业务阶段异常时不能把已提交但未结算的 Update 数据带到下一帧。
                motionDriver.ClearTransientRequests();
                throw;
            }
        }

        /// <summary>请求 CharacterManager 收集当前角色物理运动，然后由 MotionDriver 统一移动一次。</summary>
        private void FixedUpdate()
        {
            try
            {
                // Manager 只收集当前角色 GAS 与 Locomotion 请求，不执行最终 CharacterController.Move。
                if (!characterManager.AdvanceFixedStep(Time.fixedDeltaTime)) return;
                // 所有候选请求在同一物理边界统一仲裁；Resolve 自己清空瞬时提交，不跨步复用。
                motionDriver.ResolveFixedMotion();
            }
            catch
            {
                // 预检或提交阶段失败时清理 Fixed/Animator/Update 瞬时数据，避免下一阶段复用半成品。
                motionDriver.ClearTransientRequests();
                throw;
            }
        }

        /// <summary>请求 CharacterManager 推进全队能力与当前 Locomotion 延迟阶段。</summary>
        private void LateUpdate()
        {
            // Late 阶段只处理能力和 FSM 的延迟逻辑，避免同一帧出现第二次 CharacterController.Move。
            characterManager.AdvanceLateFrame(Time.deltaTime);
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
            try
            {
                if (!characterManager.TryAdvanceAnimatorStep(source, deltaPosition, deltaRotation, Time.deltaTime)) return;
                lastAnimatorMoveFrame = Time.frameCount;
                motionDriver.ResolveAnimatorMotion();
            }
            catch
            {
                // Animator 阶段异常时不得保留当前根运动提交。
                motionDriver.ClearTransientRequests();
                throw;
            }
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

        #region 摄像机绑定

        /// <summary>解析当前启用且带 MainCamera 标签的输出摄像机，不在逐帧路径中调用。</summary>
        /// <param name="reason">触发本次解析的生命周期原因。</param>
        /// <returns>找到有效主摄像机时返回 true。</returns>
        private bool ResolveMainCameraTransform(string reason)
        {
            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                cameraTransform = null;
                if (!hasLoggedMissingMainCamera)
                {
                    Debug.LogWarning($"[PlayerController] {reason} 时未找到 MainCamera，移动输入暂时停用。", this);
                    hasLoggedMissingMainCamera = true;
                }
                return false;
            }

            Transform resolvedTransform = mainCamera.transform;
            bool changed = cameraTransform != resolvedTransform;
            cameraTransform = resolvedTransform;
            hasLoggedMissingMainCamera = false;
            if (changed)
            {
                Vector3 horizontalForward = Vector3.ProjectOnPlane(resolvedTransform.forward, Vector3.up).normalized;
                Debug.Log($"[PlayerController] 已绑定主摄像机，reason={reason}, camera={mainCamera.name}, horizontalForward={horizontalForward}。", this);
            }
            return true;
        }

        /// <summary>主摄像机暂缺时低频重试；常规渲染帧不会重复查找场景对象。</summary>
        private void BeginMainCameraResolution()
        {
            if (!isActiveAndEnabled || cameraResolveCoroutine != null) return;
            cameraResolveCoroutine = StartCoroutine(ResolveMainCameraWhenAvailable());
        }

        /// <summary>摄像机缺失时低频检查 MainCamera 是否已恢复可用。</summary>
        /// <returns>等待主摄像机恢复的协程。</returns>
        private IEnumerator ResolveMainCameraWhenAvailable()
        {
            WaitForSecondsRealtime retryInterval = new WaitForSecondsRealtime(0.25f);
            while (isActiveAndEnabled && cameraTransform == null)
            {
                yield return retryInterval;
                if (ResolveMainCameraTransform("低频恢复检查")) break;
            }
            cameraResolveCoroutine = null;
        }

        #endregion

        #region 角色切换

        /// <summary>在玩家级阻断通过后切换角色。</summary>
        /// <param name="characterId">目标角色标识。</param>
        /// <returns>明确的切换状态。</returns>
        public CharacterSwitchStatus TrySwitchCharacter(CharacterId characterId)
        {
            if (!characterManager.IsReady) return CharacterSwitchStatus.NotInitialized;
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
            if (!characterManager.IsReady) return CharacterSwitchStatus.NotInitialized;
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
            if (!characterManager.IsReady || dialogueSwitchLocked) return false;
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
