using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;
using WS_Modules.Singleton;

namespace RPG.PlayerInputSystem
{
    /// <summary>统一管理离散 InputAction、输入 Request 及其真实时间缓冲生命周期。</summary>
    [DefaultExecutionOrder(-900)]
    public sealed class PlayerInputController : SingletonMonoBase<PlayerInputController>, IPlayerInputRequestBuffer
    {
        #region 常量与依赖字段

        // 输入 Map 名称
        private const string PlayerActionMapName = "Player";
        private const string UiActionMapName = "UI";

        // Inspector 配置字段
        [SerializeField, LabelText("Input Actions Asset")]
        private InputActionAsset inputActionAsset;
        [SerializeField] private List<PlayerInputBinding> bindings = new();
        // 连续移动输入仅由本组件采样，离散 Request 的缓冲与消费不共用该状态。
        [SerializeField] private InputActionReference moveAction;
        [SerializeField, MinValue(0f), MaxValue(1f)] private float moveDeadzone = 0.1f;

        #endregion

        #region 请求状态与运行时映射

        // 请求状态
        // key：PlayerInputType；value：该输入当前手势的 Request。
        private readonly Dictionary<PlayerInputType, PlayerInputRequest> requestsByType = new();
        // key：InputAction；value：该动作唯一对应的输入绑定资产。
        private readonly Dictionary<InputAction, ResolvedBinding> bindingsByAction = new();
        private readonly List<PlayerInputRequest> requests = new();

        // 依赖映射
        private InputAction resolvedMoveAction;
        private InputActionMap playerActionMap;
        private InputActionMap uiActionMap;
        private bool gameplayInputBlocked;

        /// <inheritdoc />
        public IReadOnlyList<IReadOnlyPlayerInputRequest> Requests => requests;

        #endregion

        #region 即时输入事件

        /// <summary>玩家输入控制器单例建立或销毁时通知窗口协调器重新应用输入锁。</summary>
        public static event Action<PlayerInputController> InstanceChanged;

        /// <summary>当配置为即时交付的 InputAction 发生 performed 时通知订阅者。</summary>
        public event Action<PlayerInputType> ImmediateInputPerformed;

        /// <summary>通过输入类型从内部索引查询请求，供 Arbiter 在不扫描列表的情况下读取输入阶段。</summary>
        /// <param name="inputType">需要查询的输入类型。</param>
        /// <param name="request">找到时返回当前手势的只读请求。</param>
        /// <returns>存在该输入类型请求时返回 true。</returns>
        public bool TryGetRequest(PlayerInputType inputType, out IReadOnlyPlayerInputRequest request)
        {
            if (requestsByType.TryGetValue(inputType, out PlayerInputRequest resolvedRequest))
            {
                request = resolvedRequest;
                return true;
            }

            request = null;
            return false;
        }

        /// <summary>获取当前帧采样的连续移动输入；该值保留模拟摇杆幅度。</summary>
        public Vector2 MoveInput { get; private set; }
        #endregion

        #region Unity 生命周期
        /// <summary>校验输入配置后注册场景单例，确保外部不会读取半初始化组件。</summary>
        /// <exception cref="InvalidOperationException">Actions 资源、Map 或 Action 引用配置无效时抛出。</exception>
        protected override void Awake()
        {
            // 重复 Player 必须在配置解析与 InputAction 订阅前停用；基类负责销毁重复根对象。
            if (Instance != null && Instance != this)
            {
                Debug.LogError(
                    $"[PlayerInputController] 检测到重复 Player 输入控制器，销毁对象 '{name}'。", this);
                gameObject.SetActive(false);
                base.Awake();
                return;
            }

            // 资源是 Map 的唯一来源，避免从某个 Action 反向推导控制器配置。
            if (inputActionAsset == null)
                throw CreateConfigurationException(
                    $"[PlayerInputController] '{name}' 未配置有效的 InputActionAsset。");

            playerActionMap = inputActionAsset.FindActionMap(PlayerActionMapName, false);
            if (playerActionMap == null)
                throw CreateConfigurationException(
                    $"[PlayerInputController] '{name}' 指定的 InputActionAsset 缺少 {PlayerActionMapName} Map。");

            uiActionMap = inputActionAsset.FindActionMap(UiActionMapName, false);
            if (uiActionMap == null)
                throw CreateConfigurationException(
                    $"[PlayerInputController] '{name}' 指定的 InputActionAsset 缺少 {UiActionMapName} Map。");

            // 移动引用只声明具体 Action；所有权通过已指定的资源和 Player Map 进行校验。
            resolvedMoveAction = ResolveAndValidateActionReference(
                moveAction, playerActionMap, "移动输入 moveAction");
            if (resolvedMoveAction.type != InputActionType.Value ||
                !string.Equals(resolvedMoveAction.expectedControlType, "Vector2", StringComparison.Ordinal))
                throw CreateConfigurationException(
                    $"[PlayerInputController] '{name}' 的 moveAction 必须是 Value/Vector2 Action。");

            BuildBindingLookup();

            // 单例注册和 DontDestroyOnLoad 由基类统一负责，放在所有输入校验之后。
            base.Awake();
            Debug.Log($"[PlayerInputController] 已注册场景单例，player={gameObject.name}。", this);
            InstanceChanged?.Invoke(this);
        }

