#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 使用固定 Overlay 中的 IMGUIContainer 绘制贯穿标尺与可见 Lane 的当前帧指针。
    /// </summary>
    internal sealed class PlayheadView : IDisposable
    {
        #region 依赖与状态

        private readonly CanvasModel canvasModel;
        private readonly CoordinateMapper mapper;
        private readonly EditorConfig config;

        private readonly IMGUIContainer drawing;
        private bool visible = true;

        #endregion

        #region 生命周期与刷新

        /// <summary>
        /// 创建直接读取 CanvasModel 当前帧与滚动状态的播放头绘制层。
        /// </summary>
        public PlayheadView(VisualElement overlay, CanvasModel canvasModel,
            CoordinateMapper mapper,
            EditorConfig config)
        {
            this.canvasModel = canvasModel ?? throw new ArgumentNullException(nameof(canvasModel));
            this.mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            if (overlay == null) throw new ArgumentNullException(nameof(overlay));
            drawing = new IMGUIContainer(DrawPlayhead)
            {
                name = "PlayheadDrawing",
                pickingMode = PickingMode.Ignore
            };
            drawing.AddToClassList("playhead-drawing");
            overlay.Add(drawing);
        }

        /// <summary>
        /// 切换播放头绘制层的表现可见性，不修改 CurrentFrame。
        /// </summary>
        public void SetVisible(bool value)
        {
            if (visible == value) return;
            visible = value;
            drawing.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
            drawing.MarkDirtyRepaint();
        }

        /// <summary>
        /// 标记播放头在下一次 GUI 更新时重绘。
        /// </summary>
        public void MarkDirtyRepaint() => drawing.MarkDirtyRepaint();

        /// <summary>
        /// 释放 IMGUI 回调并移除播放头绘制元素。
        /// </summary>
        public void Dispose()
        {
            drawing.onGUIHandler = null;
            drawing.RemoveFromHierarchy();
        }

        #endregion

        #region 绘制

        // 将 Model 当前帧转换为固定视口坐标；超出可见范围时不钉在边缘。
        private void DrawPlayhead()
        {
            if (!visible) return;
            Rect rect = drawing.contentRect;
            if (rect.width <= 0f || rect.height <= 0f) return;
            float inset = config.PlayheadEdgeInset;
            float halfWidth = config.PlayheadHalfWidth;
            float x = mapper.FrameToViewportX(canvasModel.CurrentFrame);
            if (x < -halfWidth || x > rect.width + halfWidth) return;
            x = Mathf.Clamp(x, inset, Mathf.Max(inset, rect.width - inset));

            Handles.BeginGUI();
            Color previous = Handles.color;
            Handles.color = config.PlayheadColor;
            Handles.DrawAAConvexPolygon(
                new Vector3(x - config.PlayheadHalfWidth, 0f),
                new Vector3(x + config.PlayheadHalfWidth, 0f),
                new Vector3(x, config.PlayheadHeight));
            Handles.DrawLine(new Vector3(x, config.PlayheadLineStart), new Vector3(x, rect.height));
            Handles.color = previous;
            Handles.EndGUI();
        }

        #endregion
    }
}
#endif
