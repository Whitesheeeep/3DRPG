using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace WS_Modules.UIToolkitExtensions.Editor
{
    /// <summary>
    /// 提供可选固定 Pane 尺寸约束与会话恢复能力的双面板分隔视图。
    /// 未调用 <see cref="ConfigureFixedPane"/> 时保持 Unity 原生 <see cref="TwoPaneSplitView"/> 行为。
    /// </summary>
    public class CustomTwoPanelSplitView : TwoPaneSplitView
    {
        #region 常量与字段

        private const string DragLineAnchorClassName = "unity-two-pane-split-view__dragline-anchor";
        private const int InvalidPointerId = -1;

        // IVisualElementScheduleItem 是 UIToolkit 中用于调度定时任务的接口
        private IVisualElementScheduledItem restoreItem;
        private string sessionStateKey;
        // 固定 Pane 的主轴的最小、最大、现在的主轴长度
        private float minimumFixedPaneDimension;
        private float maximumFixedPaneDimension;
        private float currentFixedPaneDimension;
        // 用于判断是否设置了最小、最大 Panel 长度，没有设置则不进行拖拽限制
        private bool fixedPaneConfigured;
        // 用于判断是否已经从 SessionState 恢复了尺寸，避免在布局未就绪时覆盖会话宽度
        private bool dimensionRestored;
        // 是否正在设置 Pane 的主轴长度
        private bool applyingDimension;

        // 拖拽状态维护
        private int activePointerId = InvalidPointerId;
        private float dragStartPointerPosition;
        private float dragStartDimension;

        #endregion

        #region UXML 支持

        /// <summary>
        /// 支持在 UXML 中创建 <see cref="CustomTwoPanelSplitView"/>。
        /// </summary>
        public new class UxmlFactory : UxmlFactory<CustomTwoPanelSplitView, UxmlTraits>
        {
        }

        /// <summary>
        /// 复用 Unity 原生双面板视图的 UXML 属性定义。
        /// </summary>
        public new class UxmlTraits : TwoPaneSplitView.UxmlTraits
        {
        }

        #endregion

        #region 生命周期

        /// <summary>
        /// 创建一个可通过 UXML 初始化的双面板分隔视图。
        /// </summary>
        public CustomTwoPanelSplitView()
        {
            RegisterInternalCallbacks();
        }

        /// <summary>
        /// 使用固定 Pane、初始尺寸和布局方向创建双面板分隔视图。
        /// </summary>
        /// <param name="fixedPaneIndex">固定 Pane 的子元素索引，只允许为 0 或 1。</param>
        /// <param name="fixedPaneInitialDimension">固定 Pane 的初始主轴尺寸，单位为像素。</param>
        /// <param name="orientation">两个 Pane 的排列方向。</param>
        public CustomTwoPanelSplitView(int fixedPaneIndex, int fixedPaneInitialDimension,
            TwoPaneSplitViewOrientation orientation)
            : base(fixedPaneIndex, fixedPaneInitialDimension, orientation)
        {
            RegisterInternalCallbacks();
        }

        #endregion

        #region 公开配置

        /// <summary>
        /// 配置固定 Pane 的最小、默认和最大主轴尺寸，并可选择通过 SessionState 恢复当前 Unity 会话宽度。
        /// 重复调用会替换原配置，不会重复注册 Pointer 或 Geometry 回调。
        /// </summary>
        /// <param name="minimumDimension">允许的最小主轴尺寸，单位为像素。</param>
        /// <param name="defaultDimension">没有会话记录时使用的默认主轴尺寸，单位为像素。</param>
        /// <param name="maximumDimension">允许的最大主轴尺寸，单位为像素。</param>
        /// <param name="sessionStateKey">可选的 SessionState 键；为空时不恢复或保存会话尺寸。</param>
        /// <exception cref="ArgumentOutOfRangeException">尺寸不是有限正数、范围倒置或默认尺寸越界时抛出。</exception>
        public void ConfigureFixedPane(float minimumDimension, float defaultDimension,
            float maximumDimension, string sessionStateKey = null)
        {
            ValidateDimensions(minimumDimension, defaultDimension, maximumDimension);

            minimumFixedPaneDimension = minimumDimension;
            maximumFixedPaneDimension = maximumDimension;
            this.sessionStateKey = string.IsNullOrWhiteSpace(sessionStateKey) ? null : sessionStateKey;
            currentFixedPaneDimension = this.sessionStateKey == null
                ? defaultDimension
                : SessionState.GetFloat(this.sessionStateKey, defaultDimension);
            currentFixedPaneDimension = Mathf.Clamp(
                currentFixedPaneDimension, minimumDimension, maximumDimension);
            fixedPaneConfigured = true;
            dimensionRestored = false;
            ScheduleDimensionRestore();
        }

        #endregion

        #region 回调注册

        // 控件生命周期内只注册一次通用回调；未配置尺寸约束时所有处理器都会直接退出。
        private void RegisterInternalCallbacks()
        {
            RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            RegisterCallback<PointerDownEvent>(OnDragLinePointerDown, TrickleDown.TrickleDown);
            RegisterCallback<PointerMoveEvent>(OnDragLinePointerMove, TrickleDown.TrickleDown);
            RegisterCallback<PointerUpEvent>(OnDragLinePointerUp, TrickleDown.TrickleDown);
            RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
        }

        // 重新挂载到 Panel 后再次应用最后一次合法尺寸，兼容窗口关闭后复用同一 VisualElement。
        private void OnAttachToPanel(AttachToPanelEvent _)
        {
            if (fixedPaneConfigured) ScheduleDimensionRestore();
        }

        // 脱离 Panel 时停止延迟任务并释放 Pointer Capture，避免捕获状态泄漏到其他窗口。
        private void OnDetachFromPanel(DetachFromPanelEvent _)
        {
            restoreItem?.Pause();
            restoreItem = null;
            CancelActiveDrag();
        }

        #endregion

        #region 尺寸恢复与约束

        // 延迟到 UXML 子 Pane 和 SplitView 内部 DragLine 完成初始化后再应用权威尺寸。
        private void ScheduleDimensionRestore()
        {
            restoreItem?.Pause();
            restoreItem = schedule.Execute(RestoreConfiguredDimension);
        }

        // 应用 Config 或 SessionState 提供的尺寸；布局未就绪时继续延迟，禁止提前覆盖会话宽度。
        private void RestoreConfiguredDimension()
        {
            restoreItem = null;
            if (!fixedPaneConfigured) return;
            if (fixedPane == null || flexedPane == null || GetSplitDimension() <= 0f)
            {
                ScheduleDimensionRestore();
                return;
            }

            ApplyFixedPaneDimension(currentFixedPaneDimension, false);
            dimensionRestored = true;
        }

        // 统一设置固定 Pane 与 DragLine，确保两者使用完全相同的夹紧结果。
        private float ApplyFixedPaneDimension(float requestedDimension, bool persist)
        {
            if (!fixedPaneConfigured || fixedPane == null) return currentFixedPaneDimension;

            float clampedDimension = ClampFixedPaneDimension(requestedDimension);
            applyingDimension = true;
            if (orientation == TwoPaneSplitViewOrientation.Horizontal)
                fixedPane.style.width = clampedDimension;
            else
                fixedPane.style.height = clampedDimension;
            SynchronizeDragLine(clampedDimension);
            applyingDimension = false;

            currentFixedPaneDimension = clampedDimension;
            if (persist) PersistFixedPaneDimension(clampedDimension);
            return clampedDimension;
        }

        // 同时考虑配置范围和 FlexedPane 最小空间；窗口过小时允许固定 Pane 临时小于配置最小值。
        private float ClampFixedPaneDimension(float requestedDimension)
        {
            float splitDimension = GetSplitDimension();
            if (splitDimension <= 0f)
                return Mathf.Clamp(requestedDimension, minimumFixedPaneDimension, maximumFixedPaneDimension);

            float availableMaximum = Mathf.Max(0f,
                splitDimension - GetFlexedPaneMinimumDimension() -
                GetMainAxisMargins(fixedPane) - GetMainAxisMargins(flexedPane));
            float effectiveMaximum = Mathf.Min(maximumFixedPaneDimension, availableMaximum);
            float effectiveMinimum = Mathf.Min(minimumFixedPaneDimension, effectiveMaximum);
            return Mathf.Clamp(requestedDimension, effectiveMinimum, effectiveMaximum);
        }

        // 将内部 DragLine Anchor 移到固定 Pane 的实际边界，避免仅设置 Pane 最大宽度造成视觉分离。
        private void SynchronizeDragLine(float fixedPaneDimension)
        {
            VisualElement dragLineAnchor = GetDragLineAnchor();
            float splitDimension = GetSplitDimension();
            if (dragLineAnchor == null || splitDimension <= 0f) return;

            float fixedPaneMargins = GetMainAxisMargins(fixedPane);
            float offset = fixedPaneIndex == 0
                ? fixedPaneDimension + fixedPaneMargins
                : splitDimension - fixedPaneDimension - fixedPaneMargins;
            if (orientation == TwoPaneSplitViewOrientation.Horizontal)
                dragLineAnchor.style.left = offset;
            else
                dragLineAnchor.style.top = offset;
        }

        // 返回 SplitView 当前主轴尺寸；首次布局中的 NaN 或 Infinity 统一视为未就绪。
        private float GetSplitDimension()
        {
            float dimension = orientation == TwoPaneSplitViewOrientation.Horizontal
                ? resolvedStyle.width
                : resolvedStyle.height;
            return NormalizeFiniteDimension(dimension);
        }

        // 读取可伸缩 Pane 的主轴最小尺寸，保证拖拽不会挤占另一个区域的最低操作空间。
        private float GetFlexedPaneMinimumDimension()
        {
            if (flexedPane == null) return 0f;
            StyleFloat minimum = orientation == TwoPaneSplitViewOrientation.Horizontal
                ? flexedPane.resolvedStyle.minWidth
                : flexedPane.resolvedStyle.minHeight;
            return minimum.keyword == StyleKeyword.Undefined
                ? NormalizeFiniteDimension(minimum.value)
                : 0f;
        }

        // 汇总指定 Pane 在主轴方向上的两个外边距，空 Pane 或未解析数值按零处理。
        private float GetMainAxisMargins(VisualElement pane)
        {
            if (pane == null) return 0f;
            float margins = orientation == TwoPaneSplitViewOrientation.Horizontal
                ? pane.resolvedStyle.marginLeft + pane.resolvedStyle.marginRight
                : pane.resolvedStyle.marginTop + pane.resolvedStyle.marginBottom;
            return NormalizeFiniteDimension(margins);
        }

        // 获取当前 SplitView 自己的 DragLine Anchor，排除嵌套 SplitView 创建的同名元素。
        private VisualElement GetDragLineAnchor()
        {
            VisualElement ownedAnchor = null;
            this.Query<VisualElement>(className: DragLineAnchorClassName).ForEach(candidate =>
            {
                if (ownedAnchor == null && IsOwnedDragLineAnchor(candidate)) ownedAnchor = candidate;
            });
            return ownedAnchor;
        }

        // 候选 DragLine 最近所属的 TwoPaneSplitView 必须是当前实例，避免外层抢占内层拖拽事件。
        private bool IsOwnedDragLineAnchor(VisualElement candidate)
        {
            if (candidate == null) return false;
            for (VisualElement ancestor = candidate.parent; ancestor != null; ancestor = ancestor.parent)
            {
                if (ancestor is TwoPaneSplitView splitView) return ReferenceEquals(splitView, this);
            }

            return false;
        }

        // 过滤布局早期可能出现的非有限数值，并保证几何尺寸不为负数。
        private static float NormalizeFiniteDimension(float value) =>
            float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Max(0f, value);

        // 将最终合法尺寸写入当前 Unity 会话；没有配置键时保持纯视觉控件行为。
        private void PersistFixedPaneDimension(float dimension)
        {
            if (sessionStateKey != null) SessionState.SetFloat(sessionStateKey, dimension);
        }

        // 拒绝无效尺寸配置，避免 NaN 或倒置区间进入后续布局计算。
        private static void ValidateDimensions(float minimumDimension, float defaultDimension,
            float maximumDimension)
        {
            if (!IsFinitePositive(minimumDimension))
                throw new ArgumentOutOfRangeException(nameof(minimumDimension), "最小尺寸必须是有限正数。");
            if (!IsFinitePositive(defaultDimension))
                throw new ArgumentOutOfRangeException(nameof(defaultDimension), "默认尺寸必须是有限正数。");
            if (!IsFinitePositive(maximumDimension))
                throw new ArgumentOutOfRangeException(nameof(maximumDimension), "最大尺寸必须是有限正数。");
            if (maximumDimension < minimumDimension)
                throw new ArgumentOutOfRangeException(nameof(maximumDimension), "最大尺寸不能小于最小尺寸。");
            if (defaultDimension < minimumDimension || defaultDimension > maximumDimension)
                throw new ArgumentOutOfRangeException(nameof(defaultDimension), "默认尺寸必须位于最小值和最大值之间。");
        }

        // 判断尺寸是否为可参与 UI Toolkit 布局的有限正数。
        private static bool IsFinitePositive(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;

        #endregion

        #region Geometry 与拖拽事件

        // SplitView 尺寸变化后重新夹紧当前尺寸，并同步内部 DragLine 坐标。
        private void OnGeometryChanged(GeometryChangedEvent _)
        {
            if (!fixedPaneConfigured || !dimensionRestored || applyingDimension || fixedPane == null) return;
            ApplyFixedPaneDimension(currentFixedPaneDimension, true);
        }

        // 在下沉阶段接管 DragLine，阻止 Unity 内置 Resizer 绕过自定义最大尺寸。
        private void OnDragLinePointerDown(PointerDownEvent evt)
        {
            if (!fixedPaneConfigured || !dimensionRestored || activePointerId != InvalidPointerId || evt.button != 0 ||
                !IsDragLineTarget(evt.target) || fixedPane == null) return;

            activePointerId = evt.pointerId;
            dragStartPointerPosition = GetPointerPosition(evt.position);
            dragStartDimension = GetResolvedFixedPaneDimension();
            this.CapturePointer(activePointerId);
            evt.StopImmediatePropagation();
        }

        // 将 Pointer 位移换算为固定 Pane 尺寸；越界时重置拖拽基线，避免反向拖动产生空行程。
        private void OnDragLinePointerMove(PointerMoveEvent evt)
        {
            if (evt.pointerId != activePointerId || !this.HasPointerCapture(activePointerId)) return;

            float pointerPosition = GetPointerPosition(evt.position);
            float pointerDelta = pointerPosition - dragStartPointerPosition;
            float direction = fixedPaneIndex == 0 ? 1f : -1f;
            float requestedDimension = dragStartDimension + pointerDelta * direction;
            float clampedDimension = ApplyFixedPaneDimension(requestedDimension, true);
            if (!Mathf.Approximately(requestedDimension, clampedDimension))
            {
                dragStartPointerPosition = pointerPosition;
                dragStartDimension = clampedDimension;
            }
            evt.StopImmediatePropagation();
        }

        // 完成自定义拖拽并保存最终尺寸；该表现层操作不产生任何资产 Undo。
        private void OnDragLinePointerUp(PointerUpEvent evt)
        {
            if (evt.pointerId != activePointerId) return;
            ApplyFixedPaneDimension(GetResolvedFixedPaneDimension(), true);
            CancelActiveDrag();
            evt.StopImmediatePropagation();
        }

        // 捕获意外丢失时只清理 Pointer 状态，已经应用的 Pane 尺寸保持不变。
        private void OnPointerCaptureOut(PointerCaptureOutEvent _)
        {
            activePointerId = InvalidPointerId;
        }

        // 判断事件目标是否是内部 DragLine Anchor 或其可见子元素。
        private bool IsDragLineTarget(IEventHandler target)
        {
            VisualElement dragLineAnchor = GetDragLineAnchor();
            VisualElement targetElement = target as VisualElement;
            return dragLineAnchor != null && targetElement != null &&
                   (targetElement == dragLineAnchor || dragLineAnchor.Contains(targetElement));
        }

        // 按 SplitView 主轴读取 Pointer 的 Panel 坐标。
        private float GetPointerPosition(Vector3 position) =>
            orientation == TwoPaneSplitViewOrientation.Horizontal ? position.x : position.y;

        // 读取固定 Pane 当前解析尺寸，避免拖拽起点依赖可能尚未刷新的 Style 值。
        private float GetResolvedFixedPaneDimension()
        {
            if (fixedPane == null) return currentFixedPaneDimension;
            float dimension = orientation == TwoPaneSplitViewOrientation.Horizontal
                ? fixedPane.resolvedStyle.width
                : fixedPane.resolvedStyle.height;
            dimension = NormalizeFiniteDimension(dimension);
            return dimension > 0f ? dimension : currentFixedPaneDimension;
        }

        // 释放当前 Pointer Capture；先清空 ID，避免 CaptureOut 回调重复清理。
        private void CancelActiveDrag()
        {
            if (activePointerId == InvalidPointerId) return;
            int pointerId = activePointerId;
            activePointerId = InvalidPointerId;
            if (panel != null && this.HasPointerCapture(pointerId)) this.ReleasePointer(pointerId);
        }

        #endregion
    }
}