        /// <summary>订阅所有绑定；玩法动作随 Player Map 启停，UI 快捷键单独管理。</summary>
        private void OnEnable()
        {
            if (Instance != this) return;

            foreach (KeyValuePair<InputAction, ResolvedBinding> bindingEntry in bindingsByAction)
            {
                InputAction action = bindingEntry.Key;
                action.performed += OnPerformed;
                action.canceled += OnCanceled;
            }

            SetUiShortcutActionsEnabled(true);
            if (!gameplayInputBlocked)
                playerActionMap.Enable();
            Debug.Log(
                $"[PlayerInputController] 已启用输入监听，bindingCount={bindings.Count}，" +
                $"playerMapEnabled={playerActionMap.enabled}。", this);
        }

        /// <summary>在 Intent 仲裁前按真实时间推进全部输入 Request。</summary>
        private void Update()
        {
            Advance(Time.frameCount);
            if (gameplayInputBlocked)
            {
                MoveInput = Vector2.zero;
                return;
            }

            // 连续输入是状态快照，不走离散 Request 的消费生命周期，供多个 FixedUpdate 读取。
            Vector2 value = resolvedMoveAction == null ? Vector2.zero : resolvedMoveAction.ReadValue<Vector2>();
            MoveInput = value.sqrMagnitude <= moveDeadzone * moveDeadzone
                ? Vector2.zero
                : Vector2.ClampMagnitude(value, 1f);
        }

        /// <summary>退订输入回调并停用 Player Map 与快捷键，清理可能残留的按住状态。</summary>
        private void OnDisable()
        {
            if (Instance != this) return;

            foreach (KeyValuePair<InputAction, ResolvedBinding> bindingEntry in bindingsByAction)
            {
                InputAction action = bindingEntry.Key;
                action.performed -= OnPerformed;
                action.canceled -= OnCanceled;
            }

            playerActionMap?.Disable();
            SetUiShortcutActionsEnabled(false);
            MoveInput = Vector2.zero;

            Clear();
            Debug.Log($"[PlayerInputController] 已停用输入监听并清空 Request。", this);
        }

        /// <summary>记录唯一输入控制器注销，并由基类清除静态实例引用。</summary>
        protected override void OnDestroy()
        {
            bool wasCurrentInstance = Instance == this;
            if (wasCurrentInstance)
                Debug.Log($"[PlayerInputController] 已注销场景单例，player={gameObject.name}。", this);
            base.OnDestroy();
            if (wasCurrentInstance)
                InstanceChanged?.Invoke(null);
        }

        /// <summary>清除连续输入，使失焦、停用和场景迁移不会复用旧摇杆状态。</summary>
        public void ClearMoveInput() => MoveInput = Vector2.zero;

