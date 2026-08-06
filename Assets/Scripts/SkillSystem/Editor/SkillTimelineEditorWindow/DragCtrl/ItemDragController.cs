#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using RPG.SkillSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 管理 Item 水平帧草稿、同类型 Lane 目标、三层移动预览和单次移动或 Resize 提交。
    /// </summary>
    internal sealed class ItemDragController : IDisposable
    {
        #region 拖拽状态类型

        /// <summary>
        /// 保存一次拖拽的原始 Config 引用、帧草稿、目标 Lane 与校验结果。
        /// </summary>
        internal sealed class DragState
        {
            public DragMode Mode;
            public TrackConfigBase SourceTrack;
            public TrackConfigBase TargetTrack;
            public TimelineItemConfigBase Item;
            public int OriginalStartFrame;
            public int OriginalDurationFrames;
            public int DraftStartFrame;
            public int DraftDurationFrames;
            public float PointerStartX;
            public bool IsDropValid;
        }

        /// <summary>
        /// 标识移动或双侧 Resize 交互。
        /// </summary>
        internal enum DragMode
        {
            Move,
            ResizeLeft,
            ResizeRight
        }

        /// <summary>
        /// 保存实际 Track 与其 Lane 元素的绑定。
        /// </summary>
        private sealed class LaneBinding
        {
            public TrackConfigBase Track { get; }
            public VisualElement Lane { get; }

            /// <summary>
            /// 保存动态 Lane 引用；该映射只存在于本次 Canvas 行生命周期。
            /// </summary>
            public LaneBinding(TrackConfigBase track, VisualElement lane)
            {
                Track = track;
                Lane = lane;
            }
        }

        #endregion

        #region 依赖与状态

        private const string ValidLaneClass = "is-item-drop-valid";
        private const string InvalidLaneClass = "is-item-drop-invalid";

        private readonly CanvasModel canvasModel;
        private readonly ItemDragPreviewView previewView;
        private readonly List<VisualElement> registeredElements = new();
        private readonly List<LaneBinding> lanes = new();
        private EditorViewModel viewModel;
        private DragState state;
        private ItemView activeView;
        private VisualElement highlightedLane;
        private int pointerId = -1;

        #endregion

        #region 生命周期与注册

        /// <summary>
        /// 创建使用 Canvas 缩放状态和独立三层预览的拖拽控制器。
        /// </summary>
        public ItemDragController(CanvasModel canvasModel, ItemDragPreviewView previewView)
        {
            this.canvasModel = canvasModel ?? throw new ArgumentNullException(nameof(canvasModel));
            this.previewView = previewView ?? throw new ArgumentNullException(nameof(previewView));
        }

        /// <summary>
        /// 绑定语义操作接收方。
        /// </summary>
        public void Bind(EditorViewModel model) => viewModel = model;

        /// <summary>
        /// 注册一个可作为垂直移动目标的实际 Track Lane。
        /// </summary>
        public void RegisterLane(TrackConfigBase track, VisualElement lane) =>
            lanes.Add(new LaneBinding(track, lane));

        /// <summary>
        /// 注册一个动态 Item View 的 Pointer 事件。
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
        /// 取消草稿并解除全部动态注册。
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
            lanes.Clear();
        }

        /// <summary>
        /// 丢弃草稿、移除全部预览、恢复权威几何并释放 Pointer Capture。
        /// </summary>
        public void Cancel()
        {
            ClearLaneHighlight();
            previewView.End();
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
        /// 释放全部拖拽回调、临时 View 和 ViewModel 引用。
        /// </summary>
        public void Dispose()
        {
            Reset();
            previewView.Dispose();
            viewModel = null;
        }

        #endregion

        #region Pointer 交互

        // 左键开始时选择实际 Item，并区分移动与 Resize 手柄；移动模式才创建三层预览。
        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || evt.currentTarget is not VisualElement element ||
                element.userData is not ItemView itemView || itemView.Track.EditorLocked) return;

            DragMode mode = DragMode.Move;
            if (evt.target == itemView.ResizeLeft) mode = DragMode.ResizeLeft;
            else if (evt.target == itemView.ResizeRight) mode = DragMode.ResizeRight;
            if (itemView.Item is SkillEventMarkerConfig) mode = DragMode.Move;

            activeView = itemView;
            pointerId = evt.pointerId;
            state = new DragState
            {
                Mode = mode,
                SourceTrack = itemView.Track,
                TargetTrack = itemView.Track,
                Item = itemView.Item,
                OriginalStartFrame = itemView.Item.StartFrame,
                OriginalDurationFrames = itemView.Item.DurationFrames,
                DraftStartFrame = itemView.Item.StartFrame,
                DraftDurationFrames = itemView.Item.DurationFrames,
                PointerStartX = evt.position.x
            };
            viewModel.SelectItem(itemView.Track, itemView.Item);
            if (mode == DragMode.Move)
            {
                previewView.Begin(itemView, evt.position);
                UpdateTargetLane(evt.position);
            }
            element.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        // PointerMove 只刷新整数帧草稿与临时视觉；移动模式绝不改变源 Item 的权威位置。
        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (state == null || activeView == null || pointerId != evt.pointerId) return;
            int delta = Mathf.RoundToInt((evt.position.x - state.PointerStartX) / canvasModel.PixelsPerFrame);
            int originalEnd = state.OriginalStartFrame + state.OriginalDurationFrames;
            switch (state.Mode)
            {
                case DragMode.Move:
                    state.DraftStartFrame = Mathf.Max(0, state.OriginalStartFrame + delta);
                    previewView.UpdatePointer(evt.position);
                    UpdateTargetLane(evt.position);
                    break;
                case DragMode.ResizeLeft:
                    state.DraftStartFrame = Mathf.Clamp(state.OriginalStartFrame + delta, 0, originalEnd - 1);
                    state.DraftDurationFrames = originalEnd - state.DraftStartFrame;
                    activeView.RefreshGeometry(state.DraftStartFrame, state.DraftDurationFrames);
                    break;
                case DragMode.ResizeRight:
                    state.DraftDurationFrames = Mathf.Max(1, state.OriginalDurationFrames + delta);
                    activeView.RefreshGeometry(state.DraftStartFrame, state.DraftDurationFrames);
                    break;
            }
            evt.StopPropagation();
        }

        // PointerUp 只在有效落点提交一次；无效位置、取消和无变化点击均不创建 Undo。
        private void OnPointerUp(PointerUpEvent evt)
        {
            if (state == null || activeView == null || pointerId != evt.pointerId) return;
            DragState completed = state;
            ItemView itemView = activeView;
            VisualElement capture = itemView.Element;
            state = null;
            activeView = null;
            pointerId = -1;
            ClearLaneHighlight();
            previewView.End();
            if (capture.HasPointerCapture(evt.pointerId)) capture.ReleasePointer(evt.pointerId);

            EditResult result;
            if (completed.Mode == DragMode.Move)
            {
                if (!completed.IsDropValid)
                {
                    itemView.RefreshGeometry(completed.OriginalStartFrame, completed.OriginalDurationFrames);
                    evt.StopPropagation();
                    return;
                }

                if (completed.TargetTrack != completed.SourceTrack)
                    result = viewModel.MoveItemToTrack(completed.SourceTrack, completed.Item,
                        completed.TargetTrack, completed.DraftStartFrame);
                else if (!HasDraftChanged(completed))
                {
                    evt.StopPropagation();
                    return;
                }
                else
                    result = viewModel.MoveItem(completed.SourceTrack, completed.Item,
                        completed.DraftStartFrame);
            }
            // Resize 模式只在草稿变化时提交；零位移或零 Resize 不创建 Undo。
            else if (!HasDraftChanged(completed))
            {
                evt.StopPropagation();
                return;
            }
            else
            {
                result = viewModel.ResizeItem(completed.SourceTrack, completed.Item,
                    completed.DraftStartFrame, completed.DraftDurationFrames);
            }

            if (!result.Succeeded)
                itemView.RefreshGeometry(completed.OriginalStartFrame, completed.OriginalDurationFrames);
            evt.StopPropagation();
        }

        // Pointer Capture 意外丢失时恢复权威位置，并清除 Ghost 与 Placeholder。
        private void OnPointerCaptureOut(PointerCaptureOutEvent _)
        {
            if (state != null) Cancel();
        }

        #endregion

        #region 目标 Lane 与校验

        // 命中任意 Lane 后展示占位；具体类型、锁定和区间校验共同决定有效状态。
        private void UpdateTargetLane(Vector3 worldPosition)
        {
            ClearLaneHighlight();
            state.TargetTrack = null;
            state.IsDropValid = false;
            foreach (LaneBinding binding in lanes)
            {
                if (!binding.Lane.worldBound.Contains(new Vector2(worldPosition.x, worldPosition.y))) continue;

                state.TargetTrack = binding.Track;
                bool compatible = binding.Track.GetType() == state.SourceTrack.GetType() &&
                                  !binding.Track.EditorLocked;
                if (compatible)
                {
                    EditResult validation = binding.Track == state.SourceTrack
                        ? viewModel.CanMoveItem(state.SourceTrack, state.Item, state.DraftStartFrame)
                        : viewModel.CanMoveItemToTrack(state.SourceTrack, state.Item,
                            binding.Track, state.DraftStartFrame);
                    state.IsDropValid = validation.Succeeded;
                }

                highlightedLane = binding.Lane;
                highlightedLane.AddToClassList(state.IsDropValid ? ValidLaneClass : InvalidLaneClass);
                previewView.ShowPlacement(binding.Lane, state.DraftStartFrame,
                    state.DraftDurationFrames, state.IsDropValid);
                return;
            }

            previewView.ClearPlacement();
        }

        // 清除上一目标 Lane 的有效或无效 USS 状态。
        private void ClearLaneHighlight()
        {
            highlightedLane?.RemoveFromClassList(ValidLaneClass);
            highlightedLane?.RemoveFromClassList(InvalidLaneClass);
            highlightedLane = null;
        }

        /// 比较帧草稿，避免零位移或零 Resize 产生 Undo。
        private static bool HasDraftChanged(DragState dragState) =>
            dragState.DraftStartFrame != dragState.OriginalStartFrame ||
            dragState.DraftDurationFrames != dragState.OriginalDurationFrames;

        #endregion
    }
}
#endif