#if UNITY_EDITOR
using RPG.Markers;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 保存技能时间轴 EditorWindow 的布局、绘制和交互参数，不包含任何运行时技能数据。
    /// </summary>
    [CreateAssetMenu(fileName = "SkillTimelineEditorConfig", menuName = "RPG/Skill Timeline Editor Config")]
    internal sealed class EditorConfig : ScriptableObject
    {
        #region 序列化字段

        [Header("窗口")]
        [SerializeField, LabelText("最小窗口尺寸")] private Vector2 minimumWindowSize = new(860f, 520f);
        [SerializeField, Min(1f), LabelText("轨道标题最小宽度")] private float trackHeaderMinimumWidth = 190f;
        [SerializeField, Min(1f), LabelText("轨道标题默认宽度")] private float trackHeaderDefaultWidth = 240f;
        [SerializeField, Min(1f), LabelText("轨道标题最大宽度")] private float trackHeaderMaximumWidth = 420f;

        [Header("时间轴视口")]
        [SerializeField, Min(1f), LabelText("默认每帧像素")] private float defaultPixelsPerFrame = 12f;
        [SerializeField, Min(1f), LabelText("最小每帧像素")] private float minimumPixelsPerFrame = 4f;
        [SerializeField, Min(1f), LabelText("最大每帧像素")] private float maximumPixelsPerFrame = 48f;
        [SerializeField, Min(0.01f), LabelText("缩放灵敏度")] private float zoomSensitivity = 0.5f;
        [SerializeField, Min(1f), LabelText("水平滚轮步长")] private float horizontalWheelStep = 18f;
        [SerializeField, Min(0.0001f), LabelText("滚动偏移比较阈值")] private float scrollOffsetEpsilon = 0.01f;
        [SerializeField, Min(0f), LabelText("内容右侧留白")] private float contentRightPadding = 120f;
        [SerializeField, Min(0f), LabelText("播放头自动滚动边距")] private float playheadAutoScrollMargin = 24f;
        [SerializeField, Min(1), LabelText("最小时间轴帧数")] private int minimumTimelineFrameCount = 120;
        [SerializeField, Min(1f), LabelText("最小时间轴内容高度")] private float minimumTimelineContentHeight = 800f;
        [SerializeField, LabelText("最小可滚动余量")] private Vector2 minimumScrollableOverflow = new(240f, 240f);

        [Header("攻击检测预览")]
        [SerializeField, LabelText("检测形状颜色")] private Color attackDetectionColor = new(1f, 0.42f, 0.16f, 0.9f);
        [SerializeField, LabelText("选中检测颜色")] private Color attackDetectionSelectedColor = new(1f, 0.85f, 0.2f, 1f);
        [SerializeField, Range(0f, 1f), LabelText("非采样帧透明度")] private float attackDetectionUnsampledAlpha = 0.25f;
        [SerializeField, Range(0f, 1f), LabelText("检测表面透明度")] private float attackDetectionFillAlpha = 0.15f;
        [SerializeField, Min(8), LabelText("曲面分段数")] private int attackDetectionSurfaceSegments = 24;
        [SerializeField, LabelText("武器轨迹刀根 Key")] private MarkerKey previewWeaponTraceRootKey;
        [SerializeField, LabelText("武器轨迹刀尖 Key")] private MarkerKey previewWeaponTraceTipKey;

        [Header("素材拖入")]
        [SerializeField, Min(1), LabelText("默认特效持续帧")] private int defaultVfxClipDurationFrames = 30;

        [Header("标尺")]
        [SerializeField, Min(1f), LabelText("短刻度高度")] private float minorTickHeight = 5f;
        [SerializeField, Min(1f), LabelText("中刻度高度")] private float mediumTickHeight = 9f;
        [SerializeField, Min(1f), LabelText("主刻度高度")] private float majorTickHeight = 16f;
        [SerializeField, LabelText("帧标签偏移")] private Vector2 rulerLabelOffset = new(3f, 0f);
        [SerializeField, LabelText("帧标签尺寸")] private Vector2 rulerLabelSize = new(56f, 18f);
        [SerializeField, LabelText("次刻度颜色")] private Color minorTickColor = new(0.62f, 0.62f, 0.62f, 0.65f);
        [SerializeField, LabelText("主刻度颜色")] private Color majorTickColor = new(0.82f, 0.82f, 0.82f, 0.9f);
        [SerializeField, LabelText("持续帧边界颜色")] private Color durationBoundaryColor = new(0.9f, 0.45f, 0.3f, 0.8f);
        [SerializeField, LabelText("范围外遮罩颜色")] private Color outsideRangeColor = new(0f, 0f, 0f, 0.22f);

        [Header("网格")]
        [SerializeField, LabelText("次网格颜色")] private Color minorGridColor = new(0.43f, 0.43f, 0.43f, 0.16f);
        [SerializeField, LabelText("主网格颜色")] private Color majorGridColor = new(0.57f, 0.57f, 0.57f, 0.38f);

        [Header("播放头")]
        [SerializeField, Min(0f), LabelText("边缘内缩")] private float playheadEdgeInset = 1f;
        [SerializeField, Min(1f), LabelText("指针半宽")] private float playheadHalfWidth = 6f;
        [SerializeField, Min(1f), LabelText("指针高度")] private float playheadHeight = 10f;
        [SerializeField, Min(0f), LabelText("贯穿线起点")] private float playheadLineStart = 9f;
        [SerializeField, LabelText("播放头颜色")] private Color playheadColor = new(0.96f, 0.32f, 0.32f, 1f);

        #endregion

        #region 属性

        public Vector2 MinimumWindowSize => minimumWindowSize;
        public float TrackHeaderMinimumWidth => trackHeaderMinimumWidth;
        public float TrackHeaderDefaultWidth => trackHeaderDefaultWidth;
        public float TrackHeaderMaximumWidth => trackHeaderMaximumWidth;
        public float DefaultPixelsPerFrame => defaultPixelsPerFrame;
        public float MinimumPixelsPerFrame => minimumPixelsPerFrame;
        public float MaximumPixelsPerFrame => maximumPixelsPerFrame;
        public float ZoomSensitivity => zoomSensitivity;
        public float HorizontalWheelStep => horizontalWheelStep;
        public float ScrollOffsetEpsilon => scrollOffsetEpsilon;
        public float ContentRightPadding => contentRightPadding;
        public float PlayheadAutoScrollMargin => playheadAutoScrollMargin;
        public int MinimumTimelineFrameCount => minimumTimelineFrameCount;
        public float MinimumTimelineContentHeight => minimumTimelineContentHeight;
        public Vector2 MinimumScrollableOverflow => minimumScrollableOverflow;
        public Color AttackDetectionColor => attackDetectionColor;
        public Color AttackDetectionSelectedColor => attackDetectionSelectedColor;
        public float AttackDetectionUnsampledAlpha => attackDetectionUnsampledAlpha;
        public float AttackDetectionFillAlpha => attackDetectionFillAlpha;
        public int AttackDetectionSurfaceSegments => attackDetectionSurfaceSegments;
        public MarkerKey PreviewWeaponTraceRootKey => previewWeaponTraceRootKey;
        public MarkerKey PreviewWeaponTraceTipKey => previewWeaponTraceTipKey;
        public int DefaultVfxClipDurationFrames => defaultVfxClipDurationFrames;
        public float MinorTickHeight => minorTickHeight;
        public float MediumTickHeight => mediumTickHeight;
        public float MajorTickHeight => majorTickHeight;
        public Vector2 RulerLabelOffset => rulerLabelOffset;
        public Vector2 RulerLabelSize => rulerLabelSize;
        public Color MinorTickColor => minorTickColor;
        public Color MajorTickColor => majorTickColor;
        public Color DurationBoundaryColor => durationBoundaryColor;
        public Color OutsideRangeColor => outsideRangeColor;
        public Color MinorGridColor => minorGridColor;
        public Color MajorGridColor => majorGridColor;
        public float PlayheadEdgeInset => playheadEdgeInset;
        public float PlayheadHalfWidth => playheadHalfWidth;
        public float PlayheadHeight => playheadHeight;
        public float PlayheadLineStart => playheadLineStart;
        public Color PlayheadColor => playheadColor;

        #endregion

        // 保证通过 Inspector 修改后的缩放范围和绘制尺寸始终有效。
        private void OnValidate()
        {
            trackHeaderMinimumWidth = Mathf.Max(1f, trackHeaderMinimumWidth);
            trackHeaderMaximumWidth = Mathf.Max(trackHeaderMinimumWidth, trackHeaderMaximumWidth);
            trackHeaderDefaultWidth = Mathf.Clamp(
                trackHeaderDefaultWidth, trackHeaderMinimumWidth, trackHeaderMaximumWidth);
            minimumPixelsPerFrame = Mathf.Max(1f, minimumPixelsPerFrame);
            maximumPixelsPerFrame = Mathf.Max(minimumPixelsPerFrame, maximumPixelsPerFrame);
            defaultPixelsPerFrame = Mathf.Clamp(defaultPixelsPerFrame, minimumPixelsPerFrame, maximumPixelsPerFrame);
            minimumTimelineFrameCount = Mathf.Max(1, minimumTimelineFrameCount);
            minimumTimelineContentHeight = Mathf.Max(1f, minimumTimelineContentHeight);
            minimumScrollableOverflow.x = Mathf.Max(0f, minimumScrollableOverflow.x);
            minimumScrollableOverflow.y = Mathf.Max(0f, minimumScrollableOverflow.y);
            attackDetectionUnsampledAlpha = Mathf.Clamp01(attackDetectionUnsampledAlpha);
            attackDetectionFillAlpha = Mathf.Clamp01(attackDetectionFillAlpha);
            attackDetectionSurfaceSegments = Mathf.Max(8, attackDetectionSurfaceSegments);
            defaultVfxClipDurationFrames = Mathf.Max(1, defaultVfxClipDurationFrames);
            playheadHalfWidth = Mathf.Max(1f, playheadHalfWidth);
            playheadHeight = Mathf.Max(1f, playheadHeight);
        }
    }
}
#endif