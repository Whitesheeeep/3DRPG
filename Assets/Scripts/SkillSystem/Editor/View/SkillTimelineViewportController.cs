#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 保存时间轴缩放和滚动偏移，并通过 SessionState 恢复 EditorWindow 表现状态。
    /// </summary>
    internal sealed class SkillTimelineViewportController
    {
        private const string ZoomKey = "RPG.SkillTimeline.Zoom";
        private const string HorizontalOffsetKey = "RPG.SkillTimeline.Offset";
        private const string VerticalOffsetKey = "RPG.SkillTimeline.Offset.Vertical";
        private readonly SkillTimelineEditorConfig config;

        public event Action ViewportChanged;
        public event Action ScrollOffsetChanged;

        public float PixelsPerFrame { get; private set; }
        public Vector2 ScrollOffset { get; private set; }
        public float MinimumPixelsPerFrame => config.MinimumPixelsPerFrame;
        public float MaximumPixelsPerFrame => config.MaximumPixelsPerFrame;

        /// <summary>
        /// 使用 Editor-only 配置初始化缩放范围和会话滚动状态。
        /// </summary>
        public SkillTimelineViewportController(SkillTimelineEditorConfig config)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            PixelsPerFrame = Mathf.Clamp(SessionState.GetFloat(ZoomKey, config.DefaultPixelsPerFrame),
                config.MinimumPixelsPerFrame, config.MaximumPixelsPerFrame);
            ScrollOffset = new Vector2(
                SessionState.GetFloat(HorizontalOffsetKey, 0f),
                SessionState.GetFloat(VerticalOffsetKey, 0f));
        }

        /// <summary>
        /// 修改每帧像素宽度并保存当前会话缩放值。
        /// </summary>
        public void SetZoom(float pixelsPerFrame)
        {
            pixelsPerFrame = Mathf.Clamp(pixelsPerFrame,
                config.MinimumPixelsPerFrame, config.MaximumPixelsPerFrame);
            if (Mathf.Approximately(PixelsPerFrame, pixelsPerFrame)) return;
            PixelsPerFrame = pixelsPerFrame;
            SessionState.SetFloat(ZoomKey, PixelsPerFrame);
            ViewportChanged?.Invoke();
        }

        /// <summary>
        /// 修改并持久化 ScrollView 的实际水平和垂直偏移。
        /// </summary>
        public void SetScrollOffset(Vector2 offset)
        {
            offset.x = Mathf.Max(0f, offset.x);
            offset.y = Mathf.Max(0f, offset.y);
            if ((ScrollOffset - offset).sqrMagnitude < config.ScrollOffsetEpsilon * config.ScrollOffsetEpsilon) return;
            ScrollOffset = offset;
            SessionState.SetFloat(HorizontalOffsetKey, offset.x);
            SessionState.SetFloat(VerticalOffsetKey, offset.y);
            ScrollOffsetChanged?.Invoke();
        }

        /// <summary>
        /// 在需要时调整水平偏移，保证指定帧位于当前可见区域内。
        /// </summary>
        public void EnsureFrameVisible(int frame, float viewportWidth)
        {
            float position = Mathf.Max(0, frame) * PixelsPerFrame;
            Vector2 offset = ScrollOffset;
            if (position < offset.x) offset.x = position;
            else if (position > offset.x + viewportWidth - config.PlayheadAutoScrollMargin)
                offset.x = Mathf.Max(0f, position - viewportWidth + config.PlayheadAutoScrollMargin);
            SetScrollOffset(offset);
        }
    }

    /// <summary>
    /// 统一执行帧坐标、内容坐标和固定视口坐标之间的整数帧换算。
    /// </summary>
    internal sealed class SkillTimelineCoordinateMapper
    {
        private readonly SkillTimelineViewportController viewport;

        /// <summary>
        /// 创建使用指定视口缩放状态的坐标映射器。
        /// </summary>
        public SkillTimelineCoordinateMapper(SkillTimelineViewportController viewport) =>
            this.viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));

        /// <summary>
        /// 把整数帧转换为 ScrollView 内容坐标。
        /// </summary>
        public float FrameToContentX(int frame) => Mathf.Max(0, frame) * viewport.PixelsPerFrame;

        /// <summary>
        /// 把整数帧转换为扣除水平滚动偏移后的固定视口坐标。
        /// </summary>
        public float FrameToViewportX(int frame, float horizontalOffset) =>
            FrameToContentX(frame) - Mathf.Max(0f, horizontalOffset);

        /// <summary>
        /// 把固定视口坐标转换并吸附到最近的非负整数帧。
        /// </summary>
        public int ViewportXToFrame(float viewportX, float horizontalOffset) => Mathf.Max(0,
            Mathf.RoundToInt((viewportX + Mathf.Max(0f, horizontalOffset)) / viewport.PixelsPerFrame));

        /// <summary>
        /// 把 ScrollView 内容坐标转换并吸附到最近的非负整数帧。
        /// </summary>
        public int ContentXToFrame(float contentX) =>
            Mathf.Max(0, Mathf.RoundToInt(contentX / viewport.PixelsPerFrame));

        /// <summary>
        /// 把半开帧区间的持续帧数转换为显示宽度。
        /// </summary>
        public float DurationToWidth(int durationFrames) =>
            Mathf.Max(viewport.PixelsPerFrame, durationFrames * viewport.PixelsPerFrame);
    }
}
#endif