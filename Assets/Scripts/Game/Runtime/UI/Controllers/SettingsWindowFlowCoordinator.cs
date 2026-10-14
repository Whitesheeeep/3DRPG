using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using RPG.Game.UI.Flow;
using RPG.Game.UI.Settings;
using UnityEngine;
using WS_Modules.CustomEventSystem;
using WS_Modules.LogModule;
using WS_Modules.UIModule;

namespace RPG.Game.UI.Controllers
{
    /// <summary>协调 HUD 隐藏、设定窗口打开及关闭后 HUD 恢复。</summary>
    internal sealed class SettingsWindowFlowCoordinator : IGameWindowSubCoordinator
    {
        #region 依赖与流程状态
        private IUnRegister openRequestedUnregister;
        private IUnRegister visibilityRejectedUnregister;
        private bool requestRunning;
        private bool waitingForHudHidden;
        private bool hudWasVisible;
        private bool taskWindowReplacedHud;
        private GameWindowTransitionRequestId visibilityRequestId;
        private bool disposed;
        #endregion

        #region 生命周期
        /// <summary>订阅窗口请求、HUD 显隐拒绝和 UIManager 窗口隐藏通知。</summary>
        public void Register()
        {
            openRequestedUnregister = EventSystem.Register_Type<SettingsWindowOpenRequestedEventArgs>(
                typeof(SettingsWindowOpenRequestedEventArgs), HandleOpenRequested);
            visibilityRejectedUnregister = EventSystem.Register_Type<HudVisibilityChangeRejectedEventArgs>(
                typeof(HudVisibilityChangeRejectedEventArgs), HandleVisibilityRejected);
            UIManager.Instance.WindowHidden += HandleWindowHidden;
            WSLog.Log("[SettingsWindowFlowCoordinator] 已注册设定窗口打开过渡监听。");
        }

        /// <summary>释放窗口事件订阅和当前过渡状态。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            openRequestedUnregister?.UnRegister();
            visibilityRejectedUnregister?.UnRegister();
            UIManager.Instance.WindowHidden -= HandleWindowHidden;
            requestRunning = false;
            waitingForHudHidden = false;
            taskWindowReplacedHud = false;
            WSLog.Log("[SettingsWindowFlowCoordinator] 已注销设定窗口打开过渡监听。");
        }
        #endregion

        #region 打开与关闭过渡
        /// <summary>收到窗口打开意图后隐藏可见 HUD，再请求显示设定窗口。</summary>
        /// <param name="eventArgs">统一设定窗口打开意图。</param>
        private void HandleOpenRequested(SettingsWindowOpenRequestedEventArgs eventArgs)
        {
            if (disposed || requestRunning || GameArchitecture.Interface.GetSystem<RPG.Game.Saving.GameSaveSystem>().State != RPG.Game.Saving.E_GameSaveSessionState.Playing) return;
            IReadOnlyList<UIWindowSnapshot> windowSnapshots = UIManager.Instance.GetWindowSnapshots();
            for (int index = 0; index < windowSnapshots.Count; index++)
            {
                UIWindowSnapshot snapshot = windowSnapshots[index];
                if (snapshot.WindowName != nameof(SettingsWindow)) continue;
                if (snapshot.State == UIWindowState.Visible)
                {
                    UIManager.Instance.HideWindowAsync<SettingsWindow>().Forget(HandleAsyncException);
                    WSLog.Log("[SettingsWindowFlowCoordinator] 收到重复打开意图，切换为关闭已显示的设定窗口。");
                    return;
                }

                // 显示、隐藏或异步加载过程中忽略重复输入，等待当前窗口过渡先结束。
                if (snapshot.State != UIWindowState.Hidden) return;
            }

            if (TryGetBlockingFullScreenWindow(out UIWindowSnapshot blockingWindow))
            {
                WSLog.LogWarning(
                    $"[SettingsWindowFlowCoordinator] 暂不打开设定窗口，已有全屏窗口占用：{blockingWindow.WindowName}，state={blockingWindow.State}。");
                return;
            }

            requestRunning = true;
            hudWasVisible = UIManager.Instance.TryGetWindow<HUDWindow>(out HUDWindow hudWindow) && hudWindow.Visible;
            if (!hudWasVisible)
            {
                OpenSettingsWindowAsync().Forget(HandleAsyncException);
                return;
            }

            waitingForHudHidden = true;
            visibilityRequestId = GameWindowTransitionRequestId.Create();
            EventSystem.EventTrigger_Type(typeof(HudVisibilityChangeRequestedEventArgs),
                new HudVisibilityChangeRequestedEventArgs(visibilityRequestId, false));
            WSLog.Log("[SettingsWindowFlowCoordinator] 已请求隐藏 HUD，以打开全屏设定窗口。");
        }

