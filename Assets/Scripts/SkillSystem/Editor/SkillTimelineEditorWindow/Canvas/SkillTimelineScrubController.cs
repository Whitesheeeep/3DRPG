#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 统一处理标尺与时间轴视口的点击、拖动和整数帧吸附。
    /// </summary>
    internal sealed class SkillTimelineScrubController : IDisposable
    {
        #region 依赖与状态

        private readonly VisualElement rulerLane;
        private readonly VisualElement timelineViewport;
        private readonly SkillTimelineCoordinateMapper mapper;
        private readonly Func<float> getHorizontalOffset;
        private readonly Func<int> getMaximumFrame;
        private SkillTimelineEditorViewModel viewModel;
        private VisualElement captureElement;
        private int pointerId = -1;
        private int lastFrame = -1;

        #endregion

        /// <summary>
        /// 创建同时覆盖标尺和空白轨道区域、支持空 Config 虚拟帧的播放头拖动控制器。
        /// </summary>
        public SkillTimelineScrubController(VisualElement rulerLane, VisualElement timelineViewport,
            SkillTimelineCoordinateMapper mapper, Func<float> getHorizontalOffset,
            Func<int> getMaximumFrame)
        {
            this.rulerLane = rulerLane ?? throw new ArgumentNullException(nameof(rulerLane));
            this.timelineViewport = timelineViewport ?? throw new ArgumentNullException(nameof(timelineViewport));
            this.mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            this.getHorizontalOffset = getHorizontalOffset ?? throw new ArgumentNullException(nameof(getHorizontalOffset));
            this.getMaximumFrame = getMaximumFrame ?? throw new ArgumentNullException(nameof(getMaximumFrame));
        }

        /// <summary>
        /// 绑定语义帧写入并注册两个交互区域。
        /// </summary>
        public void Bind(SkillTimelineEditorViewModel model)
        {
            viewModel = model ?? throw new ArgumentNullException(nameof(model));
            RegisterSurface(rulerLane);
            RegisterSurface(timelineViewport);
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
            UnregisterSurface(timelineViewport);
            viewModel = null;
        }

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

        // 将世界坐标转换到右侧视口，加上真实水平偏移后吸附并夹紧到当前可用帧范围。
        private void SubmitFrame(Vector2 worldPosition)
        {
            if (viewModel == null) return;
            Vector2 local = timelineViewport.WorldToLocal(worldPosition);
            int frame = mapper.ViewportXToFrame(local.x, getHorizontalOffset());
            frame = Mathf.Clamp(frame, 0, Mathf.Max(0, getMaximumFrame()));
            if (frame == lastFrame) return;
            lastFrame = frame;
            viewModel.SetCurrentFrame(frame);
        }

        #endregion
    }
}
#endif