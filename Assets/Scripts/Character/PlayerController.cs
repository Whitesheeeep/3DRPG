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
        private bool sceneTransitioning;
        private bool playerInputLocked;
        private bool dialogueSwitchLocked;
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

            // CharacterManager 只负责构建队伍和选出当前角色，不接收运动请求或推进时序。
            characterManager.Initialize();
            // 每个角色获得同一个 IMotionDriver 和 PlayerController 回调入口，但保留独立 ASC、Animator 与 FSM。
            foreach (CharacterActor actor in characterManager.Characters)
                actor.BindRuntime(characterRoot, motionDriver, this);
            characterManager.ActiveCharacterChanged += OnActiveCharacterChanged;
            CharacterActor active = characterManager.ActiveCharacter ??
                                    throw new InvalidOperationException("CharacterManager 初始化后没有可用 ActiveCharacter。");

            // ActiveOwner 决定本帧可获胜的请求；Tag 读取也从当前 ASC 开始。
            motionDriver.SetActiveOwner(active, active.AbilitySystemComponent);

            // Blackboard 实例属于 Player，切人时只替换它代理的 ASC，不重建长期持有者。
            StateBlackboard = new PlayerStateBlackboard(active.AbilitySystemComponent);
            active.Locomotion.Activate();

            // 输入仲裁必须在 GAS 消费前完成，LooseTag 与对话均绑定稳定 Player 身份。
            InputIntentArbiterManager = new GameplayInputIntentArbiterManager(inputController, StateBlackboard);
            InputIntentArbiterManager.RegisterArbiter(
                new CharacterInputIntentArbiter(() => characterManager.ActiveCharacter));
            looseGameplayTagEventBridge = new LooseGameplayTagEventBridge(this);
            dialogueParticipant?.SetAnimationPlayer(active.AnimationPlayer);
        }

        /// <summary>恢复输入消费回传、LooseTag 桥接和帧末清理。</summary>
        private void OnEnable()
        {
            looseGameplayTagEventBridge?.Enable();
            if (StateBlackboard == null) return;
            lastAnimatorMoveFrame = -1;
            motionDriver.Resume();
            characterManager.ActiveCharacter.Locomotion.Activate();
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

        /// <summary>依次推进全队 ASC、输入 Tag 仲裁与当前角色普通阶段。</summary>
        private void Update()
        {
            // 先推进全部 ASC，后台角色的冷却、持续 GE 和普通能力时间不会因切人停止。
            characterManager.TickCharacters(Time.deltaTime);
            // AbilityTags 必须是本帧最新值；Manager 在同一入口统一发布离散 IntentTag 和连续 Move。
            InputIntentArbiterManager.ArbitrateFrame(cameraTransform);

            // 场景切换过程中或者对话占用时，玩家输入不再被消费到当前角色的 ASC。
            if (!sceneTransitioning && !playerInputLocked)
                characterManager.ActiveCharacter.ConsumeAbilityIntents(StateBlackboard);

            // Manager 始终保留真实输入；玩家级锁定只阻止当前 Locomotion 响应，不篡改 Blackboard。
            Vector3 locomotionInput = sceneTransitioning || playerInputLocked
                ? Vector3.zero
                : StateBlackboard.MoveWorldInput;
            characterManager.ActiveCharacter.Locomotion.SetMovementInput(locomotionInput);
            // Update 只更新动画状态和请求生命周期；CharacterController.Move 延迟到 Fixed 或 AnimatorMove 结算。
            characterManager.ActiveCharacter.Locomotion.Tick(Time.deltaTime);
        }

        /// <summary>同步收集 GAS 与 Locomotion 的物理请求，然后统一移动一次。</summary>
        private void FixedUpdate()
        {
            if (sceneTransitioning) return;
            CharacterActor active = characterManager.ActiveCharacter;
            // 先让当前 GA 提交技能位移，再让 Locomotion 提交代码移动和重力。
            active.FixedTickAbility(Time.fixedDeltaTime);
            active.Locomotion.FixedTick(Time.fixedDeltaTime);
            // 所有候选请求在同一物理边界统一仲裁；Resolve 自己清空瞬时提交，不跨步复用。
            motionDriver.ResolveFixedMotion();
        }

        /// <summary>推进全队能力与当前 Locomotion 延迟阶段，不产生额外移动。</summary>
        private void LateUpdate()
        {
            // Late 阶段只处理能力和 FSM 的延迟逻辑，避免同一帧出现第二次 CharacterController.Move。
            characterManager.LateTickCharacters(Time.deltaTime);
            if (!sceneTransitioning)
                characterManager.ActiveCharacter.Locomotion.LateTick(Time.deltaTime);
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
            if (!isActiveAndEnabled || sceneTransitioning ||
                !ReferenceEquals(source, characterManager.ActiveCharacter)) return;
            // 同一渲染帧只允许当前角色结算一次，避免重复 Animator 求值导致根运动被重复消费。
            if (lastAnimatorMoveFrame == Time.frameCount) return;
            lastAnimatorMoveFrame = Time.frameCount;
            // AnimatorMove 阶段先让当前 GA 和 Locomotion 读取增量，再由 MotionDriver 按控制权决定是否消费。
            source.UpdateAnimationMoveAbility();
            source.Locomotion.UpdateAnimationMove(deltaPosition, Time.deltaTime);
            motionDriver.ResolveAnimatorMotion(deltaPosition, deltaRotation);
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

        #region 切换与场景迁移
        /// <summary>在玩家级阻断通过后切换角色。</summary>
        /// <param name="characterId">目标角色标识。</param>
        /// <returns>明确的切换状态。</returns>
        public CharacterSwitchStatus TrySwitchCharacter(CharacterId characterId)
        {
            if (sceneTransitioning || playerInputLocked || dialogueSwitchLocked ||
                StateBlackboard.AbilityTags.HasTag(GameplayTags.Tag_State_Block_AbilityActivation))
                return CharacterSwitchStatus.CharacterBusy;
            return characterManager.TrySwitch(characterId);
        }

        /// <summary>设置玩家级输入锁。</summary>
        /// <param name="locked">是否阻止切换。</param>
        public void SetPlayerInputLocked(bool locked) => playerInputLocked = locked;

        /// <summary>设置对话是否阻止角色切换。</summary>
        /// <param name="locked">是否正在占用角色表现。</param>
        public void SetDialogueSwitchLocked(bool locked) => dialogueSwitchLocked = locked;

        /// <summary>暂停运动并禁用碰撞器，准备迁移 CharacterRoot。</summary>
        public void PrepareForSceneTransition()
        {
            sceneTransitioning = true;
            motionDriver.Suspend();
            motionDriver.ClearTransientRequests();
            characterController.enabled = false;
        }

        /// <summary>设置新场景出生位姿并恢复控制。</summary>
        /// <param name="spawnPose">CharacterRoot 世界位姿。</param>
        public void CompleteSceneTransition(Pose spawnPose)
        {
            if (!sceneTransitioning)
                throw new InvalidOperationException("必须先 PrepareForSceneTransition，再设置场景出生位姿。");
            characterRoot.SetPositionAndRotation(spawnPose.position, spawnPose.rotation);
            characterController.enabled = true;
            motionDriver.Resume();
            sceneTransitioning = false;
        }

        /// <summary>取消迁移并恢复运动出口。</summary>
        public void CancelSceneTransition()
        {
            characterController.enabled = true;
            motionDriver.ClearTransientRequests();
            motionDriver.Resume();
            sceneTransitioning = false;
        }

        /// <summary>同步切换 MotionDriver Owner、Blackboard ASC 与对话动画目标。</summary>
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
            StateBlackboard?.BindAbilitySystem(current.AbilitySystemComponent);
            // IntentTag 和连续 Move 表示稳定 Player 的输入意图；切人只更换 ASC 来源，不清理本帧意图。
            current.Locomotion.Activate();
            dialogueParticipant?.SetAnimationPlayer(current.AnimationPlayer);
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
