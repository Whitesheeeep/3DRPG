#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 管理动态 TrackHeader 的右键菜单，并把静音与锁定切换转发为 ViewModel 语义命令。
    /// </summary>
    internal sealed class TrackContextMenuController
    {
        #region 依赖与注册状态

        private readonly EditorViewModel viewModel;
        private readonly Dictionary<VisualElement, ContextualMenuManipulator> manipulators = new();

        #endregion

        #region 生命周期

        // 创建只负责 TrackHeader 右键交互的控制器，不直接访问 Document 或 SkillConfig。
        internal TrackContextMenuController(EditorViewModel viewModel)
        {
            this.viewModel = viewModel;
        }

        // 为一条动态轨道标题注册菜单；行重建前必须通过 Reset 注销旧 Manipulator。
        internal void Register(TrackConfigBase track, VisualElement header)
        {
            ContextualMenuManipulator manipulator = new(evt => PopulateMenu(evt, track));
            header.AddManipulator(manipulator);
            manipulators.Add(header, manipulator);
        }

        // 注销全部动态标题菜单，避免 Timeline 重建后保留重复回调。
        internal void Reset()
        {
            foreach (KeyValuePair<VisualElement, ContextualMenuManipulator> pair in manipulators)
                pair.Key.RemoveManipulator(pair.Value);
            manipulators.Clear();
        }

        // 释放当前控制器持有的全部动态菜单注册。
        internal void Dispose() => Reset();

        #endregion

        #region 菜单构建与语义提交

        // 打开菜单时先选中目标轨道，再按当前投影状态显示勾选项。
        private void PopulateMenu(ContextualMenuPopulateEvent evt, TrackConfigBase track)
        {
            viewModel.SelectTrack(track);
            evt.menu.AppendAction("静音",
                _ => viewModel.SetTrackMuted(track, !track.Muted),
                _ => track.Muted
                    ? DropdownMenuAction.Status.Checked
                    : DropdownMenuAction.Status.Normal);
            evt.menu.AppendAction("锁定",
                _ => viewModel.SetTrackLocked(track, !track.EditorLocked),
                _ => track.EditorLocked
                    ? DropdownMenuAction.Status.Checked
                    : DropdownMenuAction.Status.Normal);
        }

        #endregion
    }
}
#endif
