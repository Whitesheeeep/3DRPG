#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using RPG.SkillSystem.Editor;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.GAS.Generated;
using WS_Modules.GAS.TAG;

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
            AttackDetectionSkillClipConfig attack => attack.DetectionType.ToString(),
            VfxSkillClipConfig vfx => vfx.Prefab != null ? vfx.Prefab.name : "VFX Clip",
            AudioSkillClipConfig audio => audio.AudioClip != null ? audio.AudioClip.name : "Audio Clip",
            CameraModifierSkillClipConfig modifier => modifier.ModifierType.ToString(),
            SkillEventMarkerConfig marker => marker.DisplayName,
            ProjectileSkillClipConfig projectile => projectile.SpawnConfig.FallbackPrefab != null
                ? projectile.SpawnConfig.FallbackPrefab.name : "Projectile",
            _ => item.GetType().Name
        };

        // 将动作阶段枚举转换为紧凑中文标题，并显示 RuntimeTag 转换窗口。
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
            string transitions = GetActionPhaseTransitionDescription(item.RuntimeTags);
            return string.IsNullOrEmpty(transitions) ? phase : $"{phase} · {transitions}";
        }

        /// <summary>
        /// 将 RuntimeTag 转换窗口转换为 Inspector 与时间轴 Tooltip 共用的完整中文描述。
        /// </summary>
        /// <param name="transitions">当前动作阶段的 RuntimeTags。</param>
        /// <returns>转换窗口的中文名称；无权限时返回“无”。</returns>
        protected static string GetActionPhaseTransitionDescription(IReadOnlyList<GameplayTag> transitions)
        {
            if (transitions == null || transitions.Count == 0) return string.Empty;
            List<string> names = new();
            for (int i = 0; i < transitions.Count; i++)
            {
                GameplayTag tag = transitions[i];
                if (tag == GameplayTags.Tag_Skill_Window_CancelBy_Move) names.Add("移动");
                else if (tag == GameplayTags.Tag_Skill_Window_CancelBy_Jump) names.Add("跳跃");
                else if (tag == GameplayTags.Tag_Skill_Window_CancelBy_Ability) names.Add("其他 Ability");
            }
            return string.Join("、", names);
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
            Element.tooltip = $"阶段：{GetDisplayName(item)}\n" +
                              $"起始帧：{item.StartFrame}\n" +
                              $"持续帧：{item.DurationFrames}\n" +
                              $"普通取消：{(item.IsCancelable ? "是" : "否")}\n" +
                              $"Runtime 窗口：{GetActionPhaseTransitionDescription(item.RuntimeTags)}\n" +
                              $"其他 RuntimeTag：{Math.Max(0, item.RuntimeTags.Count - CountWindowTags(item.RuntimeTags))}\n" +
                              $"Block Tag 数量：{item.BlockAbilityTags.Count}";
        }

        /// <summary>统计动作阶段 RuntimeTags 中的标准转换窗口数量。</summary>
        /// <param name="tags">当前阶段 RuntimeTags。</param>
        /// <returns>标准窗口 Tag 数量。</returns>
        private static int CountWindowTags(IReadOnlyList<GameplayTag> tags)
        {
            int count = 0;
            for (int i = 0; i < tags.Count; i++)
                if (tags[i] == GameplayTags.Tag_Skill_Window_CancelBy_Move ||
                    tags[i] == GameplayTags.Tag_Skill_Window_CancelBy_Jump ||
                    tags[i] == GameplayTags.Tag_Skill_Window_CancelBy_Ability)
                    count++;
            return count;
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
            // 攻击检测使用根 Label 承载两个可忽略命中测试的子标签，根元素仍负责选择、拖拽和 Resize。
            if (Element is not Label rootLabel)
                throw new InvalidOperationException("攻击检测 Clip 模板必须使用 Label 作为根元素。");

            rootLabel.text = string.Empty;

            Label idBadge = new(FormatDetectionId(item.DetectionId))
            {
                name = "DetectionIdBadge",
                pickingMode = PickingMode.Ignore
            };
            idBadge.AddToClassList("attack-detection-id-badge");
            idBadge.style.backgroundColor = ResolveDetectionIdColor(item.DetectionId);

            Label typeLabel = new(item.DetectionType.ToString())
            {
                name = "DetectionTypeLabel",
                pickingMode = PickingMode.Ignore
            };
            typeLabel.AddToClassList("attack-detection-type-label");

            rootLabel.Add(idBadge);
            rootLabel.Add(typeLabel);
            Element.tooltip = $"攻击检测：{item.DetectionType}\n检测 ID：{item.DetectionId}";
        }

        /// <summary>
        /// 将检测 ID 格式化为至少两位的编辑器显示文本，不截断更长的整数。
        /// </summary>
        /// <param name="detectionId">攻击检测配置中的分组 ID。</param>
        /// <returns>至少两位的十进制 ID 文本。</returns>
        private static string FormatDetectionId(int detectionId) =>
            detectionId.ToString("D2", CultureInfo.InvariantCulture);

        /// <summary>
        /// 根据完整检测 ID 生成稳定的色相，使同一 ID 在不同 Clip 和轨道中保持相同颜色。
        /// </summary>
        /// <param name="detectionId">攻击检测配置中的分组 ID。</param>
        /// <returns>用于 ID 色块背景的稳定颜色。</returns>
        private static Color ResolveDetectionIdColor(int detectionId)
        {
            // 黄金分割色相让相邻 ID 尽量分散，同时不把颜色写入 SkillConfig。
            const float goldenRatioConjugate = 0.61803398875f;
            float hue = Mathf.Repeat(detectionId * goldenRatioConjugate, 1f);
            return Color.HSVToRGB(hue, 0.68f, 0.78f);
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

    /// <summary>显示不可裁剪的 Projectile 发射帧标记。</summary>
    internal sealed class ProjectileMarkerView : ItemView
    {
        /// <summary>创建 Projectile 标记视图并显示发射方向符号。</summary>
        public ProjectileMarkerView(TrackConfigBase track,
            ProjectileSkillClipConfig item, VisualElement element,
            CoordinateMapper mapper) : base(track, item, element, mapper)
        {
            if (Element is Label label) label.text = "➤";
            Element.tooltip = GetDisplayName(item);
            RefreshGeometry(item.StartFrame, item.DurationFrames);
        }

        /// <inheritdoc />
        public override void RefreshGeometry(int startFrame, int durationFrames) =>
            Element.style.left = Mapper.FrameToContentX(startFrame);
    }
}

#endif
