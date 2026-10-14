using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;
using WS_Modules.Singleton;

namespace RPG.PlayerInputSystem
{
    /// <summary>管理输入 Request、即时通知和 Pointer 玩法门禁，保留已有手势的真实时间生命周期。</summary>
    [DefaultExecutionOrder(-900)]
    public sealed class PlayerInputController : SingletonMonoBase<PlayerInputController>, IPlayerInputRequestBuffer
    {
        #region 常量与依赖字段

        // Inspector 配置字段
        [SerializeField, LabelText("Input Actions Asset")]
        private InputActionAsset inputActionAsset;
        [SerializeField] private List<PlayerInputBinding> bindings = new();
        // 连续移动输入仅由本组件采样，离散 Request 的缓冲与消费不共用该状态。
        [SerializeField, Required, LabelText("移动 Action")]
        private InputActionReference moveAction;
        [SerializeField, Required, LabelText("镜头观察 Action")]
        private InputActionReference lookAction;
        [SerializeField, MinValue(0f), MaxValue(1f)] private float moveDeadzone = 0.1f;

        #endregion

        #region 请求状态与运行时映射

        // 请求状态
        // key：E_PlayerInputType；value：该输入当前手势的 Request。
        private readonly Dictionary<E_PlayerInputType, PlayerInputRequest> requestsByType = new();
        // key：InputAction；value：该动作唯一对应的输入绑定资产。
        private readonly Dictionary<InputAction, ResolvedBinding> bindingsByAction = new();
        // key：E_PlayerInputType；value：通过该业务类型取得的已校验 InputAction。
        private readonly Dictionary<E_PlayerInputType, InputAction> actionByInputTypeMap = new();
        private readonly List<PlayerInputRequest> requests = new();

        // 依赖映射
        private InputAction resolvedMoveAction;
        private InputAction resolvedLookAction;
        private InputActionMap playerActionMap;
        private InputActionMap uiActionMap;
        // 只记录已交付的手势，UI 中被忽略的鼠标按下不得在抬起时结束旧 Request。
        private readonly HashSet<InputAction> deliveredBufferedActions = new();
        private readonly HashSet<InputAction> deliveredImmediateActions = new();
        private bool gameplayInputBlocked;
        private bool pointerGameplayInputBlocked;
        private bool discardNextPointerLookDelta;
        private bool applicationHasFocus = true;

        /// <inheritdoc />
        public IReadOnlyList<IReadOnlyPlayerInputRequest> Requests => requests;

        #endregion

        #region 即时输入事件

        /// <summary>玩家输入控制器单例建立或销毁时通知窗口协调器重新应用输入锁。</summary>
        public static event Action<PlayerInputController> InstanceChanged;

        /// <summary>当配置为即时交付的 InputAction 发生 performed 时通知订阅者。</summary>
        public event Action<E_PlayerInputType> ImmediateInputPerformed;
        /// <summary>即时交付的动作结束时通知订阅者；不生成 Release Request。</summary>
        public event Action<E_PlayerInputType> ImmediateInputCanceled;
        /// <summary>输入监听与 Action 启停完成后通知依赖组件。</summary>
        public event Action<bool> InputAvailabilityChanged;
        /// <summary>当某个手势收到物理松开事件时发送真实按下时长。</summary>
        public event Action<InputRequestHandle, float> RequestReleased;
        /// <summary>当业务主动清空整个请求缓冲区时发送。</summary>
        public event Action RequestsCleared;

        /// <summary>通过输入类型从内部索引查询请求，供 Arbiter 在不扫描列表的情况下读取输入阶段。</summary>
        /// <param name="inputType">需要查询的输入类型。</param>
        /// <param name="request">找到时返回当前手势的只读请求。</param>
        /// <returns>存在该输入类型请求时返回 true。</returns>
        public bool TryGetRequest(E_PlayerInputType inputType, out IReadOnlyPlayerInputRequest request)
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

        /// <summary>获取当前帧连续镜头观察输入；Pointer 输入是像素增量，摇杆输入是归一化速率。</summary>
        public Vector2 LookInput { get; private set; }

