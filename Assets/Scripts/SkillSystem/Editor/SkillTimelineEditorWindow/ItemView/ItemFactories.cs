#if UNITY_EDITOR
using System;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 使用动画 Clip 独立模板创建动画时间轴 Item View。
    /// </summary>
    internal sealed class AnimationItemFactory : IItemViewFactory
    {
        /// <summary>
        /// 创建动画 Clip View，并绑定统一坐标映射器。
        /// </summary>
        public ItemView Create(TrackViewData track, ItemViewData item,
            ElementFactory elements, CoordinateMapper mapper)
        {
            if (item is not AnimationClipViewData animation)
                throw new ArgumentException("动画 ItemFactory 收到不匹配的 ViewData。", nameof(item));
            return new AnimationClipView(track, animation,
                elements.Instantiate("Item/SkillTimelineAnimationClipItem.uxml", "AnimationClipRoot"), mapper);
        }
    }

    /// <summary>
    /// 使用攻击检测 Clip 独立模板创建攻击检测时间轴 Item View。
    /// </summary>
    internal sealed class AttackDetectionItemFactory : IItemViewFactory
    {
        /// <summary>
        /// 创建攻击检测 Clip View，并绑定统一坐标映射器。
        /// </summary>
        public ItemView Create(TrackViewData track, ItemViewData item,
            ElementFactory elements, CoordinateMapper mapper)
        {
            if (item is not AttackDetectionClipViewData attack)
                throw new ArgumentException("攻击检测 ItemFactory 收到不匹配的 ViewData。", nameof(item));
            return new AttackDetectionClipView(track, attack,
                elements.Instantiate("Item/SkillTimelineAttackDetectionClipItem.uxml",
                    "AttackDetectionClipRoot"), mapper);
        }
    }

    /// <summary>
    /// 使用特效 Clip 独立模板创建特效时间轴 Item View。
    /// </summary>
    internal sealed class VfxItemFactory : IItemViewFactory
    {
        /// <summary>
        /// 创建特效 Clip View，并绑定统一坐标映射器。
        /// </summary>
        public ItemView Create(TrackViewData track, ItemViewData item,
            ElementFactory elements, CoordinateMapper mapper)
        {
            if (item is not VfxClipViewData vfx)
                throw new ArgumentException("特效 ItemFactory 收到不匹配的 ViewData。", nameof(item));
            return new VfxClipView(track, vfx,
                elements.Instantiate("Item/SkillTimelineVfxClipItem.uxml", "VfxClipRoot"), mapper);
        }
    }

    /// <summary>
    /// 使用音频 Clip 独立模板创建音频时间轴 Item View。
    /// </summary>
    internal sealed class AudioItemFactory : IItemViewFactory
    {
        /// <summary>
        /// 创建音频 Clip View，并绑定统一坐标映射器。
        /// </summary>
        public ItemView Create(TrackViewData track, ItemViewData item,
            ElementFactory elements, CoordinateMapper mapper)
        {
            if (item is not AudioClipViewData audio)
                throw new ArgumentException("音频 ItemFactory 收到不匹配的 ViewData。", nameof(item));
            return new AudioClipView(track, audio,
                elements.Instantiate("Item/SkillTimelineAudioClipItem.uxml", "AudioClipRoot"), mapper);
        }
    }
    /// <summary>
    /// 使用事件 Marker 独立模板创建事件时间轴 Item View。
    /// </summary>
    internal sealed class EventItemFactory : IItemViewFactory
    {
        /// <summary>
        /// 创建事件 Marker View，并绑定统一坐标映射器。
        /// </summary>
        public ItemView Create(TrackViewData track, ItemViewData item,
            ElementFactory elements, CoordinateMapper mapper)
        {
            if (item is not EventMarkerViewData marker)
                throw new ArgumentException("事件 ItemFactory 收到不匹配的 ViewData。", nameof(item));
            return new EventMarkerView(track, marker,
                elements.Instantiate("Item/SkillTimelineEventMarkerItem.uxml", "EventMarkerRoot"), mapper);
        }
    }
}
#endif
