using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using WS_Modules.UIModule;

namespace RPG.InteractionSystem.UI
{
    /// <summary>把 PlayerInteractor 的 Option 状态连接到 WSFrame 常驻交互 HUD。</summary>
    [DefaultExecutionOrder(-650)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInteractor))]
    public sealed class InteractionUIController : MonoBehaviour
    {
        #region 状态

        [SerializeField] private PlayerInteractor interactor;
        private InteractionOptionWindow window;
        private bool openRequested;

        #endregion

        #region Unity 生命周期

        /// <summary>解析同节点交互编排组件。</summary>
        private void Awake()
        {
            if (interactor == null) interactor = GetComponent<PlayerInteractor>();
        }

        /// <summary>订阅交互状态并尝试连接窗口。</summary>
        private void OnEnable()
        {
            interactor.OptionsChanged += OnOptionsChanged;
            interactor.SelectionChanged += OnSelectionChanged;
            TryOpenWindow();
        }

        /// <summary>在 UIManager 初始化完成后重试窗口打开。</summary>
        private void Update()
        {
            if (window == null && !openRequested) TryOpenWindow();
        }

        /// <summary>解绑交互事件并隐藏当前窗口。</summary>
        private void OnDisable()
        {
            if (interactor != null)
            {
                interactor.OptionsChanged -= OnOptionsChanged;
                interactor.SelectionChanged -= OnSelectionChanged;
            }

            if (window != null && UIManager.Instance.IsInitialized)
                UIManager.Instance.HideWindow<InteractionOptionWindow>();
        }

        #endregion

        #region 窗口连接

        /// <summary>通过 UIManager 异步获取常驻窗口实例。</summary>
        private void TryOpenWindow()
        {
            if (openRequested || !isActiveAndEnabled || !UIManager.Instance.IsInitialized) return;
            openRequested = true;
            OpenWindowAsync().Forget();
        }

        /// <summary>等待 WSFrame 窗口加载完成并完成首次状态刷新。</summary>
        private async UniTaskVoid OpenWindowAsync()
        {
            InteractionOptionWindow openedWindow = await UIManager.Instance.PopUpWindowAsync<InteractionOptionWindow>();
            openRequested = false;
            if (!isActiveAndEnabled)
            {
                if (openedWindow != null) UIManager.Instance.HideWindow<InteractionOptionWindow>();
                return;
            }

            window = openedWindow;
            RefreshWindow();
        }

        /// <summary>把当前交互列表和选择传给窗口。</summary>
        private void RefreshWindow()
        {
            if (window == null || interactor == null) return;
            window.Refresh(interactor.Options, interactor.SelectedOption);
        }

        /// <summary>响应 Option 列表变化。</summary>
        /// <param name="options">最新 Option 列表。</param>
        private void OnOptionsChanged(IReadOnlyList<InteractionOption> options) => RefreshWindow();

        /// <summary>响应选中 Option 变化。</summary>
        /// <param name="option">最新选中 Option。</param>
        private void OnSelectionChanged(InteractionOption option) => RefreshWindow();

        #endregion
    }
}
