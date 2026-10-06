using System;
using Cysharp.Threading.Tasks;
using RPG.Game.Loading;
using UnityEngine;
using WS_Modules.LogModule;
using WS_Modules.Singleton;
using WS_Modules.UIModule;

namespace RPG.Game.UI
{
    /// <summary>
    /// 项目级窗口预加载服务，统一初始化 HUD、Choice、Dialogue、Bag、Character 和装备培养窗口。
    /// </summary>
    //TODO: 后续：1. 将 Preload Services 作为 SO 加入到统一的预加载管理器中，允许按需注册和初始化；2. 将窗口预加载服务拆分为独立的 HUD、Choice、Dialogue、Bag、Character 和装备培养预加载服务，允许按需初始化。
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-850)]
    public sealed class GameWindowPreloadService : SingletonMonoBase<GameWindowPreloadService>, IWindowPreloadService
    {
        #region 状态

        // 预加载任务作为跨调用方共享的完成信号，保证并发请求不会重复初始化窗口。
        private UniTaskCompletionSource preloadCompletionSource;
        private UniTaskCompletionSource hudShowCompletionSource;
        private bool preloadStarted;
        private bool preloaded;
        private bool hudShowStarted;
        private int completedWindowCount;
        private const int TotalPreloadWindowCount = 6;
        // 直接启动场景沿用原行为；统一加载场景可关闭后等待显式预加载任务。
        [SerializeField, Tooltip("关闭后由统一场景加载任务启动窗口预加载。")]
        private bool preloadOnStart = true;

        #endregion

        #region 属性

        /// <summary>获取全部项目窗口是否已经完成预加载。</summary>
        public bool IsPreloaded => preloaded;
        /// <summary>获取已完成的窗口准备占比，用于加载树任务映射局部进度。</summary>
        public float PreloadProgress => preloaded ? 1f : completedWindowCount / (float)TotalPreloadWindowCount;
        /// <summary>某个项目窗口完成准备时发送新的窗口预加载占比。</summary>
        public event Action<float> PreloadProgressChanged;

        #endregion

        #region Unity 生命周期

        /// <summary>在框架根节点保留后启动一次窗口预加载。</summary>
        private void Start()
        {
            if (preloadOnStart) PreloadAsync().Forget();
        }

        /// <summary>提供编辑器调试用 F1 显隐切换，不参与窗口预加载时序。</summary>
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1) && UIManager.Instance.IsInitialized &&
                UIManager.Instance.TryGetWindow(out HUDWindow hudWindow))
            {
                if (hudWindow.Visible)
                    UIManager.Instance.HideWindow<HUDWindow>();
                else
                    UIManager.Instance.PopUpWindow<HUDWindow>();
            }
        }
        #endregion

        #region 预加载入口

        /// <summary>
        /// 执行可重复等待的全窗口预加载；并发调用共享同一个完成任务。
        /// </summary>
        /// <returns>HUD、Choice、Dialogue、Bag、Character 和装备培养窗口全部初始化完成的任务。</returns>
        public UniTask PreloadAsync()
        {
            return PreloadAndShowHudAsync();
        }

        /// <summary>确保项目窗口已全部初始化但不更改 HUD 可见状态。</summary>
        /// <returns>全部窗口完成准备后的共享异步任务。</returns>
        public UniTask PrepareAsync()
        {
            if (!preloadStarted)
            {
                preloadStarted = true;
                preloadCompletionSource = new UniTaskCompletionSource();
                WSLog.Log($"[GameWindowPreloadService] 开始共享窗口准备，service={name}。");
                PreloadCoreAsync().Forget();
            }

            return preloadCompletionSource.Task;
        }

        /// <summary>等待窗口准备完成后只打开一次 HUD，并共享该显示操作的结果。</summary>
        /// <returns>HUD 已显示或显示失败的异步任务。</returns>
        public async UniTask ShowHudAsync()
        {
            await PrepareAsync();
            if (!hudShowStarted)
            {
                hudShowStarted = true;
                hudShowCompletionSource = new UniTaskCompletionSource();
                ShowHudCoreAsync().Forget();
            }
            await hudShowCompletionSource.Task;
        }

        /// <summary>兼容既有调用：共享准备完成后显示 HUD。</summary>
        private async UniTask PreloadAndShowHudAsync()
        {
            await PrepareAsync();
            await ShowHudAsync();
        }

        /// <summary>
        /// 等待 UIManager 初始化后并行准备六个项目窗口，并按成功完成数量发布进度。
        /// </summary>
        private async UniTaskVoid PreloadCoreAsync()
        {
            try
            {
                await UniTask.WaitUntil(() => UIManager.Instance.IsInitialized);

                // 各窗口互不依赖并行准备；加载阶段不切换 HUD 可见状态。
                UniTask[] preloadTasks =
                {
                    TrackWindowAsync<HUDWindow>(),
                    TrackWindowAsync<ChoiceWindow>(),
                    TrackWindowAsync<DialogueWindow>(),
                    TrackWindowAsync<BagWindow>(),
                    TrackWindowAsync<CharacterWindow>(),
                    TrackWindowAsync<EquipmentDevelopmentWindow>()
                };
                await UniTask.WhenAll(preloadTasks);
                preloaded = true;
                preloadCompletionSource.TrySetResult();
                WSLog.Log($"[GameWindowPreloadService] 全部项目窗口准备完成，service={name}。");
            }
            catch (Exception exception)
            {
                preloadCompletionSource.TrySetException(exception);
                WSLog.LogError($"[GameWindowPreloadService] 窗口预加载失败，service={name}，exception={exception}");
            }
        }

        /// <summary>兼容入口在共享窗口资源准备后执行一次 HUD 显示。</summary>
        private async UniTaskVoid ShowHudCoreAsync()
        {
            try
            {
                HUDWindow openedHud = await UIManager.Instance.PopUpWindowAsync<HUDWindow>();
                if (openedHud == null || !openedHud.Visible)
                    throw new InvalidOperationException("HUDWindow 准备后打开失败或仍处于隐藏状态。");
                hudShowCompletionSource.TrySetResult();
                WSLog.Log("[GameWindowPreloadService] HUDWindow 已显示。");
            }
            catch (Exception exception)
            {
                hudShowCompletionSource.TrySetException(exception);
                WSLog.LogError($"[GameWindowPreloadService] HUD 显示失败，service={name}，exception={exception}");
            }
        }

        /// <summary>准备单个窗口并在该窗口成功后推进共享的预加载进度。</summary>
        /// <typeparam name="TWindow">需要实例化并初始化的窗口类型。</typeparam>
        /// <returns>窗口准备与进度通知结束后的异步任务。</returns>
        private async UniTask TrackWindowAsync<TWindow>() where TWindow : WindowBase, new()
        {
            if (typeof(TWindow) == typeof(ChoiceWindow))
                await PreloadChoiceAsync();
            else if (typeof(TWindow) == typeof(DialogueWindow))
                await PreloadDialogueAsync();
            else
                await PreloadWindowAsync<TWindow>();
            completedWindowCount++;
            PublishPreloadProgress();
        }

        /// <summary>发布常驻窗口准备进度，并隔离观察者异常以免阻断共享预加载。</summary>
        private void PublishPreloadProgress()
        {
            try
            {
                PreloadProgressChanged?.Invoke(PreloadProgress);
            }
            catch (Exception listenerException)
            {
                WSLog.LogError($"[GameWindowPreloadService] 预加载进度订阅者发生异常，completed={completedWindowCount}/{TotalPreloadWindowCount}，exception={listenerException}");
            }
        }

        /// <summary>预加载 ChoiceWindow 并等待其交互 View 初始化完成。</summary>
        private async UniTask PreloadChoiceAsync()
        {
            await PreloadWindowAsync<ChoiceWindow>();
            if (!UIManager.Instance.TryGetWindow(out ChoiceWindow choiceWindow))
                throw new InvalidOperationException("ChoiceWindow 预加载后未找到窗口实例。");

            await choiceWindow.WaitUntilReadyAsync();
            choiceWindow.ActivateInteractionController();
        }

        /// <summary>预加载 DialogueWindow 并等待其内部 View 初始化完成。</summary>
        private async UniTask PreloadDialogueAsync()
        {
            await PreloadWindowAsync<DialogueWindow>();
            if (!UIManager.Instance.TryGetWindow(out DialogueWindow dialogueWindow))
                throw new InvalidOperationException("DialogueWindow 预加载后未找到窗口实例。");

            await dialogueWindow.WaitUntilReadyAsync();
        }

        /// <summary>
        /// 通过 UIManager 预加载指定窗口并确认窗口实例已经进入注册表。
        /// </summary>
        /// <typeparam name="TWindow">窗口类型。</typeparam>
        /// <returns>窗口初始化任务。</returns>
        private async UniTask PreloadWindowAsync<TWindow>() where TWindow : WindowBase, new()
        {
            await UIManager.Instance.PreLoadWindowAsync<TWindow>();
            if (!UIManager.Instance.TryGetWindow<TWindow>(out _))
                throw new InvalidOperationException($"窗口预加载后未找到实例：{typeof(TWindow).Name}。");
        }

        #endregion
    }
}