        /// <summary>获取窗口协调器是否正在阻断 Player Map。</summary>
        public bool IsGameplayInputBlocked => gameplayInputBlocked;

        /// <summary>当前 Look 来源是否为需要按时间积分的摇杆或游戏手柄。</summary>
        public bool LookInputIsRate => resolvedLookAction?.activeControl?.device is not Pointer;
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

            // 显式动作引用同时锚定 Map；运行时仍验证它们来自 Inspector 指定的唯一资源。
            if (inputActionAsset == null)
                throw CreateConfigurationException(
                    $"[PlayerInputController] '{name}' 未配置有效的 InputActionAsset。");

            // Move 引用提供 Player Map，Look 必须指向同一 Map；不以 Map 或 Action 名称重新查找。
            resolvedMoveAction = ResolveAndValidateActionReference(
                moveAction, null, "移动输入 moveAction");
            playerActionMap = resolvedMoveAction.actionMap;
            if (resolvedMoveAction.type != InputActionType.Value ||
                !string.Equals(resolvedMoveAction.expectedControlType, "Vector2", StringComparison.Ordinal))
                throw CreateConfigurationException(
                    $"[PlayerInputController] '{name}' 的 moveAction 必须是 Value/Vector2 Action。");

            resolvedLookAction = ResolveAndValidateActionReference(
                lookAction, playerActionMap, "镜头观察输入 lookAction");
            if (resolvedLookAction.type != InputActionType.Value ||
                !string.Equals(resolvedLookAction.expectedControlType, "Vector2", StringComparison.Ordinal))
                throw CreateConfigurationException(
                    $"[PlayerInputController] '{name}' 的 lookAction 必须是 Value/Vector2 Action。");

            // ShowCursor 的显式绑定 Action 提供 UI Map；所有其它 Action 都校验属于这两个 Map 之一。
            PlayerInputBinding cursorBinding = GetRequiredBinding(
                E_PlayerInputType.ShowCursor, "临时显示光标");
            InputAction showCursorAction = ResolveAndValidateActionReference(
                cursorBinding.Action, null, "临时显示光标绑定");
            uiActionMap = showCursorAction.actionMap;
            if (uiActionMap == playerActionMap)
                throw CreateConfigurationException(
                    $"[PlayerInputController] '{name}' 的 ShowCursor 引用必须属于独立 UI Map。");

            BuildBindingLookup();
            ValidateImmediateButtonBinding(E_PlayerInputType.ShowCursor, "临时显示光标");
            ValidateImmediateButtonBinding(E_PlayerInputType.TaskWindow, "任务窗口快捷键");

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
            InputAvailabilityChanged?.Invoke(true);
        }

