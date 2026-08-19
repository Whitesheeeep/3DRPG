#if UNITY_EDITOR
using System;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules;
using WS_Modules.GAS.TAG;
using RPG.Character;
using RPG.Character.State;

namespace RPG.PlayerInputSystem.Tests
{
    /// <summary>使用真实 InputAction 验证 Request 仲裁、Intent 发布和三种消费模式。</summary>
    public sealed class GameplayInputOdinTester : MonoBehaviour
    {
        #region 测试配置
        [Title("Tag 数据库")]
        [SerializeField, AssetsOnly, Required]
        private GameplayTagDatabase tagDatabase;

        [Title("Intent")]
        [SerializeField] private GameplayTag pressIntentTag = GameplayTag.Empty;
        [SerializeField] private GameplayTag releaseIntentTag = GameplayTag.Empty;
        [SerializeField] private IntentConsumeTestMode consumeMode = IntentConsumeTestMode.Manual;
        [SerializeField, Min(0.01f)] private float consumeInterval = .2f;
        [SerializeField, Min(0f)] private float heldThreshold = 0.5f;
        [SerializeField, ReadOnly] private bool showOnGui;
        [SerializeField, ReadOnly] private string lastConfirmationResult = "尚未确认 Intent";
        [SerializeField, ReadOnly] private string lastPublicationResult = "尚未尝试发布 Intent";
        [SerializeField, ReadOnly] private string lastPressConfirmationResult = "尚未确认 Press Intent";
        [SerializeField, ReadOnly] private string lastReleaseConfirmationResult = "尚未确认 Release Intent";

        private PlayerInputController inputController;
        private PlayerController playerController;
        private PlayerStateBlackboard blackboard;
        private TestIntentArbiter testArbiter;
        private IntentConsumeTestMode observedMode;
        private float intervalElapsed;
        private GameplayTagDatabase previousTagDatabase;
        private bool ownsTagDatabase;
        private bool confirmationInProgress;
        private GameplayTag confirmationTag;
        private int confirmationAcceptedCount;
        private int confirmationRejectedCount;
        #endregion

        #region Unity 生命周期
        /// <summary>缓存真实运行时组件并创建由 Manager 执行的测试仲裁策略。</summary>
        private void Awake()
        {
            inputController = GetComponent<PlayerInputController>();
            playerController = GetComponent<PlayerController>();
            if (inputController == null || playerController == null)
                throw new InvalidOperationException("GameplayInputOdinTester 必须与 PlayerInputController 和 PlayerController 同节点。");

            blackboard = playerController.StateBlackboard;
            testArbiter = new TestIntentArbiter(this);
            observedMode = consumeMode;
        }

        /// <summary>初始化测试 Tag 数据库、订阅诊断事件并注册测试仲裁策略。</summary>
        private void OnEnable()
        {
            InitializeTagDatabase();
            playerController.InputIntentArbiterManager.IntentPublicationReported += OnIntentPublicationReported;
            playerController.InputRequestConsumptionForwarded += OnInputRequestConsumptionForwarded;
            playerController.InputIntentArbiterManager.RegisterArbiter(testArbiter);
            ValidateIntentTags();
        }

        /// <summary>在 PlayerController 完成本帧仲裁后推进自动消费测试。</summary>
        private void Update()
        {
            if (observedMode != consumeMode)
            {
                observedMode = consumeMode;
                intervalElapsed = 0f;
                ResetDiagnostics($"已切换为 {consumeMode}");
            }

            if (consumeMode == IntentConsumeTestMode.Interval) UpdateIntervalConsumption();
            else if (consumeMode == IntentConsumeTestMode.HeldThreshold) ConfirmAvailableIntents("HeldThreshold");
        }

        /// <summary>注销测试策略和诊断事件，并恢复进入测试前的 Tag 数据库。</summary>
        private void OnDisable()
        {
            if (playerController != null)
            {
                GameplayInputIntentArbiterManager manager = playerController.InputIntentArbiterManager;
                if (manager != null)
                {
                    manager.IntentPublicationReported -= OnIntentPublicationReported;
                    manager.UnregisterArbiter(testArbiter);
                }

                playerController.InputRequestConsumptionForwarded -= OnInputRequestConsumptionForwarded;
            }

            RestoreTagDatabase();
        }

