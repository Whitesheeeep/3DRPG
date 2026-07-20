#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 使用固定视口坐标绘制 Animation 风格刻度、帧标签和技能结束边界。
    /// </summary>
    internal sealed class SkillTimelineRulerView : IDisposable
    {
        private readonly IMGUIContainer drawing;
        private readonly SkillTimelineViewportController viewport;
        private readonly SkillTimelineCoordinateMapper mapper;
        private readonly SkillTimelineEditorConfig editorConfig;
        private readonly Func<SkillConfig> getConfig;
        private readonly Func<float> getHorizontalOffset;

        /// <summary>
        /// 创建由 Editor-only Config 控制视觉参数的固定标尺。
        /// </summary>
        public SkillTimelineRulerView(VisualElement rulerLane, SkillTimelineViewportController viewport,
            SkillTimelineCoordinateMapper mapper, SkillTimelineEditorConfig editorConfig,
            Func<SkillConfig> getConfig, Func<float> getHorizontalOffset)
        {
            this.viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
            this.mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            this.editorConfig = editorConfig ?? throw new ArgumentNullException(nameof(editorConfig));
            this.getConfig = getConfig ?? throw new ArgumentNullException(nameof(getConfig));
            this.getHorizontalOffset = getHorizontalOffset ?? throw new ArgumentNullException(nameof(getHorizontalOffset));
            drawing = new IMGUIContainer(DrawRuler) { name = "RulerDrawing", pickingMode = PickingMode.Ignore };
            drawing.AddToClassList("timeline-ruler-drawing");
            rulerLane.Add(drawing);
        }

        /// <summary>
        /// 标记标尺在下一次 GUI 更新时重绘。
        /// </summary>
        public void MarkDirtyRepaint() => drawing.MarkDirtyRepaint();

        /// <summary>
        /// 释放 IMGUI 回调并移除标尺绘制元素。
        /// </summary>
        public void Dispose()
        {
            drawing.onGUIHandler = null;
            drawing.RemoveFromHierarchy();
        }

        // 按当前缩放和真实水平偏移，只绘制视口内的三级刻度。
        private void DrawRuler()
        {
            Rect rect = drawing.contentRect;
            if (rect.width <= 0f || rect.height <= 0f) return;
            float offset = getHorizontalOffset();
            SkillConfig config = getConfig();
            DrawOutsideRange(rect, config, offset);

            int minorStep = SkillTimelineTickUtility.GetMinorStep(viewport.PixelsPerFrame);
            int majorStep = SkillTimelineTickUtility.GetMajorStep(viewport.PixelsPerFrame);
            int firstFrame = Mathf.Max(0, Mathf.FloorToInt(offset / viewport.PixelsPerFrame));
            int lastFrame = Mathf.CeilToInt((offset + rect.width) / viewport.PixelsPerFrame);
            int firstMinor = firstFrame - firstFrame % minorStep;
            int firstMajor = firstFrame - firstFrame % majorStep;

            Handles.BeginGUI();
            Color previous = Handles.color;
            Handles.color = editorConfig.MinorTickColor;
            for (int frame = firstMinor; frame <= lastFrame; frame += minorStep)
            {
                float x = mapper.FrameToViewportX(frame, offset);
                bool medium = majorStep > 1 && frame % Mathf.Max(1, majorStep / 2) == 0;
                float height = medium ? editorConfig.MediumTickHeight : editorConfig.MinorTickHeight;
                Handles.DrawLine(new Vector3(x, rect.height - height), new Vector3(x, rect.height));
            }

            Handles.color = editorConfig.MajorTickColor;
            for (int frame = firstMajor; frame <= lastFrame; frame += majorStep)
            {
                float x = mapper.FrameToViewportX(frame, offset);
                Handles.DrawLine(new Vector3(x, rect.height - editorConfig.MajorTickHeight), new Vector3(x, rect.height));
                Vector2 labelPosition = new(x + editorConfig.RulerLabelOffset.x, editorConfig.RulerLabelOffset.y);
                GUI.Label(new Rect(labelPosition, editorConfig.RulerLabelSize), frame.ToString(), EditorStyles.miniLabel);
            }

            if (config != null)
            {
                float endX = mapper.FrameToViewportX(config.DurationFrames, offset);
                Handles.color = editorConfig.DurationBoundaryColor;
                Handles.DrawLine(new Vector3(endX, 0f), new Vector3(endX, rect.height));
            }
            Handles.color = previous;
            Handles.EndGUI();
        }

        // 弱化技能结束帧之后的区域，但不改变可滚动内容宽度。
        private void DrawOutsideRange(Rect rect, SkillConfig config, float offset)
        {
            if (config == null) return;
            float endX = mapper.FrameToViewportX(config.DurationFrames, offset);
            if (endX >= rect.width) return;
            float start = Mathf.Clamp(endX, 0f, rect.width);
            EditorGUI.DrawRect(new Rect(start, 0f, rect.width - start, rect.height), editorConfig.OutsideRangeColor);
        }
    }
}
#endif