        /// <summary>通过 UIManager 显示 SettingsWindow 并核对返回窗口状态。</summary>
        private async UniTask OpenSettingsWindowAsync()
        {
            waitingForHudHidden = false;
            // HUD 动画期间可能有其他全屏界面抢先打开，因此在实际弹出前重新检查占用状态。
            if (TryGetBlockingFullScreenWindow(out UIWindowSnapshot blockingWindow))
            {
                requestRunning = false;
                WSLog.LogWarning(
                    $"[SettingsWindowFlowCoordinator] HUD 隐藏后发现全屏窗口占用，取消打开设定面板：{blockingWindow.WindowName}，state={blockingWindow.State}。");
                if (hudWasVisible) PublishHudVisibilityRequest(true);
                return;
            }

            try
            {
                SettingsWindow openedWindow = await UIManager.Instance.PopUpWindowAsync<SettingsWindow>();
                if (openedWindow == null || !openedWindow.Visible)
                    throw new InvalidOperationException("SettingsWindow 打开请求未返回可见窗口。");
                taskWindowReplacedHud = hudWasVisible;
                requestRunning = false;
                WSLog.Log("[SettingsWindowFlowCoordinator] SettingsWindow 已打开。");
            }
            catch
            {
                requestRunning = false;
                if (hudWasVisible) PublishHudVisibilityRequest(true);
                throw;
            }
        }

        /// <summary>响应 HUD 隐藏完成并继续打开设定窗口，或在设定窗口关闭后恢复 HUD。</summary>
        /// <param name="snapshot">UIManager 发布的稳定窗口快照。</param>
        private void HandleWindowHidden(UIWindowSnapshot snapshot)
        {
            if (disposed) return;
            if (waitingForHudHidden && snapshot.WindowName == nameof(HUDWindow))
            {
                waitingForHudHidden = false;
                OpenSettingsWindowAsync().Forget(HandleAsyncException);
                return;
            }
            if (snapshot.WindowName != nameof(SettingsWindow) || !taskWindowReplacedHud || !hudWasVisible) return;

            taskWindowReplacedHud = false;
            hudWasVisible = false;
            PublishHudVisibilityRequest(true);
            WSLog.Log("[SettingsWindowFlowCoordinator] SettingsWindow 已关闭，请求恢复先前可见的 HUD。");
        }

        /// <summary>结束被 HUD 策略拒绝的打开请求。</summary>
        /// <param name="eventArgs">显隐请求拒绝事实。</param>
        private void HandleVisibilityRejected(HudVisibilityChangeRejectedEventArgs eventArgs)
        {
            if (disposed || !waitingForHudHidden || eventArgs.RequestId != visibilityRequestId) return;
            waitingForHudHidden = false;
            requestRunning = false;
            WSLog.LogWarning("[SettingsWindowFlowCoordinator] HUD 隐藏请求被拒绝：" + eventArgs.Reason);
        }

        /// <summary>发布 HUD 可见性意图并分配唯一过渡标识。</summary>
        /// <param name="visible">HUD 目标可见状态。</param>
        private void PublishHudVisibilityRequest(bool visible)
        {
            visibilityRequestId = GameWindowTransitionRequestId.Create();
            EventSystem.EventTrigger_Type(typeof(HudVisibilityChangeRequestedEventArgs),
                new HudVisibilityChangeRequestedEventArgs(visibilityRequestId, visible));
        }

        /// <summary>查找当前占用设定面板全屏层级的其他窗口。</summary>
        /// <param name="blockingWindow">找到时返回阻塞窗口的只读快照。</param>
        /// <returns>存在非 HUD、非 Task 全屏显示或过渡窗口时返回 true。</returns>
        private static bool TryGetBlockingFullScreenWindow(out UIWindowSnapshot blockingWindow)
        {
            IReadOnlyList<UIWindowSnapshot> windowSnapshots = UIManager.Instance.GetWindowSnapshots();
            for (int index = 0; index < windowSnapshots.Count; index++)
            {
                UIWindowSnapshot snapshot = windowSnapshots[index];
                if (!snapshot.FullScreenWindow || snapshot.State == UIWindowState.Hidden ||
                    snapshot.WindowName == nameof(HUDWindow) || snapshot.WindowName == nameof(SettingsWindow))
                    continue;

                blockingWindow = snapshot;
                return true;
            }

            blockingWindow = UIWindowSnapshot.Empty;
            return false;
        }

        /// <summary>记录窗口异步流程中的非取消异常。</summary>
        /// <param name="exception">异步流程异常。</param>
        private static void HandleAsyncException(Exception exception)
        {
            if (!(exception is OperationCanceledException)) Debug.LogException(exception);
        }
        #endregion
    }
}