        #endregion

        #region Tag 数据库
        /// <summary>为输入测试绑定当前场景配置的运行时 Tag 数据库。</summary>
        private void InitializeTagDatabase()
        {
            previousTagDatabase = GameplayTagManager.Instance.Database;
            ownsTagDatabase = tagDatabase != null && previousTagDatabase != tagDatabase;
            if (tagDatabase != null && ownsTagDatabase)
                GameplayTagManager.Instance.Initialize(tagDatabase);
        }

        /// <summary>只在 Manager 仍由本测试器接管时恢复之前的数据库引用。</summary>
        private void RestoreTagDatabase()
        {
            if (!ownsTagDatabase || GameplayTagManager.Instance.Database != tagDatabase) return;
            if (previousTagDatabase != null) GameplayTagManager.Instance.Initialize(previousTagDatabase);
            else GameplayTagManager.Instance.Reset();
            ownsTagDatabase = false;
        }

        /// <summary>检查测试 Intent 是否存在于当前数据库，并把错误直接显示到面板。</summary>
        private void ValidateIntentTags()
        {
            if (!GameplayTagManager.Instance.IsInitialized)
            {
                lastPublicationResult = "Tag Database 未初始化，Intent 不会发布";
                return;
            }

            bool pressValid = GameplayTagManager.Instance.IsValidTag(pressIntentTag);
            bool releaseValid = GameplayTagManager.Instance.IsValidTag(releaseIntentTag);
            if (!pressValid || !releaseValid)
                lastPublicationResult = $"Tag 无效：Press={pressValid}, Release={releaseValid}";
        }
        #endregion

        #region 自动消费
        /// <summary>使用真实帧间隔推进周期计时，并在到点时尝试消费两种 Intent。</summary>
        private void UpdateIntervalConsumption()
        {
            intervalElapsed += Time.unscaledDeltaTime;
            if (intervalElapsed < consumeInterval) return;
            intervalElapsed %= consumeInterval;
            ConfirmAvailableIntents("Interval");
        }

        /// <summary>分别确认当前帧的 Press 与 Release Intent，保留两阶段独立结果。</summary>
        /// <param name="reason">触发本次自动确认的测试模式。</param>
        private void ConfirmAvailableIntents(string reason)
        {
            string pressResult = ConfirmIntent(pressIntentTag, "Press", reason);
            string releaseResult = ConfirmIntent(releaseIntentTag, "Release", reason);
            lastConfirmationResult = $"{reason}: {pressResult} | {releaseResult}";
        }

        /// <summary>模拟业务成功后通过黑板确认指定阶段 Intent，并记录 Request 实际接受数量。</summary>
        /// <param name="intentTag">需要确认的测试 Intent Tag。</param>
        /// <param name="stageName">用于面板和日志的阶段名称。</param>
        /// <param name="reason">本次确认的触发来源。</param>
        /// <returns>本次确认的结构化文字结果。</returns>
        private string ConfirmIntent(GameplayTag intentTag, string stageName, string reason)
        {
            bool intentPresentBefore = blackboard.HasIntent(intentTag);
            confirmationTag = intentTag;
            confirmationAcceptedCount = 0;
            confirmationRejectedCount = 0;
            confirmationInProgress = true;
            bool blackboardConfirmed = blackboard.TryConfirmIntentConsumed(intentTag);
            confirmationInProgress = false;

            string result = $"{reason} {stageName}: IntentBefore={intentPresentBefore}, " +
                            $"BlackboardConfirmed={blackboardConfirmed}, " +
                            $"RequestAccepted={confirmationAcceptedCount}, " +
                            $"RequestRejected={confirmationRejectedCount}";
            if (stageName == "Press") lastPressConfirmationResult = result;
            else lastReleaseConfirmationResult = result;
            return result;
        }

