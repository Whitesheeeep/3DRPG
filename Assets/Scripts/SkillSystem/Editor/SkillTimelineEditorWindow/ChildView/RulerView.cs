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
    internal sealed class RulerView : IDisposable
    {
        #region 依赖

        private readonly CanvasModel canvasModel;
        private readonly CoordinateMapper mapper;
        private readonly EditorConfig editorConfig;

        #endregion

        #region 运行时
        private readonly IMGUIContainer drawing;
        #endregion

        #region 生命周期与刷新

        /// <summary>
        /// 创建直接读取 CanvasModel 实时缩放与滚动状态的固定标尺。
        /// </summary>
        public RulerView(VisualElement rulerLane, CanvasModel canvasModel,
            CoordinateMapper mapper,
            EditorConfig editorConfig)
        {
            this.canvasModel = canvasModel ?? throw new ArgumentNullException(nameof(canvasModel));
            this.mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            this.editorConfig = editorConfig ?? throw new ArgumentNullException(nameof(editorConfig));
            if (rulerLane == null) throw new ArgumentNullException(nameof(rulerLane));
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

        #endregion

        #region 绘制

        // 按当前缩放和右侧 ScrollView 的真实水平偏移，只绘制视口内的三级刻度。
        private void DrawRuler()
        {
            Rect rect = drawing.contentRect;
            if (rect.width <= 0f || rect.height <= 0f) return;
            float offset = Mathf.Max(0f, canvasModel.ScrollOffset.x);
            SkillConfig skillConfig = canvasModel.CurrentConfig;
            DrawOutsideRange(rect, skillConfig);

            int minorStep = TickUtility.GetMinorStep(canvasModel.PixelsPerFrame);
            int majorStep = TickUtility.GetMajorStep(canvasModel.PixelsPerFrame);
            int firstFrame = Mathf.Max(0, Mathf.FloorToInt(offset / canvasModel.PixelsPerFrame));
            int lastFrame = Mathf.CeilToInt((offset + rect.width) / canvasModel.PixelsPerFrame);
            int firstMinor = firstFrame - firstFrame % minorStep;
            int firstMajor = firstFrame - firstFrame % majorStep;

            Handles.BeginGUI();
            Color previous = Handles.color;
            Handles.color = editorConfig.MinorTickColor;
            for (int frame = firstMinor; frame <= lastFrame; frame += minorStep)
            {
                float x = mapper.FrameToViewportX(frame);
                bool medium = majorStep > 1 && frame % Mathf.Max(1, majorStep / 2) == 0;
                float height = medium ? editorConfig.MediumTickHeight : editorConfig.MinorTickHeight;
                Handles.DrawLine(new Vector3(x, rect.height - height), new Vector3(x, rect.height));
            }

            Handles.color = editorConfig.MajorTickColor;
            for (int frame = firstMajor; frame <= lastFrame; frame += majorStep)
            {
                float x = mapper.FrameToViewportX(frame);
                Handles.DrawLine(new Vector3(x, rect.height - editorConfig.MajorTickHeight),
                    new Vector3(x, rect.height));
                Vector2 labelPosition = new(x + editorConfig.RulerLabelOffset.x,
                    editorConfig.RulerLabelOffset.y);
                GUI.Label(new Rect(labelPosition, editorConfig.RulerLabelSize),
                    frame.ToString(), EditorStyles.miniLabel);
            }

            if (skillConfig != null)
            {
                float endX = mapper.FrameToViewportX(skillConfig.DurationFrames);
                Handles.color = editorConfig.DurationBoundaryColor;
                Handles.DrawLine(new Vector3(endX, 0f), new Vector3(endX, rect.height));
            }
            Handles.color = previous;
            Handles.EndGUI();
        }

        // 弱化技能结束帧之后的区域，但不改变可滚动内容宽度。
        private void DrawOutsideRange(Rect rect, SkillConfig skillConfig)
        {
            if (skillConfig == null) return;
            float endX = mapper.FrameToViewportX(skillConfig.DurationFrames);
            if (endX >= rect.width) return;
            float start = Mathf.Clamp(endX, 0f, rect.width);
            EditorGUI.DrawRect(new Rect(start, 0f, rect.width - start, rect.height),
                editorConfig.OutsideRangeColor);
        }

        #endregion
    }
}
#endif
