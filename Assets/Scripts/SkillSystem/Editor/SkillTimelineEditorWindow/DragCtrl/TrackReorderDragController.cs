#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using RPG.SkillSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 通过 Track Header 专用把手管理统一轨道列表的插入预览、边缘自动滚动与单次重排提交。
    /// </summary>
    internal sealed class TrackReorderDragController : IDisposable
    {
        #region 常量与嵌套类型

        private const float AutoScrollEdge = 28f;
        private const float AutoScrollStep = 9f;
        private const long AutoScrollIntervalMilliseconds = 16L;
        private const string SourceClass = "is-track-reorder-source";

        /// <summary>
        /// 保存同一轨道在固定标题栏和时间轴内容区的三个同步行元素。
        /// </summary>
        private sealed class RowBinding
        {
            public TrackConfigBase Track { get; }
            public VisualElement Header { get; }
            public VisualElement Background { get; }
            public VisualElement ItemLane { get; }
            public VisualElement Handle { get; }

            /// <summary>
            /// 创建仅在当前 Timeline 重建周期内有效的轨道行绑定。
            /// </summary>
            public RowBinding(TrackConfigBase track, VisualElement header,
                VisualElement background, VisualElement itemLane, VisualElement handle)
            {
                Track = track;
                Header = header;
                Background = background;
                ItemLane = itemLane;
                Handle = handle;
            }
        }

        #endregion

        #region 依赖与状态

        private readonly ScrollView headerScroll;
        private readonly VisualElement headerRows;
        private readonly VisualElement laneItemRows;
        private readonly CanvasModel canvasModel;
        private readonly EditorViewModel viewModel;
        private readonly List<RowBinding> rows = new();
        private readonly IVisualElementScheduledItem autoScrollTask;

        private RowBinding source;
        private VisualElement headerInsertionLine;
        private VisualElement laneInsertionLine;
        private Vector2 lastPointerWorld;
        private int insertionIndex;
        private int pointerId = -1;

        #endregion

        /// <summary>
        /// 创建轨道排序输入控制器；真实纵向偏移仍由 CanvasModel 与双 ScrollView 同步链路负责。
        /// </summary>
        public TrackReorderDragController(ScrollView headerScroll, VisualElement headerRows,
            VisualElement laneItemRows, CanvasModel canvasModel, EditorViewModel viewModel)
        {
            this.headerScroll = headerScroll ?? throw new ArgumentNullException(nameof(headerScroll));
            this.headerRows = headerRows ?? throw new ArgumentNullException(nameof(headerRows));
            this.laneItemRows = laneItemRows ?? throw new ArgumentNullException(nameof(laneItemRows));
            this.canvasModel = canvasModel ?? throw new ArgumentNullException(nameof(canvasModel));
            this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            autoScrollTask = headerRows.schedule.Execute(AutoScroll)
                .Every(AutoScrollIntervalMilliseconds);
            autoScrollTask.Pause();
        }

        #region 注册与生命周期

        /// <summary>
        /// 注册一组 Track 行与唯一拖拽把手；名称、ObjectField 和操作按钮不会启动重排。
        /// </summary>
        public void Register(TrackConfigBase track, VisualElement header,
            VisualElement background, VisualElement itemLane, VisualElement handle)
        {
            RowBinding binding = new(track, header, background, itemLane, handle);
            rows.Add(binding);
            handle.userData = binding;
            handle.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            handle.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            handle.RegisterCallback<PointerUpEvent>(OnPointerUp);
            handle.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
        }

        /// <summary>
        /// 取消拖拽并解除本次 Timeline 行重建中的全部把手回调。
        /// </summary>
        public void Reset()
        {
            Cancel();
            foreach (RowBinding row in rows)
            {
                row.Handle.UnregisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
                row.Handle.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
                row.Handle.UnregisterCallback<PointerUpEvent>(OnPointerUp);
                row.Handle.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
                row.Handle.userData = null;
            }
            rows.Clear();
        }

        /// <summary>
        /// 丢弃插入草稿、移除两侧插入线并恢复源轨道显示。
        /// </summary>
        public void Cancel()
        {
            RowBinding capturedSource = source;
            int capturedPointerId = pointerId;
            source = null;
            pointerId = -1;
            autoScrollTask.Pause();
            RemoveInsertionLines();
            SetSourceState(capturedSource, false);
            if (capturedSource?.Handle != null && capturedPointerId >= 0 &&
                capturedSource.Handle.HasPointerCapture(capturedPointerId))
                capturedSource.Handle.ReleasePointer(capturedPointerId);
        }

        /// <summary>
        /// 释放动态轨道行的全部 Pointer 注册。
        /// </summary>
        public void Dispose() => Reset();

        #endregion

        #region Pointer 交互

        // 只有把手左键启动轨道排序；锁定只限制 Item 编辑，不限制轨道结构重排。
        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || evt.currentTarget is not VisualElement handle ||
                handle.userData is not RowBinding binding) return;

            source = binding;
            pointerId = evt.pointerId;
            lastPointerWorld = evt.position;
            viewModel.SelectTrack(binding.Track);
            SetSourceState(binding, true);
            CreateInsertionLines();
            UpdateInsertion(evt.position.y);
            handle.CapturePointer(evt.pointerId);
            autoScrollTask.Resume();
            evt.StopImmediatePropagation();
        }

        // PointerMove 仅更新插入边界与两侧预览线，不修改 SkillConfig.Tracks。
        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (source == null || evt.pointerId != pointerId) return;
            lastPointerWorld = evt.position;
            UpdateInsertion(evt.position.y);
            evt.StopPropagation();
        }

        // PointerUp 清理视觉状态后向 ViewModel 提交一次插入边界语义命令。
        private void OnPointerUp(PointerUpEvent evt)
        {
            if (source == null || evt.pointerId != pointerId) return;
            RowBinding completed = source;
            int completedInsertionIndex = insertionIndex;
            VisualElement handle = completed.Handle;
            source = null;
            pointerId = -1;
            autoScrollTask.Pause();
            RemoveInsertionLines();
            SetSourceState(completed, false);
            if (handle.HasPointerCapture(evt.pointerId)) handle.ReleasePointer(evt.pointerId);
            viewModel.MoveTrack(completed.Track, completedInsertionIndex);
            evt.StopPropagation();
        }

        // 捕获意外丢失代表取消，不提交尚未完成的轨道顺序草稿。
        private void OnPointerCaptureOut(PointerCaptureOutEvent _)
        {
            if (source != null) Cancel();
        }

        #endregion

        #region 插入预览与自动滚动

        // 根据鼠标相对每一行中点的位置计算 0..TrackCount 的插入边界。
        private void UpdateInsertion(float pointerWorldY)
        {
            insertionIndex = rows.Count;
            for (int index = 0; index < rows.Count; index++)
            {
                if (pointerWorldY >= rows[index].Header.worldBound.center.y) continue;
                insertionIndex = index;
                break;
            }

            float headerTop = ResolveInsertionTop(insertionIndex, true);
            float laneTop = ResolveInsertionTop(insertionIndex, false);
            if (headerInsertionLine != null) headerInsertionLine.style.top = headerTop;
            if (laneInsertionLine != null) laneInsertionLine.style.top = laneTop;
        }

        // 靠近固定标签视口上下边缘时推进 CanvasModel 纵向偏移，由现有同步控制器应用到左右 ScrollView。
        private void AutoScroll()
        {
            if (source == null) return;
            Rect viewport = headerScroll.contentViewport.worldBound;
            float delta = 0f;
            if (lastPointerWorld.y < viewport.yMin + AutoScrollEdge) delta = -AutoScrollStep;
            else if (lastPointerWorld.y > viewport.yMax - AutoScrollEdge) delta = AutoScrollStep;
            if (Mathf.Approximately(delta, 0f)) return;

            Vector2 offset = canvasModel.ScrollOffset;
            offset.y = Mathf.Clamp(offset.y + delta, 0f, headerScroll.verticalScroller.highValue);
            canvasModel.SetScrollOffset(offset);
            UpdateInsertion(lastPointerWorld.y);
        }

        // 创建固定标题区与时间轴内容区各自的插入线，二者共享同一个插入边界。
        private void CreateInsertionLines()
        {
            headerInsertionLine = new VisualElement { pickingMode = PickingMode.Ignore };
            laneInsertionLine = new VisualElement { pickingMode = PickingMode.Ignore };
            headerInsertionLine.AddToClassList("track-reorder-insertion-line");
            laneInsertionLine.AddToClassList("track-reorder-insertion-line");
            headerRows.Add(headerInsertionLine);
            laneItemRows.Add(laneInsertionLine);
        }

        // 移除临时插入线，防止 Timeline 重建后残留在新行集合中。
        private void RemoveInsertionLines()
        {
            headerInsertionLine?.RemoveFromHierarchy();
            laneInsertionLine?.RemoveFromHierarchy();
            headerInsertionLine = null;
            laneInsertionLine = null;
        }

        // 以行容器的内容坐标返回插入线顶部，避免纵向滚动偏移被重复扣除。
        private float ResolveInsertionTop(int boundary, bool useHeader)
        {
            if (rows.Count == 0) return 0f;
            if (boundary < rows.Count)
            {
                VisualElement row = useHeader ? rows[boundary].Header : rows[boundary].ItemLane;
                return row.layout.y;
            }

            VisualElement last = useHeader ? rows[^1].Header : rows[^1].ItemLane;
            return last.layout.yMax;
        }

        // 同时弱化标题、Lane 背景和 Item Lane，保证左右两侧明确指向同一源轨道。
        private static void SetSourceState(RowBinding binding, bool active)
        {
            if (binding == null) return;
            binding.Header.EnableInClassList(SourceClass, active);
            binding.Background.EnableInClassList(SourceClass, active);
            binding.ItemLane.EnableInClassList(SourceClass, active);
        }

        #endregion
    }
}
#endif