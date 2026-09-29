using System;
using System.Collections.Generic;
using RPG.Game.UI.Events;
using WS_Modules.CustomEventSystem;
using WS_Modules.LogModule;
using WS_Modules.UIModule;

namespace RPG.InteractionSystem
{
    /// <summary>
    /// 集中管理交互检测的生命周期、窗口占用和独占 Game UI 锁，不持有 PlayerInteractor 的业务列表。
    /// </summary>
    internal sealed class InteractionDetectionController : IDisposable
    {
        #region 依赖字段

        // 检测结果回调仍由 PlayerInteractor 持有，控制器只决定何时允许刷新或清空。
        private readonly InteractionDetector detector;
        private readonly Action refreshOptions;
        private readonly Action clearOptions;
        private readonly UIManager uiManager;
        private readonly bool startDetectOnEnable;

        #endregion

        #region 状态字段

        // key：独占 Game UI 来源 ID；集合中的每个 ID 表示该来源仍持有一份检测阻断。
        private readonly HashSet<string> gameUILockSources = new();
        private IUnRegister gameUILockUnregister;
        private bool windowBlocksDetection;
        private bool detectionRequested;
        private bool active;
        private bool disposed;

        #endregion

        #region 初始化与释放

        /// <summary>建立检测器、UI 窗口和独占 Game UI 之间的生命周期连接。</summary>
        /// <param name="detector">执行范围扫描的玩家检测器。</param>
        /// <param name="startDetectOnEnable">交互组件启用时是否默认请求检测。</param>
        /// <param name="refreshOptions">扫描允许时重建玩家最终交互选项的回调。</param>
        /// <param name="clearOptions">检测暂停时清空玩家交互选项的回调。</param>
        /// <exception cref="ArgumentNullException">任一必需依赖为空时抛出。</exception>
        public InteractionDetectionController(
            InteractionDetector detector,
            bool startDetectOnEnable,
            Action refreshOptions,
            Action clearOptions)
        {
            this.detector = detector ?? throw new ArgumentNullException(nameof(detector));
            this.startDetectOnEnable = startDetectOnEnable;
            this.refreshOptions = refreshOptions ?? throw new ArgumentNullException(nameof(refreshOptions));
            this.clearOptions = clearOptions ?? throw new ArgumentNullException(nameof(clearOptions));
            uiManager = UIManager.Instance;

            // 先订阅再读取快照，避免初始化期间漏掉窗口进入显示状态的变化。
            uiManager.WindowStateChanged += HandleWindowStateChanged;
            uiManager.WindowDestroyed += HandleWindowDestroyed;
            gameUILockUnregister = EventSystem.Register_Type<GameUILockChangeRequestedEventArgs>(
                typeof(GameUILockChangeRequestedEventArgs), HandleGameUILockChangeRequested);
            windowBlocksDetection = HasBlockingWindow();
            WSLog.Log("[InteractionDetectionController] 已订阅窗口状态和 Game UI 锁，" +
                      $"windowBlocked={windowBlocksDetection}。");
        }

        /// <summary>解绑扫描、窗口和 Game UI 锁事件，结束控制器生命周期。</summary>
        public void Dispose()
        {
            if (disposed) return;
            Deactivate();
            disposed = true;
            uiManager.WindowStateChanged -= HandleWindowStateChanged;
            uiManager.WindowDestroyed -= HandleWindowDestroyed;
            gameUILockUnregister?.UnRegister();
            gameUILockUnregister = null;
            gameUILockSources.Clear();
            WSLog.Log("[InteractionDetectionController] 已释放检测控制与窗口锁订阅。");
        }

        #endregion

        #region 检测生命周期与意图

        /// <summary>激活扫描回调，并按组件配置请求检测或继承当前 UI 阻断状态。</summary>
        public void Activate()
        {
            if (active) return;
            active = true;
            detectionRequested = startDetectOnEnable;
            detector.ScanCompleted += HandleScanCompleted;
            ApplyDetectionState();
            WSLog.Log("[InteractionDetectionController] 已激活检测控制，" +
                      $"requested={detectionRequested}, blocked={IsBlocked}。");
        }

        /// <summary>停用扫描回调、停止检测，并清空当前选项。</summary>
        public void Deactivate()
        {
            if (!active) return;
            active = false;
            detectionRequested = false;
            detector.ScanCompleted -= HandleScanCompleted;
            StopDetectorAndClearOptions();
            WSLog.Log("[InteractionDetectionController] 已停用检测控制。");
        }