        /// <summary>
        /// 按窗口状态暂停或恢复 Player Map，并丢弃锁定前尚未消费的玩法请求。
        /// </summary>
        /// <param name="blocked">是否暂停玩法输入；UI Map 快捷键不受影响。</param>
        public void SetGameplayInputBlocked(bool blocked)
        {
            if (Instance != this || gameplayInputBlocked == blocked) return;

            gameplayInputBlocked = blocked;
            if (blocked)
            {
                // 先设置锁定状态再停用 Map，确保 Disable 引发的 canceled 回调不会补写 Release Request。
                playerActionMap.Disable();
                MoveInput = Vector2.zero;
                Clear();
                Debug.Log("[PlayerInputController] Player Map 已因窗口显示而停用。", this);
                return;
            }

            if (isActiveAndEnabled)
                playerActionMap.Enable();
            Debug.Log(
                $"[PlayerInputController] Player Map 已恢复，enabled={playerActionMap.enabled}。", this);
        }
        #endregion

        #region 输入回调
        /// <summary>把真实 performed 回调按绑定模式转换为缓冲 Request 或即时通知。</summary>
        /// <param name="context">新输入系统提供的动作回调上下文。</param>
        private void OnPerformed(InputAction.CallbackContext context)
        {
            ResolvedBinding binding = bindingsByAction[context.action];
            if (gameplayInputBlocked && binding.DeliveryMode == PlayerInputDeliveryMode.BufferedRequest)
                return;

            if (binding.DeliveryMode == PlayerInputDeliveryMode.ImmediateNotification)
            {
                // UI 快捷键只转发一次物理 performed，不进入玩法 Request 生命周期。
                Debug.Log($"[PlayerInputController] 即时转发 {binding.InputType}。", this);
                ImmediateInputPerformed?.Invoke(binding.InputType);
                return;
            }

            // 每次开始新手势都从 SO 复制时长；进行中的 request 随后不再依赖可编辑资产。
            NotifyPerformed(binding.InputType, binding.Binding.PressBufferDuration,
                binding.Binding.ReleaseBufferDuration, binding.Binding.ClickMaxHeldDuration,
                binding.Binding.ClickBufferDuration);
        }

        /// <summary>把缓冲输入的真实 canceled 回调转换为 Release Request。</summary>
        /// <param name="context">新输入系统提供的动作回调上下文。</param>
        private void OnCanceled(InputAction.CallbackContext context)
        {
            ResolvedBinding binding = bindingsByAction[context.action];
            if (binding.DeliveryMode == PlayerInputDeliveryMode.ImmediateNotification || gameplayInputBlocked)
                return;
            bool released = NotifyCanceled(binding.InputType);
            if (released && TryGetRequest(binding.InputType, out IReadOnlyPlayerInputRequest request))
                Debug.Log(
                    $"[PlayerInputController] 输入 {binding.InputType} 松开，held={request.HeldDuration:F3}s，" +
                    $"clickBuffered={request.HasBufferedClick}，clickRemaining={request.ClickBufferRemaining:F3}s。",
                    this);
        }
        #endregion

        #region 请求操作

        // 生产或者刷新 Handle 方法
        /// <inheritdoc />
        public void NotifyPerformed(PlayerInputType inputType, float pressBufferDuration,
            float releaseBufferDuration, float clickMaxHeldDuration, float clickBufferDuration)
        {
            if (gameplayInputBlocked) return;

            ValidateDuration(pressBufferDuration, nameof(pressBufferDuration));
            ValidateDuration(releaseBufferDuration, nameof(releaseBufferDuration));
            ValidateDuration(clickMaxHeldDuration, nameof(clickMaxHeldDuration));
            ValidateDuration(clickBufferDuration, nameof(clickBufferDuration));
            if (!requestsByType.TryGetValue(inputType, out PlayerInputRequest request))
            {
                request = new PlayerInputRequest(inputType);
                requestsByType.Add(inputType, request);
                requests.Add(request);
            }
            Debug.Log($"[PlayerInputController] 收到 {inputType} Pressed，pressBuffer={pressBufferDuration:F3}s，" +
                      $"releaseBuffer={releaseBufferDuration:F3}s，" +
                      $"clickThreshold={clickMaxHeldDuration:F3}s，clickBuffer={clickBufferDuration:F3}s。", this);
            request.Perform(pressBufferDuration, releaseBufferDuration, clickMaxHeldDuration, clickBufferDuration,
                Time.frameCount, Time.realtimeSinceStartupAsDouble);
        }

        /// <inheritdoc />
        public bool NotifyCanceled(PlayerInputType inputType)
        {
            if (gameplayInputBlocked) return false;
            if (!requestsByType.TryGetValue(inputType, out PlayerInputRequest request)) return false;
            return request.Release(Time.realtimeSinceStartupAsDouble);
        }

