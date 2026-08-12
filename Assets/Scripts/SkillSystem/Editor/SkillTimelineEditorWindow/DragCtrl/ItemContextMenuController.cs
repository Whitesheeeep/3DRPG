#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 管理 Item 的播放头吸附、复制和删除右键命令。
    /// </summary>
    internal sealed class ItemContextMenuController : IDisposable
    {
        #region 依赖与注册

        private readonly EditorViewModel viewModel;
        private readonly Dictionary<VisualElement, ContextualMenuManipulator> manipulators = new();

        /// <summary>
        /// 创建 Item 上下文菜单控制器。
        /// </summary>
        public ItemContextMenuController(EditorViewModel viewModel) =>
            this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        /// <summary>
        /// 为动态 Item 注册上下文菜单。
        /// </summary>
        /// <param name="itemView">需要响应右键操作的实际 Item View。</param>
        public void Register(ItemView itemView)
        {
            VisualElement element = itemView.Element;
            ContextualMenuManipulator manipulator = new(evt => PopulateMenu(evt, itemView));
            manipulators.Add(element, manipulator);
            element.AddManipulator(manipulator);
        }

        /// <summary>
        /// 解除全部动态菜单。
        /// </summary>
        public void Reset()
        {
            foreach (KeyValuePair<VisualElement, ContextualMenuManipulator> pair in manipulators)
                pair.Key.RemoveManipulator(pair.Value);
            manipulators.Clear();
        }

        /// <summary>
        /// 释放菜单注册。
        /// </summary>
        public void Dispose() => Reset();

        #endregion

        #region 菜单构建

        /// <summary>
        /// 在菜单打开时冻结播放头帧、选中目标 Item，并构建语义操作菜单。
        /// </summary>
        /// <param name="evt">当前 Item 的上下文菜单构建事件。</param>
        /// <param name="itemView">菜单对应的实际 Track 与 Item 引用。</param>
        private void PopulateMenu(ContextualMenuPopulateEvent evt, ItemView itemView)
        {
            int targetFrame = viewModel.CurrentFrame;
            viewModel.SelectItem(itemView.Track, itemView.Item);
            DropdownMenuAction.Status snapStatus =
                itemView.Track.EditorLocked || itemView.Item.StartFrame == targetFrame
                    ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal;
            evt.menu.AppendAction($"吸附到播放头（帧 {targetFrame}）",
                _ => viewModel.MoveItem(itemView.Track, itemView.Item, targetFrame), snapStatus);

            evt.menu.AppendSeparator();
            DropdownMenuAction.Status editStatus = itemView.Track.EditorLocked
                ? DropdownMenuAction.Status.Disabled
                : DropdownMenuAction.Status.Normal;
            // ViewModel 已在菜单打开时选中目标，因此复用现有选择语义和单次 Undo 事务。
            evt.menu.AppendAction("复制片段", _ => viewModel.DuplicateSelectedItem(), editStatus);
            evt.menu.AppendAction("删除片段", _ => viewModel.RemoveSelectedItem(), editStatus);
        }

        #endregion
    }
}
#endif
