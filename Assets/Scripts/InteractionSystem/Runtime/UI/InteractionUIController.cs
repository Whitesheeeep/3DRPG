using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using WS_Modules.UIModule;

namespace RPG.InteractionSystem.UI
{
    /// <summary>
    /// 连接 PlayerInteractor 与 ChoiceWindowView 的交互 Controller，负责 UI 意图适配和窗口显隐协调。
    /// </summary>
    public sealed class InteractionUIController : IDisposable
    {
        #region 依赖与状态

        // Controller 只协调领域模型、窗口 View 和 WSFrame 窗口 API，不拥有任何 Unity 生命周期。
        private readonly ChoiceWindow window;
        private readonly ChoiceWindowView view;
        private readonly List<string> optionNames = new();
        private readonly List<InteractionOptionId> optionIds = new();

        private PlayerInteractor interactor;
        private bool activated;
        private bool suppressNextVisibility;
        private bool openRequested;
        private bool disposed;

        #endregion

        #region 构造与生命周期

        /// <summary>
        /// 创建交互 UI Controller，并连接 View 和玩家实例变化事件。
        /// </summary>
        /// <param name="window">承载本次 MVC 组合的 ChoiceWindow。</param>
        /// <param name="view">负责选项渲染和点击转发的 View。</param>
        public InteractionUIController(ChoiceWindow window, ChoiceWindowView view)
        {
            this.window = window ?? throw new ArgumentNullException(nameof(window));
            this.view = view ?? throw new ArgumentNullException(nameof(view));

            view.ChoiceRequested += OnChoiceRequested;
            view.SelectionRequested += OnSelectionRequested;
            PlayerInteractor.InstanceChanged += OnPlayerInstanceChanged;
        }

        /// <summary>
        /// 激活 Controller 并绑定当前玩家；可重复调用而不会重复订阅。
        /// </summary>
        /// <param name="showCurrentOptions">是否允许本次激活立即按当前列表显示窗口；预加载阶段传入 false。</param>
        public void Activate(bool showCurrentOptions = true)
        {
            if (disposed) return;
            activated = true;
            suppressNextVisibility = !showCurrentOptions;
            BindInteractor(PlayerInteractor.Instance);
        }

        /// <summary>
        /// 释放 View、玩家和实例变化事件连接。
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            activated = false;

            PlayerInteractor.InstanceChanged -= OnPlayerInstanceChanged;
            view.ChoiceRequested -= OnChoiceRequested;
            view.SelectionRequested -= OnSelectionRequested;
            UnbindInteractor();
            optionNames.Clear();
            optionIds.Clear();
        }

        #endregion

        #region 玩家绑定

        /// <summary>
        /// 响应玩家单例建立或销毁，并在窗口已激活时切换模型订阅。
        /// </summary>
        /// <param name="nextInteractor">新的玩家交互模型；销毁时为空。</param>
        private void OnPlayerInstanceChanged(PlayerInteractor nextInteractor)
        {
            if (!activated || disposed) return;
            BindInteractor(nextInteractor);
        }

        /// <summary>
        /// 绑定新的 PlayerInteractor，并立即以其当前状态完成首次刷新。
        /// </summary>
        /// <param name="nextInteractor">待绑定的玩家交互模型。</param>
        private void BindInteractor(PlayerInteractor nextInteractor)
        {
            if (ReferenceEquals(interactor, nextInteractor))
            {
                if (activated && nextInteractor != null) HandleOptionsChanged(nextInteractor.Options);
                return;
            }

            UnbindInteractor();
            interactor = nextInteractor;
            if (interactor == null)
            {
                HandleOptionsChanged(Array.Empty<InteractionOption>());
                return;
            }

            interactor.OptionsChanged += OnOptionsChanged;
            interactor.SelectionChanged += OnSelectionChanged;
            HandleOptionsChanged(interactor.Options);
        }

        /// <summary>
        /// 对称解除当前 PlayerInteractor 的领域事件订阅。
        /// </summary>
        private void UnbindInteractor()
        {
            if (interactor == null) return;
            interactor.OptionsChanged -= OnOptionsChanged;
            interactor.SelectionChanged -= OnSelectionChanged;
            interactor = null;
        }

        #endregion

        #region 领域状态同步

        /// <summary>
        /// 响应最终 Option 列表变化并更新 View 与窗口可见性。
        /// </summary>
        /// <param name="options">最新的最终交互列表。</param>
        private void OnOptionsChanged(IReadOnlyList<InteractionOption> options) => HandleOptionsChanged(options);

