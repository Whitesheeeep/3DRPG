using System;
using Cysharp.Threading.Tasks;
using RPG.Game.UI.Bag;
using RPG.Game.UI.Flow;
using UnityEngine;
using WS_Modules.CustomEventSystem;
using WS_Modules.LogModule;
using WS_Modules.UIModule;

namespace RPG.Game.UI.Controllers
{
    /// <summary>
    /// 负责 BagWindow 与 HUDWindow 之间的打开过渡，不持有 HUD 锁或全屏窗口策略。
    /// </summary>
    internal sealed class BagWindowFlowCoordinator : IGameWindowSubCoordinator
    {
        #region 依赖与状态字段

        private IUnRegister bagOpenRequestedUnregister;
        private IUnRegister hudVisibilityRejectedUnregister;
        private bool bagOpenRequestRunning;
        private bool waitingForHudHidden;
        private bool bagOpenedByHudReplacement;
        private bool hudWasVisibleBeforeBagOpen;
        private GameWindowTransitionRequestId hudVisibilityRequestId;
        private bool disposed;

        #endregion

        #region 生命周期

        /// <summary>注册 Bag 打开意图、HUD 拒绝通知和窗口隐藏通知。</summary>
        public void Register()
        {
            bagOpenRequestedUnregister = EventSystem.Register_Type<BagWindowOpenRequestedEventArgs>(
                typeof(BagWindowOpenRequestedEventArgs), HandleBagWindowOpenRequested);
            hudVisibilityRejectedUnregister = EventSystem.Register_Type<HudVisibilityChangeRejectedEventArgs>(
                typeof(HudVisibilityChangeRejectedEventArgs), HandleHudVisibilityRejected);
            UIManager.Instance.WindowHidden += HandleWindowHidden;
            WSLog.Log("[BagWindowFlowCoordinator] 已注册 Bag 打开过渡监听。");
        }

        /// <summary>注销事件并终止当前 Bag 过渡状态。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            bagOpenRequestedUnregister?.UnRegister();
            hudVisibilityRejectedUnregister?.UnRegister();
            UIManager.Instance.WindowHidden -= HandleWindowHidden;
            bagOpenRequestRunning = false;
            waitingForHudHidden = false;
            bagOpenedByHudReplacement = false;
            WSLog.Log("[BagWindowFlowCoordinator] 已注销 Bag 打开过渡监听。");
        }

        #endregion

        #region Bag 打开流程

        /// <summary>接收 Bag 打开意图并启动一次 HUD 到 Bag 的过渡。</summary>
        /// <param name="eventArgs">Bag 打开请求来源。</param>
        private void HandleBagWindowOpenRequested(BagWindowOpenRequestedEventArgs eventArgs)
        {
            if (disposed || bagOpenRequestRunning ||
                (UIManager.Instance.TryGetWindow<BagWindow>(out BagWindow currentBag) && currentBag.Visible))
                return;

            bagOpenRequestRunning = true;
            waitingForHudHidden = false;
            hudVisibilityRequestId = GameWindowTransitionRequestId.Create();
            hudWasVisibleBeforeBagOpen = UIManager.Instance.TryGetWindow<HUDWindow>(
                out HUDWindow hudWindow) && hudWindow.Visible;

            // 已存在的隐藏实例立即启动图集任务；不等待它完成，和 HUD 隐藏动画并行。
            if (UIManager.Instance.TryGetWindow<BagWindow>(out BagWindow bagWindow))
                bagWindow.PrepareOpen();

            WSLog.Log($"[BagWindowFlowCoordinator] 收到 Bag 打开请求，来源={eventArgs.Source}，" +
                      $"HUDVisible={hudWasVisibleBeforeBagOpen}。");
            if (hudWasVisibleBeforeBagOpen)
            {
                waitingForHudHidden = true;
                EventSystem.EventTrigger_Type(
                    typeof(HudVisibilityChangeRequestedEventArgs),
                    new HudVisibilityChangeRequestedEventArgs(hudVisibilityRequestId, false));
                return;
            }

            OpenBagWindowAsync().Forget(HandleAsyncException);
        }

        /// <summary>在 HUD 稳定隐藏后调用 UIManager 打开 BagWindow。</summary>
        private async UniTask OpenBagWindowAsync()
        {
            waitingForHudHidden = false;
            try
            {
                BagWindow openedWindow = await UIManager.Instance.PopUpWindowAsync<BagWindow>();
                if (openedWindow == null || !openedWindow.Visible)
                    throw new InvalidOperationException("BagWindow 打开请求未返回可见窗口。");

                bagOpenedByHudReplacement = hudWasVisibleBeforeBagOpen;
                bagOpenRequestRunning = false;
                WSLog.Log("[BagWindowFlowCoordinator] HUD 隐藏完成，BagWindow 已打开。");
            }
            catch
            {
                bagOpenRequestRunning = false;
                if (hudWasVisibleBeforeBagOpen)
                    PublishHudVisibilityRequest(true);
                throw;
            }
        }

        #endregion

        #region 窗口生命周期事件

        /// <summary>消费当前流程的 HUD 隐藏通知，避免 Bag 重复打开。</summary>
        /// <param name="snapshot">UIManager 发布的稳定窗口快照。</param>
        private void HandleWindowHidden(UIWindowSnapshot snapshot)
        {
            if (disposed) return;
            if (waitingForHudHidden && snapshot.WindowName == nameof(HUDWindow))
            {
                // 清除等待标志后再启动异步打开，同一次通知不会再次消费流程。
                waitingForHudHidden = false;
                OpenBagWindowAsync().Forget(HandleAsyncException);
                return;
            }

            if (snapshot.WindowName != nameof(BagWindow) || !bagOpenedByHudReplacement)
                return;

            bagOpenedByHudReplacement = false;
            if (!hudWasVisibleBeforeBagOpen) return;

            // HUD 是否应该恢复由 HudWindowLockCoordinator 统一判断，这里只发布意图。
            PublishHudVisibilityRequest(true);
            hudWasVisibleBeforeBagOpen = false;
            WSLog.Log("[BagWindowFlowCoordinator] BagWindow 已关闭，请求恢复此前可见的 HUD。");
        }

        /// <summary>处理 HUD 对当前隐藏请求的明确拒绝并结束本次打开过渡。</summary>
        /// <param name="eventArgs">HUD 显隐拒绝通知。</param>
        private void HandleHudVisibilityRejected(HudVisibilityChangeRejectedEventArgs eventArgs)
        {
            if (disposed || !waitingForHudHidden || eventArgs.RequestId != hudVisibilityRequestId)
                return;

            waitingForHudHidden = false;
            bagOpenRequestRunning = false;
            WSLog.LogWarning("[BagWindowFlowCoordinator] HUD 隐藏请求被拒绝：" + eventArgs.Reason);
        }

        /// <summary>发布 HUD 显示或隐藏意图，交由 HUD 锁协调器执行策略。</summary>
        /// <param name="visible">HUD 目标可见状态。</param>
        private void PublishHudVisibilityRequest(bool visible)
        {
            hudVisibilityRequestId = GameWindowTransitionRequestId.Create();
            EventSystem.EventTrigger_Type(
                typeof(HudVisibilityChangeRequestedEventArgs),
                new HudVisibilityChangeRequestedEventArgs(hudVisibilityRequestId, visible));
        }

        /// <summary>记录跨窗口异步流程中的非取消异常。</summary>
        /// <param name="exception">异步流程异常。</param>
        private static void HandleAsyncException(Exception exception)
        {
            if (!(exception is OperationCanceledException))
                Debug.LogException(exception);
        }

        #endregion
    }
}
