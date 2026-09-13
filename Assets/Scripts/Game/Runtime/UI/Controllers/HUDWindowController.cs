using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using RPG.Game.UI.Events;
using RPG.Game.UI.Flow;
using UnityEngine;
using WS_Modules.CustomEventSystem;
using WS_Modules.UIModule;

namespace RPG.Game.UI.Controllers
{
    /// <summary>
    /// 挂载在 HUDWindow prefab 根对象上的 HUD 生命周期控制器。
    /// 它只执行 HUD 自身的显隐请求，不再决定哪个业务窗口应该打开。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HUDWindowController : MonoBehaviour
    {
        #region 状态

        // 每个来源独立保存自己的占用，避免对话、交易等流程互相提前恢复 HUD。
        private readonly HashSet<string> gameUILockSources = new();
        private bool restoreHudAfterGameUIUnlock;
        private bool windowTransitionRunning;
        private bool currentTransitionTargetVisible;
        private int transitionVersion;
        private IUnRegister hudVisibilityRequestUnregister;

        #endregion

        #region 生命周期

        /// <summary>注册 HUD 锁和显隐请求事件。</summary>
        private void Awake()
        {
            EventSystem
                .Register_Type<GameUILockChangeRequestedEventArgs>(
                    typeof(GameUILockChangeRequestedEventArgs), OnGameUILockChangeRequested)
                .UnRegisterWhenGameObjectDestroyed(gameObject);
            hudVisibilityRequestUnregister = EventSystem
                .Register_Type<HudVisibilityChangeRequestedEventArgs>(
                    typeof(HudVisibilityChangeRequestedEventArgs), OnHudVisibilityChangeRequested);
        }

        /// <summary>销毁时停止正在进行的 HUD 异步显隐并注销事件。</summary>
        private void OnDestroy()
        {
            transitionVersion++;
            hudVisibilityRequestUnregister?.UnRegister();
            hudVisibilityRequestUnregister = null;
        }

        #endregion

        #region 事件处理

        /// <summary>按来源聚合 GameUILock，并在首个申请和最后释放时切换 HUD 显隐。</summary>
        /// <param name="eventArgs">GameUILock 变更请求。</param>
        private void OnGameUILockChangeRequested(GameUILockChangeRequestedEventArgs eventArgs)
        {
            if (eventArgs.Operation == GameUILockOperation.Acquire)
            {
                if (!gameUILockSources.Add(eventArgs.SourceId)) return;
                if (gameUILockSources.Count != 1) return;

                restoreHudAfterGameUIUnlock =
                    UIManager.Instance.IsInitialized &&
                    UIManager.Instance.TryGetWindow<HUDWindow>(out HUDWindow hudWindow) &&
                    hudWindow.Visible;
                if (restoreHudAfterGameUIUnlock)
                    RequestHideHud(GameWindowTransitionRequestId.Create());
                return;
            }

            if (!gameUILockSources.Remove(eventArgs.SourceId) || gameUILockSources.Count != 0)
                return;

            if (restoreHudAfterGameUIUnlock)
                    RequestShowHud(GameWindowTransitionRequestId.Create());
            restoreHudAfterGameUIUnlock = false;
        }

        /// <summary>执行业务窗口发出的 HUD 显隐请求。</summary>
        /// <param name="eventArgs">HUD 显隐请求。</param>
        private void OnHudVisibilityChangeRequested(HudVisibilityChangeRequestedEventArgs eventArgs)
        {
            if (eventArgs.Visible)
            {
                RequestShowHud(eventArgs.RequestId);
                return;
            }

            if (gameUILockSources.Count != 0)
            {
                RejectHudRequest(eventArgs.RequestId, "Game UI Lock 正在占用 HUD。");
                return;
            }

            RequestHideHud(eventArgs.RequestId);
        }

        #endregion

        #region HUD 显隐

        /// <summary>请求异步隐藏 HUD；完整隐藏通知由 UIManager.WindowHidden 发布。</summary>
        /// <param name="requestId">关联过渡请求。</param>
        private void RequestHideHud(GameWindowTransitionRequestId requestId)
        {
            if (windowTransitionRunning)
            {
                // 多个业务窗口可以共同等待同一次 HUD 隐藏完成，不为相同方向启动重复动画。
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

        /// <summary>请求异步显示 HUD；存在 Game UI Lock 时延迟到最后一个锁释放。</summary>
        /// <param name="requestId">关联过渡请求。</param>
        private void RequestShowHud(GameWindowTransitionRequestId requestId)
        {
            if (gameUILockSources.Count != 0)
            {
                restoreHudAfterGameUIUnlock = true;
                return;
            }

            if (windowTransitionRunning)
            {
                if (currentTransitionTargetVisible) return;
                RejectHudRequest(requestId, "HUD 仍在执行上一次显隐过渡。");
                return;
            }

            if (!UIManager.Instance.IsInitialized ||
                !UIManager.Instance.TryGetWindow<HUDWindow>(out HUDWindow hudWindow))
            {
                RejectHudRequest(requestId, "HUDWindow 尚未完成预加载。");
                return;
            }

            if (hudWindow.Visible) return;
            RunShowHudAsync(requestId).Forget(exception => HandleTransitionException(requestId, exception));
        }

        /// <summary>等待 UIManager 完整隐藏 HUD。</summary>
        private async UniTask RunHideHudAsync(GameWindowTransitionRequestId requestId)
        {
            windowTransitionRunning = true;
            currentTransitionTargetVisible = false;
            int currentVersion = ++transitionVersion;
            try
            {
                await UIManager.Instance.HideWindowAsync<HUDWindow>();
            }
            finally
            {
                if (currentVersion == transitionVersion) windowTransitionRunning = false;
            }
        }

        /// <summary>等待 UIManager 稳定显示 HUD。</summary>
        private async UniTask RunShowHudAsync(GameWindowTransitionRequestId requestId)
        {
            windowTransitionRunning = true;
            currentTransitionTargetVisible = true;
            int currentVersion = ++transitionVersion;
            try
            {
                await UIManager.Instance.PopUpWindowAsync<HUDWindow>();
            }
            finally
            {
                if (currentVersion == transitionVersion) windowTransitionRunning = false;
            }
        }

        /// <summary>发布 HUD 无法执行的显隐请求。</summary>
        /// <param name="requestId">关联请求。</param>
        /// <param name="reason">拒绝原因。</param>
        private static void RejectHudRequest(GameWindowTransitionRequestId requestId, string reason)
        {
            EventSystem.EventTrigger_Type(
                typeof(HudVisibilityChangeRejectedEventArgs),
                new HudVisibilityChangeRejectedEventArgs(requestId, reason));
        }

        /// <summary>统一记录异步显隐中的非取消异常。</summary>
        /// <param name="exception">显隐异常。</param>
        private void HandleTransitionException(GameWindowTransitionRequestId requestId, Exception exception)
        {
            if (!(exception is OperationCanceledException)) Debug.LogException(exception, this);
            windowTransitionRunning = false;
            if (!(exception is OperationCanceledException))
                RejectHudRequest(requestId, exception.Message);
        }

        #endregion
    }
}
