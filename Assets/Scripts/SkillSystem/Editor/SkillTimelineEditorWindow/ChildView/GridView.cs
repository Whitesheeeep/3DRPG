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
    internal sealed class GridView : IDisposable
    {
        #region 依赖

        private readonly CanvasModel canvasModel;
        private readonly EditorConfig config;
        // 辅助类
        private readonly CoordinateMapper mapper;

        #endregion

        private readonly IMGUIContainer drawing;

        #region 生命周期与刷新

        /// <summary>
        /// 创建直接读取 CanvasModel 实时缩放与滚动状态的统一网格绘制层。
        /// </summary>
        public GridView(VisualElement host, CanvasModel canvasModel,
            CoordinateMapper mapper,
            EditorConfig config)
        {
            this.canvasModel = canvasModel ?? throw new ArgumentNullException(nameof(canvasModel));
            this.mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            if (host == null) throw new ArgumentNullException(nameof(host));

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
            float offset = Mathf.Max(0f, canvasModel.ScrollOffset.x);
            int minorStep = TickUtility.GetMinorStep(canvasModel.PixelsPerFrame);
            int majorStep = TickUtility.GetMajorStep(canvasModel.PixelsPerFrame);
            int firstFrame = Mathf.Max(0, Mathf.FloorToInt(offset / canvasModel.PixelsPerFrame));
            int lastFrame = Mathf.CeilToInt(
                (offset + Mathf.Max(0f, canvasModel.ViewportWidth)) / canvasModel.PixelsPerFrame);
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

        #endregion
    }
}
#endif
