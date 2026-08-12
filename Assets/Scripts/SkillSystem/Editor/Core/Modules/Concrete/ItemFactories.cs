#if UNITY_EDITOR
using System;

namespace RPG.SkillSystem.Editor
{
    /// <summary>使用统一 Item 模板和摄像机修饰样式创建 Clip View。</summary>
    internal sealed class CameraModifierItemFactory : IItemViewFactory
    {
        /// <summary>创建并绑定摄像机修饰 Clip。</summary>
        public ItemView Create(TrackConfigBase track, TimelineItemConfigBase item,
            ElementFactory elements, CoordinateMapper mapper)
        {
            if (item is not CameraModifierSkillClipConfig modifier)
                throw new ArgumentException("摄像机修饰 ItemFactory 收到不匹配的配置。", nameof(item));
            return new CameraModifierClipView(track, modifier,
                elements.CreateItem("camera-modifier-item"), mapper);
        }
    }

    /// <summary>
    /// 使用统一 Item 模板和动作阶段样式创建时间轴 Item View。
    /// </summary>
    internal sealed class ActionPhaseItemFactory : IItemViewFactory
    {
        /// <summary>
        /// 创建动作阶段 Clip View，并绑定统一坐标映射器。
        /// </summary>
        /// <param name="track">Item 所属的实际轨道配置。</param>
        /// <param name="item">待显示的实际 Item 配置。</param>
        /// <param name="elements">UXML 元素工厂。</param>
        /// <param name="mapper">帧与内容坐标映射器。</param>
        /// <returns>绑定实际配置的动作阶段视图。</returns>
        public ItemView Create(TrackConfigBase track, TimelineItemConfigBase item,
            ElementFactory elements, CoordinateMapper mapper)
        {
            if (item is not ActionPhaseSkillClipConfig actionPhase)
                throw new ArgumentException("动作阶段 ItemFactory 收到不匹配的 Item Config。", nameof(item));
            return new ActionPhaseClipView(track, actionPhase,
                elements.CreateItem("action-phase-clip-item"), mapper);
        }
    }
    /// <summary>
    /// 使用统一 Item 模板和动画样式创建时间轴 Item View。
    /// </summary>
    internal sealed class AnimationItemFactory : IItemViewFactory
    {
        /// <summary>
        /// 创建动画 Clip View，并绑定统一坐标映射器。
        /// </summary>
        public ItemView Create(TrackConfigBase track, TimelineItemConfigBase item,
            ElementFactory elements, CoordinateMapper mapper)
        {
            if (item is not AnimationSkillClipConfig animation)
                throw new ArgumentException("动画 ItemFactory 收到不匹配的 Item Config。", nameof(item));
            return new AnimationClipView(track, animation,
                elements.CreateItem("animation-clip-item"), mapper);
        }
    }

    /// <summary>
    /// 使用统一 Item 模板和攻击检测样式创建时间轴 Item View。
    /// </summary>
    internal sealed class AttackDetectionItemFactory : IItemViewFactory
    {
        /// <summary>
        /// 创建攻击检测 Clip View，并绑定统一坐标映射器。
        /// </summary>
        public ItemView Create(TrackConfigBase track, TimelineItemConfigBase item,
            ElementFactory elements, CoordinateMapper mapper)
        {
            if (item is not AttackDetectionSkillClipConfig attack)
                throw new ArgumentException("攻击检测 ItemFactory 收到不匹配的 Item Config。", nameof(item));
            return new AttackDetectionClipView(track, attack,
                elements.CreateItem("attack-detection-clip-item"), mapper);
        }
    }

    /// <summary>
    /// 使用统一 Item 模板和特效样式创建时间轴 Item View。
    /// </summary>
    internal sealed class VfxItemFactory : IItemViewFactory
    {
        /// <summary>
        /// 创建特效 Clip View，并绑定统一坐标映射器。
        /// </summary>
        public ItemView Create(TrackConfigBase track, TimelineItemConfigBase item,
            ElementFactory elements, CoordinateMapper mapper)
        {
            if (item is not VfxSkillClipConfig vfx)
                throw new ArgumentException("特效 ItemFactory 收到不匹配的 Item Config。", nameof(item));
            return new VfxClipView(track, vfx,
                elements.CreateItem("vfx-clip-item"), mapper);
        }
    }

    /// <summary>
    /// 使用统一 Item 模板和音频样式创建时间轴 Item View。
    /// </summary>
    internal sealed class AudioItemFactory : IItemViewFactory
    {
        /// <summary>
        /// 创建音频 Clip View，并绑定统一坐标映射器。
        /// </summary>
        public ItemView Create(TrackConfigBase track, TimelineItemConfigBase item,
            ElementFactory elements, CoordinateMapper mapper)
        {
            if (item is not AudioSkillClipConfig audio)
                throw new ArgumentException("音频 ItemFactory 收到不匹配的 Item Config。", nameof(item));
            return new AudioClipView(track, audio,
                elements.CreateItem("audio-clip-item"), mapper);
        }
    }
    /// <summary>
    /// 使用统一 Item 模板和事件样式创建时间轴 Item View。
    /// </summary>
    internal sealed class EventItemFactory : IItemViewFactory
    {
        /// <summary>
        /// 创建事件 Marker View，并绑定统一坐标映射器。
        /// </summary>
        public ItemView Create(TrackConfigBase track, TimelineItemConfigBase item,
            ElementFactory elements, CoordinateMapper mapper)
        {
            if (item is not SkillEventMarkerConfig marker)
                throw new ArgumentException("事件 ItemFactory 收到不匹配的 Item Config。", nameof(item));
            return new EventMarkerView(track, marker,
                elements.CreateItem("event-marker-item"), mapper);
        }
    }
}
#endif