        /// <summary>在 Intent 仲裁前按真实时间推进全部输入 Request。</summary>
        private void Update()
        {
            Advance(Time.frameCount);
            if (gameplayInputBlocked)
            {
                MoveInput = Vector2.zero;
                LookInput = Vector2.zero;
                discardNextPointerLookDelta = true;
                return;
            }

            // 连续输入是状态快照，不走离散 Request 的消费生命周期，供多个 FixedUpdate 读取。
            Vector2 value = resolvedMoveAction == null ? Vector2.zero : resolvedMoveAction.ReadValue<Vector2>();
            MoveInput = value.sqrMagnitude <= moveDeadzone * moveDeadzone
                ? Vector2.zero
                : Vector2.ClampMagnitude(value, 1f);

            LookInput = !applicationHasFocus || resolvedLookAction == null
                ? Vector2.zero
                : resolvedLookAction.ReadValue<Vector2>();
            if (pointerGameplayInputBlocked && !LookInputIsRate)
                LookInput = Vector2.zero;
            if (discardNextPointerLookDelta && resolvedLookAction?.activeControl?.device is Pointer)
            {
                // 锁输入或窗口恢复后的首个 Pointer delta 常包含光标跳转，只丢弃这一帧。
                LookInput = Vector2.zero;
                discardNextPointerLookDelta = false;
            }
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
            deliveredImmediateActions.Clear();
            MoveInput = Vector2.zero;
            LookInput = Vector2.zero;
            discardNextPointerLookDelta = true;

            Clear();
            Debug.Log($"[PlayerInputController] 已停用输入监听并清空 Request。", this);
            InputAvailabilityChanged?.Invoke(false);
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

        /// <summary>光标实际重新锁定后丢弃下一次 Pointer 采样；不影响摇杆和已有玩法 Request。</summary>
        public void DiscardNextPointerLookDelta()
        {
            if (!LookInputIsRate) LookInput = Vector2.zero;
            discardNextPointerLookDelta = true;
        }

        /// <summary>读取 Awake 已校验并缓存的 Action，供对应输入协调器绑定事件。</summary>
        /// <param name="inputType">绑定中的唯一业务输入类型。</param>
        /// <returns>属于指定 InputActionAsset 和 Map 的已验证 Action。</returns>
        /// <exception cref="InvalidOperationException">输入类型没有通过配置校验时抛出。</exception>
        public InputAction GetInputAction(E_PlayerInputType inputType)
        {
            if (actionByInputTypeMap.TryGetValue(inputType, out InputAction action))
                return action;

            throw CreateConfigurationException(
                $"[PlayerInputController] '{name}' 没有已校验的 {inputType} Action。");
        }

        /// <summary>控制新 Pointer 玩法输入和鼠标 Look 的通用门禁，不修改现有 Request。</summary>
        /// <param name="blocked">是否忽略后续 Pointer 玩法输入。</param>
        public void SetPointerGameplayInputBlocked(bool blocked)
        {
            if (Instance != this || pointerGameplayInputBlocked == blocked)
                return;

            pointerGameplayInputBlocked = blocked;
            if (blocked && resolvedLookAction?.activeControl?.device is Pointer)
                LookInput = Vector2.zero;

            Debug.Log(
                $"[PlayerInputController] Pointer 玩法输入门禁已{(blocked ? "开启" : "关闭")}，" +
                "已有 Request 保持原状态。", this);
        }

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
                LookInput = Vector2.zero;
                discardNextPointerLookDelta = true;
                Clear();
                Debug.Log("[PlayerInputController] Player Map 已因窗口显示而停用。", this);
                return;
            }

