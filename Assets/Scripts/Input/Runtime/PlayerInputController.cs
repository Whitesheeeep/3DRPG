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
        #endregion

        #region 请求状态
        // inputAction -> ResolvedBinding / ResolvedBinding.PlayerInputType -> PlayerInputRequest
        private readonly Dictionary<PlayerInputType, PlayerInputRequest> requestsByType = new();
        private readonly Dictionary<InputAction, ResolvedBinding> bindingsByAction = new();
        private readonly List<PlayerInputRequest> requests = new();

        /// <inheritdoc />
        public IReadOnlyList<IReadOnlyPlayerInputRequest> Requests => requests;
        #endregion

        #region Unity 生命周期
        /// <summary>校验序列化输入配置并建立无分配回调查询表。</summary>
        private void Awake() => BuildBindingLookup();

        /// <summary>订阅并启用由当前 Controller 独占管理的全部离散动作。</summary>
        private void OnEnable()
        {
            foreach (InputAction action in bindingsByAction.Keys)
            {
                action.performed += OnPerformed;
                action.canceled += OnCanceled;
                action.Enable();
            }
        }

        /// <summary>在 Intent 仲裁前按真实时间推进全部输入 Request。</summary>
        private void Update() => Advance(Time.unscaledDeltaTime, Time.frameCount);

        /// <summary>退订并停用全部动作，再清除可能残留的按住状态和阶段句柄。</summary>
        private void OnDisable()
        {
            foreach (InputAction action in bindingsByAction.Keys)
            {
                action.performed -= OnPerformed;
                action.canceled -= OnCanceled;
                action.Disable();
            }

            Clear();
        }
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

            var inputTypes = new HashSet<PlayerInputType>();
            for (int i = 0; i < bindings.Count; i++)
            {
                PlayerInputBinding binding = bindings[i] ??
                    throw new InvalidOperationException($"输入绑定 {i} 未配置。");
                InputAction action = binding.Action != null ? binding.Action.action : null;
                if (action == null) throw new InvalidOperationException($"输入绑定 {i} 缺少 InputActionReference。");
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
        /// <summary>直接代理内部 Request 列表的只读适配器，不复制集合或 Request 实例。</summary>
        private sealed class ReadOnlyRequestList : IReadOnlyList<IReadOnlyPlayerInputRequest>
        {
            private readonly List<PlayerInputRequest> source;

            /// <summary>创建绑定指定内部 Request 列表的只读适配器。</summary>
            /// <param name="source">唯一持有 Request 顺序和生命周期的内部列表。</param>
            public ReadOnlyRequestList(List<PlayerInputRequest> source) =>
                this.source = source ?? throw new ArgumentNullException(nameof(source));

            /// <summary>获取当前内部 Request 数量。</summary>
            public int Count => source.Count;

            /// <summary>按当前内部顺序读取指定位置的只读 Request。</summary>
            /// <param name="index">Request 在内部列表中的位置。</param>
            /// <returns>对应位置的 Request 只读接口。</returns>
            public IReadOnlyPlayerInputRequest this[int index] => source[index];

            /// <summary>按当前内部列表顺序枚举 Request，不创建副本。</summary>
            /// <returns>直接读取内部列表元素的枚举器。</returns>
            public IEnumerator<IReadOnlyPlayerInputRequest> GetEnumerator()
            {
                for (int i = 0; i < source.Count; i++)
                    yield return source[i];
            }

            /// <summary>提供非泛型只读枚举入口。</summary>
            /// <returns>当前 Request 的只读枚举器。</returns>
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

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
