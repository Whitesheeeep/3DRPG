#if UNITY_EDITOR
using System;
using UnityEngine;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 统一执行帧坐标、内容坐标和固定视口坐标之间的整数帧换算。
    /// </summary>
    internal sealed class CoordinateMapper
    {
        private readonly CanvasModel canvasModel;

        /// <summary>
        /// 创建使用指定 Canvas 表现状态的坐标映射器。
        /// </summary>
        public CoordinateMapper(CanvasModel canvasModel) =>
            this.canvasModel = canvasModel ?? throw new ArgumentNullException(nameof(canvasModel));

        /// <summary>
        /// 把整数帧转换为 ScrollView 内容坐标。
        /// </summary>
        public float FrameToContentX(int frame) => Mathf.Max(0, frame) * canvasModel.PixelsPerFrame;

        /// <summary>
        /// 把整数帧转换为扣除水平滚动偏移后的固定视口坐标。
        /// </summary>
        public float FrameToViewportX(int frame) =>
            FrameToContentX(frame) - Mathf.Max(0f, canvasModel.ScrollOffset.x);

        /// <summary>
        /// 把固定视口坐标转换并吸附到最近的非负整数帧。
        /// </summary>
        public int ViewportXToFrame(float viewportX) => Mathf.Max(0,
            Mathf.RoundToInt((viewportX + Mathf.Max(0f, canvasModel.ScrollOffset.x)) /
                             canvasModel.PixelsPerFrame));

        /// <summary>
        /// 把 ScrollView 内容坐标转换并吸附到最近的非负整数帧。
        /// </summary>
        public int ContentXToFrame(float contentX) =>
            Mathf.Max(0, Mathf.RoundToInt(contentX / canvasModel.PixelsPerFrame));

        /// <summary>
        /// 把半开帧区间的持续帧数转换为显示宽度。
        /// </summary>
        public float DurationToWidth(int durationFrames) =>
            Mathf.Max(canvasModel.PixelsPerFrame, durationFrames * canvasModel.PixelsPerFrame);
    }
}
#endif
