#if UNITY_EDITOR
using System;
using UnityEngine.UIElements;
using WS_Modules.MVVM;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 组合工具栏、时间轴 Canvas 与 Inspector 三个主视图区域。
    /// </summary>
    internal sealed class SkillTimelineEditorView : IView<SkillTimelineEditorViewModel>
    {
        private readonly VisualElement root;
        private readonly SkillTimelineViewportController viewport;
        private readonly SkillTimelineEditorConfig config;
        private SkillTimelineEditorViewModel viewModel;
        private SkillTimelineToolbarView toolbarView;
        private SkillTimelineCanvasView canvasView;
        private SkillTimelineInspectorView inspectorView;
        private Label statusLabel;

        /// <summary>
        /// 创建使用同一视口状态和 Editor-only Config 的主视图。
        /// </summary>
        public SkillTimelineEditorView(VisualElement root, SkillTimelineViewportController viewport,
            SkillTimelineEditorConfig config)
        {
            this.root = root ?? throw new ArgumentNullException(nameof(root));
            this.viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// 绑定 ViewModel、注册事件并执行首次界面刷新。
        /// </summary>
        public void Bind(SkillTimelineEditorViewModel model)
        {
            Unbind();
            viewModel = model;
            statusLabel = root.Q<Label>("StatusLabel");
            toolbarView = new SkillTimelineToolbarView(root, viewport);
            canvasView = new SkillTimelineCanvasView(root, viewport, config);
            inspectorView = new SkillTimelineInspectorView(root);
            toolbarView.Bind(model);
            canvasView.Bind(model);
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
            canvasView?.Unbind();
            inspectorView?.Unbind();
            toolbarView = null;
            canvasView = null;
            inspectorView = null;
            viewModel = null;
        }

        // 刷新窗口底部状态提示，不参与任何资产修改。
        private void RefreshStatus()
        {
            if (statusLabel != null) statusLabel.text = viewModel?.StatusMessage ?? string.Empty;
        }
    }
}
#endif