        // 消费 Handle 方法
        /// <inheritdoc />
        public bool TryConfirmConsumed(InputRequestHandle handle)
        {
            if (!requestsByType.TryGetValue(handle.InputType, out PlayerInputRequest request) ||
                !request.TryConsume(handle))
                return false;

            Debug.Log($"[PlayerInputController] 已消费输入阶段 {handle}。", this);
            return true;
        }

        /// <inheritdoc />
        public void Clear()
        {
            int requestCount = requests.Count;
            requestsByType.Clear();
            requests.Clear();
            if (requestCount > 0)
                Debug.Log($"[PlayerInputController] 已清除 {requestCount} 个输入 Request。", this);
        }

        /// <summary>按当前单调真实时间推进 Request，供运行时 Update 和诊断代码复用。</summary>
        /// <param name="frame">用于 Pressed 转 Held 的 Unity 帧号。</param>
        public void Advance(int frame)
        {
            double realtime = Time.realtimeSinceStartupAsDouble;
            for (int i = requests.Count - 1; i >= 0; i--)
            {
                PlayerInputRequest request = requests[i];
                request.Tick(frame, realtime);
                if (!request.CanRemove) continue;
                requestsByType.Remove(request.InputType);
                requests.RemoveAt(i);
            }
        }

        /// <summary>兼容原诊断入口；阶段期限使用回调真实时间戳，不累计调用方估算的帧间隔。</summary>
        /// <param name="unscaledDeltaTime">用于兼容调用方校验的非负帧间隔。</param>
        /// <param name="frame">用于 Pressed 转 Held 的 Unity 帧号。</param>
        public void Advance(float unscaledDeltaTime, int frame)
        {
            ValidateDuration(unscaledDeltaTime, nameof(unscaledDeltaTime));
            Advance(frame);
        }
        #endregion

        #region 配置校验
        /// <summary>建立动作映射，并拒绝错 Map、缺失引用、重复动作或重复输入类型。</summary>
        /// <exception cref="InvalidOperationException">任一序列化绑定不符合指定资源及 Map 约束时抛出。</exception>
        private void BuildBindingLookup()
        {
            bindingsByAction.Clear();
            if (playerActionMap == null || uiActionMap == null)
                throw CreateConfigurationException(
                    $"[PlayerInputController] '{name}' 的 Player/UI Map 尚未解析。");
            if (bindings == null || bindings.Count == 0)
                throw CreateConfigurationException(
                    "[PlayerInputController] 至少需要一个显式 PlayerInputBinding。");

            var inputTypes = new HashSet<PlayerInputType>();
            for (int i = 0; i < bindings.Count; i++)
            {
                PlayerInputBinding binding = bindings[i] ??
                    throw CreateConfigurationException($"[PlayerInputController] 输入绑定 {i} 未配置。");
                if (!Enum.IsDefined(typeof(PlayerInputType), binding.InputType))
                    throw CreateConfigurationException(
                        $"[PlayerInputController] 输入绑定 {i} 使用未知 PlayerInputType 值 {(int)binding.InputType}。");
                if (!Enum.IsDefined(typeof(PlayerInputDeliveryMode), binding.DeliveryMode))
                    throw CreateConfigurationException(
                        $"[PlayerInputController] 输入绑定 {binding.InputType} 使用未知交付模式 {(int)binding.DeliveryMode}。");
                InputActionMap expectedMap = binding.DeliveryMode == PlayerInputDeliveryMode.ImmediateNotification
                    ? uiActionMap
                    : playerActionMap;
                InputAction action = ResolveAndValidateActionReference(
                    binding.Action, expectedMap, $"输入绑定 {i} ({binding.InputType})");
                ValidateDuration(binding.PressBufferDuration, $"bindings[{i}].PressBufferDuration");
                ValidateDuration(binding.ReleaseBufferDuration, $"bindings[{i}].ReleaseBufferDuration");
                ValidateDuration(binding.ClickMaxHeldDuration, $"bindings[{i}].ClickMaxHeldDuration");
                ValidateDuration(binding.ClickBufferDuration, $"bindings[{i}].ClickBufferDuration");
                var resolved = new ResolvedBinding(binding);
                if (!bindingsByAction.TryAdd(action, resolved))
                    throw CreateConfigurationException(
                        $"[PlayerInputController] Input Action {action.name} 被重复绑定。");
                if (!inputTypes.Add(binding.InputType))
                    throw CreateConfigurationException(
                        $"[PlayerInputController] 输入类型 {binding.InputType} 被重复绑定。");
            }
        }

