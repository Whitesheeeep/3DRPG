#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 使用单个 IMGUIContainer 绘制全部可见 Lane 的主次帧网格。
    /// </summary>
    internal sealed class SkillTimelineGridView : IDisposable
    {
        private readonly IMGUIContainer drawing;
        private readonly SkillTimelineViewportController viewport;
        private readonly SkillTimelineCoordinateMapper mapper;
        private readonly SkillTimelineEditorConfig config;
        private readonly Func<float> getHorizontalOffset;
        private readonly Func<float> getViewportWidth;

        /// <summary>
        /// 创建不会参与 Picking 的统一网格绘制层。
        /// </summary>
        public SkillTimelineGridView(VisualElement host, SkillTimelineViewportController viewport,
            SkillTimelineCoordinateMapper mapper, SkillTimelineEditorConfig config,
            Func<float> getHorizontalOffset, Func<float> getViewportWidth)
        {
            this.viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
            this.mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.getHorizontalOffset = getHorizontalOffset ?? throw new ArgumentNullException(nameof(getHorizontalOffset));
            this.getViewportWidth = getViewportWidth ?? throw new ArgumentNullException(nameof(getViewportWidth));
            drawing = new IMGUIContainer(DrawGrid) { pickingMode = PickingMode.Ignore };
            drawing.AddToClassList("timeline-grid-drawing");
            host.Add(drawing);
        }

        /// <summary>
        /// 标记统一网格在下一次 GUI 更新时重绘。
        /// </summary>
        public void MarkDirtyRepaint() => drawing.MarkDirtyRepaint();

        /// <summary>
        /// 释放 IMGUI 回调并移除绘制元素。
        /// </summary>
        public void Dispose()
        {
            drawing.onGUIHandler = null;
            drawing.RemoveFromHierarchy();
        }

        // 仅遍历当前可见帧范围，避免网格复杂度随轨道数量增长。
        private void DrawGrid()
        {
            Rect rect = drawing.contentRect;
            if (rect.width <= 0f || rect.height <= 0f) return;
            float offset = getHorizontalOffset();
            int minorStep = SkillTimelineTickUtility.GetMinorStep(viewport.PixelsPerFrame);
            int majorStep = SkillTimelineTickUtility.GetMajorStep(viewport.PixelsPerFrame);
            int firstFrame = Mathf.Max(0, Mathf.FloorToInt(offset / viewport.PixelsPerFrame));
            int lastFrame = Mathf.CeilToInt((offset + Mathf.Max(0f, getViewportWidth())) / viewport.PixelsPerFrame);
            int firstMinor = firstFrame - firstFrame % minorStep;

            Handles.BeginGUI();
            Color previous = Handles.color;
            for (int frame = firstMinor; frame <= lastFrame; frame += minorStep)
            {
                Handles.color = frame % majorStep == 0 ? config.MajorGridColor : config.MinorGridColor;
                float x = mapper.FrameToContentX(frame);
                Handles.DrawLine(new Vector3(x, 0f), new Vector3(x, rect.height));
            }
            Handles.color = previous;
            Handles.EndGUI();
        }
    }
}
#endif