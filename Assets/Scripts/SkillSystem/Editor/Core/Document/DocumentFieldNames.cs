#if UNITY_EDITOR
namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 集中声明 Document 与轨道数据处理器访问 SerializedProperty 时使用的稳定字段名。
    /// </summary>
    internal static class DocumentFieldNames
    {
        /// <summary>技能配置、Track 子资产和内容共用的稳定 GUID 字段。</summary>
        internal const string Id = "id";
        /// <summary>技能配置的每秒帧数。</summary>
        internal const string FrameRate = "frameRate";
        /// <summary>技能配置或区间内容的持续帧数。</summary>
        internal const string DurationFrames = "durationFrames";
        /// <summary>技能配置是否在运行时消费动画根运动。</summary>
        internal const string IsRootMotion = "isRootMotion";
        /// <summary>技能配置中按物理行排序的 Track 子资产引用列表。</summary>
        internal const string Tracks = "tracks";
        /// <summary>Track 子资产或事件标记的显示名称。</summary>
        internal const string DisplayName = "displayName";
        /// <summary>轨道运行时静音状态。</summary>
        internal const string Muted = "muted";
        /// <summary>轨道仅供编辑器使用的锁定状态。</summary>
        internal const string EditorLocked = "editorLocked";
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
        /// <summary>动画内容进入时使用的固定淡入时长，单位为秒。</summary>
        internal const string FadeDuration = "fadeDuration";
        /// <summary>特效内容引用的 Prefab Asset。</summary>
        internal const string Prefab = "prefab";
        /// <summary>特效内容在角色层级中按语义解析的 MarkerKey；空值表示角色根节点。</summary>
        internal const string MarkerKey = "markerKey";
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
        /// <summary>动作阶段片段的阶段枚举。</summary>
        internal const string ActionPhase = "phase";
        /// <summary>动作阶段内当前动作是否允许被外部逻辑打断。</summary>
        internal const string CanBeInterrupted = "canBeInterrupted";
        /// <summary>攻击检测片段的采样间隔帧，最小为一帧。</summary>
        internal const string SampleIntervalFrames = "sampleIntervalFrames";
        /// <summary>攻击检测片段保存的局部多态检测参数。</summary>
        internal const string DetectionData = "detectionData";
        /// <summary>摄像机修饰片段保存的局部多态参数。</summary>
        internal const string ModifierData = "modifierData";
        /// <summary>事件 Marker 用于运行时业务路由的稳定键。</summary>
        internal const string EventKey = "eventKey";
        /// <summary>事件 Marker 当前有效候选值的类型。</summary>
        internal const string EventValueType = "valueType";
        /// <summary>事件 Marker 保留的整数候选值。</summary>
        internal const string IntValue = "intValue";
        /// <summary>事件 Marker 保留的字符串候选值。</summary>
        internal const string StringValue = "stringValue";
        /// <summary>事件 Marker 保留的长整数候选值。</summary>
        internal const string LongValue = "longValue";
        /// <summary>事件 Marker 保留的布尔候选值。</summary>
        internal const string BoolValue = "boolValue";
        /// <summary>事件 Marker 保留的双精度候选值。</summary>
        internal const string DoubleValue = "doubleValue";
        /// <summary>事件 Marker 保留的单精度候选值。</summary>
        internal const string FloatValue = "floatValue";
        /// <summary>事件 Marker 保留的 Unity 资产候选值。</summary>
        internal const string ObjectValue = "objectValue";
    }
}
#endif
