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
        private TrackDragController trackDragController;
        private ScrubController scrubController;
        private ViewportInputController viewportInputController;

        private RowCollectionView rowCollectionView;
        private RulerView rulerView;
        private GridView gridView;
        private PlayheadView playheadView;
        private bool isBound;

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
            UnregisterEvents();
            scrubController?.Dispose();
            viewportInputController?.Dispose();
            if (rowCollectionView != null)
            {
                rowCollectionView.RowsChanged -= OnRowsChanged;
                rowCollectionView.Unbind();
            }
            trackDragController?.Dispose();
            dragController?.Dispose();
            gridView?.Dispose();
            rulerView?.Dispose();
            playheadView?.Dispose();
            scrubController = null;
            viewportInputController = null;
            rowCollectionView = null;
            trackDragController = null;
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
            dragController = new ItemDragController(canvasModel);
            dragController.Bind(viewModel);
            trackDragController = new TrackDragController(mapper, modules, viewModel);

            ElementFactory factory = new();
            rowCollectionView = new RowCollectionView(
                view.TrackHeaderRows, view.LaneBackgroundRows, view.LaneItemRows,
                factory, mapper, modules, dragController, trackDragController);
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

        // Config 或 Track 内容变化后重建动态行，并在布局完成后更新纵向范围。
        private void RefreshTimelineView()
        {
            if (!isBound) return;
            rowCollectionView.Rebuild(viewModel.Groups);
            RecalculateCanvasGeometry(false);
            RefreshFixedDrawing();
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
            RecalculateCanvasGeometry(false);
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

        // 真实 contentViewport 尺寸变化后重新测量，并把数值写回 CanvasModel。
        private void OnViewportGeometryChanged(GeometryChangedEvent _)
        {
            if (!isBound) return;
            RecalculateCanvasGeometry(true);
            RefreshFixedDrawing();
        }

        // 统一计算水平与纵向范围，并原子写入 CanvasModel，避免各 View 分别推导最大帧。
        private void RecalculateCanvasGeometry(bool includeResolvedRowHeight)
        {
            float viewportWidth = ReadTimelineViewportWidth();
            float viewportHeight = ReadTimelineViewportHeight();
            int duration = canvasModel.CurrentConfig?.DurationFrames ?? 0;
            int frameCount = Mathf.Max(duration, config.MinimumTimelineFrameCount);
            float frameRangeWidth = mapper.FrameToContentX(frameCount) + config.ContentRightPadding;
            float viewportRangeWidth = viewportWidth + config.MinimumScrollableOverflow.x;
            float contentWidth = Mathf.Max(1f, frameRangeWidth, viewportRangeWidth);

            float rowHeight = includeResolvedRowHeight ? CalculateRowHeight() : 0f;
            float contentHeight = includeResolvedRowHeight
                ? Mathf.Max(rowHeight, config.MinimumTimelineContentHeight,
                    viewportHeight + config.MinimumScrollableOverflow.y, 1f)
                : Mathf.Max(canvasModel.ContentHeight, config.MinimumTimelineContentHeight,
                    viewportHeight + config.MinimumScrollableOverflow.y, 1f);
            int maximumFrame = CalculateMaximumFrame(contentWidth);
            canvasModel.SynchronizeGeometry(contentWidth, contentHeight,
                viewportWidth, viewportHeight, maximumFrame);
        }

        // RowCollection 完成折叠或内容重建后，等待布局解析再同步左右内容高度。
        private void OnRowsChanged()
        {
            if (!isBound) return;
            view.TimelineContent.schedule.Execute(RecalculateScheduledHeight);
        }

        // 动态行布局完成后重新读取真实行高，修正左右一致的纵向可滚动范围。
        private void RecalculateScheduledHeight()
        {
            if (!isBound) return;
            RecalculateCanvasGeometry(true);
        }

        // 将 Model 中同一份虚拟范围应用到左右内容容器、背景、网格和 Item 层。
        private void ApplyCanvasGeometry()
        {
            view.TimelineContent.style.width = canvasModel.ContentWidth;
            view.LaneBackgroundRows.style.width = canvasModel.ContentWidth;
            view.LaneItemRows.style.width = canvasModel.ContentWidth;
            view.GridHost.style.width = canvasModel.ContentWidth;
            view.TrackHeaderContent.style.height = canvasModel.ContentHeight;
            view.TimelineContent.style.height = canvasModel.ContentHeight;
            view.LaneBackgroundRows.style.height = canvasModel.ContentHeight;
            view.LaneItemRows.style.height = canvasModel.ContentHeight;
            view.GridHost.style.height = canvasModel.ContentHeight;
            RefreshFixedDrawing();
        }

        // 汇总动态背景行的真实布局高度，不读取被虚拟高度扩展后的父容器尺寸。
        private float CalculateRowHeight()
        {
            float height = 0f;
            foreach (VisualElement row in view.LaneBackgroundRows.Children())
                height += row.resolvedStyle.height;
            return height;
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
            scrubController?.Cancel();
            evt.StopPropagation();
        }

        #endregion
    }
}
#endif
