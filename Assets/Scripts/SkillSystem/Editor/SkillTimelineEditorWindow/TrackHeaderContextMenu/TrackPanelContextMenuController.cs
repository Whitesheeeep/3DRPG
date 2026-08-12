#if UNITY_EDITOR
using System;
using UnityEngine.UIElements;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 为轨道面板空白区域提供扫描式添加轨道与显式类型重排菜单。
    /// </summary>
    internal sealed class TrackPanelContextMenuController : IDisposable
    {
        private readonly VisualElement panel;
        private readonly TrackModuleRegistry modules;
        private readonly EditorViewModel viewModel;
        private readonly ContextualMenuManipulator manipulator;

        /// <summary>
        /// 创建并注册轨道面板菜单。
        /// </summary>
        public TrackPanelContextMenuController(VisualElement panel,
            TrackModuleRegistry modules, EditorViewModel viewModel)
        {
            this.panel = panel ?? throw new ArgumentNullException(nameof(panel));
            this.modules = modules ?? throw new ArgumentNullException(nameof(modules));
            this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            manipulator = new ContextualMenuManipulator(PopulateMenu);
            panel.AddManipulator(manipulator);
        }

        /// <summary>
        /// 移除面板菜单操纵器。
        /// </summary>
        public void Dispose() => panel.RemoveManipulator(manipulator);

        // 只响应面板自身空白区域，避免与 TrackHeader 右键菜单叠加。
        private void PopulateMenu(ContextualMenuPopulateEvent evt)
        {
            if (evt.target != panel) return;
            foreach (TrackModule module in modules.Modules)
            {
                TrackModule captured = module;
                DropdownMenuAction.Status status = viewModel.CanAddTrack(captured).Succeeded
                    ? DropdownMenuAction.Status.Normal
                    : DropdownMenuAction.Status.Disabled;
                evt.menu.AppendAction($"添加轨道/{module.Metadata.MenuPath}",
                    _ => viewModel.AddTrack(captured), status);
            }
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("按轨道类型重排", _ => viewModel.SortTracksByType());
        }
    }
}
#endif
