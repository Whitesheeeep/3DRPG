#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 协调左右 ScrollView 的双向纵向同步，并以右侧实际偏移作为水平滚动权威来源。
    /// </summary>
    internal sealed class SkillTimelineViewportInputController : IDisposable
    {
        #region 依赖与状态

        private readonly VisualElement panel;
        private readonly ScrollView trackHeaderScroll;
        private readonly ScrollView timelineScroll;
        private readonly SkillTimelineViewportController viewport;
        private readonly SkillTimelineEditorConfig config;
        private bool applyingScrollOffset;

        #endregion

        #region 事件

        /// <summary>
        /// 当缩放、滚动或视口尺寸变化，需要重绘固定层时触发。
        /// </summary>
        public event Action PresentationChanged;

        #endregion

        /// <summary>
        /// 创建左右纵向双向同步、右侧水平独占的视口输入控制器。
        /// </summary>
        public SkillTimelineViewportInputController(VisualElement panel, ScrollView trackHeaderScroll,
            ScrollView timelineScroll, SkillTimelineViewportController viewport,
            SkillTimelineEditorConfig config)
        {
            this.panel = panel ?? throw new ArgumentNullException(nameof(panel));
            this.trackHeaderScroll = trackHeaderScroll ?? throw new ArgumentNullException(nameof(trackHeaderScroll));
            this.timelineScroll = timelineScroll ?? throw new ArgumentNullException(nameof(timelineScroll));
            this.viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
            this.config = config ?? throw new ArgumentNullException(nameof(config));

            ConfigureScrollViews();
            RegisterEvents();
            timelineScroll.schedule.Execute(ApplyViewportOffset);
        }

        /// <summary>
        /// 注销左右滚动、缩放和 Geometry 回调。
        /// </summary>
        public void Dispose()
        {
            panel.UnregisterCallback<WheelEvent>(OnWheel, TrickleDown.TrickleDown);
            timelineScroll.contentViewport.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            trackHeaderScroll.contentViewport.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            timelineScroll.horizontalScroller.valueChanged -= OnRightHorizontalChanged;
            timelineScroll.verticalScroller.valueChanged -= OnRightVerticalChanged;
            trackHeaderScroll.verticalScroller.valueChanged -= OnLeftVerticalChanged;
            viewport.ViewportChanged -= OnViewportChanged;
            viewport.ScrollOffsetChanged -= ApplyViewportOffset;
        }

        #region 初始化与事件注册

        // 左侧只允许纵向滚动并隐藏滚动条，右侧保留始终可见的双向滚动条。
        private void ConfigureScrollViews()
        {
            trackHeaderScroll.mode = ScrollViewMode.Vertical;
            trackHeaderScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            trackHeaderScroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            timelineScroll.mode = ScrollViewMode.VerticalAndHorizontal;
            timelineScroll.horizontalScrollerVisibility = ScrollerVisibility.AlwaysVisible;
            timelineScroll.verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible;
        }

        // 注册两侧滚动源、缩放输入、尺寸变化与 SessionState 恢复事件。
        private void RegisterEvents()
        {
            panel.RegisterCallback<WheelEvent>(OnWheel, TrickleDown.TrickleDown);
            timelineScroll.contentViewport.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            trackHeaderScroll.contentViewport.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            timelineScroll.horizontalScroller.valueChanged += OnRightHorizontalChanged;
            timelineScroll.verticalScroller.valueChanged += OnRightVerticalChanged;
            trackHeaderScroll.verticalScroller.valueChanged += OnLeftVerticalChanged;
            viewport.ViewportChanged += OnViewportChanged;
            viewport.ScrollOffsetChanged += ApplyViewportOffset;
        }

        #endregion

        #region 输入与滚动同步

        // Ctrl/Command 滚轮以右侧鼠标帧为锚点缩放；左侧触发时使用右侧视口中心作为锚点。
        private void OnWheel(WheelEvent evt)
        {
            if (evt.ctrlKey || evt.commandKey)
            {
                float anchorX = timelineScroll.contentViewport.worldBound.Contains(evt.mousePosition)
                    ? timelineScroll.contentViewport.WorldToLocal(evt.mousePosition).x
                    : timelineScroll.contentViewport.resolvedStyle.width * 0.5f;
                float oldPixelsPerFrame = viewport.PixelsPerFrame;
                float anchorFrame = (anchorX + timelineScroll.scrollOffset.x) / oldPixelsPerFrame;
                viewport.SetZoom(oldPixelsPerFrame - evt.delta.y * config.ZoomSensitivity);
                Vector2 offset = timelineScroll.scrollOffset;
                offset.x = Mathf.Max(0f, anchorFrame * viewport.PixelsPerFrame - anchorX);
                viewport.SetScrollOffset(offset);
                evt.StopPropagation();
                return;
            }

            if (!evt.shiftKey) return;
            Vector2 horizontalOffset = timelineScroll.scrollOffset;
            horizontalOffset.x = Mathf.Max(0f,
                horizontalOffset.x + evt.delta.y * config.HorizontalWheelStep);
            viewport.SetScrollOffset(horizontalOffset);
            evt.StopPropagation();
        }

        // 右侧水平滚动仅写入权威偏移，不改变左侧水平位置。
        private void OnRightHorizontalChanged(float _)
        {
            if (applyingScrollOffset) return;
            viewport.SetScrollOffset(timelineScroll.scrollOffset);
            PresentationChanged?.Invoke();
        }

        // 右侧纵向滚动时立即把真实 y 偏移同步到左侧标签 ScrollView。
        private void OnRightVerticalChanged(float _)
        {
            if (applyingScrollOffset) return;
            SynchronizeFromRight();
        }

        // 左侧标签滚动时保留右侧 x，只把真实 y 偏移同步到右侧 Lane。
        private void OnLeftVerticalChanged(float _)
        {
            if (applyingScrollOffset) return;
            applyingScrollOffset = true;
            Vector2 rightOffset = timelineScroll.scrollOffset;
            rightOffset.y = trackHeaderScroll.scrollOffset.y;
            timelineScroll.scrollOffset = rightOffset;
            applyingScrollOffset = false;
            viewport.SetScrollOffset(rightOffset);
            PresentationChanged?.Invoke();
        }

        // 从右侧读取完整偏移，并保证左侧始终处于 x=0、y 相同的状态。
        private void SynchronizeFromRight()
        {
            applyingScrollOffset = true;
            Vector2 rightOffset = timelineScroll.scrollOffset;
            trackHeaderScroll.scrollOffset = new Vector2(0f, rightOffset.y);
            applyingScrollOffset = false;
            viewport.SetScrollOffset(rightOffset);
            PresentationChanged?.Invoke();
        }

        #endregion

        #region 状态恢复与刷新

        // 任一内容视口尺寸改变时通知 Canvas 重算虚拟画布范围。
        private void OnGeometryChanged(GeometryChangedEvent _) => PresentationChanged?.Invoke();

        // 缩放改变后由 Canvas 重算内容宽度和动态 Item 几何。
        private void OnViewportChanged() => PresentationChanged?.Invoke();

        // 把 SessionState 或播放头定位产生的权威偏移同时恢复到左右 ScrollView。
        private void ApplyViewportOffset()
        {
            Vector2 target = viewport.ScrollOffset;
            Vector2 leftTarget = new(0f, target.y);
            bool rightMatches = (timelineScroll.scrollOffset - target).sqrMagnitude <
                                config.ScrollOffsetEpsilon * config.ScrollOffsetEpsilon;
            bool leftMatches = (trackHeaderScroll.scrollOffset - leftTarget).sqrMagnitude <
                               config.ScrollOffsetEpsilon * config.ScrollOffsetEpsilon;
            if (rightMatches && leftMatches) return;

            applyingScrollOffset = true;
            timelineScroll.scrollOffset = target;
            trackHeaderScroll.scrollOffset = leftTarget;
            applyingScrollOffset = false;
            PresentationChanged?.Invoke();
        }

        #endregion
    }
}
#endif