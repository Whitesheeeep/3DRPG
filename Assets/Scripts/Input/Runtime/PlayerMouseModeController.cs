using System;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using WS_Modules.FSM;
using WS_Modules.Singleton;

namespace RPG.PlayerInputSystem
{
    /// <summary>表示鼠标当前由玩法或 UI 接收；切换不改动已创建 Request 的生命周期。</summary>
    public enum E_PlayerMouseMode
    {
        /// <summary>鼠标隐藏并锁定，由 Pointer 输入控制玩法镜头。</summary>
        Gameplay,
        /// <summary>鼠标显示并解锁，仅拦截新产生的 Pointer 玩法输入。</summary>
        UI
    }

    /// <summary>独立管理 Alt 临时 UI 鼠标模式、状态切换与失焦后的输入校准。</summary>
    [DefaultExecutionOrder(-890), DisallowMultipleComponent]
    [InfoBox("依赖同一 Player 上显式绑定的 PlayerInputController；缺少该组件或 ShowCursor Action 时无法初始化。")]
    public sealed class PlayerMouseModeController : SingletonMonoBase<PlayerMouseModeController>
    {
        #region 依赖字段与运行时状态

        // 依赖字段：输入监听由 PlayerInputController 拥有，本组件仅订阅其即时事件并设置 Pointer 门禁。
        [SerializeField, Required, LabelText("玩家输入控制器")]
        private PlayerInputController inputController;

        private InputAction showCursorAction;
        private StateMachine<E_PlayerMouseMode, PlayerMouseModeController> mouseModeStateMachine;
        private bool applicationHasFocus;
        private bool inputAvailable;
        private bool stateMachineEntered;
        private bool reconciliationScheduled;

        #endregion

        #region 事件

        /// <summary>鼠标模式单例建立或销毁时通知需要跟随实例的业务组件。</summary>
        public static event Action<PlayerMouseModeController> InstanceChanged;

        /// <summary>鼠标模式实际变化后发送进入的模式。</summary>
        public event Action<E_PlayerMouseMode> MouseModeChanged;

        #endregion

        #region 查询属性

        /// <summary>获取当前鼠标模式；状态机尚未进入或已退出时按 Gameplay 处理。</summary>
        public E_PlayerMouseMode MouseMode => mouseModeStateMachine?.CurrentState?.StateId ?? E_PlayerMouseMode.Gameplay;

        #endregion

        #region Unity 生命周期

        /// <summary>校验输入依赖、装配鼠标 FSM 后注册单例，避免外部读取半初始化实例。</summary>
        /// <exception cref="InvalidOperationException">同节点输入依赖或 ShowCursor 绑定缺失时抛出。</exception>
        protected override void Awake()
        {
            // 重复 Player 在订阅任何输入前停用；基类随后负责销毁重复根对象。
            if (Instance != null && Instance != this)
            {
                Debug.LogError($"[PlayerMouseModeController] 检测到重复鼠标模式控制器，销毁对象 '{name}'。", this);
                gameObject.SetActive(false);
                base.Awake();
                return;
            }

            if (inputController == null)
                throw CreateConfigurationException($"[PlayerMouseModeController] '{name}' 未绑定同一 Player 的 PlayerInputController。");

            showCursorAction = inputController.GetInputAction(E_PlayerInputType.ShowCursor);
            mouseModeStateMachine = new StateMachine<E_PlayerMouseMode, PlayerMouseModeController>(
                E_PlayerMouseMode.Gameplay, this);
            mouseModeStateMachine.State(E_PlayerMouseMode.Gameplay);
            mouseModeStateMachine.State(E_PlayerMouseMode.UI);
            mouseModeStateMachine.OnEnter();
            stateMachineEntered = true;
            applicationHasFocus = Application.isFocused;
            inputAvailable = inputController.isActiveAndEnabled;
            inputController.SetPointerGameplayInputBlocked(false);

            // 单例注册放在引用校验和 FSM 装配之后。
            base.Awake();
            Debug.Log($"[PlayerMouseModeController] 鼠标模式控制器已注册，player={gameObject.name}。", this);
            InstanceChanged?.Invoke(this);
        }