        /// <summary>处理 OnGUI 手动确认按钮。</summary>
        /// <param name="intentTag">需要确认的 Intent Tag。</param>
        /// <param name="stageName">输入阶段名称。</param>
        private void ConfirmIntentFromGui(GameplayTag intentTag, string stageName)
        {
            lastConfirmationResult = ConfirmIntent(intentTag, stageName, "Manual");
            Debug.Log($"[InputTest] {lastConfirmationResult}", this);
        }

        /// <summary>清除最近一次自动消费结果，避免模式切换后继续显示旧结论。</summary>
        /// <param name="reason">重置原因。</param>
        private void ResetDiagnostics(string reason)
        {
            lastConfirmationResult = reason;
            lastPublicationResult = "尚未尝试发布 Intent";
            lastPressConfirmationResult = "尚未确认 Press Intent";
            lastReleaseConfirmationResult = "尚未确认 Release Intent";
        }
        #endregion

        #region 诊断事件
        /// <summary>记录仲裁管理器尝试发布 Intent 的结果。</summary>
        /// <param name="intentTag">尝试发布的 Intent Tag。</param>
        /// <param name="sourceHandle">Intent 来源句柄。</param>
        /// <param name="published">黑板是否成功接收该 Intent。</param>
        private void OnIntentPublicationReported(GameplayTag intentTag, InputRequestHandle sourceHandle, bool published)
        {
            lastPublicationResult = $"Frame={Time.frameCount}, Tag={intentTag}, Handle={sourceHandle}, Published={published}";
        }

        /// <summary>记录 PlayerController 转交来源句柄后的真实 Request 接受结果。</summary>
        /// <param name="intentTag">来源对应的 Intent Tag。</param>
        /// <param name="handle">被转交的 Request 阶段句柄。</param>
        /// <param name="accepted">输入 Controller 是否接受该句柄。</param>
        private void OnInputRequestConsumptionForwarded(GameplayTag intentTag, InputRequestHandle handle, bool accepted)
        {
            if (!confirmationInProgress || intentTag != confirmationTag) return;
            if (accepted)
            {
                confirmationAcceptedCount++;
                Debug.Log($"[InputTest] Request {handle} for Intent {intentTag} accepted.", this);
            }
            else confirmationRejectedCount++;
        }
        #endregion

        #region Odin 显示控制
        /// <summary>切换真实输入调试面板；关闭后不再执行任何 OnGUI 绘制。</summary>
        [Button("切换 Input OnGUI", ButtonSizes.Medium)]
        private void ToggleOnGui()
        {
            showOnGui = !showOnGui;
            Debug.Log($"[InputTest] OnGUI 显示已{(showOnGui ? "开启" : "关闭")}。", this);
        }
        #endregion

        #region OnGUI 调试面板
        /// <summary>绘制 Tag 状态、当前 Intent、历史发布、消费结果和真实 Request。</summary>
        private void OnGUI()
        {
            if (!showOnGui || blackboard == null) return;
            GameplayTagManager tagManager = GameplayTagManager.Instance;
            GUILayout.BeginArea(new Rect(16f, 16f, 820f, Screen.height - 32f), "Player Input Debug", GUI.skin.window);
            GUILayout.Label($"Mode: {consumeMode} | Frame: {Time.frameCount}");
            GUILayout.Label($"Interval: {intervalElapsed:F3}/{consumeInterval:F3}s | Held Threshold: {heldThreshold:F3}s");
            GUILayout.Label($"Tag Database: {(tagManager.IsInitialized ? tagManager.Database.name : "未初始化")}");
            GUILayout.Label($"Tag Valid: Press={tagManager.IsValidTag(pressIntentTag)}, Release={tagManager.IsValidTag(releaseIntentTag)}");
            GUILayout.Label($"Registered Arbiters: {playerController.InputIntentArbiterManager.Arbiters.Count}");
            GUILayout.Label($"Current Press Intent: {blackboard.HasIntent(pressIntentTag)} ({pressIntentTag})");
            GUILayout.Label($"Current Release Intent: {blackboard.HasIntent(releaseIntentTag)} ({releaseIntentTag})");
            GUILayout.Label($"Last Publication: {lastPublicationResult}", GUI.skin.box);
            GUILayout.Label($"Last Press Confirmation: {lastPressConfirmationResult}", GUI.skin.box);
            GUILayout.Label($"Last Release Confirmation: {lastReleaseConfirmationResult}", GUI.skin.box);
            GUILayout.Label($"Last Confirm Summary: {lastConfirmationResult}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Confirm Press")) ConfirmIntentFromGui(pressIntentTag, "Press");
            if (GUILayout.Button("Confirm Release")) ConfirmIntentFromGui(releaseIntentTag, "Release");
            GUILayout.EndHorizontal();
            GUILayout.Space(8f);
            GUILayout.Label($"Active Requests: {inputController.Requests.Count}");
            if (inputController.Requests.Count == 0) GUILayout.Label("当前没有活跃 Request。", GUI.skin.box);
            for (int i = 0; i < inputController.Requests.Count; i++) DrawRequest(inputController.Requests[i]);
            GUILayout.EndArea();
        }

