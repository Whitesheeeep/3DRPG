#if UNITY_EDITOR
using System;
using RPG.SkillSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 管理 Item 移动时的源元素弱化、鼠标 Ghost 与目标 Lane 吸附占位三层视觉预览。
    /// </summary>
    internal sealed class ItemDragPreviewView : IDisposable
    {
        #region 常量与依赖

        private const string SourceClass = "is-drag-source";
        private const string GhostClass = "is-drag-ghost";
        private const string PlaceholderClass = "is-drag-placeholder";
        private const string ValidClass = "is-drag-valid";
        private const string InvalidClass = "is-drag-invalid";

        private readonly VisualElement overlay;
        private readonly TrackModuleRegistry modules;
        private readonly ElementFactory elementFactory;
        private readonly CoordinateMapper mapper;

        #endregion

        #region 预览状态

        private ItemView sourceView;
        private ItemView ghostView;
        private ItemView placeholderView;
        private Vector2 pointerGrabOffset;

        #endregion

        /// <summary>
        /// 创建只负责临时视觉投影的拖拽预览，不持有或修改任何 Config 数据。
        /// </summary>
        public ItemDragPreviewView(VisualElement overlay, TrackModuleRegistry modules,
            ElementFactory elementFactory, CoordinateMapper mapper)
        {
            this.overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
            this.modules = modules ?? throw new ArgumentNullException(nameof(modules));
            this.elementFactory = elementFactory ?? throw new ArgumentNullException(nameof(elementFactory));
            this.mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        }

        #region 预览操作

        /// <summary>
        /// 根据实际 Item 创建两个同类型临时 View，并记录鼠标在源块内的抓取偏移。
        /// </summary>
        public void Begin(ItemView source, Vector3 pointerWorldPosition)
        {
            End();
            sourceView = source ?? throw new ArgumentNullException(nameof(source));
            pointerGrabOffset = new Vector2(
                pointerWorldPosition.x - source.Element.worldBound.xMin,
                pointerWorldPosition.y - source.Element.worldBound.yMin);
            source.Element.AddToClassList(SourceClass);

            ghostView = CreatePreview(source, GhostClass);
            placeholderView = CreatePreview(source, PlaceholderClass);
            overlay.Add(ghostView.Element);
            UpdatePointer(pointerWorldPosition);
        }

        /// <summary>
        /// 将 Ghost 放在鼠标位置，同时保持 PointerDown 时的块内抓取点不跳变。
        /// </summary>
        public void UpdatePointer(Vector3 pointerWorldPosition)
        {
            if (ghostView == null) return;
            Vector2 local = overlay.WorldToLocal(pointerWorldPosition);
            ghostView.Element.style.left = local.x - pointerGrabOffset.x;
            ghostView.Element.style.top = local.y - pointerGrabOffset.y;
        }

        /// <summary>
        /// 在目标 Lane 内容坐标中显示整数帧占位，并同步有效或无效状态。
        /// </summary>
        public void ShowPlacement(VisualElement lane, int startFrame, int durationFrames, bool isValid)
        {
            if (placeholderView == null) return;
            if (placeholderView.Element.parent != lane)
            {
                placeholderView.Element.RemoveFromHierarchy();
                lane?.Add(placeholderView.Element);
            }

            if (lane != null)
                placeholderView.RefreshGeometry(startFrame, durationFrames);
            SetValidationState(ghostView?.Element, isValid);
            SetValidationState(placeholderView.Element, isValid);
        }

        /// <summary>
        /// 清除目标 Lane 占位，并将 Ghost 标为不可放置状态。
        /// </summary>
        public void ClearPlacement()
        {
            placeholderView?.Element.RemoveFromHierarchy();
            SetValidationState(ghostView?.Element, false);
        }

        /// <summary>
        /// 删除全部临时元素并恢复源 Item 的正常显示。
        /// </summary>
        public void End()
        {
            sourceView?.Element.RemoveFromClassList(SourceClass);
            ghostView?.Element.RemoveFromHierarchy();
            placeholderView?.Element.RemoveFromHierarchy();
            sourceView = null;
            ghostView = null;
            placeholderView = null;
        }

        /// <summary>
        /// 释放当前拖拽预览。
        /// </summary>
        public void Dispose() => End();

        #endregion

        #region 内部辅助

        // 复用模块 ItemFactory 保持五类 Item 的 UXML 与 USS 外观完全一致，并关闭预览元素 Picking。
        private ItemView CreatePreview(ItemView source, string stateClass)
        {
            ItemView preview = modules.CreateItemView(source.Track, source.Item, elementFactory, mapper);
            preview.Element.pickingMode = PickingMode.Ignore;
            preview.Element.AddToClassList(stateClass);
            if (preview.ResizeLeft != null) preview.ResizeLeft.style.display = DisplayStyle.None;
            if (preview.ResizeRight != null) preview.ResizeRight.style.display = DisplayStyle.None;
            preview.RefreshGeometry(0, source.Item.DurationFrames);
            return preview;
        }

        // 有效性只通过 USS 状态表达，避免预览层参与任何业务校验或提交。
        private static void SetValidationState(VisualElement element, bool isValid)
        {
            if (element == null) return;
            element.EnableInClassList(ValidClass, isValid);
            element.EnableInClassList(InvalidClass, !isValid);
        }

        #endregion
    }

}
#endif
