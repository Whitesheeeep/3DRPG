using System.Collections.Generic;
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

            if (restoreHudAfterGameUIUnlock && UIManager.Instance.IsInitialized)
                UIManager.Instance.PopUpWindow<HUDWindow>();
            restoreHudAfterGameUIUnlock = false;
        }

        #endregion
    }
}
