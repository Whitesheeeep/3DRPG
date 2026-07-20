#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.MVVM;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 作为时间轴表现层组合根，负责绑定子 View、协调虚拟画布范围并统一释放资源。
    /// </summary>
    internal sealed class SkillTimelineCanvasView : IView<SkillTimelineEditorViewModel>
    {
        #region 依赖与元素

        private readonly VisualElement root;
        private readonly SkillTimelineViewportController viewport;
        private readonly SkillTimelineCoordinateMapper mapper;
        private readonly SkillTimelineEditorConfig config;
        private SkillTimelineEditorViewModel viewModel;
        private VisualElement timelinePanel;
        private VisualElement rulerLane;
        private ScrollView trackHeaderScroll;
        private VisualElement trackHeaderContent;
        private VisualElement trackHeaderRows;
        private ScrollView timelineScroll;
        private VisualElement timelineContent;
        private VisualElement laneBackgroundRows;
        private VisualElement laneItemRows;
        private VisualElement gridHost;
        private VisualElement playheadOverlay;
        private float contentWidth = 1f;
        private float lastViewportWidth = -1f;
        private float lastViewportHeight = -1f;

        #endregion

        #region 子视图与控制器

        private SkillTimelineItemDragController dragController;
        private SkillTimelineScrubController scrubController;
        private SkillTimelineViewportInputController viewportInputController;
        private SkillTimelineRowCollectionView rowCollectionView;
        private SkillTimelineRulerView rulerView;
        private SkillTimelineGridView gridView;
        private SkillTimelinePlayheadView playheadView;

        #endregion

        /// <summary>
        /// 创建依赖 Editor-only Config 的时间轴表现层组合根。
        /// </summary>
        public SkillTimelineCanvasView(VisualElement root, SkillTimelineViewportController viewport,
            SkillTimelineEditorConfig config)
        {
            this.root = root ?? throw new ArgumentNullException(nameof(root));
            this.viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            mapper = new SkillTimelineCoordinateMapper(viewport);
        }

        #region 绑定生命周期

        /// <summary>
        /// 查询静态布局、创建子组件并绑定 ViewModel 事件。
        /// </summary>
        public void Bind(SkillTimelineEditorViewModel model)
        {
            Unbind();
            viewModel = model ?? throw new ArgumentNullException(nameof(model));
            QueryElements();
            ConfigureViews();
            CreateChildren();
            RegisterEvents();
            RefreshTimeline();
            RefreshSelection();
            RefreshPlayhead();
        }

        /// <summary>
        /// 按控制器、绘制 View、动态行的逆序解除事件并释放引用。
        /// </summary>
        public void Unbind()
        {
            UnregisterEvents();
            scrubController?.Dispose();
            viewportInputController?.Dispose();
            rowCollectionView?.Unbind();
            dragController?.Dispose();
            gridView?.Dispose();
            rulerView?.Dispose();
            playheadView?.Dispose();
            scrubController = null;
            viewportInputController = null;
            rowCollectionView = null;
            dragController = null;
            gridView = null;
            rulerView = null;
            playheadView = null;
            viewModel = null;
        }

        #endregion

        #region 组合创建

        // 查询主 UXML 中稳定存在的组合节点，动态时间轴元素由 RowCollection 创建。
        private void QueryElements()
        {
            timelinePanel = root.Q<VisualElement>("TimelinePanel");
            rulerLane = root.Q<VisualElement>("RulerLane");
            trackHeaderScroll = root.Q<ScrollView>("TrackHeaderScroll");
            trackHeaderContent = root.Q<VisualElement>("TrackHeaderContent");
            trackHeaderRows = root.Q<VisualElement>("TrackHeaderRows");
            timelineScroll = root.Q<ScrollView>("TimelineScroll");
            timelineContent = root.Q<VisualElement>("TimelineContent");
            laneBackgroundRows = root.Q<VisualElement>("TimelineLaneBackgroundRows");
            laneItemRows = root.Q<VisualElement>("TimelineLaneRows");
            gridHost = root.Q<VisualElement>("TimelineGridHost");
            playheadOverlay = root.Q<VisualElement>("PlayheadOverlay");
        }

        // 这里只设置焦点能力，ScrollView 模式与滚动条策略由输入控制器统一配置。
        private void ConfigureViews() => timelinePanel.focusable = true;

        // 按工厂、交互控制器、动态行和 IMGUI 绘制层的依赖顺序创建子对象。
        private void CreateChildren()
        {
            SkillTimelineElementFactory factory = new(mapper, config);
            dragController = new SkillTimelineItemDragController(viewport);
            dragController.Bind(viewModel);
            rowCollectionView = new SkillTimelineRowCollectionView(
                trackHeaderRows, laneBackgroundRows, laneItemRows, factory, dragController);
            rowCollectionView.Bind(viewModel);

            rulerView = new SkillTimelineRulerView(rulerLane, viewport, mapper, config,
                () => viewModel?.CurrentConfig, GetHorizontalOffset);
            gridView = new SkillTimelineGridView(gridHost, viewport, mapper, config, GetHorizontalOffset,
                GetTimelineViewportWidth);
            playheadView = new SkillTimelinePlayheadView(playheadOverlay, mapper, config,
                () => viewModel?.CurrentFrame ?? 0, GetHorizontalOffset);
            scrubController = new SkillTimelineScrubController(rulerLane, timelineScroll.contentViewport,
                mapper, GetHorizontalOffset, GetScrubMaximumFrame);
            scrubController.Bind(viewModel);
            viewportInputController = new SkillTimelineViewportInputController(
                timelinePanel, trackHeaderScroll, timelineScroll, viewport, config);
        }

        #endregion

        #region 事件与刷新

        // 注册 ViewModel、视口输入和 Escape 取消事件。
        private void RegisterEvents()
        {
            viewModel.TimelineChanged += RefreshTimeline;
            viewModel.SelectionChanged += RefreshSelection;
            viewModel.PlayheadChanged += RefreshPlayhead;
            viewport.ViewportChanged += RefreshZoomGeometry;
            viewportInputController.PresentationChanged += RefreshViewportPresentation;
            timelinePanel.RegisterCallback<KeyDownEvent>(OnKeyDown);
        }

        // 在释放子对象前注销组合根持有的全部回调。
        private void UnregisterEvents()
        {
            if (viewModel != null)
            {
                viewModel.TimelineChanged -= RefreshTimeline;
                viewModel.SelectionChanged -= RefreshSelection;
                viewModel.PlayheadChanged -= RefreshPlayhead;
            }
            viewport.ViewportChanged -= RefreshZoomGeometry;
            if (viewportInputController != null)
                viewportInputController.PresentationChanged -= RefreshViewportPresentation;
            timelinePanel?.UnregisterCallback<KeyDownEvent>(OnKeyDown);
        }

        // 重建权威 ViewData 行，并在布局完成后同步左右虚拟内容高度。
        private void RefreshTimeline()
        {
            if (viewModel == null) return;
            rowCollectionView.Rebuild(viewModel.Groups);
            UpdateContentWidth();
            RefreshFixedDrawing();
            timelineContent.schedule.Execute(SynchronizeContentHeight);
        }

        // 缩放变化时刷新时间几何、虚拟宽度与全部 Item 的权威位置。
        private void RefreshZoomGeometry()
        {
            if (viewModel == null) return;
            UpdateContentWidth();
            rowCollectionView.RefreshItemGeometry();
            RefreshFixedDrawing();
        }

        // 滚动只重绘固定层；视口尺寸变化时额外重算可滚动画布范围。
        private void RefreshViewportPresentation()
        {
            float viewportWidth = GetTimelineViewportWidth();
            float viewportHeight = GetTimelineViewportHeight();
            bool geometryChanged = !Mathf.Approximately(lastViewportWidth, viewportWidth) ||
                                   !Mathf.Approximately(lastViewportHeight, viewportHeight);
            if (geometryChanged)
            {
                lastViewportWidth = viewportWidth;
                lastViewportHeight = viewportHeight;
                UpdateContentWidth();
                SynchronizeContentHeight();
            }
            RefreshFixedDrawing();
        }

        // 根据具体 Selection 更新动态行和内容的 USS 选中状态。
        private void RefreshSelection() => rowCollectionView?.RefreshSelection();

        // 播放头变化时先保证帧可见，再立即重绘最高层 IMGUI Overlay。
        private void RefreshPlayhead()
        {
            if (viewModel == null || timelineScroll == null) return;
            viewport.EnsureFrameVisible(viewModel.CurrentFrame, GetTimelineViewportWidth());
            playheadView?.MarkDirtyRepaint();
        }

        // 同步标尺、统一网格和贯穿播放头，三者使用同一映射器和右侧真实偏移。
        private void RefreshFixedDrawing()
        {
            rulerView?.MarkDirtyRepaint();
            gridView?.MarkDirtyRepaint();
            playheadView?.MarkDirtyRepaint();
        }

        #endregion

        #region 虚拟画布范围

        // 以技能帧数、Editor 最小帧数和视口滚动余量三者的最大值确定内容宽度。
        private void UpdateContentWidth()
        {
            int duration = viewModel?.CurrentConfig?.DurationFrames ?? 0;
            int frameCount = Mathf.Max(duration, config.MinimumTimelineFrameCount);
            float frameRangeWidth = mapper.FrameToContentX(frameCount) + config.ContentRightPadding;
            float viewportRangeWidth = GetTimelineViewportWidth() + config.MinimumScrollableOverflow.x;
            contentWidth = Mathf.Max(1f, frameRangeWidth, viewportRangeWidth);
            timelineContent.style.width = contentWidth;
            laneBackgroundRows.style.width = contentWidth;
            laneItemRows.style.width = contentWidth;
            gridHost.style.width = contentWidth;
        }

        // 把实际行高、Editor 最小高度和视口滚动余量统一应用到左右内容容器与绘制层。
        private void SynchronizeContentHeight()
        {
            if (laneBackgroundRows == null || trackHeaderContent == null) return;
            float rowHeight = CalculateRowHeight();
            float viewportRangeHeight = GetTimelineViewportHeight() + config.MinimumScrollableOverflow.y;
            float contentHeight = Mathf.Max(rowHeight, config.MinimumTimelineContentHeight,
                viewportRangeHeight, 1f);
            trackHeaderContent.style.height = contentHeight;
            timelineContent.style.height = contentHeight;
            laneBackgroundRows.style.height = contentHeight;
            laneItemRows.style.height = contentHeight;
            gridHost.style.height = contentHeight;
            RefreshFixedDrawing();
        }

        // 汇总动态背景行的真实布局高度，避免读取被虚拟高度扩展后的父容器尺寸。
        private float CalculateRowHeight()
        {
            float height = 0f;
            foreach (VisualElement row in laneBackgroundRows.Children())
                height += row.resolvedStyle.height;
            return height;
        }

        // 有技能时使用技能末帧；空技能时使用当前虚拟内容能够完整表示的最大帧。
        private int GetScrubMaximumFrame()
        {
            if (viewModel?.CurrentConfig is SkillConfig skillConfig)
                return Mathf.Max(0, skillConfig.DurationFrames - 1);
            float frameRangeWidth = Mathf.Max(0f, contentWidth - config.ContentRightPadding);
            return Mathf.Max(0, Mathf.FloorToInt(frameRangeWidth / viewport.PixelsPerFrame) - 1);
        }

        // 返回右侧 ScrollView 当前真实水平偏移，禁止从左侧或另一套状态推导坐标。
        private float GetHorizontalOffset() => timelineScroll?.scrollOffset.x ?? 0f;

        // 返回右侧时间内容视口宽度，为虚拟宽度、缩放锚点和网格可见范围提供依据。
        private float GetTimelineViewportWidth() =>
            Mathf.Max(0f, timelineScroll?.contentViewport.resolvedStyle.width ?? 0f);

        // 返回右侧时间内容视口高度，为左右一致的虚拟纵向范围提供依据。
        private float GetTimelineViewportHeight() =>
            Mathf.Max(0f, timelineScroll?.contentViewport.resolvedStyle.height ?? 0f);

        #endregion

        #region 键盘交互

        // Escape 同时取消 Item 草稿和播放头 Scrub，不修改 Config。
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