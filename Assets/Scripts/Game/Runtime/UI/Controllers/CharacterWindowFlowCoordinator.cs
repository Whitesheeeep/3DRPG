using System;
using Cysharp.Threading.Tasks;
using RPG.Game.UI.Character;
using RPG.Game.UI.Flow;
using UnityEngine;
using WS_Modules.CustomEventSystem;
using WS_Modules.LogModule;
using WS_Modules.UIModule;

namespace RPG.Game.UI.Controllers
{
    /// <summary>负责 HUD 到 CharacterWindow 的全屏打开过渡和关闭后恢复 HUD。</summary>
    internal sealed class CharacterWindowFlowCoordinator : IGameWindowSubCoordinator
    {
        #region 依赖与状态

        private IUnRegister openRequestedUnregister;
        private IUnRegister hudVisibilityRejectedUnregister;
        private bool openRequestRunning;
        private bool waitingForHudHidden;
        private bool openedByHudReplacement;
        private bool hudWasVisibleBeforeOpen;
        private GameWindowTransitionRequestId visibilityRequestId;
        private bool disposed;

        #endregion

        #region 生命周期

        /// <summary>注册角色窗口打开意图和窗口生命周期通知。</summary>
        public void Register()
        {
            openRequestedUnregister = EventSystem.Register_Type<CharacterWindowOpenRequestedEventArgs>(
                typeof(CharacterWindowOpenRequestedEventArgs), HandleOpenRequested);
            hudVisibilityRejectedUnregister = EventSystem.Register_Type<HudVisibilityChangeRejectedEventArgs>(
                typeof(HudVisibilityChangeRejectedEventArgs), HandleHudVisibilityRejected);
            UIManager.Instance.WindowHidden += HandleWindowHidden;
            WSLog.Log("[CharacterWindowFlowCoordinator] 已注册角色窗口过渡监听。");
        }

        /// <summary>注销角色窗口过渡监听。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            openRequestedUnregister?.UnRegister();
            hudVisibilityRejectedUnregister?.UnRegister();
            UIManager.Instance.WindowHidden -= HandleWindowHidden;
            openRequestRunning = false;
            waitingForHudHidden = false;
            openedByHudReplacement = false;
            WSLog.Log("[CharacterWindowFlowCoordinator] 已注销角色窗口过渡监听。");
        }

        #endregion

        #region 打开流程

        /// <summary>接收 HUD 角色按钮请求，并确保 HUD 隐藏后再打开窗口。</summary>
        /// <param name="eventArgs">打开请求来源。</param>
        private void HandleOpenRequested(CharacterWindowOpenRequestedEventArgs eventArgs)
        {
            if (disposed || openRequestRunning ||
                UIManager.Instance.TryGetWindow<CharacterWindow>(out CharacterWindow current) && current.Visible)
                return;

            openRequestRunning = true;
            waitingForHudHidden = false;
            visibilityRequestId = GameWindowTransitionRequestId.Create();
            hudWasVisibleBeforeOpen = UIManager.Instance.TryGetWindow<HUDWindow>(out HUDWindow hud) && hud.Visible;
            if (UIManager.Instance.TryGetWindow<CharacterWindow>(out CharacterWindow existing))
                existing.PrepareOpen();

            WSLog.Log($"[CharacterWindowFlowCoordinator] 收到角色窗口请求，来源={eventArgs.Source}，HUDVisible={hudWasVisibleBeforeOpen}。");
            if (hudWasVisibleBeforeOpen)
            {
                waitingForHudHidden = true;
                EventSystem.EventTrigger_Type(typeof(HudVisibilityChangeRequestedEventArgs),
                    new HudVisibilityChangeRequestedEventArgs(visibilityRequestId, false));
                return;
            }
            OpenCharacterWindowAsync().Forget(HandleAsyncException);
        }

        /// <summary>HUD 隐藏完成后打开 CharacterWindow。</summary>
        private async UniTask OpenCharacterWindowAsync()
        {
            waitingForHudHidden = false;
            try
            {
                CharacterWindow window = await UIManager.Instance.PopUpWindowAsync<CharacterWindow>();
                if (window == null || !window.Visible)
                    throw new InvalidOperationException("CharacterWindow 打开请求未返回可见窗口。");
                openedByHudReplacement = hudWasVisibleBeforeOpen;
                openRequestRunning = false;
                WSLog.Log("[CharacterWindowFlowCoordinator] CharacterWindow 已打开。");
            }
            catch
            {
                openRequestRunning = false;
                if (hudWasVisibleBeforeOpen) PublishHudVisibilityRequest(true);
                throw;
            }
        }

        #endregion

        #region 窗口生命周期

        /// <summary>消费 HUD 隐藏通知或恢复此前可见的 HUD。</summary>
        /// <param name="snapshot">稳定窗口快照。</param>
        private void HandleWindowHidden(UIWindowSnapshot snapshot)
        {
            if (disposed) return;
            if (waitingForHudHidden && snapshot.WindowName == nameof(HUDWindow))
            {
                waitingForHudHidden = false;
                OpenCharacterWindowAsync().Forget(HandleAsyncException);
                return;
            }
            if (snapshot.WindowName != nameof(CharacterWindow) || !openedByHudReplacement) return;
            openedByHudReplacement = false;
            if (!hudWasVisibleBeforeOpen) return;
            PublishHudVisibilityRequest(true);
            hudWasVisibleBeforeOpen = false;
            WSLog.Log("[CharacterWindowFlowCoordinator] CharacterWindow 已关闭，请求恢复 HUD。");
        }

        /// <summary>处理 HUD 隐藏被拒绝的结果。</summary>
        /// <param name="eventArgs">拒绝事件。</param>
        private void HandleHudVisibilityRejected(HudVisibilityChangeRejectedEventArgs eventArgs)
        {
            if (disposed || !waitingForHudHidden || eventArgs.RequestId != visibilityRequestId) return;
            waitingForHudHidden = false;
            openRequestRunning = false;
            WSLog.LogWarning("[CharacterWindowFlowCoordinator] HUD 隐藏请求被拒绝：" + eventArgs.Reason);
        }

        /// <summary>发布 HUD 显示或隐藏意图。</summary>
        /// <param name="visible">目标可见状态。</param>
        private void PublishHudVisibilityRequest(bool visible)
        {
            visibilityRequestId = GameWindowTransitionRequestId.Create();
            EventSystem.EventTrigger_Type(typeof(HudVisibilityChangeRequestedEventArgs),
                new HudVisibilityChangeRequestedEventArgs(visibilityRequestId, visible));
        }

        /// <summary>记录跨窗口异步流程异常。</summary>
        /// <param name="exception">异常对象。</param>
        private static void HandleAsyncException(Exception exception)
        {
            if (!(exception is OperationCanceledException)) Debug.LogException(exception);
        }

        #endregion
    }
}
