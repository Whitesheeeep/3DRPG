#if UNITY_EDITOR
using System;
using UnityEngine.UIElements;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 作为 Canvas MVC 的被动 View，只负责查询和暴露稳定 UXML 元素。
    /// </summary>
    internal sealed class CanvasView : IDisposable
    {
        #region 依赖

        private readonly VisualElement root;

        #endregion

        #region UXML 元素

        internal VisualElement TimelinePanel { get; private set; }
        internal VisualElement RulerLane { get; private set; }
        internal ScrollView TrackHeaderScroll { get; private set; }
        internal VisualElement TrackHeaderContent { get; private set; }
        internal VisualElement TrackHeaderRows { get; private set; }
        internal ScrollView TimelineScroll { get; private set; }
        internal VisualElement TimelineContent { get; private set; }
        internal VisualElement LaneBackgroundRows { get; private set; }
        internal VisualElement LaneItemRows { get; private set; }
        internal VisualElement GridHost { get; private set; }
        internal VisualElement PlayheadOverlay { get; private set; }

        #endregion

        /// <summary>
        /// 创建只负责查询 Canvas UXML 元素的被动 View。
        /// </summary>
        public CanvasView(VisualElement root)
        {
            this.root = root ?? throw new ArgumentNullException(nameof(root));
        }

        #region 初始化与释放

        /// <summary>
        /// 查询稳定 UXML 元素并准备接收 CanvasController 的视觉更新。
        /// </summary>
        public void Initialize()
        {
            Dispose();
            QueryElements();
            TimelinePanel.focusable = true;
        }

        /// <summary>
        /// 清空本次 UXML 查询得到的元素引用；控制器生命周期由上层组合者负责。
        /// </summary>
        public void Dispose()
        {
            ClearElementReferences();
        }

        #endregion

        #region 元素查询

        // 查询主 UXML 中稳定存在的节点；动态 Track 与 Item 仍由 Controller 组合的行 View 创建。
        private void QueryElements()
        {
            TimelinePanel = RequireElement<VisualElement>("TimelinePanel");
            RulerLane = RequireElement<VisualElement>("RulerLane");
            TrackHeaderScroll = RequireElement<ScrollView>("TrackHeaderScroll");
            TrackHeaderContent = RequireElement<VisualElement>("TrackHeaderContent");
            TrackHeaderRows = RequireElement<VisualElement>("TrackHeaderRows");
            TimelineScroll = RequireElement<ScrollView>("TimelineScroll");
            TimelineContent = RequireElement<VisualElement>("TimelineContent");
            LaneBackgroundRows = RequireElement<VisualElement>("TimelineLaneBackgroundRows");
            LaneItemRows = RequireElement<VisualElement>("TimelineLaneRows");
            GridHost = RequireElement<VisualElement>("TimelineGridHost");
            PlayheadOverlay = RequireElement<VisualElement>("PlayheadOverlay");
        }

        // 对缺失的关键 UXML 节点立即报出明确错误，避免后续控制器以空引用失败。
        private T RequireElement<T>(string elementName) where T : VisualElement
        {
            T element = root.Q<T>(elementName);
            return element ?? throw new InvalidOperationException($"时间轴 UXML 缺少节点：{elementName}");
        }

        // 关闭或重新绑定时清除旧 VisualTree 中的引用，防止 Domain Reload 后误用。
        private void ClearElementReferences()
        {
            TimelinePanel = null;
            RulerLane = null;
            TrackHeaderScroll = null;
            TrackHeaderContent = null;
            TrackHeaderRows = null;
            TimelineScroll = null;
            TimelineContent = null;
            LaneBackgroundRows = null;
            LaneItemRows = null;
            GridHost = null;
            PlayheadOverlay = null;
        }

        #endregion
    }
}
#endif