        /// <summary>解析 Inspector 中现有 ActionReference，并校验它属于指定资源和预期 Map。</summary>
        /// <param name="actionReference">由 Inspector 序列化的具体 Action 引用。</param>
        /// <param name="expectedMap">该引用必须所属的 Map。</param>
        /// <param name="configurationName">用于异常定位的配置字段或绑定索引。</param>
        /// <returns>已验证且属于预期 Map 的 InputAction。</returns>
        /// <exception cref="InvalidOperationException">引用为空、未解析或指向其他资源及 Map 时抛出。</exception>
        private InputAction ResolveAndValidateActionReference(
            InputActionReference actionReference, InputActionMap expectedMap, string configurationName)
        {
            InputAction action = actionReference?.action;
            if (action == null)
                throw CreateConfigurationException(
                    $"[PlayerInputController] '{name}' 的 {configurationName} 未配置有效 InputActionReference。");

            InputActionMap actionMap = action.actionMap;
            if (actionMap != expectedMap || actionMap.asset != inputActionAsset)
                throw CreateConfigurationException(
                    $"[PlayerInputController] '{name}' 的 {configurationName} 必须引用指定 InputActionAsset " +
                    $"中的 {expectedMap.name} Map Action。");

            return action;
        }

        /// <summary>单独启停背包和取消快捷键，避免窗口期间影响 EventSystem 正在使用的 UI Map。</summary>
        /// <param name="enabled">是否启用即时 UI 通知 Action。</param>
        private void SetUiShortcutActionsEnabled(bool enabled)
        {
            foreach (KeyValuePair<InputAction, ResolvedBinding> bindingEntry in bindingsByAction)
            {
                if (bindingEntry.Value.DeliveryMode != PlayerInputDeliveryMode.ImmediateNotification)
                    continue;

                if (enabled)
                    bindingEntry.Key.Enable();
                else
                    bindingEntry.Key.Disable();
            }
        }

        /// <summary>记录配置边界校验失败并返回附带业务上下文的异常。</summary>
        /// <param name="message">包含失败配置与原因的错误消息。</param>
        /// <returns>由调用点立即抛出的配置异常。</returns>
        private InvalidOperationException CreateConfigurationException(string message)
        {
            Debug.LogError(message, this);
            return new InvalidOperationException(message);
        }

        /// <summary>拒绝来自序列化配置或诊断入口的非法时间。</summary>
        /// <param name="duration">待验证秒数。</param>
        /// <param name="parameterName">异常中使用的参数名称。</param>
        private static void ValidateDuration(float duration, string parameterName)
        {
            if (duration < 0f || float.IsNaN(duration) || float.IsInfinity(duration))
            {
                Debug.LogError(
                    $"[PlayerInputController] 输入时长非法，parameter={parameterName}，duration={duration}。");
                throw new ArgumentOutOfRangeException(parameterName, duration, "Duration 必须是有限非负数。");
            }
        }
        #endregion

        #region 嵌套类型
        /// <summary>缓存校验后的绑定资产与 Action 身份；时长在新手势开始时从资产快照。</summary>
        private readonly struct ResolvedBinding
        {
            /// <summary>获取完整的每项输入设置资产。</summary>
            public PlayerInputBinding Binding { get; }
            /// <summary>获取建立监听时校验过的输入类型。</summary>
            public PlayerInputType InputType { get; }
            /// <summary>获取 InputAction 触发后的交付方式。</summary>
            public PlayerInputDeliveryMode DeliveryMode { get; }

            /// <summary>创建已校验的绑定身份；本次手势的时长仍从资产复制快照。</summary>
            /// <param name="binding">本项输入配置资产。</param>
            public ResolvedBinding(PlayerInputBinding binding)
            {
                Binding = binding;
                InputType = binding.InputType;
                DeliveryMode = binding.DeliveryMode;
            }
        }
        #endregion
    }
}
