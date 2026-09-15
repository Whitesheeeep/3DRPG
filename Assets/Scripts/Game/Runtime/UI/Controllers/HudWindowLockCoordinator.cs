using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using RPG.Game.UI.Events;
using RPG.Game.UI.Flow;
using UnityEngine;
using WS_Modules.CustomEventSystem;
using WS_Modules.LogModule;
using WS_Modules.UIModule;

namespace RPG.Game.UI.Controllers
{
    /// <summary>
    /// 聚合 Game UI Lock 并执行 HUD 全局显隐策略的纯 C# 子协调器。
    /// </summary>
    internal sealed class HudWindowLockCoordinator : IGameWindowSubCoordinator
    {
        #region 依赖与状态字段

        // key：Game UI Lock 来源 ID；value：该来源当前是否占用 HUD。
        private readonly HashSet<string> gameUILockSources = new();
        private IUnRegister gameUILockRequestUnregister;
        private IUnRegister hudVisibilityRequestUnregister;
        private bool hudRestorePending;
        private bool hudTransitionRunning;
        private bool currentTransitionTargetVisible;
        private int transitionVersion;
        private bool disposed;

        #endregion

        #region 生命周期

        /// <summary>注册 Game UI Lock、HUD 显隐请求和窗口生命周期监听。</summary>
        public void Register()
        {
            gameUILockRequestUnregister = EventSystem.Register_Type<GameUILockChangeRequestedEventArgs>(
                typeof(GameUILockChangeRequestedEventArgs), HandleGameUILockChangeRequested);
            hudVisibilityRequestUnregister = EventSystem.Register_Type<HudVisibilityChangeRequestedEventArgs>(
                typeof(HudVisibilityChangeRequestedEventArgs), HandleHudVisibilityChangeRequested);
            UIManager.Instance.WindowHidden += HandleWindowHidden;
            UIManager.Instance.WindowOpened += HandleWindowOpened;
            WSLog.Log("[HudWindowLockCoordinator] 已注册 HUD 锁与全局显隐监听。");
        }

        /// <summary>注销所有事件并使当前 HUD 异步过渡失效。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            transitionVersion++;
            gameUILockRequestUnregister?.UnRegister();
            hudVisibilityRequestUnregister?.UnRegister();
            UIManager.Instance.WindowHidden -= HandleWindowHidden;
            UIManager.Instance.WindowOpened -= HandleWindowOpened;
            gameUILockSources.Clear();
            hudRestorePending = false;
            hudTransitionRunning = false;
            WSLog.Log("[HudWindowLockCoordinator] 已注销 HUD 锁与全局显隐监听。");
        }

        #endregion

        #region Game UI Lock

        /// <summary>按稳定来源聚合锁，并在首个来源申请时隐藏 HUD。</summary>
        /// <param name="eventArgs">Game UI Lock 变更请求。</param>
        private void HandleGameUILockChangeRequested(GameUILockChangeRequestedEventArgs eventArgs)
        {
            if (disposed) return;
            if (eventArgs.Operation == GameUILockOperation.Acquire)
            {
                if (!gameUILockSources.Add(eventArgs.SourceId)) return;
                if (gameUILockSources.Count != 1) return;

                if (TryGetVisibleHud())
                {
                    hudRestorePending = true;
                    RequestHideHud(GameWindowTransitionRequestId.Create());
                }

                WSLog.Log("[HudWindowLockCoordinator] 获取 Game UI Lock：" + eventArgs.SourceId);
                return;
            }

            if (!gameUILockSources.Remove(eventArgs.SourceId) || gameUILockSources.Count != 0)
                return;

            WSLog.Log("[HudWindowLockCoordinator] 释放最后一个 Game UI Lock：" + eventArgs.SourceId);
            TryRestoreHud();
        }

        #endregion

        #region HUD 显隐请求

        /// <summary>执行 Bag 等业务流程发出的 HUD 显隐意图。</summary>
        /// <param name="eventArgs">HUD 显隐请求。</param>
        private void HandleHudVisibilityChangeRequested(HudVisibilityChangeRequestedEventArgs eventArgs)
        {
            if (disposed) return;
            if (eventArgs.Visible)
            {
                hudRestorePending = true;
                TryRestoreHud(eventArgs.RequestId);
                return;
            }

            if (gameUILockSources.Count != 0)
            {
                RejectHudRequest(eventArgs.RequestId, "Game UI Lock 正在占用 HUD。");
                return;
            }

            RequestHideHud(eventArgs.RequestId);
        }

        /// <summary>发起 HUD 隐藏异步流程；完成通知由 UIManager.WindowHidden 提供。</summary>
        /// <param name="requestId">关联显隐请求 ID。</param>
        private void RequestHideHud(GameWindowTransitionRequestId requestId)
        {
            if (hudTransitionRunning)
            {
                if (!currentTransitionTargetVisible) return;
                RejectHudRequest(requestId, "HUD 仍在执行上一次显隐过渡。");
                return;
            }

            if (!UIManager.Instance.IsInitialized ||
                !UIManager.Instance.TryGetWindow<HUDWindow>(out HUDWindow hudWindow))
            {
                RejectHudRequest(requestId, "HUDWindow 尚未完成预加载。");
                return;
            }

            if (!hudWindow.Visible) return;
            RunHideHudAsync(requestId).Forget(exception => HandleTransitionException(requestId, exception));
        }

