#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 统一处理标尺与时间轴视口的点击、拖动和整数帧吸附。
    /// </summary>
    internal sealed class ScrubController : IDisposable
    {
        #region 依赖与状态

        private readonly VisualElement rulerLane;
        private readonly ScrollView timelineScroll;
        private readonly CanvasModel canvasModel;
        private readonly CoordinateMapper mapper;
        private EditorViewModel viewModel;
        private VisualElement captureElement;
        private int pointerId = -1;
        private int lastFrame = -1;

        #endregion

        #region 生命周期

        /// <summary>
        /// 创建直接读取右侧 ScrollView 和 CanvasModel 实时状态的播放头拖动控制器。
        /// </summary>
        public ScrubController(VisualElement rulerLane, ScrollView timelineScroll,
            CanvasModel canvasModel, CoordinateMapper mapper)
        {
            this.rulerLane = rulerLane ?? throw new ArgumentNullException(nameof(rulerLane));
            this.timelineScroll = timelineScroll ?? throw new ArgumentNullException(nameof(timelineScroll));
            this.canvasModel = canvasModel ?? throw new ArgumentNullException(nameof(canvasModel));
            this.mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        }

        /// <summary>
        /// 绑定语义帧写入并注册标尺与右侧时间视口两个交互区域。
        /// </summary>
        public void Bind(EditorViewModel model)
        {
            viewModel = model ?? throw new ArgumentNullException(nameof(model));
            RegisterSurface(rulerLane);
            RegisterSurface(timelineScroll.contentViewport);
        }

        /// <summary>
        /// 取消正在进行的 Scrub，并安全释放 Pointer Capture。
        /// </summary>
        public void Cancel()
        {
            VisualElement captured = captureElement;
            int capturedPointer = pointerId;
            captureElement = null;
            pointerId = -1;
            lastFrame = -1;
            if (captured != null && capturedPointer >= 0 && captured.HasPointerCapture(capturedPointer))
                captured.ReleasePointer(capturedPointer);
        }

        /// <summary>
        /// 注销交互事件并释放当前捕获状态。
        /// </summary>
        public void Dispose()
        {
            Cancel();
            UnregisterSurface(rulerLane);
            UnregisterSurface(timelineScroll.contentViewport);
            viewModel = null;
        }

        #endregion

        #region 事件注册

        // 为一个固定视口区域注册完整的 Scrub 生命周期事件。
        private void RegisterSurface(VisualElement surface)
        {
            surface.RegisterCallback<PointerDownEvent>(OnPointerDown);
            surface.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            surface.RegisterCallback<PointerUpEvent>(OnPointerUp);
            surface.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
        }

        // 注销一个固定视口区域的 Scrub 生命周期事件。
        private void UnregisterSurface(VisualElement surface)
        {
            surface.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            surface.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            surface.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            surface.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
        }

        #endregion

        #region Pointer 交互

        // 左键按下时立即吸附到帧，并由实际命中的固定区域捕获指针。
        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || evt.currentTarget is not VisualElement surface) return;
            captureElement = surface;
            pointerId = evt.pointerId;
            surface.CapturePointer(pointerId);
            SubmitFrame(evt.position);
            evt.StopPropagation();
        }

        // 拖动期间仅在吸附后的整数帧变化时提交 Seek。
        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (pointerId != evt.pointerId || captureElement == null) return;
            SubmitFrame(evt.position);
            evt.StopPropagation();
        }

        // 松开指针时提交最后位置并结束捕获。
        private void OnPointerUp(PointerUpEvent evt)
        {
            if (pointerId != evt.pointerId || captureElement == null) return;
            SubmitFrame(evt.position);
            Cancel();
            evt.StopPropagation();
        }

        // 捕获意外丢失时只清空交互状态，不再次释放指针。
        private void OnPointerCaptureOut(PointerCaptureOutEvent _)
        {
            captureElement = null;
            pointerId = -1;
            lastFrame = -1;
        }

        // 把世界坐标转换到右侧视口，并使用真实水平偏移和 Model 最大帧完成吸附夹紧。
        private void SubmitFrame(Vector2 worldPosition)
        {
            if (viewModel == null) return;
            Vector2 local = timelineScroll.contentViewport.WorldToLocal(worldPosition);
            int frame = mapper.ViewportXToFrame(local.x);
            frame = Mathf.Clamp(frame, 0, canvasModel.MaximumFrame);
            if (frame == lastFrame) return;
            lastFrame = frame;
            viewModel.SetCurrentFrame(frame);
        }

        #endregion
    }
}
#endif
