#if UNITY_EDITOR
using UnityEngine.UIElements;
using WS_Modules.MVVM;

namespace RPG.SkillSystem.Editor
{
    #region Inspector host
    /// <summary>
    /// 管理 Inspector Drawer 的选择、绑定与重绘生命周期。
    /// </summary>
    internal sealed class InspectorView
    {
        private readonly VisualElement root;
        private readonly TrackModuleRegistry modules;
        private EditorViewModel viewModel;
        private VisualElement container;

        /// <summary>
        /// 创建 Inspector 主视图。
        /// </summary>
        public InspectorView(VisualElement root, TrackModuleRegistry modules)
        {
            this.root = root ?? throw new System.ArgumentNullException(nameof(root));
            this.modules = modules ?? throw new System.ArgumentNullException(nameof(modules));
        }

        /// <summary>
        /// 绑定 ViewModel 并执行首次 Inspector 刷新。
        /// </summary>
        public void Bind(EditorViewModel model)
        {
            viewModel = model;
            container = root.Q<VisualElement>("InspectorContainer");
            viewModel.InspectorChanged += RefreshInspector;
            RefreshInspector();
        }

        /// <summary>
        /// 解除事件绑定并清理动态 Inspector 内容。
        /// </summary>
        public void Unbind()
        {
            if (viewModel != null) viewModel.InspectorChanged -= RefreshInspector;
            container?.Clear();
            viewModel = null;
        }

        // 根据当前具体 ViewData 类型选择 Drawer，避免使用 Kind 枚举分发。
        private void RefreshInspector()
        {
            container.Clear();
            IViewData selected = viewModel.SelectedViewData;
            if (selected == null)
            {
                Label empty = new("选择 Group、Track、Clip 或 Marker 后在这里编辑属性。");
                empty.AddToClassList("empty-inspector");
                container.Add(empty);
                return;
            }

            IInspectorDrawer drawer = modules.GetInspector(selected);
            if (drawer == null)
            {
                container.Add(new Label("当前选择没有可用 Inspector。"));
                return;
            }

            drawer.Draw(container, selected, viewModel);
        }
    }
    #endregion
}
#endif