        /// <summary>检查恢复条件并发起 HUD 显示异步流程。</summary>
        /// <param name="requestId">关联显隐请求 ID。</param>
        private void TryRestoreHud(GameWindowTransitionRequestId requestId = default)
        {
            if (!hudRestorePending || gameUILockSources.Count != 0 ||
                HasVisibleFullScreenBusinessWindow() || hudTransitionRunning)
                return;
            if (!UIManager.Instance.IsInitialized ||
                !UIManager.Instance.TryGetWindow<HUDWindow>(out HUDWindow hudWindow))
                return;
            if (hudWindow.Visible)
            {
                hudRestorePending = false;
                return;
            }

            if (requestId == default)
                requestId = GameWindowTransitionRequestId.Create();
            hudTransitionRunning = true;
            currentTransitionTargetVisible = true;
            int currentVersion = ++transitionVersion;
            RunShowHudAsync(requestId, currentVersion).Forget(exception =>
                HandleTransitionException(requestId, exception));
        }

        /// <summary>等待 UIManager 完整隐藏 HUD。</summary>
        /// <param name="requestId">关联显隐请求 ID。</param>
        private async UniTask RunHideHudAsync(GameWindowTransitionRequestId requestId)
        {
            hudTransitionRunning = true;
            currentTransitionTargetVisible = false;
            int currentVersion = ++transitionVersion;
            try
            {
                await UIManager.Instance.HideWindowAsync<HUDWindow>();
                WSLog.Log("[HudWindowLockCoordinator] HUD 已隐藏，请求 ID=" + requestId);
            }
            finally
            {
                if (currentVersion == transitionVersion)
                    hudTransitionRunning = false;
            }
        }

        /// <summary>等待 UIManager 稳定显示 HUD，并在成功后清除恢复请求。</summary>
        /// <param name="requestId">关联显隐请求 ID。</param>
        /// <param name="currentVersion">本次过渡版本号。</param>
        private async UniTask RunShowHudAsync(GameWindowTransitionRequestId requestId, int currentVersion)
        {
            try
            {
                HUDWindow shownHud = await UIManager.Instance.PopUpWindowAsync<HUDWindow>();
                if (shownHud == null || !shownHud.Visible)
                    throw new InvalidOperationException("HUDWindow 恢复请求未返回可见窗口。");

                hudRestorePending = false;
                WSLog.Log("[HudWindowLockCoordinator] HUD 已恢复，请求 ID=" + requestId);
            }
            finally
            {
                if (currentVersion == transitionVersion)
                    hudTransitionRunning = false;
            }
        }

        #endregion

        #region 窗口策略

        /// <summary>窗口隐藏后重新评估挂起的 HUD 恢复请求。</summary>
        /// <param name="snapshot">稳定隐藏的窗口快照。</param>
        private void HandleWindowHidden(UIWindowSnapshot snapshot)
        {
            if (disposed) return;
            TryRestoreHud();
        }

        /// <summary>窗口打开后同步全屏占用状态，避免恢复 HUD 覆盖新全屏窗口。</summary>
        /// <param name="snapshot">稳定打开的窗口快照。</param>
        private void HandleWindowOpened(UIWindowSnapshot snapshot)
        {
            if (disposed) return;
            TryRestoreHud();
        }

        /// <summary>判断 HUD 之外是否存在可见的全屏业务窗口。</summary>
        /// <returns>存在可见全屏窗口时返回 true。</returns>
        private static bool HasVisibleFullScreenBusinessWindow()
        {
            IReadOnlyList<UIWindowSnapshot> snapshots = UIManager.Instance.GetWindowSnapshots();
            for (int index = 0; index < snapshots.Count; index++)
            {
                UIWindowSnapshot snapshot = snapshots[index];
                if (snapshot.Visible && snapshot.FullScreenWindow &&
                    snapshot.WindowName != nameof(HUDWindow))
                    return true;
            }

            return false;
        }

        /// <summary>判断当前 HUD 是否处于稳定可见状态。</summary>
        /// <returns>HUD 已初始化且可见时返回 true。</returns>
        private static bool TryGetVisibleHud()
        {
            return UIManager.Instance.IsInitialized &&
                   UIManager.Instance.TryGetWindow<HUDWindow>(out HUDWindow hudWindow) &&
                   hudWindow.Visible;
        }

        #endregion

        #region 异常与拒绝

        /// <summary>发布 HUD 无法执行某次显隐请求的原因。</summary>
        /// <param name="requestId">关联请求 ID。</param>
        /// <param name="reason">拒绝原因。</param>
        private static void RejectHudRequest(GameWindowTransitionRequestId requestId, string reason)
        {
            EventSystem.EventTrigger_Type(
                typeof(HudVisibilityChangeRejectedEventArgs),
                new HudVisibilityChangeRejectedEventArgs(requestId, reason));
        }

        /// <summary>记录 HUD 异步显隐中的非取消异常，并通知请求方。</summary>
        /// <param name="requestId">关联请求 ID。</param>
        /// <param name="exception">显隐异常。</param>
        private void HandleTransitionException(GameWindowTransitionRequestId requestId, Exception exception)
        {
            if (!(exception is OperationCanceledException))
                Debug.LogException(exception);
            hudTransitionRunning = false;
            if (!(exception is OperationCanceledException))
                RejectHudRequest(requestId, exception.Message);
        }

        #endregion
    }
}
