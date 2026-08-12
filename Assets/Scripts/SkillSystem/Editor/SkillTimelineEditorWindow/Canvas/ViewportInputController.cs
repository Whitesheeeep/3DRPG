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
        private IVisualElementScheduledItem geometryClampItem;
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
            geometryClampItem?.Pause();
            geometryClampItem = null;
            panel.UnregisterCallback<WheelEvent>(OnWheel, TrickleDown.TrickleDown);
            timelineScroll.horizontalScroller.valueChanged -= OnRightHorizontalChanged;
            timelineScroll.verticalScroller.valueChanged -= OnRightVerticalChanged;
            trackHeaderScroll.verticalScroller.valueChanged -= OnLeftVerticalChanged;
            canvasModel.ScrollOffsetChanged -= ApplyCanvasScrollOffset;
            canvasModel.GeometryChanged -= ScheduleGeometryClamp;
        }

        #region 初始化与事件注册

        /// <summary>
        /// 配置左右 ScrollView 的滚动方向和滚动条可见性。
        /// </summary>
        private void ConfigureScrollViews()
        {
            trackHeaderScroll.mode = ScrollViewMode.Vertical;
            trackHeaderScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            trackHeaderScroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            timelineScroll.mode = ScrollViewMode.VerticalAndHorizontal;
            timelineScroll.horizontalScrollerVisibility = ScrollerVisibility.AlwaysVisible;
            timelineScroll.verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible;
        }

        /// <summary>
        /// 注册滚轮、Scroller 和 CanvasModel 偏移同步回调。
        /// </summary>
        private void RegisterEvents()
        {
            panel.RegisterCallback<WheelEvent>(OnWheel, TrickleDown.TrickleDown);
            timelineScroll.horizontalScroller.valueChanged += OnRightHorizontalChanged;
            timelineScroll.verticalScroller.valueChanged += OnRightVerticalChanged;
            trackHeaderScroll.verticalScroller.valueChanged += OnLeftVerticalChanged;
            canvasModel.ScrollOffsetChanged += ApplyCanvasScrollOffset;
            canvasModel.GeometryChanged += ScheduleGeometryClamp;
        }

        #endregion

        #region 输入与状态提交

        /// <summary>
        /// 将滚轮输入转换为锚点缩放或受边界约束的水平滚动。
        /// </summary>
        /// <param name="evt">当前时间轴面板接收到的滚轮事件。</param>
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

            // 普通鼠标通常写入 Y，触控板或 Unity 的 Shift 转换可能直接写入 X。
            float wheelDelta = ResolveHorizontalWheelDelta(evt.delta);
            float minimumOffset = timelineScroll.horizontalScroller.lowValue;
            float maximumOffset = Mathf.Max(minimumOffset,
                timelineScroll.horizontalScroller.highValue);
            Vector2 horizontalOffset = canvasModel.ScrollOffset;
            // 使用真实 ScrollView 偏移作为本次输入起点，避免 Model 与 Scroller 边界不同步。
            horizontalOffset.x = Mathf.Clamp(
                timelineScroll.scrollOffset.x + wheelDelta * config.HorizontalWheelStep,
                minimumOffset, maximumOffset);
            canvasModel.SetScrollOffset(horizontalOffset);

            // 阻止 ScrollView 再执行默认滚动，避免横移后继续纵向滚动或重复横移。
            evt.PreventDefault();
            evt.StopPropagation();
        }

        /// <summary>
        /// 从 WheelEvent 的二维增量中提取横向滚动量。
        /// </summary>
        /// <param name="delta">Unity UI Toolkit 提供的滚轮增量。</param>
        /// <returns>优先使用直接横向输入，否则使用普通滚轮的纵向输入。</returns>
        private static float ResolveHorizontalWheelDelta(Vector3 delta) =>
            !Mathf.Approximately(delta.x, 0f) ? delta.x : delta.y;

        /// <summary>
        /// 将右侧水平 Scroller 的真实值提交到 CanvasModel。
        /// </summary>
        /// <param name="value">右侧 ScrollView 当前水平像素偏移。</param>
        private void OnRightHorizontalChanged(float value)
        {
            if (applyingScrollOffset) return;
            Vector2 offset = canvasModel.ScrollOffset;
            offset.x = value;
            canvasModel.SetScrollOffset(offset);
        }

        /// <summary>
        /// 将右侧纵向 Scroller 的真实值提交到 CanvasModel，并由 Model 同步左侧。
        /// </summary>
        /// <param name="value">右侧 ScrollView 当前纵向像素偏移。</param>
        private void OnRightVerticalChanged(float value)
        {
            if (applyingScrollOffset) return;
            Vector2 offset = canvasModel.ScrollOffset;
            offset.y = value;
            canvasModel.SetScrollOffset(offset);
        }

        /// <summary>
        /// 将左侧纵向 Scroller 的真实值提交到 CanvasModel，并由 Model 同步右侧。
        /// </summary>
        /// <param name="value">左侧 ScrollView 当前纵向像素偏移。</param>
        private void OnLeftVerticalChanged(float value)
        {
            if (applyingScrollOffset) return;
            Vector2 offset = canvasModel.ScrollOffset;
            offset.y = value;
            canvasModel.SetScrollOffset(offset);
        }

        #endregion

        #region Model 状态应用

        /// <summary>
        /// 在自定义内容尺寸应用后的下一次布局中夹紧偏移，避免使用尚未更新的 Scroller 范围。
        /// </summary>
        private void ScheduleGeometryClamp()
        {
            geometryClampItem?.Pause();
            geometryClampItem = timelineScroll.schedule.Execute(ClampOffsetToResolvedRanges);
        }

        /// <summary>
        /// 根据真实水平范围和左右共有的纵向范围夹紧 CanvasModel，并同步两个 ScrollView。
        /// </summary>
        private void ClampOffsetToResolvedRanges()
        {
            geometryClampItem = null;

            float horizontalMinimum = timelineScroll.horizontalScroller.lowValue;
            float horizontalMaximum = Mathf.Max(horizontalMinimum,
                timelineScroll.horizontalScroller.highValue);
            float rightVerticalMinimum = timelineScroll.verticalScroller.lowValue;
            float leftVerticalMinimum = trackHeaderScroll.verticalScroller.lowValue;
            float verticalMinimum = Mathf.Max(rightVerticalMinimum, leftVerticalMinimum);
            float verticalMaximum = Mathf.Max(verticalMinimum, Mathf.Min(
                timelineScroll.verticalScroller.highValue,
                trackHeaderScroll.verticalScroller.highValue));

            Vector2 offset = canvasModel.ScrollOffset;
            offset.x = Mathf.Clamp(offset.x, horizontalMinimum, horizontalMaximum);
            offset.y = Mathf.Clamp(offset.y, verticalMinimum, verticalMaximum);
            canvasModel.SetScrollOffset(offset);

            // Model 值未变化时不会发送事件，仍需把新布局后的真实 ScrollView 对齐到权威偏移。
            ApplyCanvasScrollOffset();
        }

        /// <summary>
        /// 在 ScrollView 完成首次布局后恢复 SessionState 中的权威偏移。
        /// </summary>
        private void RestoreCanvasScrollOffset()
        {
            restoreItem = null;
            ApplyCanvasScrollOffset();
        }

        /// <summary>
        /// 将会话恢复、用户输入或播放头定位产生的权威偏移应用到左右 ScrollView。
        /// </summary>
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
