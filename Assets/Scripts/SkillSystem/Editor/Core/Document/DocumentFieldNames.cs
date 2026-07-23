#if UNITY_EDITOR
namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 集中声明 Document 与轨道数据处理器访问 SerializedProperty 时使用的稳定字段名。
    /// </summary>
    internal static class DocumentFieldNames
    {
        /// <summary>技能配置、轨道头和内容共用的稳定 GUID 字段。</summary>
        internal const string Id = "id";
        /// <summary>技能配置的每秒帧数。</summary>
        internal const string FrameRate = "frameRate";
        /// <summary>技能配置或区间内容的持续帧数。</summary>
        internal const string DurationFrames = "durationFrames";
        /// <summary>技能配置中的动画轨道列表。</summary>
        internal const string AnimationTracks = "animationTracks";
        /// <summary>技能配置中的攻击检测轨道列表。</summary>
        internal const string AttackDetectionTracks = "attackDetectionTracks";
        /// <summary>技能配置中的特效轨道列表。</summary>
        internal const string VfxTracks = "vfxTracks";
        /// <summary>技能配置中的音频轨道列表。</summary>
        internal const string AudioTracks = "audioTracks";
        /// <summary>技能配置中的事件轨道列表。</summary>
        internal const string EventTracks = "eventTracks";
        /// <summary>每条轨道保存公共名称、静音和编辑器状态的轨道头。</summary>
        internal const string Header = "header";
        /// <summary>轨道头或事件标记的显示名称。</summary>
        internal const string DisplayName = "displayName";
        /// <summary>轨道运行时静音状态。</summary>
        internal const string Muted = "muted";
        /// <summary>轨道仅供编辑器使用的锁定状态。</summary>
        internal const string EditorLocked = "editorLocked";
        /// <summary>轨道仅供编辑器使用的显示颜色。</summary>
        internal const string EditorColor = "editorColor";
        /// <summary>动画、特效和音频轨道使用的区间内容列表。</summary>
        internal const string Clips = "clips";
        /// <summary>事件轨道使用的单帧标记列表。</summary>
        internal const string Markers = "markers";
        /// <summary>区间内容的起始帧，区间规则为 [StartFrame, EndFrame)。</summary>
        internal const string StartFrame = "startFrame";
        /// <summary>事件 Marker 所在的整数帧。</summary>
        internal const string Frame = "frame";
        /// <summary>动画内容引用的 AnimationClip。</summary>
        internal const string AnimationClip = "animationClip";
        /// <summary>动画内容从源 AnimationClip 开始采样的偏移帧。</summary>
        internal const string SourceStartFrame = "sourceStartFrame";
        /// <summary>动画内容的播放速度。</summary>
        internal const string PlaybackSpeed = "playbackSpeed";
        /// <summary>特效内容引用的 Prefab Asset。</summary>
        internal const string Prefab = "prefab";
        /// <summary>特效内容的局部位置。</summary>
        internal const string LocalPosition = "localPosition";
        /// <summary>特效内容的局部欧拉角。</summary>
        internal const string LocalEulerAngles = "localEulerAngles";
        /// <summary>特效内容的局部缩放。</summary>
        internal const string LocalScale = "localScale";
        /// <summary>特效内容的跟随策略。</summary>
        internal const string FollowMode = "followMode";
        /// <summary>特效内容到达结束帧后的停止策略。</summary>
        internal const string StopMode = "stopMode";
        /// <summary>音频内容引用的 AudioClip。</summary>
        internal const string AudioClip = "audioClip";
        /// <summary>音频内容的线性音量。</summary>
        internal const string Volume = "volume";
        /// <summary>音频内容的播放音调。</summary>
        internal const string Pitch = "pitch";
        /// <summary>攻击检测片段的采样间隔帧，最小为一帧。</summary>
        internal const string SampleIntervalFrames = "sampleIntervalFrames";
        /// <summary>攻击检测片段保存的局部多态检测参数。</summary>
        internal const string DetectionData = "detectionData";
        /// <summary>事件 Marker 的事件类型名称。</summary>
        internal const string EventTypeName = "eventTypeName";
        /// <summary>事件 Marker 的序列化参数文本。</summary>
        internal const string ParameterText = "parameterText";
    }
}
#endif