            if (isActiveAndEnabled)
                playerActionMap.Enable();
            Debug.Log(
                $"[PlayerInputController] Player Map 已恢复，enabled={playerActionMap.enabled}。", this);
        }

        /// <summary>失焦时清空 Look；重新获得焦点后丢弃第一笔 Pointer 增量。</summary>
        /// <param name="hasFocus">应用是否重新成为前台窗口。</param>
        private void OnApplicationFocus(bool hasFocus)
        {
            if (applicationHasFocus == hasFocus) return;

            applicationHasFocus = hasFocus;
            LookInput = Vector2.zero;
            discardNextPointerLookDelta = hasFocus;
            Debug.Log($"[PlayerInputController] 应用焦点变化，hasFocus={hasFocus}，Look 输入已清理。", this);
        }
        #endregion

        #region 输入回调
        /// <summary>把真实 performed 回调按绑定模式转换为缓冲 Request 或即时通知。</summary>
        /// <param name="context">新输入系统提供的动作回调上下文。</param>
        private void OnPerformed(InputAction.CallbackContext context)
        {
            ResolvedBinding binding = bindingsByAction[context.action];
            if (gameplayInputBlocked && context.action.actionMap == playerActionMap)
                return;
            // 仅停止产生新的鼠标玩法输入；已存在的 Request 和帧快照完全保留。
            if (pointerGameplayInputBlocked && context.action.actionMap == playerActionMap &&
                context.control.device is Pointer)
                return;

            if (binding.DeliveryMode == PlayerInputDeliveryMode.ImmediateNotification)
            {
                // 滚轮正负由独立反向处理器区分；每个动作只接受自己负责的负向脉冲。
                if (binding.InputType == E_PlayerInputType.LockPrevious ||
                    binding.InputType == E_PlayerInputType.LockNext)
                {
                    if (context.ReadValue<float>() >= 0f)
                        return;
                }

                // 即时输入不进入玩法 Request 生命周期；Player Map 锁定动作另受 UI 门禁控制。
                deliveredImmediateActions.Add(context.action);
                Debug.Log($"[PlayerInputController] 即时转发 {binding.InputType}。", this);
                ImmediateInputPerformed?.Invoke(binding.InputType);
                return;
            }

            // 每次开始新手势都从 SO 复制时长；进行中的 request 随后不再依赖可编辑资产。
            deliveredBufferedActions.Add(context.action);
            NotifyPerformed(binding.InputType, binding.Binding.PressBufferDuration,
                binding.Binding.ReleaseBufferDuration, binding.Binding.ClickMaxHeldDuration,
                binding.Binding.ClickBufferDuration);
        }

        /// <summary>即时转发动作结束，或结束真正交付过的缓冲手势；跨 Alt 的旧手势仍正常释放。</summary>
        /// <param name="context">新输入系统提供的动作回调上下文。</param>
        private void OnCanceled(InputAction.CallbackContext context)
        {
            ResolvedBinding binding = bindingsByAction[context.action];
            if (binding.DeliveryMode == PlayerInputDeliveryMode.ImmediateNotification)
            {
                if (deliveredImmediateActions.Remove(context.action))
                    ImmediateInputCanceled?.Invoke(binding.InputType);
                return;
            }
            if (!deliveredBufferedActions.Remove(context.action) || gameplayInputBlocked)
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
        public void NotifyPerformed(E_PlayerInputType inputType, float pressBufferDuration,
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
        public bool NotifyCanceled(E_PlayerInputType inputType)
        {
            if (gameplayInputBlocked) return false;
            if (!requestsByType.TryGetValue(inputType, out PlayerInputRequest request)) return false;
            if (!request.Release(Time.realtimeSinceStartupAsDouble)) return false;

            // 先锁定请求中的 Release 句柄和真实按住时长，再同步通知正在等待该手势的 Ability。
            RequestReleased?.Invoke(request.ReleaseHandle, request.HeldDuration);
            return true;
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
            // 清理通知先于移除快照发送，使等待中的 Ability 能同步取消并解除自己的订阅。
            RequestsCleared?.Invoke();
            requestsByType.Clear();
            requests.Clear();
            deliveredBufferedActions.Clear();
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
        /// <summary>从绑定 SO 建立动作映射，并拒绝错 Map、缺失引用、重复动作或重复输入类型。</summary>
        /// <exception cref="InvalidOperationException">任一序列化绑定不符合指定资源及 Map 约束时抛出。</exception>
        private void BuildBindingLookup()
        {
            bindingsByAction.Clear();
            actionByInputTypeMap.Clear();
            if (playerActionMap == null || uiActionMap == null)
                throw CreateConfigurationException(
                    $"[PlayerInputController] '{name}' 的 Player/UI Map 尚未解析。");
            if (bindings == null || bindings.Count == 0)
                throw CreateConfigurationException(
                    "[PlayerInputController] 至少需要一个显式 PlayerInputBinding。");

            for (int i = 0; i < bindings.Count; i++)
            {
                PlayerInputBinding binding = bindings[i] ??
                    throw CreateConfigurationException($"[PlayerInputController] 输入绑定 {i} 未配置。");
                if (!Enum.IsDefined(typeof(E_PlayerInputType), binding.InputType))
                    throw CreateConfigurationException(
                        $"[PlayerInputController] 输入绑定 {i} 使用未知 E_PlayerInputType 值 {(int)binding.InputType}。");
                if (!Enum.IsDefined(typeof(PlayerInputDeliveryMode), binding.DeliveryMode))
                    throw CreateConfigurationException(
                        $"[PlayerInputController] 输入绑定 {binding.InputType} 使用未知交付模式 {(int)binding.DeliveryMode}。");
                bool isPlayerLockInput = binding.InputType == E_PlayerInputType.LockToggle ||
                                         binding.InputType == E_PlayerInputType.LockPrevious ||
                                         binding.InputType == E_PlayerInputType.LockNext;
                InputActionMap expectedMap = binding.DeliveryMode == PlayerInputDeliveryMode.BufferedRequest ||
                                             isPlayerLockInput
                    ? playerActionMap
                    : uiActionMap;
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
                if (!actionByInputTypeMap.TryAdd(binding.InputType, action))
                    throw CreateConfigurationException(
                        $"[PlayerInputController] 输入类型 {binding.InputType} 被重复绑定。");
            }
        }

        /// <summary>解析已序列化的 ActionReference，并校验它属于指定资源及可选的预期 Map。</summary>
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
            if (actionMap == null || actionMap.asset != inputActionAsset ||
                (expectedMap != null && actionMap != expectedMap))
                throw CreateConfigurationException(
                    expectedMap == null
                        ? $"[PlayerInputController] '{name}' 的 {configurationName} 必须引用指定 InputActionAsset 中的有效 Map Action。"
                        : $"[PlayerInputController] '{name}' 的 {configurationName} 必须引用指定 InputActionAsset " +
                          $"中 {expectedMap.name} Map 的 Action。");

            return action;
        }

        /// <summary>只启停 UI Map 的快捷键，避免窗口期间影响 EventSystem 使用的滚轮和鼠标输入。</summary>
        /// <param name="enabled">是否启用即时 UI 通知 Action。</param>
        private void SetUiShortcutActionsEnabled(bool enabled)
        {
            foreach (KeyValuePair<InputAction, ResolvedBinding> bindingEntry in bindingsByAction)
            {
                if (bindingEntry.Value.DeliveryMode != PlayerInputDeliveryMode.ImmediateNotification)
                    continue;
                if (bindingEntry.Key.actionMap != uiActionMap)
                    continue;

                if (enabled)
                    bindingEntry.Key.Enable();
                else
                    bindingEntry.Key.Disable();
            }

        }

        /// <summary>从绑定列表取得某个必需的唯一输入类型。</summary>
        /// <param name="inputType">需要查找的业务输入类型。</param>
        /// <param name="configurationName">用于报告配置问题的输入名称。</param>
        /// <returns>配置完成的绑定资产。</returns>
        private PlayerInputBinding GetRequiredBinding(E_PlayerInputType inputType, string configurationName)
        {
            if (bindings == null)
                throw CreateConfigurationException(
                    $"[PlayerInputController] '{name}' 的输入绑定列表未配置，无法解析 {configurationName}。");

            PlayerInputBinding result = null;
            for (int index = 0; index < bindings.Count; index++)
            {
                PlayerInputBinding candidate = bindings[index];
                if (candidate == null || candidate.InputType != inputType)
                    continue;
                if (result != null)
                    throw CreateConfigurationException(
                        $"[PlayerInputController] '{name}' 的 {configurationName} 输入类型重复配置。");
                result = candidate;
            }

            return result ?? throw CreateConfigurationException(
                $"[PlayerInputController] '{name}' 缺少 {configurationName} 输入绑定。");
        }

        /// <summary>验证指定即时绑定是 Button 且使用即时通知交付。</summary>
        /// <param name="inputType">被校验的输入类型。</param>
        /// <param name="configurationName">用于错误消息的输入名称。</param>
        private void ValidateImmediateButtonBinding(E_PlayerInputType inputType, string configurationName)
        {
            PlayerInputBinding configuredBinding = GetRequiredBinding(inputType, configurationName);
            InputAction configuredAction = configuredBinding.Action?.action;
            if (configuredAction == null ||
                !bindingsByAction.TryGetValue(configuredAction, out ResolvedBinding binding) ||
                binding.Binding != configuredBinding ||
                binding.InputType != inputType ||
                binding.DeliveryMode != PlayerInputDeliveryMode.ImmediateNotification ||
                configuredAction.type != InputActionType.Button)
                throw CreateConfigurationException(
                    $"[PlayerInputController] '{name}' 的 {configurationName} 必须配置为 Button/ImmediateNotification。");
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
            public E_PlayerInputType InputType { get; }
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