        /// <summary>订阅即时输入与可用性事件，并在下一次 Input System 更新后校准 Alt 实际按住状态。</summary>
        private void OnEnable()
        {
            if (Instance != this)
                return;

            if (!stateMachineEntered)
            {
                mouseModeStateMachine.OnEnter();
                stateMachineEntered = true;
            }

            inputController.ImmediateInputPerformed += HandleImmediateInputPerformed;
            inputController.ImmediateInputCanceled += HandleImmediateInputCanceled;
            inputController.InputAvailabilityChanged += HandleInputAvailabilityChanged;
            inputAvailable = inputController.isActiveAndEnabled;
            applicationHasFocus = Application.isFocused;
            if (inputAvailable && applicationHasFocus)
                ScheduleFocusReconciliation();
            else
                SetMouseMode(E_PlayerMouseMode.Gameplay);

            Debug.Log(
                $"[PlayerMouseModeController] 已订阅 Alt 输入，inputAvailable={inputAvailable}, focused={applicationHasFocus}。",
                this);
        }

        /// <summary>解除输入订阅、取消待校准回调，并在组件停用时恢复玩法鼠标模式。</summary>
        private void OnDisable()
        {
            if (Instance != this)
                return;

            inputController.ImmediateInputPerformed -= HandleImmediateInputPerformed;
            inputController.ImmediateInputCanceled -= HandleImmediateInputCanceled;
            inputController.InputAvailabilityChanged -= HandleInputAvailabilityChanged;
            CancelFocusReconciliation();
            SetMouseMode(E_PlayerMouseMode.Gameplay);
            inputController.SetPointerGameplayInputBlocked(false);
            if (stateMachineEntered)
            {
                mouseModeStateMachine.OnExit();
                stateMachineEntered = false;
            }

            Debug.Log("[PlayerMouseModeController] 已解除 Alt 输入订阅并恢复 Gameplay 模式。", this);
        }

        /// <summary>失焦时结束临时 UI 模式，恢复焦点后在 Input System 更新后重新校准左右 Alt。</summary>
        /// <param name="hasFocus">应用是否处于前台。</param>
        private void OnApplicationFocus(bool hasFocus)
        {
            if (applicationHasFocus == hasFocus)
                return;

            applicationHasFocus = hasFocus;
            if (!hasFocus)
            {
                CancelFocusReconciliation();
                SetMouseMode(E_PlayerMouseMode.Gameplay);
            }
            else if (inputAvailable && isActiveAndEnabled)
            {
                ScheduleFocusReconciliation();
            }

            Debug.Log($"[PlayerMouseModeController] 应用焦点变化，focused={hasFocus}, mode={MouseMode}。", this);
        }

        /// <summary>注销当前单例，并通知相机等业务解除对已销毁组件的订阅。</summary>
        protected override void OnDestroy()
        {
            bool wasCurrentInstance = Instance == this;
            base.OnDestroy();
            if (!wasCurrentInstance)
                return;

            Debug.Log($"[PlayerMouseModeController] 鼠标模式单例已注销，player={gameObject.name}。", this);
            InstanceChanged?.Invoke(null);
        }

        #endregion

        #region 输入与状态切换

        /// <summary>根据左右 Alt 当前按住状态在 Gameplay 与 UI 间切换。</summary>
        private void ReconcileMouseMode()
        {
            if (!isActiveAndEnabled || !applicationHasFocus || !inputAvailable || !inputController.isActiveAndEnabled)
            {
                SetMouseMode(E_PlayerMouseMode.Gameplay);
                return;
            }

            SetMouseMode(IsAnyAltPressed() ? E_PlayerMouseMode.UI : E_PlayerMouseMode.Gameplay);
        }

