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
    internal sealed class ItemDragController : IDisposable
    {
        #region 拖拽状态类型

        /// <summary>
        /// 保存一次 Clip 或 Marker 拖拽期间的 UI 草稿状态。
        /// </summary>
        internal sealed class DragState
        {
            public DragMode Mode;
            public TrackViewData Track;
            public ItemViewData Item;
            public int OriginalStartFrame;
            public int OriginalDurationFrames;
            public int DraftStartFrame;
            public int DraftDurationFrames;
            public float PointerStartX;
        }
        /// <summary>
        /// 标识时间轴内容拖拽时采用的移动或裁剪方式。
        /// </summary>
        internal enum DragMode
        {
            Move,
            ResizeLeft,
            ResizeRight
        }

        #endregion

        #region 依赖与运行状态

        private readonly CanvasModel canvasModel;
        private readonly List<VisualElement> registeredElements = new();
        private EditorViewModel viewModel;
        private DragState state;
        private ItemView activeView;
        private int pointerId = -1;

        #endregion

        #region 生命周期与注册

        /// <summary>
        /// 创建使用指定 Canvas 缩放状态的拖拽控制器。
        /// </summary>
        public ItemDragController(CanvasModel canvasModel) =>
            this.canvasModel = canvasModel ?? throw new ArgumentNullException(nameof(canvasModel));

        /// <summary>
        /// 绑定最终移动、裁剪和选择意图的接收方。
        /// </summary>
        public void Bind(EditorViewModel model) => viewModel = model;

        /// <summary>
        /// 为一个动态 Item View 注册拖拽事件。
        /// </summary>
        public void Register(ItemView itemView)
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
            ItemView itemView = activeView;
            DragState dragState = state;
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

        #endregion

        #region Pointer 交互

        // 捕获左键并根据命中的手柄确定移动或裁剪模式。
        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || evt.currentTarget is not VisualElement element ||
                element.userData is not ItemView itemView || itemView.Track.Locked) return;

            DragMode mode = DragMode.Move;
            if (evt.target == itemView.ResizeLeft) mode = DragMode.ResizeLeft;
            else if (evt.target == itemView.ResizeRight) mode = DragMode.ResizeRight;
            if (!itemView.Item.IsResizable) mode = DragMode.Move;

            activeView = itemView;
            pointerId = evt.pointerId;
            state = new DragState
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
            viewModel.SelectItem(itemView.Track, itemView.Item);
            element.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        // 把像素位移吸附为整数帧，并只更新 Item View 的草稿几何。
        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (state == null || activeView == null || pointerId != evt.pointerId) return;
            int delta = Mathf.RoundToInt(
                (evt.position.x - state.PointerStartX) / canvasModel.PixelsPerFrame);
            int originalEnd = state.OriginalStartFrame + state.OriginalDurationFrames;
            switch (state.Mode)
            {
                case DragMode.Move:
                    state.DraftStartFrame = Mathf.Max(0, state.OriginalStartFrame + delta);
                    break;
                case DragMode.ResizeLeft:
                    state.DraftStartFrame = Mathf.Clamp(state.OriginalStartFrame + delta, 0, originalEnd - 1);
                    state.DraftDurationFrames = originalEnd - state.DraftStartFrame;
                    break;
                case DragMode.ResizeRight:
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
            DragState completed = state;
            ItemView itemView = activeView;
            VisualElement capture = itemView.Element;
            state = null;
            activeView = null;
            pointerId = -1;
            if (capture.HasPointerCapture(evt.pointerId)) capture.ReleasePointer(evt.pointerId);

            EditResult result = completed.Mode == DragMode.Move
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

        #endregion
    }
}
#endif
