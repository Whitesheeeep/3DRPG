#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 保存 Canvas 内部 MVC 使用的实时表现状态，不持有 UI 元素，也不修改技能资产。
    /// </summary>
    internal sealed class CanvasModel
    {
        #region 会话键与依赖

        private const string ZoomKey = "RPG.SkillTimeline.Zoom";
        private const string HorizontalOffsetKey = "RPG.SkillTimeline.Offset";
        private const string VerticalOffsetKey = "RPG.SkillTimeline.Offset.Vertical";

        private readonly EditorConfig config;

        #endregion

        #region 事件

        /// <summary>
        /// 当前技能配置或其时间轴内容发生变化时触发。
        /// </summary>
        public event Action TimelineChanged;

        /// <summary>
        /// 当前播放头整数帧发生变化时触发。
        /// </summary>
        public event Action PlayheadChanged;

        /// <summary>
        /// 虚拟画布或右侧视口的几何范围发生变化时触发。
        /// </summary>
        public event Action GeometryChanged;

        /// <summary>
        /// 每帧像素宽度发生变化时触发。
        /// </summary>
        public event Action ZoomChanged;

        /// <summary>
        /// 时间轴横向或纵向滚动偏移发生变化时触发。
        /// </summary>
        public event Action ScrollOffsetChanged;

        #endregion

        #region 只读状态

        public SkillConfig CurrentConfig { get; private set; }
        public int CurrentFrame { get; private set; }
        public int MaximumFrame { get; private set; }
        public float ContentWidth { get; private set; } = 1f;
        public float ContentHeight { get; private set; } = 1f;
        public float ViewportWidth { get; private set; }
        public float ViewportHeight { get; private set; }
        public float PixelsPerFrame { get; private set; }
        public Vector2 ScrollOffset { get; private set; }
        public float MinimumPixelsPerFrame => config.MinimumPixelsPerFrame;
        public float MaximumPixelsPerFrame => config.MaximumPixelsPerFrame;

        #endregion

        #region 生命周期

        /// <summary>
        /// 使用 Editor-only 配置创建 Canvas 表现模型，并恢复当前 Unity 会话中的缩放与滚动偏移。
        /// </summary>
        public CanvasModel(EditorConfig config)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            PixelsPerFrame = Mathf.Clamp(SessionState.GetFloat(ZoomKey, config.DefaultPixelsPerFrame),
                config.MinimumPixelsPerFrame, config.MaximumPixelsPerFrame);
            ScrollOffset = new Vector2(
                Mathf.Max(0f, SessionState.GetFloat(HorizontalOffsetKey, 0f)),
                Mathf.Max(0f, SessionState.GetFloat(VerticalOffsetKey, 0f)));
        }

        #endregion

        #region 状态同步

        // 即使 Config 引用未变化也发送通知，因为同一资产内部的 Track 或 Item 可能已经改变。
        internal void SynchronizeTimeline(SkillConfig config)
        {
            CurrentConfig = config;
            TimelineChanged?.Invoke();
        }

        // 保存外层 ViewModel 已经确认的整数帧，避免绘制 View 直接依赖 ViewModel。
        internal void SynchronizeCurrentFrame(int frame)
        {
            frame = Mathf.Max(0, frame);
            if (CurrentFrame == frame) return;
            CurrentFrame = frame;
            PlayheadChanged?.Invoke();
        }

        // 原子更新虚拟内容与视口范围，保证最大可拖动帧和绘制几何来自同一次计算。
        internal void SynchronizeGeometry(float contentWidth, float contentHeight,
            float viewportWidth, float viewportHeight, int maximumFrame)
        {
            contentWidth = Mathf.Max(1f, contentWidth);
            contentHeight = Mathf.Max(1f, contentHeight);
            viewportWidth = Mathf.Max(0f, viewportWidth);
            viewportHeight = Mathf.Max(0f, viewportHeight);
            maximumFrame = Mathf.Max(0, maximumFrame);

            bool changed = !Mathf.Approximately(ContentWidth, contentWidth) ||
                           !Mathf.Approximately(ContentHeight, contentHeight) ||
                           !Mathf.Approximately(ViewportWidth, viewportWidth) ||
                           !Mathf.Approximately(ViewportHeight, viewportHeight) ||
                           MaximumFrame != maximumFrame;
            if (!changed) return;

            ContentWidth = contentWidth;
            ContentHeight = contentHeight;
            ViewportWidth = viewportWidth;
            ViewportHeight = viewportHeight;
            MaximumFrame = maximumFrame;
            GeometryChanged?.Invoke();
        }

        // 首次绑定时即使当前帧为零也需要通知播放头执行一次初始绘制。
        internal void NotifyInitialPlayhead() => PlayheadChanged?.Invoke();

        #endregion

        #region 视口状态

        // 夹紧并保存缩放值；事件只描述缩放完成，不承载 UI 控件引用。
        internal void SetZoom(float pixelsPerFrame)
        {
            pixelsPerFrame = Mathf.Clamp(pixelsPerFrame,
                config.MinimumPixelsPerFrame, config.MaximumPixelsPerFrame);
            if (Mathf.Approximately(PixelsPerFrame, pixelsPerFrame)) return;

            PixelsPerFrame = pixelsPerFrame;
            SessionState.SetFloat(ZoomKey, PixelsPerFrame);
            ZoomChanged?.Invoke();
        }

        // 保存 ScrollView 的像素偏移镜像，用于会话恢复和程序化播放头定位。
        internal void SetScrollOffset(Vector2 offset)
        {
            offset.x = Mathf.Max(0f, offset.x);
            offset.y = Mathf.Max(0f, offset.y);
            float epsilon = config.ScrollOffsetEpsilon;
            if ((ScrollOffset - offset).sqrMagnitude < epsilon * epsilon) return;

            ScrollOffset = offset;
            SessionState.SetFloat(HorizontalOffsetKey, offset.x);
            SessionState.SetFloat(VerticalOffsetKey, offset.y);
            ScrollOffsetChanged?.Invoke();
        }

        // 只调整水平偏移，使播放头进入右侧时间轴的可见区域。
        internal void EnsureFrameVisible(int frame, float viewportWidth)
        {
            float position = Mathf.Max(0, frame) * PixelsPerFrame;
            Vector2 offset = ScrollOffset;
            if (position < offset.x)
            {
                offset.x = position;
            }
            else if (position > offset.x + viewportWidth - config.PlayheadAutoScrollMargin)
            {
                offset.x = Mathf.Max(0f,
                    position - viewportWidth + config.PlayheadAutoScrollMargin);
            }

            SetScrollOffset(offset);
        }

        #endregion
    }
}
#endif
