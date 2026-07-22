#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 将滚轮与 Scroller 输入转换为 CanvasModel 视口状态，并把权威偏移同步到左右 ScrollView。
    /// </summary>
    internal sealed class ViewportInputController : IDisposable
    {
        #region 依赖与交互状态

        private readonly VisualElement panel;
        private readonly ScrollView trackHeaderScroll;
        private readonly ScrollView timelineScroll;
        private readonly CanvasModel canvasModel;
        private readonly EditorConfig config;
        private IVisualElementScheduledItem restoreItem;
        private bool applyingScrollOffset;

        #endregion

        /// <summary>
        /// 创建左右纵向双向同步、右侧水平独占的视口输入控制器。
        /// </summary>
        public ViewportInputController(VisualElement panel, ScrollView trackHeaderScroll,
            ScrollView timelineScroll, CanvasModel canvasModel,
            EditorConfig config)
        {
            this.panel = panel ?? throw new ArgumentNullException(nameof(panel));
            this.trackHeaderScroll = trackHeaderScroll ?? throw new ArgumentNullException(nameof(trackHeaderScroll));
            this.timelineScroll = timelineScroll ?? throw new ArgumentNullException(nameof(timelineScroll));
            this.canvasModel = canvasModel ?? throw new ArgumentNullException(nameof(canvasModel));
            this.config = config ?? throw new ArgumentNullException(nameof(config));

            ConfigureScrollViews();
            RegisterEvents();
            restoreItem = timelineScroll.schedule.Execute(RestoreCanvasScrollOffset);
        }

        /// <summary>
        /// 停止延迟恢复，并注销滚轮、Scroller 与 CanvasModel 回调。
        /// </summary>
        public void Dispose()
        {
            restoreItem?.Pause();
            restoreItem = null;
            panel.UnregisterCallback<WheelEvent>(OnWheel, TrickleDown.TrickleDown);
            timelineScroll.horizontalScroller.valueChanged -= OnRightHorizontalChanged;
            timelineScroll.verticalScroller.valueChanged -= OnRightVerticalChanged;
            trackHeaderScroll.verticalScroller.valueChanged -= OnLeftVerticalChanged;
            canvasModel.ScrollOffsetChanged -= ApplyCanvasScrollOffset;
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

        // 只注册输入源和 Model 偏移回写；真实视口 Geometry 由 CanvasController 负责测量。
        private void RegisterEvents()
        {
            panel.RegisterCallback<WheelEvent>(OnWheel, TrickleDown.TrickleDown);
            timelineScroll.horizontalScroller.valueChanged += OnRightHorizontalChanged;
            timelineScroll.verticalScroller.valueChanged += OnRightVerticalChanged;
            trackHeaderScroll.verticalScroller.valueChanged += OnLeftVerticalChanged;
            canvasModel.ScrollOffsetChanged += ApplyCanvasScrollOffset;
        }

        #endregion

        #region 输入与状态提交

        // Ctrl/Command 滚轮以右侧鼠标帧为锚点缩放；Shift 滚轮提交水平偏移。
        private void OnWheel(WheelEvent evt)
        {
            if (evt.ctrlKey || evt.commandKey)
            {
                float anchorX = timelineScroll.contentViewport.worldBound.Contains(evt.mousePosition)
                    ? timelineScroll.contentViewport.WorldToLocal(evt.mousePosition).x
                    : timelineScroll.contentViewport.resolvedStyle.width * 0.5f;
                float oldPixelsPerFrame = canvasModel.PixelsPerFrame;
                float anchorFrame = (anchorX + canvasModel.ScrollOffset.x) / oldPixelsPerFrame;
                canvasModel.SetZoom(oldPixelsPerFrame - evt.delta.y * config.ZoomSensitivity);
                Vector2 offset = canvasModel.ScrollOffset;
                offset.x = Mathf.Max(0f, anchorFrame * canvasModel.PixelsPerFrame - anchorX);
                canvasModel.SetScrollOffset(offset);
                evt.StopPropagation();
                return;
            }

            if (!evt.shiftKey) return;
            Vector2 horizontalOffset = canvasModel.ScrollOffset;
            horizontalOffset.x = Mathf.Max(0f,
                horizontalOffset.x + evt.delta.y * config.HorizontalWheelStep);
            canvasModel.SetScrollOffset(horizontalOffset);
            evt.StopPropagation();
        }

        // 将右侧水平 Scroller 的真实值提交到 CanvasModel，左侧水平偏移始终保持为零。
        private void OnRightHorizontalChanged(float value)
        {
            if (applyingScrollOffset) return;
            Vector2 offset = canvasModel.ScrollOffset;
            offset.x = value;
            canvasModel.SetScrollOffset(offset);
        }

        // 将右侧纵向 Scroller 的真实值提交到 CanvasModel，由 Model 事件统一同步左侧。
        private void OnRightVerticalChanged(float value)
        {
            if (applyingScrollOffset) return;
            Vector2 offset = canvasModel.ScrollOffset;
            offset.y = value;
            canvasModel.SetScrollOffset(offset);
        }

        // 将左侧纵向 Scroller 的真实值提交到 CanvasModel，由 Model 事件统一同步右侧。
        private void OnLeftVerticalChanged(float value)
        {
            if (applyingScrollOffset) return;
            Vector2 offset = canvasModel.ScrollOffset;
            offset.y = value;
            canvasModel.SetScrollOffset(offset);
        }

        #endregion

        #region Model 状态应用

        // 延迟到 ScrollView 完成首次布局后恢复 SessionState 中的权威偏移。
        private void RestoreCanvasScrollOffset()
        {
            restoreItem = null;
            ApplyCanvasScrollOffset();
        }

        // 把会话恢复、用户输入或播放头定位产生的权威偏移同时应用到左右 ScrollView。
        private void ApplyCanvasScrollOffset()
        {
            Vector2 target = canvasModel.ScrollOffset;
            Vector2 leftTarget = new(0f, target.y);
            float epsilonSquared = config.ScrollOffsetEpsilon * config.ScrollOffsetEpsilon;
            bool rightMatches = (timelineScroll.scrollOffset - target).sqrMagnitude < epsilonSquared;
            bool leftMatches = (trackHeaderScroll.scrollOffset - leftTarget).sqrMagnitude < epsilonSquared;
            if (rightMatches && leftMatches) return;

            applyingScrollOffset = true;
            try
            {
                timelineScroll.scrollOffset = target;
                trackHeaderScroll.scrollOffset = leftTarget;
            }
            finally
            {
                applyingScrollOffset = false;
            }
        }

        #endregion
    }
}
#endif
