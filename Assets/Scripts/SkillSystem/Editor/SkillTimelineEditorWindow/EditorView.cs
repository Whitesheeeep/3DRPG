#if UNITY_EDITOR
using System;
using UnityEngine.UIElements;
using WS_Modules.MVVM;
using WS_Modules.UIToolkitExtensions.Editor;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 组合工具栏、时间轴 Canvas 与 Inspector 三个主视图区域。
    /// </summary>
    internal sealed class EditorView : IView<EditorViewModel>
    {
        #region 依赖与子视图

        private const string InspectorWidthSessionKey = "RPG.SkillTimeline.InspectorWidth";
        private const string TrackHeaderWidthSessionKey = "RPG.SkillTimeline.TrackHeaderWidth";
        private readonly VisualElement root;
        private readonly EditorConfig config;
        private readonly TrackModuleRegistry modules;
        private EditorViewModel viewModel;
        // 内层数据通信
        private readonly CanvasModel canvasModel;
        // 上侧的 Toolbar 视图
        private ToolbarView toolbarView;
        // 下侧的 Inspector 视图
        private InspectorView inspectorView;
        // Canvas MVC 的 View、Controller 与坐标映射器由主视图作为同级对象统一持有。
        private CanvasView canvasView;
        private CanvasController canvasController;
        private CoordinateMapper canvasMapper;
        private Label statusLabel;

        #endregion

        #region 绑定生命周期

        /// <summary>
        /// 创建共享同一 Canvas 表现模型和 Editor-only Config 的主视图。
        /// </summary>
        public EditorView(VisualElement root,
            EditorConfig config, TrackModuleRegistry modules)
        {
            this.canvasModel = new CanvasModel(config);
            this.root = root ?? throw new ArgumentNullException(nameof(root));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.modules = modules ?? throw new ArgumentNullException(nameof(modules));
        }

        /// <summary>
        /// 绑定 ViewModel、注册事件并执行首次界面刷新。
        /// </summary>
        public void Bind(EditorViewModel model)
        {
            Unbind();
            viewModel = model;
            statusLabel = root.Q<Label>("StatusLabel");
            ConfigureSplitViews();
            toolbarView = new ToolbarView(root, canvasModel);
            canvasView = new CanvasView(root);
            canvasView.Initialize();
            canvasMapper = new CoordinateMapper(canvasModel);
            canvasController = new CanvasController(
                canvasView, canvasModel, canvasMapper, config, modules);
            inspectorView = new InspectorView(root, modules);
            toolbarView.Bind(model);
            canvasController.Bind(model);
            inspectorView.Bind(model);
            model.StatusChanged += RefreshStatus;
            RefreshStatus();
        }

        /// <summary>
        /// 解除事件绑定并清空持有的界面引用。
        /// </summary>
        public void Unbind()
        {
            if (viewModel != null) viewModel.StatusChanged -= RefreshStatus;
            toolbarView?.Unbind();
            canvasController?.Dispose();
            canvasView?.Dispose();
            inspectorView?.Unbind();
            toolbarView = null;
            canvasController = null;
            canvasView = null;
            canvasMapper = null;
            inspectorView = null;
            viewModel = null;
        }

        #endregion

        #region 状态刷新

        // 将 Inspector 与轨道标题宽度范围注入通用 SplitView，不接管其 Pointer 交互。
        private void ConfigureSplitViews()
        {
            CustomTwoPanelSplitView mainSplitView = root.Q<CustomTwoPanelSplitView>("MainSplitView") ??
                                                    throw new InvalidOperationException("主 UXML 缺少 MainSplitView。");
            mainSplitView.ConfigureFixedPane(
                config.InspectorMinimumWidth,
                config.InspectorDefaultWidth,
                config.InspectorMaximumWidth,
                InspectorWidthSessionKey);

            CustomTwoPanelSplitView headerSplitView = root.Q<CustomTwoPanelSplitView>("HeaderTimelineSplit") ??
                                                      throw new InvalidOperationException("主 UXML 缺少 HeaderTimelineSplit。");
            headerSplitView.ConfigureFixedPane(
                config.TrackHeaderMinimumWidth,
                config.TrackHeaderDefaultWidth,
                config.TrackHeaderMaximumWidth,
                TrackHeaderWidthSessionKey);
        }

        // 刷新窗口底部状态提示，不参与任何资产修改。
        private void RefreshStatus()
        {
            if (statusLabel != null) statusLabel.text = viewModel?.StatusMessage ?? string.Empty;
        }

        #endregion
    }
}
#endif
