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
    internal sealed class SkillTimelinePlayheadView : IDisposable
    {
        private readonly IMGUIContainer drawing;
        private readonly SkillTimelineCoordinateMapper mapper;
        private readonly SkillTimelineEditorConfig config;
        private readonly Func<int> getCurrentFrame;
        private readonly Func<float> getHorizontalOffset;
        private bool visible = true;

        /// <summary>
        /// 创建使用 Editor-only Config 控制形状和颜色的播放头绘制层。
        /// </summary>
        public SkillTimelinePlayheadView(VisualElement overlay, SkillTimelineCoordinateMapper mapper,
            SkillTimelineEditorConfig config, Func<int> getCurrentFrame, Func<float> getHorizontalOffset)
        {
            this.mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.getCurrentFrame = getCurrentFrame ?? throw new ArgumentNullException(nameof(getCurrentFrame));
            this.getHorizontalOffset = getHorizontalOffset ?? throw new ArgumentNullException(nameof(getHorizontalOffset));
            drawing = new IMGUIContainer(DrawPlayhead) { name = "PlayheadDrawing", pickingMode = PickingMode.Ignore };
            drawing.AddToClassList("playhead-drawing");
            overlay.Add(drawing);
        }

        /// <summary>
        /// 根据是否存在有效技能配置显示或隐藏播放头。
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

        // 将当前整数帧转换为固定视口坐标；仅在可见范围内绘制，避免把远端帧钉在边缘。
        private void DrawPlayhead()
        {
            if (!visible) return;
            Rect rect = drawing.contentRect;
            if (rect.width <= 0f || rect.height <= 0f) return;
            float inset = config.PlayheadEdgeInset;
            float halfWidth = config.PlayheadHalfWidth;
            float x = mapper.FrameToViewportX(getCurrentFrame(), getHorizontalOffset());
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
    }
}
#endif