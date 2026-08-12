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
        public TrackConfigBase Track { get; }
        public TimelineItemConfigBase Item { get; }
        public VisualElement ResizeLeft { get; protected set; }
        public VisualElement ResizeRight { get; protected set; }

        // 关联权威 ViewData 与元素引用，不修改任何技能资产。
        protected ItemView(TrackConfigBase track, TimelineItemConfigBase item,
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

        // 直接从实际内容配置生成短标题，避免维护与 Config 重复的 ViewData。
        protected static string GetDisplayName(TimelineItemConfigBase item) => item switch
        {
            ActionPhaseSkillClipConfig actionPhase => GetActionPhaseDisplayName(actionPhase),
            AnimationSkillClipConfig animation => animation.AnimationClip != null
                ? animation.AnimationClip.name : "Animation Clip",
            AttackDetectionSkillClipConfig attack => $"{attack.DetectionType} Detection",
            VfxSkillClipConfig vfx => vfx.Prefab != null ? vfx.Prefab.name : "VFX Clip",
            AudioSkillClipConfig audio => audio.AudioClip != null ? audio.AudioClip.name : "Audio Clip",
            CameraModifierSkillClipConfig modifier => modifier.ModifierType.ToString(),
            SkillEventMarkerConfig marker => marker.DisplayName,
            _ => item.GetType().Name
        };

        // 将动作阶段枚举转换为紧凑中文标题，并显式提示该阶段可被外部打断。
        private static string GetActionPhaseDisplayName(ActionPhaseSkillClipConfig item)
        {
            string phase = item.Phase switch
            {
                ActionPhaseType.None => "未指定",
                ActionPhaseType.Startup => "前摇",
                ActionPhaseType.Active => "生效",
                ActionPhaseType.Recovery => "后摇",
                _ => item.Phase.ToString()
            };
            return item.CanBeInterrupted ? $"{phase} · 可打断" : phase;
        }
    }

    /// <summary>
    /// 封装可移动和双侧裁剪的 Clip 元素公共行为。
    /// </summary>
    internal abstract class ClipItemView : ItemView
    {
        // 创建 Clip 元素并绑定左右裁剪手柄。
        protected ClipItemView(TrackConfigBase track, TimelineItemConfigBase item,
            VisualElement element, CoordinateMapper mapper) : base(track, item, element, mapper)
        {
            Label label = Element as Label ?? Element.Q<Label>();
            if (label != null) label.text = GetDisplayName(item);
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
    /// 显示动作阶段区间，并按具体阶段切换独立的颜色状态。
    /// </summary>
    internal sealed class ActionPhaseClipView : ClipItemView
    {
        /// <summary>
        /// 创建动作阶段 Clip 视图并应用阶段 USS 状态。
        /// </summary>
        /// <param name="track">Item 所属实际轨道。</param>
        /// <param name="item">实际动作阶段配置。</param>
        /// <param name="element">从类型模板实例化的根元素。</param>
        /// <param name="mapper">帧与内容坐标映射器。</param>
        public ActionPhaseClipView(TrackConfigBase track,
            ActionPhaseSkillClipConfig item, VisualElement element,
            CoordinateMapper mapper) : base(track, item, element, mapper)
        {
            Element.AddToClassList(item.Phase switch
            {
                ActionPhaseType.None => "phase-none",
                ActionPhaseType.Startup => "phase-startup",
                ActionPhaseType.Active => "phase-active",
                ActionPhaseType.Recovery => "phase-recovery",
                _ => "phase-none"
            });
        }
    }
    /// <summary>
    /// 显示使用统一 UXML 和动画专属 USS 的 Clip 时间轴内容。
    /// </summary>
    internal sealed class AnimationClipView : ClipItemView
    {
        /// <summary>
        /// 创建动画 Clip 视图。
        /// </summary>
        public AnimationClipView(TrackConfigBase track,
            AnimationSkillClipConfig item, VisualElement element,
            CoordinateMapper mapper) : base(track, item, element, mapper)
        {
        }
    }

    /// <summary>
    /// 显示使用统一 UXML 和攻击检测专属 USS 的 Clip 时间轴内容。
    /// </summary>
    internal sealed class AttackDetectionClipView : ClipItemView
    {
        /// <summary>
        /// 创建攻击检测 Clip 视图。
        /// </summary>
        public AttackDetectionClipView(TrackConfigBase track,
            AttackDetectionSkillClipConfig item, VisualElement element,
            CoordinateMapper mapper) : base(track, item, element, mapper)
        {
        }
    }

    /// <summary>
    /// 显示使用统一 UXML 和特效专属 USS 的 Clip 时间轴内容。
    /// </summary>
    internal sealed class VfxClipView : ClipItemView
    {
        /// <summary>
        /// 创建特效 Clip 视图。
        /// </summary>
        public VfxClipView(TrackConfigBase track,
            VfxSkillClipConfig item, VisualElement element,
            CoordinateMapper mapper) : base(track, item, element, mapper)
        {
        }
    }

    /// <summary>
    /// 显示使用统一 UXML 和音频专属 USS 的 Clip 时间轴内容。
    /// </summary>
    internal sealed class AudioClipView : ClipItemView
    {
        /// <summary>
        /// 创建音频 Clip 视图。
        /// </summary>
        public AudioClipView(TrackConfigBase track,
            AudioSkillClipConfig item, VisualElement element,
            CoordinateMapper mapper) : base(track, item, element, mapper)
        {
        }
    }
    /// <summary>显示摄像机修饰区间。</summary>
    internal sealed class CameraModifierClipView : ClipItemView
    {
        /// <summary>创建摄像机修饰 Clip 视图。</summary>
        internal CameraModifierClipView(TrackConfigBase track,
            CameraModifierSkillClipConfig item, VisualElement element,
            CoordinateMapper mapper) : base(track, item, element, mapper)
        {
        }
    }

    /// <summary>
    /// 显示使用统一 UXML 和事件专属 USS、不可裁剪的事件 Marker。
    /// </summary>
    internal sealed class EventMarkerView : ItemView
    {
        /// <summary>
        /// 创建事件 Marker 视图，尺寸与居中表现完全由类型 USS 控制。
        /// </summary>
        public EventMarkerView(TrackConfigBase track,
            SkillEventMarkerConfig item, VisualElement element,
            CoordinateMapper mapper) : base(track, item, element, mapper)
        {
            // Event 使用统一 Label 模板，因此由 View 填充固定 Marker 符号而非依赖类型 UXML。
            if (Element is Label label) label.text = "◆";
            Element.tooltip = GetDisplayName(item);
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
