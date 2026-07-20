#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 管理 Clip 与 Marker 的 Pointer Capture、UI 草稿位置、取消和单次语义提交。
    /// </summary>
    internal sealed class SkillTimelineItemDragController : IDisposable
    {
        private readonly SkillTimelineViewportController viewport;
        private readonly List<VisualElement> registeredElements = new();
        private SkillTimelineEditorViewModel viewModel;
        private TimelineDragState state;
        private SkillTimelineItemView activeView;
        private int pointerId = -1;

        /// <summary>
        /// 创建使用指定视口缩放状态的拖拽控制器。
        /// </summary>
        public SkillTimelineItemDragController(SkillTimelineViewportController viewport) =>
            this.viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));

        /// <summary>
        /// 绑定最终移动、裁剪和选择意图的接收方。
        /// </summary>
        public void Bind(SkillTimelineEditorViewModel model) => viewModel = model;

        /// <summary>
        /// 为一个动态 Item View 注册拖拽事件。
        /// </summary>
        public void Register(SkillTimelineItemView itemView)
        {
            VisualElement element = itemView.Element;
            element.RegisterCallback<PointerDownEvent>(OnPointerDown);
            element.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            element.RegisterCallback<PointerUpEvent>(OnPointerUp);
            element.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            registeredElements.Add(element);
        }

        /// <summary>
        /// 取消当前草稿并解除全部动态元素事件，供时间轴重建使用。
        /// </summary>
        public void Reset()
        {
            Cancel();
            foreach (VisualElement element in registeredElements)
            {
                element.UnregisterCallback<PointerDownEvent>(OnPointerDown);
                element.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
                element.UnregisterCallback<PointerUpEvent>(OnPointerUp);
                element.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            }
            registeredElements.Clear();
        }

        /// <summary>
        /// 放弃当前拖拽草稿并恢复权威帧位置。
        /// </summary>
        public void Cancel()
        {
            SkillTimelineItemView itemView = activeView;
            TimelineDragState dragState = state;
            VisualElement capture = itemView?.Element;
            int capturedPointer = pointerId;
            state = null;
            activeView = null;
            pointerId = -1;
            if (dragState != null && itemView != null)
                itemView.RefreshGeometry(dragState.OriginalStartFrame, dragState.OriginalDurationFrames);
            if (capture != null && capturedPointer >= 0 && capture.HasPointerCapture(capturedPointer))
                capture.ReleasePointer(capturedPointer);
        }

        /// <summary>
        /// 释放拖拽状态和全部动态回调。
        /// </summary>
        public void Dispose()
        {
            Reset();
            viewModel = null;
        }

        // 捕获左键并根据命中的手柄确定移动或裁剪模式。
        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || evt.currentTarget is not VisualElement element ||
                element.userData is not SkillTimelineItemView itemView || itemView.Track.Locked) return;

            SkillTimelineDragMode mode = SkillTimelineDragMode.Move;
            if (evt.target == itemView.ResizeLeft) mode = SkillTimelineDragMode.ResizeLeft;
            else if (evt.target == itemView.ResizeRight) mode = SkillTimelineDragMode.ResizeRight;
            if (!itemView.Item.IsResizable) mode = SkillTimelineDragMode.Move;

            activeView = itemView;
            pointerId = evt.pointerId;
            state = new TimelineDragState
            {
                Mode = mode,
                Track = itemView.Track,
                Item = itemView.Item,
                OriginalStartFrame = itemView.Item.StartFrame,
                OriginalDurationFrames = itemView.Item.DurationFrames,
                DraftStartFrame = itemView.Item.StartFrame,
                DraftDurationFrames = itemView.Item.DurationFrames,
                PointerStartX = evt.position.x
            };
            viewModel.Select(SkillTimelinePresentationTypeUtility.CreateItemSelection(itemView.Track, itemView.Item));
            element.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        // 把像素位移吸附为整数帧，并只更新 Item View 的草稿几何。
        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (state == null || activeView == null || pointerId != evt.pointerId) return;
            int delta = Mathf.RoundToInt((evt.position.x - state.PointerStartX) / viewport.PixelsPerFrame);
            int originalEnd = state.OriginalStartFrame + state.OriginalDurationFrames;
            switch (state.Mode)
            {
                case SkillTimelineDragMode.Move:
                    state.DraftStartFrame = Mathf.Max(0, state.OriginalStartFrame + delta);
                    break;
                case SkillTimelineDragMode.ResizeLeft:
                    state.DraftStartFrame = Mathf.Clamp(state.OriginalStartFrame + delta, 0, originalEnd - 1);
                    state.DraftDurationFrames = originalEnd - state.DraftStartFrame;
                    break;
                case SkillTimelineDragMode.ResizeRight:
                    state.DraftDurationFrames = Mathf.Max(1, state.OriginalDurationFrames + delta);
                    break;
            }
            activeView.RefreshGeometry(state.DraftStartFrame, state.DraftDurationFrames);
            evt.StopPropagation();
        }

        // 清理 Pointer Capture 后，把最终整数帧作为一次语义操作提交给 ViewModel。
        private void OnPointerUp(PointerUpEvent evt)
        {
            if (state == null || activeView == null || pointerId != evt.pointerId) return;
            TimelineDragState completed = state;
            SkillTimelineItemView itemView = activeView;
            VisualElement capture = itemView.Element;
            state = null;
            activeView = null;
            pointerId = -1;
            if (capture.HasPointerCapture(evt.pointerId)) capture.ReleasePointer(evt.pointerId);

            TimelineEditResult result = completed.Mode == SkillTimelineDragMode.Move
                ? viewModel.MoveItem(completed.Track, completed.Item, completed.DraftStartFrame)
                : viewModel.ResizeItem(completed.Track, completed.Item,
                    completed.DraftStartFrame, completed.DraftDurationFrames);
            if (!result.Succeeded)
                itemView.RefreshGeometry(completed.OriginalStartFrame, completed.OriginalDurationFrames);
            evt.StopPropagation();
        }

        // Pointer Capture 意外丢失时恢复权威位置，避免草稿残留。
        private void OnPointerCaptureOut(PointerCaptureOutEvent _)
        {
            if (state != null) Cancel();
        }
    }
}
#endif