using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RPG.PlayerInputSystem
{
    /// <summary>统一管理离散 InputAction、输入 Request 及其真实时间缓冲生命周期。</summary>
    [DefaultExecutionOrder(-900)]
    public sealed class PlayerInputController : MonoBehaviour, IPlayerInputRequestBuffer
    {
        #region 序列化配置
        [SerializeField] private List<PlayerInputBinding> bindings = new();
        // 连续移动输入仅由本组件采样，离散 Request 的缓冲与消费不共用该状态。
        [SerializeField] private InputActionReference moveAction;
        [SerializeField, MinValue(0f), MaxValue(1f)] private float moveDeadzone = 0.1f;
        #endregion

        #region 请求状态
        // key：PlayerInputType；value：该输入当前手势的 Request。
        private readonly Dictionary<PlayerInputType, PlayerInputRequest> requestsByType = new();
        // key：InputAction；value：该动作唯一对应的输入绑定资产。
        private readonly Dictionary<InputAction, ResolvedBinding> bindingsByAction = new();
        private readonly List<PlayerInputRequest> requests = new();
        private InputAction resolvedMoveAction;

        /// <inheritdoc />
        public IReadOnlyList<IReadOnlyPlayerInputRequest> Requests => requests;

        #endregion

        #region 即时输入事件

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
        /// <summary>校验序列化输入配置并建立无分配回调查询表。</summary>
        private void Awake()
        {
            BuildBindingLookup();
            // Move 必须由 Inspector 配置对象引用，避免重复维护 Action 名称字符串。
            resolvedMoveAction = moveAction?.action;
            if (resolvedMoveAction == null)
                throw CreateConfigurationException(
                    $"[PlayerInputController] '{name}' 未配置有效的 Move InputActionReference。");
        }

        /// <summary>订阅并启用由当前 Controller 独占管理的全部离散动作。</summary>
        private void OnEnable()
        {
            foreach (InputAction action in bindingsByAction.Keys)
            {
                action.performed += OnPerformed;
                action.canceled += OnCanceled;
                action.Enable();
            }
            resolvedMoveAction?.Enable();
            Debug.Log($"[PlayerInputController] 已启用输入监听，bindingCount={bindings.Count}。", this);
        }

        /// <summary>在 Intent 仲裁前按真实时间推进全部输入 Request。</summary>
        private void Update()
        {
            Advance(Time.frameCount);
            // 连续输入是状态快照，不走离散 Request 的消费生命周期，供多个 FixedUpdate 读取。
            Vector2 value = resolvedMoveAction == null ? Vector2.zero : resolvedMoveAction.ReadValue<Vector2>();
            MoveInput = value.sqrMagnitude <= moveDeadzone * moveDeadzone
                ? Vector2.zero
                : Vector2.ClampMagnitude(value, 1f);
        }

        /// <summary>退订并停用全部动作，再清除可能残留的按住状态和阶段句柄。</summary>
        private void OnDisable()
        {
            foreach (InputAction action in bindingsByAction.Keys)
            {
                action.performed -= OnPerformed;
                action.canceled -= OnCanceled;
                action.Disable();
            }

            resolvedMoveAction?.Disable();
            MoveInput = Vector2.zero;

            Clear();
            Debug.Log($"[PlayerInputController] 已停用输入监听并清空 Request。", this);
        }

        /// <summary>清除连续输入，使失焦、停用和场景迁移不会复用旧摇杆状态。</summary>
        public void ClearMoveInput() => MoveInput = Vector2.zero;
        #endregion

        #region 输入回调
        /// <summary>把真实 performed 回调按绑定模式转换为缓冲 Request 或即时通知。</summary>
        /// <param name="context">新输入系统提供的动作回调上下文。</param>
        private void OnPerformed(InputAction.CallbackContext context)
        {
            ResolvedBinding binding = bindingsByAction[context.action];

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
            if (binding.DeliveryMode == PlayerInputDeliveryMode.ImmediateNotification) return;
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
        /// <summary>建立动作映射，并拒绝缺失引用、重复动作或重复输入类型。</summary>
        private void BuildBindingLookup()
        {
            bindingsByAction.Clear();
            if (bindings == null || bindings.Count == 0)
                throw CreateConfigurationException(
                    "[PlayerInputController] 至少需要一个显式 PlayerInputBinding。");

            var inputTypes = new HashSet<PlayerInputType>();
            for (int i = 0; i < bindings.Count; i++)
            {
                PlayerInputBinding binding = bindings[i] ??
                    throw CreateConfigurationException($"[PlayerInputController] 输入绑定 {i} 未配置。");
                InputAction action = binding.Action?.action;
                if (action == null)
                    throw CreateConfigurationException(
                        $"[PlayerInputController] 输入绑定 {binding.InputType} 缺少有效 InputActionReference。");
                if (!Enum.IsDefined(typeof(PlayerInputType), binding.InputType))
                    throw CreateConfigurationException(
                        $"[PlayerInputController] 输入绑定 {i} 使用未知 PlayerInputType 值 {(int)binding.InputType}。");
                if (!Enum.IsDefined(typeof(PlayerInputDeliveryMode), binding.DeliveryMode))
                    throw CreateConfigurationException(
                        $"[PlayerInputController] 输入绑定 {binding.InputType} 使用未知交付模式 {(int)binding.DeliveryMode}。");
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
