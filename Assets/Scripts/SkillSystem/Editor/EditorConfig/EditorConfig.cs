#if UNITY_EDITOR
using UnityEngine;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 保存技能时间轴 EditorWindow 的布局、绘制和交互参数，不包含任何运行时技能数据。
    /// </summary>
    [CreateAssetMenu(fileName = "SkillTimelineEditorConfig", menuName = "RPG/Skill Timeline Editor Config")]
    internal sealed class EditorConfig : ScriptableObject
    {
        #region Serialized fields

        [Header("窗口")]
        [SerializeField] private Vector2 minimumWindowSize = new(860f, 520f);
        [SerializeField, Min(1f)] private float inspectorMinimumWidth = 240f;
        [SerializeField, Min(1f)] private float inspectorDefaultWidth = 300f;
        [SerializeField, Min(1f)] private float inspectorMaximumWidth = 520f;

        [Header("时间轴视口")]
        [SerializeField, Min(1f)] private float defaultPixelsPerFrame = 12f;
        [SerializeField, Min(1f)] private float minimumPixelsPerFrame = 4f;
        [SerializeField, Min(1f)] private float maximumPixelsPerFrame = 48f;
        [SerializeField, Min(0.01f)] private float zoomSensitivity = 0.5f;
        [SerializeField, Min(1f)] private float horizontalWheelStep = 18f;
        [SerializeField, Min(0.0001f)] private float scrollOffsetEpsilon = 0.01f;
        [SerializeField, Min(0f)] private float contentRightPadding = 120f;
        [SerializeField, Min(0f)] private float playheadAutoScrollMargin = 24f;
        [SerializeField, Min(1)] private int minimumTimelineFrameCount = 120;
        [SerializeField, Min(1f)] private float minimumTimelineContentHeight = 800f;
        [SerializeField] private Vector2 minimumScrollableOverflow = new(240f, 240f);

        [Header("素材拖入")]
        [SerializeField, Min(1)] private int defaultVfxClipDurationFrames = 30;

        [Header("标尺")]
        [SerializeField, Min(1f)] private float minorTickHeight = 5f;
        [SerializeField, Min(1f)] private float mediumTickHeight = 9f;
        [SerializeField, Min(1f)] private float majorTickHeight = 16f;
        [SerializeField] private Vector2 rulerLabelOffset = new(3f, 0f);
        [SerializeField] private Vector2 rulerLabelSize = new(56f, 18f);
        [SerializeField] private Color minorTickColor = new(0.62f, 0.62f, 0.62f, 0.65f);
        [SerializeField] private Color majorTickColor = new(0.82f, 0.82f, 0.82f, 0.9f);
        [SerializeField] private Color durationBoundaryColor = new(0.9f, 0.45f, 0.3f, 0.8f);
        [SerializeField] private Color outsideRangeColor = new(0f, 0f, 0f, 0.22f);

        [Header("网格")]
        [SerializeField] private Color minorGridColor = new(0.43f, 0.43f, 0.43f, 0.16f);
        [SerializeField] private Color majorGridColor = new(0.57f, 0.57f, 0.57f, 0.38f);

        [Header("播放头")]
        [SerializeField, Min(0f)] private float playheadEdgeInset = 1f;
        [SerializeField, Min(1f)] private float playheadHalfWidth = 6f;
        [SerializeField, Min(1f)] private float playheadHeight = 10f;
        [SerializeField, Min(0f)] private float playheadLineStart = 9f;
        [SerializeField] private Color playheadColor = new(0.96f, 0.32f, 0.32f, 1f);

        #endregion

        #region Properties

        public Vector2 MinimumWindowSize => minimumWindowSize;
        public float InspectorMinimumWidth => inspectorMinimumWidth;
        public float InspectorDefaultWidth => inspectorDefaultWidth;
        public float InspectorMaximumWidth => inspectorMaximumWidth;
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
            inspectorMinimumWidth = Mathf.Max(1f, inspectorMinimumWidth);
            inspectorMaximumWidth = Mathf.Max(inspectorMinimumWidth, inspectorMaximumWidth);
            inspectorDefaultWidth = Mathf.Clamp(inspectorDefaultWidth, inspectorMinimumWidth, inspectorMaximumWidth);
            minimumPixelsPerFrame = Mathf.Max(1f, minimumPixelsPerFrame);
            maximumPixelsPerFrame = Mathf.Max(minimumPixelsPerFrame, maximumPixelsPerFrame);
            defaultPixelsPerFrame = Mathf.Clamp(defaultPixelsPerFrame, minimumPixelsPerFrame, maximumPixelsPerFrame);
            minimumTimelineFrameCount = Mathf.Max(1, minimumTimelineFrameCount);
            minimumTimelineContentHeight = Mathf.Max(1f, minimumTimelineContentHeight);
            minimumScrollableOverflow.x = Mathf.Max(0f, minimumScrollableOverflow.x);
            minimumScrollableOverflow.y = Mathf.Max(0f, minimumScrollableOverflow.y);
            defaultVfxClipDurationFrames = Mathf.Max(1, defaultVfxClipDurationFrames);
            playheadHalfWidth = Mathf.Max(1f, playheadHalfWidth);
            playheadHeight = Mathf.Max(1f, playheadHeight);
        }
    }
}
#endif
