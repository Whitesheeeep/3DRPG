using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using RPG.Game.UI.Bag;
using RPG.Game.UI.Events;
using UnityEngine;
using WS_Modules.CustomEventSystem;
using WS_Modules.UIModule;

namespace RPG.Game.UI.Controllers
{
    /// <summary>
    /// 挂载在 HUDWindow prefab 根对象上的窗口生命周期控制器。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HUDWindowController : MonoBehaviour
    {
        #region 状态

        // 每个来源独立保存自己的占用，避免对话、交易等流程互相提前恢复 HUD。
        private readonly HashSet<string> gameUILockSources = new();
        private bool restoreHudAfterGameUIUnlock;
        private bool windowTransitionRunning;
        private int transitionVersion;

        #endregion

        #region 生命周期

        /// <summary>
        /// 注册 GameUILock 事件，并把注销交给当前 prefab 实例的销毁触发器。
        /// </summary>
        private void Awake()
        {
            EventSystem
                .Register_Type<GameUILockChangeRequestedEventArgs>(
                    typeof(GameUILockChangeRequestedEventArgs),
                    OnGameUILockChangeRequested)
                .UnRegisterWhenGameObjectDestroyed(gameObject);
            EventSystem
                .Register_Type<BagWindowToggleRequestedEventArgs>(
                    typeof(BagWindowToggleRequestedEventArgs), OnBagWindowToggleRequested)
                .UnRegisterWhenGameObjectDestroyed(gameObject);
            EventSystem
                .Register_Type<GameWindowCancelRequestedEventArgs>(
                    typeof(GameWindowCancelRequestedEventArgs), OnGameWindowCancelRequested)
                .UnRegisterWhenGameObjectDestroyed(gameObject);
        }

        /// <summary>窗口控制器销毁时取消仍在运行的协调状态。</summary>
        private void OnDestroy()
        {
            transitionVersion++;
        }

        #endregion

        #region 事件处理

        /// <summary>
        /// 按来源聚合 GameUILock，并在首个申请和最后释放时切换 HUD 显隐。
        /// </summary>
        /// <param name="eventArgs">GameUILock 变更请求。</param>
        private void OnGameUILockChangeRequested(GameUILockChangeRequestedEventArgs eventArgs)
        {
            if (eventArgs.Operation == GameUILockOperation.Acquire)
            {
                if (!gameUILockSources.Add(eventArgs.SourceId)) return;
                if (gameUILockSources.Count != 1) return;

                // 只在第一次锁定时记录原始可见状态，确保最后释放可以精确恢复。
                restoreHudAfterGameUIUnlock =
                    UIManager.Instance.IsInitialized &&
                    UIManager.Instance.TryGetWindow<HUDWindow>(out HUDWindow hudWindow) &&
                    hudWindow.Visible;
                if (restoreHudAfterGameUIUnlock)
                    UIManager.Instance.HideWindow<HUDWindow>();
                return;
            }

            if (!gameUILockSources.Remove(eventArgs.SourceId) || gameUILockSources.Count != 0)
                return;

            if (restoreHudAfterGameUIUnlock && UIManager.Instance.IsInitialized && !windowTransitionRunning &&
                !IsBagVisible())
                UIManager.Instance.PopUpWindow<HUDWindow>();
            restoreHudAfterGameUIUnlock = false;
        }

        /// <summary>按类型化命令切换背包，重复请求在过渡期间被忽略。</summary>
        /// <param name="eventArgs">背包切换来源。</param>
        private void OnBagWindowToggleRequested(BagWindowToggleRequestedEventArgs eventArgs)
        {
            if (windowTransitionRunning)
            {
                Debug.Log("[HUDWindowController] 背包窗口仍在过渡，忽略重复切换请求。");
                return;
            }

            if (IsBagVisible())
            {
                CloseBagAsync().Forget(HandleTransitionException);
                return;
            }

            if (gameUILockSources.Count != 0)
            {
                Debug.Log("[HUDWindowController] 其他 GameUILock 占用期间拒绝打开背包。");
                return;
            }

            if (!UIManager.Instance.IsInitialized ||
                !UIManager.Instance.TryGetWindow<HUDWindow>(out HUDWindow hudWindow) || !hudWindow.Visible)
                return;
            OpenBagAsync().Forget(HandleTransitionException);
        }

        /// <summary>仅当背包是当前窗口时响应取消命令。</summary>
        /// <param name="eventArgs">取消命令来源。</param>
        private void OnGameWindowCancelRequested(GameWindowCancelRequestedEventArgs eventArgs)
        {
            if (!windowTransitionRunning && IsBagVisible())
                CloseBagAsync().Forget(HandleTransitionException);
        }

        /// <summary>提前启动背包动态图集准备，等待 HUD 完整隐藏后再显示背包。</summary>
        private async UniTask OpenBagAsync()
        {
            if (windowTransitionRunning || !UIManager.Instance.IsInitialized) return;
            windowTransitionRunning = true;
            try
            {
                if (!UIManager.Instance.TryGetWindow<BagWindow>(out BagWindow bagWindow))
                    throw new InvalidOperationException("[HUDWindowController] BagWindow 尚未完成预加载。");

                // 资源准备与 HUD 隐藏并行，但 BagWindow 必须等 HUD 隐藏完成后才进入 Show 流程。
                bagWindow.PrepareOpen();
                await UIManager.Instance.HideWindowAsync<HUDWindow>();
                await UIManager.Instance.PopUpWindowAsync<BagWindow>();
            }
            finally
            {
                windowTransitionRunning = false;
            }
        }

        /// <summary>等待背包隐藏完成后按锁状态恢复 HUD。</summary>
        private async UniTask CloseBagAsync()
        {
            if (windowTransitionRunning || !UIManager.Instance.IsInitialized) return;
            windowTransitionRunning = true;
            int currentVersion = ++transitionVersion;
            try
            {
                await UIManager.Instance.HideWindowAsync<BagWindow>();
                if (currentVersion == transitionVersion && gameUILockSources.Count == 0)
                    await UIManager.Instance.PopUpWindowAsync<HUDWindow>();
            }
            finally
            {
                windowTransitionRunning = false;
            }
        }

        /// <summary>判断已注册的背包窗口是否处于稳定显示状态。</summary>
        private static bool IsBagVisible()
        {
            return UIManager.Instance.IsInitialized &&
                   UIManager.Instance.TryGetWindow<BagWindow>(out BagWindow bagWindow) && bagWindow.Visible;
        }

        /// <summary>统一记录窗口异步过渡中的非取消异常。</summary>
        /// <param name="exception">过渡异常。</param>
        private void HandleTransitionException(Exception exception)
        {
            if (!(exception is OperationCanceledException)) Debug.LogException(exception, this);
        }

        #endregion
    }
}
