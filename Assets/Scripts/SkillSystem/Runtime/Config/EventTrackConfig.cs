using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.SkillSystem
{
    /// <summary>
    /// 保存一条事件轨道的公共轨道数据和单帧事件标记列表。
    /// </summary>
    [TimelineTrack("事件轨道", 40)]
    public sealed class EventTrackConfig : TrackConfigBase
    {
        [SerializeField] private List<SkillEventMarkerConfig> markers = new();

        public IReadOnlyList<SkillEventMarkerConfig> Markers => markers;
        public override IReadOnlyList<TimelineItemConfigBase> Items => markers;
    }

    /// <summary>
    /// 保存单帧事件的稳定标识、类型名、显示名称和参数文本。
    /// </summary>
    [Serializable]
    public sealed class SkillEventMarkerConfig : TimelineItemConfigBase
    {
        [SerializeField, ReadOnly, LabelText("内容 ID")] private string id = string.Empty;
        [SerializeField, Min(0)] private int frame;
        [SerializeField] private string eventTypeName = string.Empty;
        [SerializeField] private string displayName = "事件";
        [SerializeField, TextArea] private string parameterText = string.Empty;

        public override string Id => id;
        public int Frame => frame;
        public override int StartFrame => frame;
        public override int DurationFrames => 1;
        public string EventTypeName => eventTypeName;
        public string DisplayName => displayName;
        public string ParameterText => parameterText;
    }
}