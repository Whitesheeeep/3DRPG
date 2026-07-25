#if UNITY_EDITOR
using System;
using RPG.SkillSystem.Editor;
using UnityEngine.UIElements;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 封装一个时间轴内容元素的公共几何刷新和选中表现。
    /// </summary>
    internal abstract class ItemView
    {
        protected readonly CoordinateMapper Mapper;
        public VisualElement Element { get; }
        public TrackViewData Track { get; }
        public ItemViewData Item { get; }
        public VisualElement ResizeLeft { get; protected set; }
        public VisualElement ResizeRight { get; protected set; }

        // 关联权威 ViewData 与元素引用，不修改任何技能资产。
        protected ItemView(TrackViewData track, ItemViewData item,
            VisualElement element, CoordinateMapper mapper)
        {
            Track = track ?? throw new ArgumentNullException(nameof(track));
            Item = item ?? throw new ArgumentNullException(nameof(item));
            Element = element ?? throw new ArgumentNullException(nameof(element));
            Mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
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
    internal abstract class ClipItemView : ItemView
    {
        // 创建 Clip 元素并绑定左右裁剪手柄。
        protected ClipItemView(TrackViewData track, ItemViewData item,
            VisualElement element, CoordinateMapper mapper) : base(track, item, element, mapper)
        {
            Label label = Element as Label ?? Element.Q<Label>();
            if (label != null) label.text = item.DisplayName;
            ResizeLeft = Element.Q<VisualElement>("ResizeLeft");
            ResizeRight = Element.Q<VisualElement>("ResizeRight");
            RefreshGeometry(item.StartFrame, item.DurationFrames);
        }

        /// <summary>
        /// 将半开帧区间转换为 Clip 的内容坐标和持续宽度。
        /// </summary>
        public override void RefreshGeometry(int startFrame, int durationFrames)
        {
            Element.style.left = Mapper.FrameToContentX(startFrame);
            Element.style.width = Mapper.DurationToWidth(durationFrames);
        }
    }

    /// <summary>
    /// 显示拥有独立 UXML/USS 的动画 Clip 时间轴内容。
    /// </summary>
    internal sealed class AnimationClipView : ClipItemView
    {
        /// <summary>
        /// 创建动画 Clip 视图。
        /// </summary>
        public AnimationClipView(TrackViewData track,
            AnimationClipViewData item, VisualElement element,
            CoordinateMapper mapper) : base(track, item, element, mapper)
        {
        }
    }

    /// <summary>
    /// 显示拥有独立 UXML/USS 的攻击检测 Clip 时间轴内容。
    /// </summary>
    internal sealed class AttackDetectionClipView : ClipItemView
    {
        /// <summary>
        /// 创建攻击检测 Clip 视图。
        /// </summary>
        public AttackDetectionClipView(TrackViewData track,
            AttackDetectionClipViewData item, VisualElement element,
            CoordinateMapper mapper) : base(track, item, element, mapper)
        {
        }
    }

    /// <summary>
    /// 显示拥有独立 UXML/USS 的特效 Clip 时间轴内容。
    /// </summary>
    internal sealed class VfxClipView : ClipItemView
    {
        /// <summary>
        /// 创建特效 Clip 视图。
        /// </summary>
        public VfxClipView(TrackViewData track,
            VfxClipViewData item, VisualElement element,
            CoordinateMapper mapper) : base(track, item, element, mapper)
        {
        }
    }

    /// <summary>
    /// 显示拥有独立 UXML/USS 的音频 Clip 时间轴内容。
    /// </summary>
    internal sealed class AudioClipView : ClipItemView
    {
        /// <summary>
        /// 创建音频 Clip 视图。
        /// </summary>
        public AudioClipView(TrackViewData track,
            AudioClipViewData item, VisualElement element,
            CoordinateMapper mapper) : base(track, item, element, mapper)
        {
        }
    }
    /// <summary>
    /// 显示拥有独立 UXML/USS、不可裁剪的事件 Marker 时间轴内容。
    /// </summary>
    internal sealed class EventMarkerView : ItemView
    {
        /// <summary>
        /// 创建事件 Marker 视图，尺寸与居中表现完全由类型 USS 控制。
        /// </summary>
        public EventMarkerView(TrackViewData track,
            EventMarkerViewData item, VisualElement element,
            CoordinateMapper mapper) : base(track, item, element, mapper)
        {
            Element.tooltip = item.DisplayName;
            RefreshGeometry(item.StartFrame, item.DurationFrames);
        }

        /// <summary>
        /// 将事件帧写入内容坐标；Marker 的半宽居中位移由 USS translate 负责。
        /// </summary>
        public override void RefreshGeometry(int startFrame, int durationFrames) =>
            Element.style.left = Mapper.FrameToContentX(startFrame);
    }
}

#endif