        /// <summary>只在模式确实变化时提交 FSM、Pointer 门禁和事件通知。</summary>
        /// <param name="nextMode">需要进入的目标鼠标模式。</param>
        private void SetMouseMode(E_PlayerMouseMode nextMode)
        {
            if (MouseMode == nextMode)
                return;

            if (!mouseModeStateMachine.ChangeState(nextMode))
                throw new InvalidOperationException(
                    $"[PlayerMouseModeController] 无法从 {MouseMode} 切换到鼠标模式 {nextMode}。");

            // 输入门禁先更新，订阅者收到通知时模式和 Pointer 行为已保持一致。
            inputController.SetPointerGameplayInputBlocked(nextMode == E_PlayerMouseMode.UI);
            if (nextMode == E_PlayerMouseMode.Gameplay)
                inputController.DiscardNextPointerLookDelta();

            Debug.Log($"[PlayerMouseModeController] 鼠标模式切换，mode={nextMode}。", this);
            MouseModeChanged?.Invoke(nextMode);
        }

        /// <summary>检测 ShowCursor Action 的所有已解析 ButtonControl，兼容左右 Alt 同时按住。</summary>
        /// <returns>至少一个 Alt 控件仍按住时返回 true。</returns>
        private bool IsAnyAltPressed()
        {
            foreach (InputControl control in showCursorAction.controls)
            {
                if (control is ButtonControl button && button.isPressed)
                    return true;
            }

            return false;
        }

        /// <summary>延迟到 Input System 本轮完成后读取按钮状态，避免焦点恢复时拿到旧设备快照。</summary>
        private void ScheduleFocusReconciliation()
        {
            if (reconciliationScheduled || !inputAvailable || !applicationHasFocus)
                return;

            InputSystem.onAfterUpdate -= HandleAfterInputSystemUpdate;
            InputSystem.onAfterUpdate += HandleAfterInputSystemUpdate;
            reconciliationScheduled = true;
        }

        /// <summary>取消唯一待执行的 Input System 校准回调。</summary>
        private void CancelFocusReconciliation()
        {
            if (!reconciliationScheduled)
                return;

            InputSystem.onAfterUpdate -= HandleAfterInputSystemUpdate;
            reconciliationScheduled = false;
        }

        /// <summary>Input System 更新结束后移除一次性回调并读取左右 Alt 当前状态。</summary>
        private void HandleAfterInputSystemUpdate()
        {
            CancelFocusReconciliation();
            ReconcileMouseMode();
        }

        /// <summary>ShowCursor 按下时查询物理控件，避免把重复 performed 当作模式变化。</summary>
        /// <param name="inputType">发生即时按下的输入类型。</param>
        private void HandleImmediateInputPerformed(E_PlayerInputType inputType)
        {
            if (inputType == E_PlayerInputType.ShowCursor)
                ReconcileMouseMode();
        }

        /// <summary>ShowCursor 抬起时检查另一侧 Alt，只有最后一个 Alt 松开才恢复玩法模式。</summary>
        /// <param name="inputType">发生即时抬起的输入类型。</param>
        private void HandleImmediateInputCanceled(E_PlayerInputType inputType)
        {
            if (inputType == E_PlayerInputType.ShowCursor)
                ReconcileMouseMode();
        }

        /// <summary>输入组件完成启用或停用后更新本地门禁，并恢复或安排一次按键状态校准。</summary>
        /// <param name="available">PlayerInputController 当前是否完成启用。</param>
        private void HandleInputAvailabilityChanged(bool available)
        {
            inputAvailable = available;
            if (!available)
            {
                CancelFocusReconciliation();
                SetMouseMode(E_PlayerMouseMode.Gameplay);
            }
            else if (applicationHasFocus)
            {
                ScheduleFocusReconciliation();
            }

            Debug.Log(
                $"[PlayerMouseModeController] 输入可用状态变化，available={available}, mode={MouseMode}。",
                this);
        }

        /// <summary>在依赖配置错误处创建统一异常并记录 Player 上下文。</summary>
        /// <param name="message">面向配置作者的错误内容。</param>
        /// <returns>带业务上下文的配置异常。</returns>
        private InvalidOperationException CreateConfigurationException(string message)
        {
            Debug.LogError(message, this);
            return new InvalidOperationException(message);
        }

        #endregion
    }
}
