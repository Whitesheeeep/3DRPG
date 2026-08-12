#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 协调 Canvas 表现模型、被动子 View、Pointer 控制器与外层 ViewModel 的状态流。
    /// </summary>
    internal sealed class CanvasController : IDisposable
    {
        #region 常量

        private const float MinimumResolvedRowHeight = 1f;

        #endregion

        #region 依赖

        private readonly CanvasView view;
        private readonly CanvasModel canvasModel;
        private readonly CoordinateMapper mapper;
        private readonly EditorConfig config;
        private readonly TrackModuleRegistry modules;
        private EditorViewModel viewModel;

        #endregion

        #region 运行状态

        private ItemDragController dragController;
        private ItemContextMenuController contextMenuController;
        private TrackContextMenuController trackContextMenuController;
        private TrackPanelContextMenuController trackPanelContextMenuController;
        private TrackDragController trackDragController;
        private TrackReorderDragController trackReorderDragController;
        private ScrubController scrubController;
        private ViewportInputController viewportInputController;

        private RowCollectionView rowCollectionView;
        private RulerView rulerView;
        private GridView gridView;
        private PlayheadView playheadView;
        private IVisualElementScheduledItem rowHeightMeasurement;
        private int rowHeightMeasurementVersion;
        private float resolvedRowsHeight;
        private bool isBound;
        private bool applyingCanvasGeometry;

        #endregion

        /// <summary>
        /// 创建 Canvas 内部 MVC 的控制器，并保留同一组稳定 View 与 Model 引用。
        /// </summary>
        public CanvasController(CanvasView view,
            CanvasModel canvasModel, CoordinateMapper mapper,
            EditorConfig config, TrackModuleRegistry modules)
        {
            this.view = view ?? throw new ArgumentNullException(nameof(view));
            this.canvasModel = canvasModel ?? throw new ArgumentNullException(nameof(canvasModel));
            this.mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.modules = modules ?? throw new ArgumentNullException(nameof(modules));
        }

        #region 生命周期

        /// <summary>
        /// 绑定外层 ViewModel，创建 Canvas 子组件并执行首次完整状态同步。
        /// </summary>
        public void Bind(EditorViewModel model)
        {
            if (isBound) Dispose();
            viewModel = model ?? throw new ArgumentNullException(nameof(model));
            CreateComponents();
            RegisterEvents();
            isBound = true;

            canvasModel.SynchronizeTimeline(viewModel.CurrentConfig);
            canvasModel.SynchronizeCurrentFrame(viewModel.CurrentFrame);
            canvasModel.NotifyInitialPlayhead();
            RefreshSelection();
        }

        /// <summary>
        /// 注销全部事件，并按输入控制器、动态行和绘制 View 的逆序释放资源。
        /// </summary>
        public void Dispose()
        {
            if (!isBound && viewModel == null) return;
            isBound = false;
            CancelScheduledRowHeightMeasurement();
            UnregisterEvents();
            scrubController?.Dispose();
            viewportInputController?.Dispose();
            if (rowCollectionView != null)
            {
                rowCollectionView.RowsChanged -= OnRowsChanged;
                rowCollectionView.Unbind();
            }
            trackReorderDragController?.Dispose();
            trackDragController?.Dispose();
            contextMenuController?.Dispose();
            trackPanelContextMenuController?.Dispose();
            trackContextMenuController?.Dispose();
            dragController?.Dispose();
            gridView?.Dispose();
            rulerView?.Dispose();
            playheadView?.Dispose();
            scrubController = null;
            viewportInputController = null;
            rowCollectionView = null;
            trackReorderDragController = null;
            trackDragController = null;
            contextMenuController = null;
            trackPanelContextMenuController = null;
            trackContextMenuController = null;
            dragController = null;
            gridView = null;
            rulerView = null;
            playheadView = null;
            viewModel = null;
        }

        #endregion

        #region 组件创建与事件注册

        // 按依赖顺序创建动态元素工厂、Pointer 控制器、行集合和三个 IMGUI 绘制 View。
        private void CreateComponents()
        {
            ElementFactory factory = new();
            ItemDragPreviewView dragPreviewView = new(
                view.ItemDragOverlay, modules, factory, mapper);
            dragController = new ItemDragController(canvasModel, dragPreviewView);
            dragController.Bind(viewModel);
            contextMenuController = new ItemContextMenuController(viewModel);
            trackContextMenuController = new TrackContextMenuController(viewModel);
            trackPanelContextMenuController = new TrackPanelContextMenuController(
                view.TrackHeaderContent, modules, viewModel);
            trackDragController = new TrackDragController(mapper, modules, viewModel);
            trackReorderDragController = new TrackReorderDragController(
                view.TrackHeaderScroll, view.TrackHeaderRows, view.LaneItemRows,
                canvasModel, viewModel);

            rowCollectionView = new RowCollectionView(
                view.TrackHeaderRows, view.LaneBackgroundRows, view.LaneItemRows,
                factory, mapper, modules, dragController, contextMenuController,
                trackContextMenuController, trackDragController, trackReorderDragController);
            rowCollectionView.Bind(viewModel);
            rowCollectionView.RowsChanged += OnRowsChanged;

            rulerView = new RulerView(view.RulerLane, canvasModel, mapper, config);
            gridView = new GridView(view.GridHost, canvasModel, mapper, config);
            playheadView = new PlayheadView(
                view.PlayheadOverlay, canvasModel, mapper, config);
            scrubController = new ScrubController(view.RulerLane, view.TimelineScroll,
                canvasModel, mapper);
            scrubController.Bind(viewModel);
            viewportInputController = new ViewportInputController(
                view.TimelinePanel, view.TrackHeaderScroll, view.TimelineScroll, canvasModel, config);
        }

        // 同时连接外层 ViewModel、CanvasModel、视口状态和键盘取消入口。
        private void RegisterEvents()
        {
            viewModel.TimelineChanged += OnTimelineChanged;
            viewModel.SelectionChanged += RefreshSelection;
            viewModel.PlayheadChanged += OnPlayheadChanged;
            canvasModel.TimelineChanged += RefreshTimelineView;
            canvasModel.PlayheadChanged += RefreshPlayheadView;
            canvasModel.GeometryChanged += ApplyCanvasGeometry;
            canvasModel.ZoomChanged += RefreshZoomGeometry;
            canvasModel.ScrollOffsetChanged += RefreshScrollPresentation;
            view.TimelineScroll.contentViewport.RegisterCallback<GeometryChangedEvent>(OnViewportGeometryChanged);
            view.TrackHeaderScroll.contentViewport.RegisterCallback<GeometryChangedEvent>(OnViewportGeometryChanged);
            view.TimelinePanel.RegisterCallback<KeyDownEvent>(OnKeyDown);
        }

        // 在释放子组件前注销所有外部事件，避免 Domain Reload 后重复回调。
        private void UnregisterEvents()
        {
            if (viewModel != null)
            {
                viewModel.TimelineChanged -= OnTimelineChanged;
                viewModel.SelectionChanged -= RefreshSelection;
                viewModel.PlayheadChanged -= OnPlayheadChanged;
            }

            canvasModel.TimelineChanged -= RefreshTimelineView;
            canvasModel.PlayheadChanged -= RefreshPlayheadView;
            canvasModel.GeometryChanged -= ApplyCanvasGeometry;
            canvasModel.ZoomChanged -= RefreshZoomGeometry;
            canvasModel.ScrollOffsetChanged -= RefreshScrollPresentation;
            view.TimelineScroll?.contentViewport.UnregisterCallback<GeometryChangedEvent>(OnViewportGeometryChanged);
            view.TrackHeaderScroll?.contentViewport.UnregisterCallback<GeometryChangedEvent>(OnViewportGeometryChanged);
            view.TimelinePanel?.UnregisterCallback<KeyDownEvent>(OnKeyDown);
        }

        #endregion

        #region 外层状态同步

        // 将同一 Config 内的内容变化也投影为 CanvasModel 的时间轴变化事件。
        private void OnTimelineChanged()
        {
            if (!isBound) return;
            canvasModel.SynchronizeTimeline(viewModel.CurrentConfig);
        }

        // 只同步 ViewModel 已经夹紧后的权威整数帧，CanvasModel 不自行修改播放逻辑。
        private void OnPlayheadChanged()
        {
            if (!isBound) return;
            canvasModel.SynchronizeCurrentFrame(viewModel.CurrentFrame);
        }

        /// <summary>
        /// 重建动态行，并在过渡布局前后强制恢复 CanvasModel 保存的权威虚拟画布范围。
        /// </summary>
        private void RefreshTimelineView()
        {
            if (!isBound) return;
            rowCollectionView.Rebuild(viewModel.Tracks);

            // Clear/Rebuild 的中间布局不能成为 ScrollView 推导滚动范围的依据。
            ApplyCanvasGeometry();
            RecalculateCanvasGeometry();
            ApplyCanvasGeometry();
        }

        // 根据具体 Selection 更新动态行和 Item 的 USS 状态。
        private void RefreshSelection() => rowCollectionView?.RefreshSelection();

        // 播放头变化时先保证当前帧可见，再立即重绘最高层 Overlay。
        private void RefreshPlayheadView()
        {
            if (!isBound) return;
            if (canvasModel.ViewportWidth > 0f)
                canvasModel.EnsureFrameVisible(canvasModel.CurrentFrame, canvasModel.ViewportWidth);
            playheadView?.MarkDirtyRepaint();
        }

        #endregion

        #region 几何与绘制刷新

        // 缩放变化会影响内容宽度、Item 帧几何以及全部按帧绘制的固定层。
        private void RefreshZoomGeometry()
        {
            if (!isBound) return;
            RecalculateCanvasGeometry();
            // 刷新横排数据
            rowCollectionView.RefreshItemGeometry();
            RefreshFixedDrawing();
        }

        // Model 中的权威滚动偏移变化后，只重绘使用视口坐标的固定层。
        private void RefreshScrollPresentation()
        {
            if (!isBound) return;
            RefreshFixedDrawing();
        }

        /// <summary>
        /// 在真实视口尺寸变化后只更新视口范围，不重新读取可能处于过渡布局的轨道行高。
        /// </summary>
        /// <param name="evt">视口几何变化事件。</param>
        private void OnViewportGeometryChanged(GeometryChangedEvent evt)
        {
            if (!isBound || applyingCanvasGeometry) return;
            RecalculateCanvasGeometry();
            ApplyCanvasGeometry();
        }

        /// <summary>
        /// 使用最后一次有效轨道高度缓存，统一计算水平与纵向虚拟范围。
        /// </summary>
        private void RecalculateCanvasGeometry()
        {
            float viewportWidth = ReadTimelineViewportWidth();
            float viewportHeight = ReadTimelineViewportHeight();
            int duration = canvasModel.CurrentConfig?.DurationFrames ?? 0;
            int frameCount = Mathf.Max(duration, config.MinimumTimelineFrameCount);
            float frameRangeWidth = mapper.FrameToContentX(frameCount) + config.ContentRightPadding;
            float viewportRangeWidth = viewportWidth + config.MinimumScrollableOverflow.x;
            float contentWidth = Mathf.Max(1f, frameRangeWidth, viewportRangeWidth);

            // 视口事件只消费最后一次有效行高，不能用拖拽期间的临时布局覆盖内容范围。
            float contentHeight = Mathf.Max(
                resolvedRowsHeight + config.MinimumScrollableOverflow.y,
                config.MinimumTimelineContentHeight,
                viewportHeight + config.MinimumScrollableOverflow.y,
                1f);
            int maximumFrame = CalculateMaximumFrame(contentWidth);
            canvasModel.SynchronizeGeometry(contentWidth, contentHeight,
                viewportWidth, viewportHeight, maximumFrame);
        }

        /// <summary>
        /// 合并动态行重建产生的测量请求，避免过期任务把临时行高写回滚动范围。
        /// </summary>
        private void OnRowsChanged()
        {
            if (!isBound) return;
            CancelScheduledRowHeightMeasurement();
            int measurementVersion = ++rowHeightMeasurementVersion;
            rowHeightMeasurement = view.TimelineContent.schedule.Execute(
                () => RecalculateScheduledHeight(measurementVersion));
        }

        /// <summary>
        /// 动态行完成一次布局后读取真实行高，并只允许最新重建请求写回权威布局。
        /// </summary>
        /// <param name="measurementVersion">创建调度任务时记录的行重建版本。</param>
        private void RecalculateScheduledHeight(int measurementVersion)
        {
            rowHeightMeasurement = null;
            if (!isBound || measurementVersion != rowHeightMeasurementVersion) return;

            if (!TryCalculateRowHeight(out float measuredHeight))
            {
                // 当前 VisualTree 仍处于 Clear/Rebuild 过渡布局，保留旧缓存并等待下一次布局。
                ScheduleRowHeightMeasurement(measurementVersion);
                return;
            }

            resolvedRowsHeight = measuredHeight;
            RecalculateCanvasGeometry();
            ApplyCanvasGeometry();
        }

        /// <summary>
        /// 为当前行重建版本安排一次后续布局测量，旧版本任务不能写回状态。
        /// </summary>
        /// <param name="measurementVersion">需要继续测量的行重建版本。</param>
        private void ScheduleRowHeightMeasurement(int measurementVersion)
        {
            rowHeightMeasurement?.Pause();
            rowHeightMeasurement = view.TimelineContent.schedule.Execute(
                () => RecalculateScheduledHeight(measurementVersion));
        }

        /// <summary>
        /// 取消尚未执行的行高测量，避免重建、关闭或 Domain Reload 后访问旧 VisualTree。
        /// </summary>
        private void CancelScheduledRowHeightMeasurement()
        {
            rowHeightMeasurement?.Pause();
            rowHeightMeasurement = null;
            rowHeightMeasurementVersion++;
        }

        /// <summary>
        /// 将 Model 中同一份虚拟范围应用到左右内容容器、背景、网格和 Item 层。
        /// </summary>
        private void ApplyCanvasGeometry()
        {
            if (!isBound || applyingCanvasGeometry) return;
            applyingCanvasGeometry = true;
            try
            {
                // 自定义 TimelineContent 是 ScrollView 的唯一尺寸占位，内部 contentContainer 保持由 UI Toolkit 管理。
                view.TimelineContent.style.width = canvasModel.ContentWidth;
                view.TimelineContent.style.minWidth = canvasModel.ContentWidth;
                view.TimelineContent.style.flexShrink = 0f;
                view.LaneBackgroundRows.style.width = canvasModel.ContentWidth;
                view.LaneItemRows.style.width = canvasModel.ContentWidth;
                view.GridHost.style.width = canvasModel.ContentWidth;

                // 左右直接子内容与全部 Canvas 层共享同一权威高度，且均禁止 Flex 压缩。
                ApplyFixedContentHeight(view.TrackHeaderContent, canvasModel.ContentHeight);
                ApplyFixedContentHeight(view.TimelineContent, canvasModel.ContentHeight);
                ApplyFixedContentHeight(view.LaneBackgroundRows, canvasModel.ContentHeight);
                ApplyFixedContentHeight(view.LaneItemRows, canvasModel.ContentHeight);
                ApplyFixedContentHeight(view.GridHost, canvasModel.ContentHeight);
            }
            finally
            {
                applyingCanvasGeometry = false;
            }

            RefreshFixedDrawing();
        }

        /// <summary>
        /// 将权威高度同时写入高度下限，并禁止目标元素参与 Flex 收缩。
        /// </summary>
        /// <param name="element">需要固定虚拟高度的 UI 元素。</param>
        /// <param name="height">CanvasModel 计算出的像素高度。</param>
        private static void ApplyFixedContentHeight(VisualElement element, float height)
        {
            element.style.height = height;
            element.style.minHeight = height;
            element.style.flexShrink = 0f;
        }

        /// <summary>
        /// 验证全部动态背景行已完成有效布局，再原子汇总真实轨道高度。
        /// </summary>
        /// <param name="height">全部有效背景行的总像素高度；无轨道时为零。</param>
        /// <returns>行数量与当前 Track 一致且每个非空行都具有有效高度时返回 true。</returns>
        private bool TryCalculateRowHeight(out float height)
        {
            height = 0f;
            int expectedRowCount = 0;
            foreach (TrackConfigBase track in viewModel.Tracks)
                if (track != null) expectedRowCount++;

            if (view.LaneBackgroundRows.childCount != expectedRowCount) return false;
            if (expectedRowCount == 0) return true;

            foreach (VisualElement row in view.LaneBackgroundRows.Children())
            {
                float rowHeight = row.resolvedStyle.height;
                if (float.IsNaN(rowHeight) || float.IsInfinity(rowHeight) ||
                    rowHeight < MinimumResolvedRowHeight)
                {
                    height = 0f;
                    return false;
                }

                height += rowHeight;
            }

            return true;
        }

        // 有 Config 时返回最后有效帧；空 Config 时按虚拟画布宽度计算可用最大帧。
        private int CalculateMaximumFrame(float contentWidth)
        {
            if (canvasModel.CurrentConfig != null)
                return Mathf.Max(0, canvasModel.CurrentConfig.DurationFrames - 1);
            float frameRangeWidth = Mathf.Max(0f, contentWidth - config.ContentRightPadding);
            return Mathf.Max(0, Mathf.FloorToInt(frameRangeWidth / canvasModel.PixelsPerFrame) - 1);
        }

        // 返回右侧 ScrollView 内容视口的实际宽度。
        private float ReadTimelineViewportWidth() =>
            Mathf.Max(0f, view.TimelineScroll.contentViewport.resolvedStyle.width);

        // 返回右侧 ScrollView 内容视口的实际高度。
        private float ReadTimelineViewportHeight() =>
            Mathf.Max(0f, view.TimelineScroll.contentViewport.resolvedStyle.height);

        // 同步标尺、统一网格和贯穿播放头，三者读取同一 CanvasModel 坐标状态。
        private void RefreshFixedDrawing()
        {
            rulerView?.MarkDirtyRepaint();
            gridView?.MarkDirtyRepaint();
            playheadView?.MarkDirtyRepaint();
        }

        #endregion

        #region 键盘交互

        // Escape 同时取消 Item 草稿与 Scrub，不向资产提交任何语义操作。
        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Escape) return;
            dragController?.Cancel();
            trackReorderDragController?.Cancel();
            scrubController?.Cancel();
            evt.StopPropagation();
        }

        #endregion
    }
}
#endif
