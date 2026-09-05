using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RPG.PlayerInputSystem
{
    /// <summary>统一管理离散 InputAction、输入 Request 及其真实时间缓冲生命周期。</summary>
    [DefaultExecutionOrder(-900)]
    public sealed class PlayerInputController : MonoBehaviour, IPlayerInputRequestBuffer
    {
        #region 序列化配置
        [SerializeField, Min(0f)] private float defaultPressBufferDuration = 0.2f;
        [SerializeField, Min(0f)] private float defaultReleaseBufferDuration = 0.1f;
        [SerializeField] private List<PlayerInputBinding> bindings = new();
        // 连续移动输入仅由本组件采样，离散 Request 的缓冲与消费不共用该状态。
        [SerializeField] private InputActionReference moveAction;
        [SerializeField, Range(0f, 1f)] private float moveDeadzone = 0.1f;
        #endregion

        #region 请求状态
        // inputAction -> ResolvedBinding / ResolvedBinding.PlayerInputType -> PlayerInputRequest
        private readonly Dictionary<PlayerInputType, PlayerInputRequest> requestsByType = new();
        private readonly Dictionary<InputAction, ResolvedBinding> bindingsByAction = new();
        private readonly List<PlayerInputRequest> requests = new();
        private InputAction resolvedMoveAction;

        /// <inheritdoc />
        public IReadOnlyList<IReadOnlyPlayerInputRequest> Requests => requests;

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
            resolvedMoveAction = moveAction?.action ?? throw new InvalidOperationException(
                $"[PlayerInputController] '{name}' 未配置有效的 Move InputActionReference。 ");
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
        }

        /// <summary>在 Intent 仲裁前按真实时间推进全部输入 Request。</summary>
        private void Update()
        {
            Advance(Time.unscaledDeltaTime, Time.frameCount);
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
        }

        /// <summary>清除连续输入，使失焦、停用和场景迁移不会复用旧摇杆状态。</summary>
        public void ClearMoveInput() => MoveInput = Vector2.zero;
        #endregion

        #region 输入回调
        /// <summary>把真实 performed 回调转换为 Press Request。</summary>
        /// <param name="context">新输入系统提供的动作回调上下文。</param>
        private void OnPerformed(InputAction.CallbackContext context)
        {
            ResolvedBinding binding = bindingsByAction[context.action];
            NotifyPerformed(binding.InputType, binding.PressDuration);
        }

        /// <summary>把真实 canceled 回调转换为独立 Release Request。</summary>
        /// <param name="context">新输入系统提供的动作回调上下文。</param>
        private void OnCanceled(InputAction.CallbackContext context)
        {
            ResolvedBinding binding = bindingsByAction[context.action];
            NotifyCanceled(binding.InputType, binding.ReleaseDuration);
        }
        #endregion

        #region 请求操作

        // 生产或者刷新 Handle 方法
        /// <inheritdoc />
        public void NotifyPerformed(PlayerInputType inputType, float pressBufferDuration)
        {
            ValidateDuration(pressBufferDuration, nameof(pressBufferDuration));
            if (!requestsByType.TryGetValue(inputType, out PlayerInputRequest request))
            {
                request = new PlayerInputRequest(inputType);
                requestsByType.Add(inputType, request);
                requests.Add(request);
            }
#if UNITY_EDITOR
            Debug.Log($"PlayerInputController '{name}' 收到 {inputType} Pressed，持续 {pressBufferDuration:F3} 秒。");

#endif
            request.Perform(pressBufferDuration, Time.frameCount);
        }

        /// <inheritdoc />
        public bool NotifyCanceled(PlayerInputType inputType, float releaseBufferDuration)
        {
            ValidateDuration(releaseBufferDuration, nameof(releaseBufferDuration));
            if (!requestsByType.TryGetValue(inputType, out PlayerInputRequest request)) return false;
            request.Release(releaseBufferDuration);
            return true;
        }

        // 消费 Handle 方法
        /// <inheritdoc />
        public bool TryConfirmConsumed(InputRequestHandle handle) =>
            requestsByType.TryGetValue(handle.InputType, out PlayerInputRequest request) && request.TryConsume(handle);

        /// <inheritdoc />
        public void Clear()
        {
            requestsByType.Clear();
            requests.Clear();
        }

        /// <summary>按指定真实时间推进 Request，供运行时 Update 和诊断代码复用。</summary>
        /// <param name="unscaledDeltaTime">不受 timeScale 影响的真实时间增量。</param>
        /// <param name="frame">用于 Pressed 转 Held 的 Unity 帧号。</param>
        public void Advance(float unscaledDeltaTime, int frame)
        {
            ValidateDuration(unscaledDeltaTime, nameof(unscaledDeltaTime));
            for (int i = requests.Count - 1; i >= 0; i--)
            {
                PlayerInputRequest request = requests[i];
                request.Tick(unscaledDeltaTime, frame);
                if (!request.CanRemove) continue;
                requestsByType.Remove(request.InputType);
                requests.RemoveAt(i);
            }
        }
        #endregion

        #region 配置校验
        /// <summary>建立动作映射，并拒绝缺失引用、重复动作或重复输入类型。</summary>
        private void BuildBindingLookup()
        {
            ValidateDuration(defaultPressBufferDuration, nameof(defaultPressBufferDuration));
            ValidateDuration(defaultReleaseBufferDuration, nameof(defaultReleaseBufferDuration));
            bindingsByAction.Clear();
            if (bindings.Count == 0)
                throw new InvalidOperationException("PlayerInputController 至少需要一个显式 PlayerInputBinding。");

            // 交互导航绑定可以只保存动作名称，仍复用同一 InputActionAsset，避免手工维护内部 fileID。
            InputActionAsset fallbackAsset = null;
            for (int index = 0; index < bindings.Count; index++)
            {
                if (bindings[index]?.Action?.asset == null) continue;
                fallbackAsset = bindings[index].Action.asset;
                break;
            }

            var inputTypes = new HashSet<PlayerInputType>();
            for (int i = 0; i < bindings.Count; i++)
            {
                PlayerInputBinding binding = bindings[i] ??
                    throw new InvalidOperationException($"输入绑定 {i} 未配置。");
                InputAction action = binding.Action != null ? binding.Action.action : null;
                if (action == null && fallbackAsset != null && !string.IsNullOrWhiteSpace(binding.ActionName))
                    action = fallbackAsset.FindAction(binding.ActionName, false);
                if (action == null)
                    throw new InvalidOperationException($"输入绑定 {i} 缺少有效 InputActionReference 或 ActionName。");
                var resolved = new ResolvedBinding(binding.InputType,
                    binding.ResolvePressDuration(defaultPressBufferDuration),
                    binding.ResolveReleaseDuration(defaultReleaseBufferDuration));
                ValidateDuration(resolved.PressDuration, $"bindings[{i}].PressDuration");
                ValidateDuration(resolved.ReleaseDuration, $"bindings[{i}].ReleaseDuration");
                if (!bindingsByAction.TryAdd(action, resolved))
                    throw new InvalidOperationException($"Input Action {action.name} 被重复绑定。");
                if (!inputTypes.Add(binding.InputType))
                    throw new InvalidOperationException($"输入类型 {binding.InputType} 被重复绑定。");
            }
        }

        /// <summary>拒绝来自序列化配置或诊断入口的非法时间。</summary>
        /// <param name="duration">待验证秒数。</param>
        /// <param name="parameterName">异常中使用的参数名称。</param>
        private static void ValidateDuration(float duration, string parameterName)
        {
            if (duration < 0f || float.IsNaN(duration) || float.IsInfinity(duration))
                throw new ArgumentOutOfRangeException(parameterName, duration, "Duration 必须是有限非负数。");
        }
        #endregion

        #region 嵌套类型
        /// <summary>缓存一次校验后可直接用于回调的输入类型与两阶段时间。</summary>
        private readonly struct ResolvedBinding
        {
            /// <summary>获取输入类型。</summary>
            public PlayerInputType InputType { get; }
            /// <summary>获取 Press Buffer 秒数。</summary>
            public float PressDuration { get; }
            /// <summary>获取 Release Buffer 秒数。</summary>
            public float ReleaseDuration { get; }

            /// <summary>创建已解析且无需在输入回调中再次访问配置的绑定。</summary>
            /// <param name="inputType">输入类型。</param>
            /// <param name="pressDuration">Press Buffer 秒数。</param>
            /// <param name="releaseDuration">Release Buffer 秒数。</param>
            public ResolvedBinding(PlayerInputType inputType, float pressDuration, float releaseDuration)
            {
                InputType = inputType;
                PressDuration = pressDuration;
                ReleaseDuration = releaseDuration;
            }
        }
        #endregion
    }
}
