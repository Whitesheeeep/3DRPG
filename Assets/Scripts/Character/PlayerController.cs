using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using RPG.Character.State;
using RPG.Character.Combat;
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
        "必需：同一 Player 对象上的 PlayerInputController，以及 Player 子层级（包含非激活对象）中的 CharacterManager 和唯一 CharacterController；缺少任一项时 Awake 会中止初始化。DialogueParticipant 为可选，缺失时跳过对话动画衔接。CharacterRoot 可指定，未指定时使用 CharacterManager 所在 Transform。cameraTransform 可指定为带 Camera 组件的 Transform；未指定时低频查找启用的 MainCamera，暂缺时移动输入暂停并继续重试。")]
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
        // 独立启动场景可保留自动初始化；被统一流程管理的玩家应关闭此项，由场景任务启动。
        [SerializeField, Tooltip("关闭后由统一场景流程的玩家初始化任务启动当前角色队伍。")]
        private bool initializeOnStart = true;
        [SerializeField]
        private MotionDriver motionDriver = new();
        [SerializeField]
        private CharacterEnvironmentDetector environmentDetector = new();
        // 缓存已绑定的 MainCamera；引用暂缺或被销毁时暂停移动输入并低频恢复。
        [SerializeField] private Transform cameraTransform;
        [SerializeField, MinValue(1f)] private float lockTargetRadius = 15f;
        [SerializeField] private LayerMask lockTargetLayers = 1 << 7;
        private Camera gameplayCamera;
        private LockTargetSystem lockTargetSystem;
        private LooseGameplayTagEventBridge looseGameplayTagEventBridge;
        private Coroutine frameIntentCleanupCoroutine;
        private Coroutine cameraResolveCoroutine;
        private bool dialogueSwitchLocked;
        private bool runtimeStarted;
        // 统一加载流程在完整成功前暂停玩家控制，但允许异步队伍初始化和出生点定位。
        private bool scenePreparationActive;
        // 统一场景流程控制状态：多个入口共享一次玩家初始化任务和完成信号。
        private bool initializationStarted;
        private UniTaskCompletionSource initializationCompletionSource;
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
        /// <summary>获取队伍共用的角色根节点，作为镜头跟随位置和锁定构图基准。</summary>
        public Transform CharacterRoot => characterRoot;
        /// <summary>获取当前缓存摄像机上的 Camera 组件，用于锁定候选屏幕排序。</summary>
        public Camera GameplayCamera => gameplayCamera;
        /// <summary>向 GAS 与 Locomotion 暴露同一个运动请求接口。</summary>
        public IMotionDriver MotionDriver => motionDriver;
        /// <summary>获取角色队伍与活动角色是否已经可以进行出生点定位。</summary>
        public bool IsReady => characterManager.IsReady && runtimeStarted;
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
            if (lockTargetRadius <= 0f || float.IsNaN(lockTargetRadius) || float.IsInfinity(lockTargetRadius) ||
                lockTargetLayers.value == 0)
                throw new InvalidOperationException(
                    $"[PlayerController] '{name}' 的锁定半径必须为有限正数，且 Enemy LayerMask 不能为空。");
            if (cameraTransform != null)
                gameplayCamera = cameraTransform.GetComponent<Camera>();
            if (cameraTransform != null && gameplayCamera == null)
                throw new InvalidOperationException(
                    $"[PlayerController] '{name}' 的 cameraTransform 必须绑定带 Camera 组件的 MainCamera。");
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

        /// <summary>按 Inspector 策略自动初始化，供独立启动场景保持兼容。</summary>
        private void Start()
        {
            if (initializeOnStart)
                BeginInitializationIfNeeded();
        }

        /// <summary>开始或等待共享 Player 初始化，并将取消局限于当前调用方的等待。</summary>
        /// <param name="cancellationToken">当前场景任务的协作式取消令牌。</param>
        /// <returns>角色队伍和 PlayerController 收尾全部完成的共享结果。</returns>
        public UniTask InitializeForSceneAsync(CancellationToken cancellationToken)
        {
            BeginInitializationIfNeeded();
            return initializationCompletionSource.Task.AttachExternalCancellation(cancellationToken);
        }

        /// <summary>为直接启动或统一加载入口只创建一次共享初始化任务。</summary>
        private void BeginInitializationIfNeeded()
        {
            if (!initializationStarted)
            {
                initializationStarted = true;
                initializationCompletionSource = new UniTaskCompletionSource();
                InitializePlayerAsync().Forget(HandleInitializationException);
                WSLog.Log($"[PlayerController] 玩家初始化任务已启动，player={gameObject.name}。");
            }
        }

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
                if (!characterManager.IsReady)
                    throw new InvalidOperationException("[PlayerController] CharacterManager 初始化任务结束，但角色队伍未进入 Ready 状态。");
                characterManager.ActiveCharacterChanged += OnActiveCharacterChanged;
                CharacterActor activeActor = characterManager.ActiveCharacter ??
                                        throw new InvalidOperationException("CharacterManager Ready 后没有 ActiveCharacter。");
                motionDriver.SetActiveOwner(activeActor, activeActor.AbilitySystemComponent);
                runtimeStarted = true;
                if (scenePreparationActive)
                {
                    // 场景加载期间允许完成数据初始化，但运动请求保持暂停直到展示层宣布流程成功。
                    activeActor.Locomotion.Deactivate();
                    motionDriver.Suspend();
                }
                else
                {
                    motionDriver.Resume();
                    activeActor.Locomotion.Activate();
                }
                dialogueParticipant?.SetAnimationPlayer(activeActor.AnimationPlayer);
                Initialized?.Invoke();
                initializationCompletionSource.TrySetResult();
                WSLog.Log($"[PlayerController] 玩家初始化及控制器收尾已完成，player={gameObject.name}，character={activeActor.name}。");
            }
            catch (Exception exception)
            {
                InitializationFailed?.Invoke(exception);
                initializationCompletionSource?.TrySetException(exception);
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

            inputController.ImmediateInputPerformed += OnImmediateInputPerformed;
            looseGameplayTagEventBridge?.Enable();
            if (cameraTransform == null && !ResolveMainCameraTransform("OnEnable"))
                BeginMainCameraResolution();
            if (StateBlackboard == null) return;
            lastAnimatorMoveFrame = -1;
            if (characterManager.IsReady && !scenePreparationActive)
                motionDriver.Resume();
            if (runtimeStarted && characterManager.IsReady && !scenePreparationActive)
                characterManager.ActiveCharacter?.Locomotion.Activate();
            StateBlackboard.IntentSourceConsumed += OnIntentSourceConsumed;
            frameIntentCleanupCoroutine = StartCoroutine(ClearFrameIntentsAtFrameEnd());
        }

        /// <summary>停止帧级协调并丢弃未结算的瞬时状态。</summary>
        private void OnDisable()
        {
            if (Instance != this) return;

            if (inputController != null)
                inputController.ImmediateInputPerformed -= OnImmediateInputPerformed;
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
            if (Instance == this)
                lockTargetSystem?.ClearLockedTarget(E_LockTargetChangeReason.PlayerDestroyed);
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

        /// <summary>选中玩家对象时绘制角色环境检测器的调试范围。</summary>
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
                if (scenePreparationActive)
                {
                    inputController.ClearMoveInput();
                    StateBlackboard.ClearMoveInput();
                    StateBlackboard.ClearFrameIntents();
                    motionDriver.ClearTransientRequests();
                    return;
                }
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
                lockTargetSystem = LockTargetSystem.Instance;
                lockTargetSystem.ValidateTarget(characterManager.ActiveCharacter, lockTargetRadius);
                characterManager.AdvanceActiveFrame(inputController, Time.deltaTime);

                // HitStop 生效后丢弃本帧动作位移提交；缓冲输入仍由原输入系统按真实时间管理。
                if (characterManager.ActiveCharacter?.IsActionPaused == true)
                    motionDriver.ClearTransientRequests();
                else
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
            if (scenePreparationActive)
            {
                motionDriver.ClearTransientRequests();
                return;
            }
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
            if (scenePreparationActive) return;
            // Late 阶段只处理能力和 FSM 的延迟逻辑，避免同一帧出现第二次 CharacterController.Move。
            characterManager.AdvanceLateFrame(Time.deltaTime);
        }

        #region 场景准备

        /// <summary>暂停场景切换期间的输入与角色运动，防止初始化完成后提前进入游戏。</summary>
        public void BeginScenePreparation()
        {
            if (scenePreparationActive) return;
            scenePreparationActive = true;
            inputController.ClearMoveInput();
            StateBlackboard?.ClearMoveInput();
            StateBlackboard?.ClearFrameIntents();
            characterManager.ActiveCharacter?.Locomotion.Deactivate();
            motionDriver.Suspend();
            Debug.Log($"[PlayerController] 已进入场景准备状态，player={name}。", this);
        }

        /// <summary>将共享角色根节点定位到出生点，并重建运动、环境与 Locomotion 状态。</summary>
        /// <param name="worldPosition">出生点的世界位置，表示 CharacterRoot 原点。</param>
        /// <param name="horizontalForward">出生点世界 +Z 投影到水平面的朝向。</param>
        /// <exception cref="InvalidOperationException">角色队伍尚未就绪或出生点朝向无效时抛出。</exception>
        public void ApplySpawnPose(Vector3 worldPosition, Vector3 horizontalForward)
        {
            if (!IsReady)
            {
                Debug.LogError("[PlayerController] 角色队伍尚未就绪，不能应用出生点。", this);
                throw new InvalidOperationException("[PlayerController] 角色队伍尚未就绪，不能应用出生点。");
            }
            if (horizontalForward.sqrMagnitude <= 0.0001f)
            {
                Debug.LogError("[PlayerController] 出生点的水平前方向无效。", this);
                throw new InvalidOperationException("[PlayerController] 出生点的水平前方向无效。");
            }

            motionDriver.Suspend();
            motionDriver.ClearTransientRequests();
            bool controllerWasEnabled = characterController.enabled;
            try
            {
                // CharacterController 不参与根节点瞬移碰撞解算；恢复其原有启用状态后重新采样新位置。
                characterController.enabled = false;
                characterRoot.SetPositionAndRotation(
                    worldPosition,
                    Quaternion.LookRotation(horizontalForward.normalized, Vector3.up));
            }
            finally
            {
                characterController.enabled = controllerWasEnabled;
            }

            environmentDetector.ResetAfterTeleport(StateBlackboard);
            CharacterActor active = characterManager.ActiveCharacter;
            active.Locomotion.Deactivate();
            if (!scenePreparationActive)
                motionDriver.Resume();
            active.Locomotion.Activate();
            motionDriver.ClearTransientRequests();
            Debug.Log($"[PlayerController] 已应用场景出生点，player={name}，position={worldPosition}，forward={horizontalForward.normalized}。", this);
        }

        /// <summary>完整场景流程成功后恢复活动角色运动和游戏输入。</summary>
        public void CompleteScenePreparation()
        {
            if (!scenePreparationActive) return;
            scenePreparationActive = false;
            if (runtimeStarted && characterManager.IsReady)
            {
                motionDriver.Resume();
                characterManager.ActiveCharacter?.Locomotion.Activate();
            }
            inputController.ClearMoveInput();
            StateBlackboard?.ClearMoveInput();
            StateBlackboard?.ClearFrameIntents();
            Debug.Log($"[PlayerController] 场景准备完成，已恢复角色操作，player={name}。", this);
        }

        #endregion

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
            if (!isActiveAndEnabled || scenePreparationActive) return;
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
                gameplayCamera = null;
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
            gameplayCamera = mainCamera;
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

        #region 锁定输入回调

        /// <summary>将 Player Map 即时锁定输入交给全局目标系统处理。</summary>
        /// <param name="inputType">触发的锁定或滚轮输入。</param>
        private void OnImmediateInputPerformed(E_PlayerInputType inputType)
        {
            if (!isActiveAndEnabled || characterManager == null || !characterManager.IsReady ||
                cameraTransform == null)
                return;

            CharacterActor activeCharacter = characterManager.ActiveCharacter;
            if (activeCharacter == null)
                return;

            lockTargetSystem ??= LockTargetSystem.Instance;
            Camera gameplayCamera = GameplayCamera;
            switch (inputType)
            {
                case E_PlayerInputType.LockToggle:
                    lockTargetSystem.ToggleLock(activeCharacter, gameplayCamera, lockTargetRadius, lockTargetLayers);
                    break;
                case E_PlayerInputType.LockPrevious:
                    lockTargetSystem.SwitchTarget(activeCharacter, gameplayCamera, -1, lockTargetRadius, lockTargetLayers);
                    break;
                case E_PlayerInputType.LockNext:
                    lockTargetSystem.SwitchTarget(activeCharacter, gameplayCamera, 1, lockTargetRadius, lockTargetLayers);
                    break;
            }
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