        /// <summary>记录主动检测意图；存在任何 UI 阻断时等待解锁后再启动扫描。</summary>
        public void StartDetect()
        {
            detectionRequested = true;
            ApplyDetectionState();
        }

        /// <summary>取消主动检测意图，停止扫描并清空交互选项。</summary>
        public void PauseDetect()
        {
            detectionRequested = false;
            StopDetectorAndClearOptions();
        }

        /// <summary>根据活动状态、检测意图和全部 UI 占用统一决定检测器状态。</summary>
        private void ApplyDetectionState()
        {
            if (!active || !detectionRequested || IsBlocked)
            {
                StopDetectorAndClearOptions();
                return;
            }

            if (detector.IsDetecting) return;
            detector.StartDetect();
            WSLog.Log("[InteractionDetectionController] 已启动交互扫描，所有 UI 阻断均已解除。");
        }

        /// <summary>停止底层物理扫描并清空交互状态，防止旧候选在 UI 占用期间残留。</summary>
        private void StopDetectorAndClearOptions()
        {
            if (detector.IsDetecting) detector.PauseDetect();
            clearOptions();
        }

        /// <summary>只在检测控制已激活且没有 UI 阻断时消费扫描完成事件。</summary>
        private void HandleScanCompleted()
        {
            if (!active || !detectionRequested || IsBlocked) return;
            refreshOptions();
        }

        #endregion

        #region UI 阻断协调

        /// <summary>合并窗口占用和对话等独占 UI 来源，作为唯一检测阻断判定。</summary>
        private bool IsBlocked => windowBlocksDetection || gameUILockSources.Count > 0;

        /// <summary>任一窗口状态变化时，从 UIManager 的当前快照重新计算占用。</summary>
        /// <param name="eventArgs">刚发生状态变化的窗口快照。</param>
        private void HandleWindowStateChanged(UIWindowStateChangedEventArgs eventArgs)
        {
            ReconcileWindowBlock();
        }

        /// <summary>窗口销毁并从注册表移除后重新计算占用。</summary>
        /// <param name="snapshot">刚销毁窗口的最终快照。</param>
        private void HandleWindowDestroyed(UIWindowSnapshot snapshot)
        {
            ReconcileWindowBlock();
        }

        /// <summary>扫描全部窗口快照，保留 Showing、Visible 和 Hiding 阶段的阻断。</summary>
        private void ReconcileWindowBlock()
        {
            bool nextWindowBlock = HasBlockingWindow();
            if (windowBlocksDetection == nextWindowBlock) return;

            windowBlocksDetection = nextWindowBlock;
            ApplyDetectionState();
            WSLog.Log("[InteractionDetectionController] 窗口占用状态变化，" +
                      $"blocked={windowBlocksDetection}，gameUiLockCount={gameUILockSources.Count}。");
        }

        /// <summary>按游戏窗口策略判断当前是否存在阻断交互检测的窗口。</summary>
        /// <returns>存在非 HUD、非 Choice 的活动窗口时返回 true。</returns>
        private bool HasBlockingWindow()
        {
            if (!uiManager.IsInitialized) return false;

            IReadOnlyList<UIWindowSnapshot> windowSnapshots = uiManager.GetWindowSnapshots();
            for (int index = 0; index < windowSnapshots.Count; index++)
            {
                UIWindowSnapshot snapshot = windowSnapshots[index];
                if (snapshot.WindowName == nameof(HUDWindow) ||
                    snapshot.WindowName == nameof(ChoiceWindow))
                    continue;

                if (snapshot.State is UIWindowState.Showing or UIWindowState.Visible or UIWindowState.Hiding)
                    return true;
            }

            return false;
        }

        /// <summary>按稳定来源聚合 Game UI 锁；首个来源阻断，最后一个来源释放后重新协调检测。</summary>
        /// <param name="eventArgs">Game UI 锁申请或释放请求。</param>
        private void HandleGameUILockChangeRequested(GameUILockChangeRequestedEventArgs eventArgs)
        {
            bool changed = eventArgs.Operation == GameUILockOperation.Acquire
                ? gameUILockSources.Add(eventArgs.SourceId)
                : gameUILockSources.Remove(eventArgs.SourceId);
            if (!changed) return;

            ApplyDetectionState();
            WSLog.Log("[InteractionDetectionController] Game UI 锁来源已变化，" +
                      $"operation={eventArgs.Operation}, source={eventArgs.SourceId}, " +
                      $"sourceCount={gameUILockSources.Count}。");
        }

        #endregion
    }
}
