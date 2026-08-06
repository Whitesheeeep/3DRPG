#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 管理 Item 右键“吸附到播放头”菜单。
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
        /// 为动态 Item 注册播放头吸附菜单。
        /// </summary>
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

        // 菜单打开时冻结播放头帧并选中目标实际 Item。
        private void PopulateMenu(ContextualMenuPopulateEvent evt, ItemView itemView)
        {
            int targetFrame = viewModel.CurrentFrame;
            viewModel.SelectItem(itemView.Track, itemView.Item);
            DropdownMenuAction.Status status =
                itemView.Track.EditorLocked || itemView.Item.StartFrame == targetFrame
                    ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal;
            evt.menu.AppendAction($"吸附到播放头（帧 {targetFrame}）",
                _ => viewModel.MoveItem(itemView.Track, itemView.Item, targetFrame), status);;
        }

        #endregion
    }
}
#endif