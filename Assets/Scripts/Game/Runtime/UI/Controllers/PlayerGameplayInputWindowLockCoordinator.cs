using RPG.PlayerInputSystem;
using WS_Modules.LogModule;
using WS_Modules.UIModule;

namespace RPG.Game.UI.Controllers
{
    /// <summary>
    /// 依据窗口生命周期集中暂停 Player Map；HUD 和交互选项窗口不拦截玩法输入。
    /// </summary>
    internal sealed class PlayerGameplayInputWindowLockCoordinator : IGameWindowSubCoordinator
    {
        #region 依赖与状态字段

        // 依赖字段
        private UIManager uiManager;
        private PlayerInputController playerInputController;
        private bool gameplayInputBlocked;
        private bool disposed;

        #endregion

        #region 生命周期

        /// <summary>订阅窗口和 Player 生命周期，并立即按当前窗口状态同步输入锁。</summary>
        public void Register()
        {
            uiManager = UIManager.Instance;
            uiManager.WindowStateChanged += HandleWindowStateChanged;
            uiManager.WindowDestroyed += HandleWindowDestroyed;
            PlayerInputController.InstanceChanged += HandlePlayerInputControllerChanged;
            playerInputController = PlayerInputController.Instance;

            ReconcileGameplayInputLock();
            WSLog.Log("[PlayerGameplayInputWindowLockCoordinator] 已注册窗口与 Player 输入锁监听。");
        }

        /// <summary>解除窗口与 Player 订阅，并释放本协调器持有的输入锁。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            uiManager.WindowStateChanged -= HandleWindowStateChanged;
            uiManager.WindowDestroyed -= HandleWindowDestroyed;
            PlayerInputController.InstanceChanged -= HandlePlayerInputControllerChanged;
            if (playerInputController != null)
                playerInputController.SetGameplayInputBlocked(false);

            gameplayInputBlocked = false;
            playerInputController = null;
            WSLog.Log("[PlayerGameplayInputWindowLockCoordinator] 已注销窗口监听并释放 Player 输入锁。");
        }

        #endregion

        #region 窗口与玩家状态

        /// <summary>窗口进入显示或隐藏过渡时，根据全体窗口快照重新计算锁定状态。</summary>
        /// <param name="eventArgs">发生变化的窗口及其新状态。</param>
        private void HandleWindowStateChanged(UIWindowStateChangedEventArgs eventArgs)
        {
            if (disposed) return;
            ReconcileGameplayInputLock();
        }

        /// <summary>窗口销毁并从注册表移除后重新计算是否仍有其他窗口占用输入。</summary>
        /// <param name="snapshot">刚销毁窗口的最终状态快照。</param>
        private void HandleWindowDestroyed(UIWindowSnapshot snapshot)
        {
            if (disposed) return;
            ReconcileGameplayInputLock();
        }

        /// <summary>Player 输入单例变化时保存新实例，并立即继承当前窗口锁定状态。</summary>
        /// <param name="nextInputController">新输入控制器；销毁时为空。</param>
        private void HandlePlayerInputControllerChanged(PlayerInputController nextInputController)
        {
            if (disposed) return;

            playerInputController = nextInputController;
            ReconcileGameplayInputLock();
        }

        /// <summary>扫描当前注册窗口，保持 Showing、Visible 与 Hiding 阶段的输入锁。</summary>
        private void ReconcileGameplayInputLock()
        {
            bool shouldBlockGameplayInput = HasBlockingWindow();
            bool lockStateChanged = gameplayInputBlocked != shouldBlockGameplayInput;
            gameplayInputBlocked = shouldBlockGameplayInput;

            // Player 可能在窗口打开后才创建；每次协调都将当前状态应用到当前实例。
            if (playerInputController != null)
                playerInputController.SetGameplayInputBlocked(gameplayInputBlocked);

            if (lockStateChanged)
            {
                WSLog.Log(
                    "[PlayerGameplayInputWindowLockCoordinator] " +
                    (gameplayInputBlocked ? "窗口已锁定 Player Map。" : "所有输入窗口已关闭，恢复 Player Map。"));
            }
        }

        /// <summary>判断是否存在需要暂停玩法输入的非 HUD、非 Choice 窗口。</summary>
        /// <returns>至少一个锁定窗口正在显示、可见或隐藏过渡时返回 true。</returns>
        private bool HasBlockingWindow()
        {
            if (!uiManager.IsInitialized) return false;

            var windowSnapshots = uiManager.GetWindowSnapshots();
            for (int index = 0; index < windowSnapshots.Count; index++)
            {
                UIWindowSnapshot snapshot = windowSnapshots[index];
                if (snapshot.WindowName == nameof(HUDWindow) || snapshot.WindowName == nameof(ChoiceWindow))
                    continue;

                if (snapshot.State == UIWindowState.Showing || snapshot.State == UIWindowState.Visible ||
                    snapshot.State == UIWindowState.Hiding)
                    return true;
            }

            return false;
        }

        #endregion
    }
}
