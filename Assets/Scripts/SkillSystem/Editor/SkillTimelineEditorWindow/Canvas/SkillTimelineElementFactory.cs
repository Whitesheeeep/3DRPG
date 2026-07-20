#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 缓存动态时间轴 UXML 模板，并根据具体 ViewData 类型创建对应的小型 View。
    /// </summary>
    internal sealed class SkillTimelineElementFactory
    {
        private const string TemplateRoot = "Assets/Scripts/SkillSystem/Editor/SkillTimelineEditorWindow/Templates/";
        private readonly SkillTimelineCoordinateMapper mapper;
        private readonly SkillTimelineEditorConfig config;
        private readonly Dictionary<string, VisualTreeAsset> templates = new();
        private readonly Dictionary<Type, Func<SkillTimelineTrackViewData, SkillTimelineItemViewData, SkillTimelineItemView>> itemFactories;

        /// <summary>
        /// 创建元素工厂并注册所有具体 Item View 类型。
        /// </summary>
        public SkillTimelineElementFactory(SkillTimelineCoordinateMapper mapper, SkillTimelineEditorConfig config)
        {
            this.mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            itemFactories = new Dictionary<Type, Func<SkillTimelineTrackViewData, SkillTimelineItemViewData, SkillTimelineItemView>>
            {
                [typeof(AnimationClipTimelineItemViewData)] = (track, item) =>
                    new AnimationClipTimelineItemView(track, (AnimationClipTimelineItemViewData)item,
                        Instantiate("SkillTimelineClipItem.uxml", "ClipRoot"), mapper),
                [typeof(VfxClipTimelineItemViewData)] = (track, item) =>
                    new VfxClipTimelineItemView(track, (VfxClipTimelineItemViewData)item,
                        Instantiate("SkillTimelineClipItem.uxml", "ClipRoot"), mapper),
                [typeof(EventMarkerTimelineItemViewData)] = (track, item) =>
                    new EventMarkerTimelineItemView(track, (EventMarkerTimelineItemViewData)item,
                        Instantiate("SkillTimelineMarkerItem.uxml", "MarkerRoot"), mapper, config)
            };
        }

        /// <summary>
        /// 创建轨道分组左侧标题行。
        /// </summary>
        public VisualElement CreateGroupHeader() =>
            Instantiate("SkillTimelineGroupHeaderRow.uxml", "GroupHeaderRoot");

        /// <summary>
        /// 创建普通轨道左侧标题行。
        /// </summary>
        public VisualElement CreateTrackHeader() =>
            Instantiate("SkillTimelineTrackHeaderRow.uxml", "TrackHeaderRoot");

        /// <summary>
        /// 创建右侧轨道背景行。
        /// </summary>
        public VisualElement CreateLaneBackground() =>
            Instantiate("SkillTimelineLaneBackgroundRow.uxml", "LaneBackgroundRoot");

        /// <summary>
        /// 创建右侧内容承载行。
        /// </summary>
        public VisualElement CreateLaneItemRow() =>
            Instantiate("SkillTimelineLaneItemRow.uxml", "LaneItemRoot");

        /// <summary>
        /// 根据具体 Item ViewData 类型创建动画、特效或事件视图。
        /// </summary>
        public SkillTimelineItemView CreateItemView(
            SkillTimelineTrackViewData track, SkillTimelineItemViewData item)
        {
            if (!itemFactories.TryGetValue(item.GetType(), out var factory))
                throw new InvalidOperationException($"未注册时间轴内容视图：{item.GetType().FullName}");
            return factory(track, item);
        }

        // 加载并缓存模板，随后移除 TemplateContainer 包装以保持 USS 行布局稳定。
        private VisualElement Instantiate(string fileName, string rootName)
        {
            if (!templates.TryGetValue(fileName, out VisualTreeAsset template))
            {
                string path = TemplateRoot + fileName;
                template = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
                if (template == null) throw new InvalidOperationException($"缺少时间轴 UXML 模板：{path}");
                templates[fileName] = template;
            }

            TemplateContainer container = template.Instantiate();
            VisualElement element = container.Q<VisualElement>(rootName);
            if (element == null) throw new InvalidOperationException($"模板 {fileName} 缺少节点 {rootName}。");
            element.RemoveFromHierarchy();
            return element;
        }
    }

    /// <summary>
    /// 封装一个时间轴内容元素的公共几何刷新和选中表现。
    /// </summary>
    internal abstract class SkillTimelineItemView
    {
        protected readonly SkillTimelineCoordinateMapper Mapper;
        public VisualElement Element { get; }
        public SkillTimelineTrackViewData Track { get; }
        public SkillTimelineItemViewData Item { get; }
        public VisualElement ResizeLeft { get; protected set; }
        public VisualElement ResizeRight { get; protected set; }

        /// <summary>
        /// 创建内容元素并立即应用权威帧位置。
        /// </summary>
        protected SkillTimelineItemView(SkillTimelineTrackViewData track, SkillTimelineItemViewData item,
            VisualElement element, SkillTimelineCoordinateMapper mapper)
        {
            Track = track;
            Item = item;
            Element = element;
            Mapper = mapper;
            Element.userData = this;
        }

        /// <summary>
        /// 根据整数帧草稿刷新元素位置和持续宽度，不修改资产。
        /// </summary>
        public abstract void RefreshGeometry(int startFrame, int durationFrames);

        /// <summary>
        /// 切换元素选中状态 USS class。
        /// </summary>
        public void SetSelected(bool selected) => Element.EnableInClassList("is-selected", selected);
    }

    /// <summary>
    /// 封装可移动和双侧裁剪的 Clip 元素公共行为。
    /// </summary>
    internal abstract class SkillTimelineClipItemView : SkillTimelineItemView
    {
        /// <summary>
        /// 创建 Clip 元素并绑定左右裁剪手柄。
        /// </summary>
        protected SkillTimelineClipItemView(SkillTimelineTrackViewData track, SkillTimelineItemViewData item,
            VisualElement element, SkillTimelineCoordinateMapper mapper) : base(track, item, element, mapper)
        {
            Element.Q<Label>().text = item.DisplayName;
            ResizeLeft = Element.Q<VisualElement>("ResizeLeft");
            ResizeRight = Element.Q<VisualElement>("ResizeRight");
            RefreshGeometry(item.StartFrame, item.DurationFrames);
        }

        /// <summary>
        /// 将半开帧区间转换为 Clip 的内容坐标和宽度。
        /// </summary>
        public override void RefreshGeometry(int startFrame, int durationFrames)
        {
            Element.style.left = Mapper.FrameToContentX(startFrame);
            Element.style.width = Mapper.DurationToWidth(durationFrames);
        }
    }

    /// <summary>
    /// 显示动画 Clip 的时间轴内容视图。
    /// </summary>
    internal sealed class AnimationClipTimelineItemView : SkillTimelineClipItemView
    {
        /// <summary>
        /// 创建动画 Clip 视图。
        /// </summary>
        public AnimationClipTimelineItemView(SkillTimelineTrackViewData track,
            AnimationClipTimelineItemViewData item, VisualElement element, SkillTimelineCoordinateMapper mapper)
            : base(track, item, element, mapper) => Element.AddToClassList("animation-clip");
    }

    /// <summary>
    /// 显示特效 Clip 的时间轴内容视图。
    /// </summary>
    internal sealed class VfxClipTimelineItemView : SkillTimelineClipItemView
    {
        /// <summary>
        /// 创建特效 Clip 视图。
        /// </summary>
        public VfxClipTimelineItemView(SkillTimelineTrackViewData track,
            VfxClipTimelineItemViewData item, VisualElement element, SkillTimelineCoordinateMapper mapper)
            : base(track, item, element, mapper) => Element.AddToClassList("vfx-clip");
    }

    /// <summary>
    /// 显示不可裁剪的事件 Marker 时间轴视图。
    /// </summary>
    internal sealed class EventMarkerTimelineItemView : SkillTimelineItemView
    {
        private readonly SkillTimelineEditorConfig config;

        /// <summary>
        /// 创建事件 Marker 视图并应用 Editor 配置宽度。
        /// </summary>
        public EventMarkerTimelineItemView(SkillTimelineTrackViewData track,
            EventMarkerTimelineItemViewData item, VisualElement element,
            SkillTimelineCoordinateMapper mapper, SkillTimelineEditorConfig config)
            : base(track, item, element, mapper)
        {
            this.config = config;
            Element.tooltip = item.DisplayName;
            Element.style.width = config.MarkerWidth;
            RefreshGeometry(item.StartFrame, item.DurationFrames);
        }

        /// <summary>
        /// 将事件帧放到 Marker 图形中心对应的内容坐标。
        /// </summary>
        public override void RefreshGeometry(int startFrame, int durationFrames) =>
            Element.style.left = Mapper.FrameToContentX(startFrame) - config.MarkerWidth * 0.5f;
    }
}
#endif