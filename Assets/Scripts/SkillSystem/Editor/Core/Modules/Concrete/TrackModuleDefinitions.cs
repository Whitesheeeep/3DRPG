#if UNITY_EDITOR
using System;
using RPG.SkillSystem;

namespace RPG.SkillSystem.Editor
{
    /// <summary>组合摄像机修饰轨道的编辑与预览能力。</summary>
    internal sealed class CameraModifierModuleDefinition : TrackModuleDefinition
    {
        public override Type TrackType => typeof(CameraModifierTrackConfig);
        public override Type ItemType => typeof(CameraModifierSkillClipConfig);

        /// <summary>创建摄像机修饰模块。</summary>
        public override TrackModule Create(EditorConfig config, TimelineTrackAttribute metadata) =>
            new(TrackType, ItemType, metadata, new CameraModifierDocumentHandler(), null,
                new CameraModifierItemFactory(), new CameraModifierInspectorDrawer(),
                new CameraModifierPreviewFactory());
    }

    /// <summary>
    /// 组合动作阶段轨道的配置编辑、时间轴显示和 Inspector 能力。
    /// </summary>
    internal sealed class ActionPhaseModuleDefinition : TrackModuleDefinition
    {
        public override Type TrackType => typeof(ActionPhaseTrackConfig);
        public override Type ItemType => typeof(ActionPhaseSkillClipConfig);

        /// <summary>
        /// 创建动作阶段轨道模块；该模块不提供素材拖入和预览能力。
        /// </summary>
        /// <param name="config">编辑器窗口配置。</param>
        /// <param name="metadata">扫描得到的轨道元数据。</param>
        /// <returns>组合完成的动作阶段轨道模块。</returns>
        public override TrackModule Create(EditorConfig config, TimelineTrackAttribute metadata) =>
            new(TrackType, ItemType, metadata, new ActionPhaseDocumentHandler(),
                null, new ActionPhaseItemFactory(), new ActionPhaseInspectorDrawer());
    }
    /// <summary>
    /// 组合动画轨道的全部编辑器能力。
    /// </summary>
    internal sealed class AnimationModuleDefinition : TrackModuleDefinition
    {
        public override Type TrackType => typeof(AnimationTrackConfig);
        public override Type ItemType => typeof(AnimationSkillClipConfig);

        /// <summary>
        /// 创建动画轨道模块。
        /// </summary>
        public override TrackModule Create(EditorConfig config, TimelineTrackAttribute metadata) =>
            new(TrackType, ItemType, metadata, new AnimationDocumentHandler(),
                new AnimationDropHandler(), new AnimationItemFactory(),
                new AnimationInspectorDrawer(), new AnimationPreviewFactory());
    }

    /// <summary>
    /// 组合攻击检测轨道的全部编辑器能力。
    /// </summary>
    internal sealed class AttackDetectionModuleDefinition : TrackModuleDefinition
    {
        public override Type TrackType => typeof(AttackDetectionTrackConfig);
        public override Type ItemType => typeof(AttackDetectionSkillClipConfig);

        /// <summary>
        /// 创建攻击检测轨道模块。
        /// </summary>
        public override TrackModule Create(EditorConfig config, TimelineTrackAttribute metadata) =>
            new(TrackType, ItemType, metadata, new AttackDetectionDocumentHandler(),
                null, new AttackDetectionItemFactory(), new AttackDetectionInspectorDrawer(),
                new AttackDetectionPreviewFactory(config));
    }

    /// <summary>
    /// 组合特效轨道的全部编辑器能力。
    /// </summary>
    internal sealed class VfxModuleDefinition : TrackModuleDefinition
    {
        public override Type TrackType => typeof(VfxTrackConfig);
        public override Type ItemType => typeof(VfxSkillClipConfig);

        /// <summary>
        /// 创建特效轨道模块。
        /// </summary>
        public override TrackModule Create(EditorConfig config, TimelineTrackAttribute metadata) =>
            new(TrackType, ItemType, metadata, new VfxDocumentHandler(),
                new VfxDropHandler(config), new VfxItemFactory(),
                new VfxInspectorDrawer(), new VfxPreviewFactory());
    }

    /// <summary>
    /// 组合音频轨道的全部编辑器能力。
    /// </summary>
    internal sealed class AudioModuleDefinition : TrackModuleDefinition
    {
        public override Type TrackType => typeof(AudioTrackConfig);
        public override Type ItemType => typeof(AudioSkillClipConfig);

        /// <summary>
        /// 创建音频轨道模块。
        /// </summary>
        public override TrackModule Create(EditorConfig config, TimelineTrackAttribute metadata) =>
            new(TrackType, ItemType, metadata, new AudioDocumentHandler(),
                new AudioDropHandler(), new AudioItemFactory(),
                new AudioInspectorDrawer(), new AudioPreviewFactory());
    }

    /// <summary>
    /// 组合事件轨道的全部编辑器能力。
    /// </summary>
    internal sealed class EventModuleDefinition : TrackModuleDefinition
    {
        public override Type TrackType => typeof(EventTrackConfig);
        public override Type ItemType => typeof(SkillEventMarkerConfig);

        /// <summary>
        /// 创建事件轨道模块。
        /// </summary>
        public override TrackModule Create(EditorConfig config, TimelineTrackAttribute metadata) =>
            new(TrackType, ItemType, metadata, new EventDocumentHandler(),
                null, new EventItemFactory(), new EventInspectorDrawer());
    }
}
#endif
