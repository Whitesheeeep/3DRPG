using System;
using Cysharp.Threading.Tasks;
using RPG.Game.Loading;
using UnityEngine;
using WS_Modules.Singleton;
using WS_Modules.UIModule;

namespace RPG.Game.UI
{
    /// <summary>
    /// 项目级窗口预加载服务，统一初始化 HUD、Choice 和 Dialogue 窗口。
    /// </summary>
    //TODO: 后续：1. 将 Preload Services 作为 SO 加入到统一的预加载管理器中，允许按需注册和初始化；2. 将窗口预加载服务拆分为独立的 HUD、Choice 和 Dialogue 预加载服务，允许按需初始化。
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-850)]
    public sealed class GameWindowPreloadService : SingletonMonoBase<GameWindowPreloadService>, IWindowPreloadService
    {
        #region 状态

        // 预加载任务作为跨调用方共享的完成信号，保证并发请求不会重复初始化窗口。
        private UniTaskCompletionSource preloadCompletionSource;
        private bool preloadStarted;
        private bool preloaded;

        #endregion

        #region 属性

        /// <summary>获取全部项目窗口是否已经完成预加载。</summary>
        public bool IsPreloaded => preloaded;

        #endregion

        #region Unity 生命周期

        /// <summary>在框架根节点保留后启动一次窗口预加载。</summary>
        private void Start()
        {
            PreloadAsync().Forget();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1))
            {
                if (UIManager.Instance.GetWindow<HUDWindow>().Visible) UIManager.Instance.PopUpWindow<HUDWindow>();
                else
                {
                    UIManager.Instance.HideWindow<HUDWindow>();
                }
            }
        }
        #endregion

        #region 预加载入口

        /// <summary>
        /// 执行可重复等待的全窗口预加载；并发调用共享同一个完成任务。
        /// </summary>
        /// <returns>HUD、Choice 和 Dialogue 全部初始化完成的任务。</returns>
        public UniTask PreloadAsync()
        {
            if (!preloadStarted)
            {
                preloadStarted = true;
                preloadCompletionSource = new UniTaskCompletionSource();
                PreloadCoreAsync().Forget();
            }

            return preloadCompletionSource.Task;
        }

        /// <summary>
        /// 等待 UIManager 初始化后按固定顺序创建窗口，并等待 ChoiceWindow 的行 View 完成初始化。
        /// </summary>
        private async UniTaskVoid PreloadCoreAsync()
        {
            try
            {
                await UniTask.WaitUntil(() => UIManager.Instance.IsInitialized);

                await PreloadWindowAsync<HUDWindow>();
                await PreloadWindowAsync<ChoiceWindow>();

                if (!UIManager.Instance.TryGetWindow(out ChoiceWindow choiceWindow))
                    throw new InvalidOperationException("ChoiceWindow 预加载后未找到窗口实例。");

                await choiceWindow.WaitUntilReadyAsync();
                choiceWindow.ActivateInteractionController();

                await PreloadWindowAsync<DialogueWindow>();
                if (!UIManager.Instance.TryGetWindow(out DialogueWindow dialogueWindow))
                    throw new InvalidOperationException("DialogueWindow 预加载后未找到窗口实例。");
                await dialogueWindow.WaitUntilReadyAsync();
                preloaded = true;
                preloadCompletionSource.TrySetResult();
            }
            catch (Exception exception)
            {
                preloadCompletionSource.TrySetException(exception);
            }
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