        /// <summary>
        /// 响应当前选中项变化，只刷新 View 的高亮状态。
        /// </summary>
        /// <param name="option">最新选中的 Option。</param>
        private void OnSelectionChanged(InteractionOption option) => RefreshView();

        /// <summary>
        /// 将领域列表投影为 View 需要的名称和稳定 ID，并处理窗口显隐。
        /// </summary>
        /// <param name="options">最新的最终交互列表。</param>
        private void HandleOptionsChanged(IReadOnlyList<InteractionOption> options)
        {
            RebuildViewState(options);
            RefreshView();

            if (options.Count == 0)
            {
                HideWindow();
                suppressNextVisibility = false;
                return;
            }

            // 预加载只同步当前状态，不改变窗口可见性；下一次领域列表变化即可恢复正常显隐。
            if (suppressNextVisibility)
            {
                suppressNextVisibility = false;
                return;
            }

            if (!UIManager.Instance.IsInitialized)
            {
                return;
            }

            if (!window.Visible) TryOpenWindow();
        }

        /// <summary>
        /// 重建当前 UI 所需的轻量文本和 ID 投影；Option 领域对象仍由 PlayerInteractor 持有。
        /// </summary>
        /// <param name="options">待投影的领域 Option 列表。</param>
        private void RebuildViewState(IReadOnlyList<InteractionOption> options)
        {
            optionNames.Clear();
            optionIds.Clear();
            for (int index = 0; index < options.Count; index++)
            {
                InteractionOption option = options[index];
                optionNames.Add(option.DisplayName);
                optionIds.Add(option.Id);
            }
        }

        /// <summary>
        /// 根据当前领域选择计算 View 的选中索引并刷新显示。
        /// </summary>
        private void RefreshView()
        {
            int selectedIndex = -1;
            if (interactor?.SelectedOption != null)
            {
                for (int index = 0; index < optionIds.Count; index++)
                {
                    if (optionIds[index] != interactor.SelectedOption.Id) continue;
                    selectedIndex = index;
                    break;
                }
            }

            view.RefreshOptions(optionNames, selectedIndex);
        }

        #endregion

        #region 窗口显隐与点击

        /// <summary>
        /// 请求 UIManager 显示当前已预加载的 ChoiceWindow。
        /// </summary>
        private void TryOpenWindow()
        {
            if (openRequested || disposed || !activated || interactor == null ||
                interactor.Options.Count == 0) return;

            openRequested = true;
            OpenWindowAsync().Forget();
        }

        /// <summary>
        /// 等待窗口显示完成后再次读取领域状态，避免异步期间展示过期列表。
        /// </summary>
        private async UniTaskVoid OpenWindowAsync()
        {
            ChoiceWindow openedWindow = await UIManager.Instance.PopUpWindowAsync<ChoiceWindow>();
            openRequested = false;
            if (disposed || !activated || interactor == null)
            {
                if (openedWindow != null) UIManager.Instance.HideWindow<ChoiceWindow>();
                return;
            }

            if (interactor.Options.Count == 0)
            {
                HideWindow();
                return;
            }

            // PopUpWindowAsync 返回同一常驻实例；此处重新读取模型，覆盖打开期间可能变化的快照。
            RebuildViewState(interactor.Options);
            RefreshView();
        }

        /// <summary>
        /// 在 UIManager 已初始化时隐藏交互窗口，但保留窗口实例和 View 行。
        /// </summary>
        private void HideWindow()
        {
            if (UIManager.Instance.IsInitialized) UIManager.Instance.HideWindow<ChoiceWindow>();
        }

        /// <summary>
        /// 响应 View 点击，先按索引选择稳定 Option，再调用统一提交入口。
        /// </summary>
        /// <param name="index">被点击行在当前投影列表中的索引。</param>
        private void OnChoiceRequested(int index)
        {
            if (interactor == null || index < 0 || index >= optionIds.Count) return;
            if (!interactor.Select(optionIds[index])) return;
            interactor.SubmitSelected();
        }

        /// <summary>响应 UI Navigate 选中结果，只更新 PlayerInteractor 的领域 Selection，不执行 Option。</summary>
        /// <param name="index">被选中行在当前投影列表中的索引。</param>
        private void OnSelectionRequested(int index)
        {
            if (interactor == null || index < 0 || index >= optionIds.Count) return;
            interactor.Select(optionIds[index]);
        }

        #endregion
    }
}