        /// <summary>绘制单个 Request 的阶段状态与自动消费窗口警告。</summary>
        /// <param name="request">待显示的只读输入 Request。</param>
        private void DrawRequest(IReadOnlyPlayerInputRequest request)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label($"{request.InputType} | {request.PhysicalState} | Held {request.HeldDuration:F3}s");
            GUILayout.Label($"Press: {request.HasBufferedPress} | {request.PressBufferRemaining:F3}s | {request.PressHandle}");
            GUILayout.Label($"Release: {request.HasBufferedRelease} | {request.ReleaseBufferRemaining:F3}s | {request.ReleaseHandle}");
            float requiredWait = consumeMode == IntentConsumeTestMode.Interval
                ? Mathf.Max(0f, consumeInterval - intervalElapsed)
                : Mathf.Max(0f, heldThreshold - request.HeldDuration);
            if (consumeMode != IntentConsumeTestMode.Manual &&
                ((request.HasBufferedPress && request.PressBufferRemaining < requiredWait) ||
                 (request.HasBufferedRelease && request.ReleaseBufferRemaining < requiredWait)))
                GUILayout.Label("警告：Buffer 剩余时间短于当前自动消费等待时间。", GUI.skin.box);
            GUILayout.EndVertical();
        }
        #endregion

        #region 嵌套类型
        /// <summary>定义测试器可选择的互斥 Intent 消费模式。</summary>
        private enum IntentConsumeTestMode
        {
            Manual,
            Interval,
            HeldThreshold
        }

        /// <summary>把真实 Request 映射为测试 Intent，并执行 HeldThreshold 发布门槛。</summary>
        private sealed class TestIntentArbiter : GameplayInputIntentArbiter
        {
            private readonly GameplayInputOdinTester owner;

            /// <summary>创建读取指定测试器配置的仲裁策略。</summary>
            /// <param name="owner">提供模式、阈值和测试 Tag 的宿主。</param>
            public TestIntentArbiter(GameplayInputOdinTester owner) => this.owner = owner;

            /// <summary>根据测试模式决定当前阶段是否发布测试 Intent。</summary>
            protected override bool TryResolveIntent(IReadOnlyPlayerInputRequest request,
                PlayerInputRequestStage stage, PlayerStateBlackboard stateBlackboard, out GameplayTag intentTag)
            {
                intentTag = stage == PlayerInputRequestStage.Press ? owner.pressIntentTag : owner.releaseIntentTag;
                if (!intentTag.IsValid || !GameplayTagManager.Instance.IsValidTag(intentTag)) return false;
                if (owner.consumeMode != IntentConsumeTestMode.HeldThreshold) return true;
                if (request.HeldDuration < owner.heldThreshold) return false;
                return stage == PlayerInputRequestStage.Press
                    ? request.PhysicalState == PlayerInputPhysicalState.Held
                    : request.PhysicalState == PlayerInputPhysicalState.Released;
            }
        }
        #endregion
    }
}
#endif
