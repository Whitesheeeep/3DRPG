using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Serialization;

namespace RPG.SkillSystem
{
    /// <summary>
    /// 定义技能事件 Marker 当前生效的参数类型；Marker 始终保存全部候选值。
    /// </summary>
    public enum SkillEventValueType
    {
        /// <summary>使用整数候选值。</summary>
        Int,
        /// <summary>使用字符串候选值。</summary>
        String,
        /// <summary>使用长整数候选值。</summary>
        Long,
        /// <summary>使用布尔候选值。</summary>
        Bool,
        /// <summary>使用双精度候选值。</summary>
        Double,
        /// <summary>使用单精度候选值。</summary>
        Float,
        /// <summary>使用 Unity 资产候选值。</summary>
        Object
    }

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
    /// 保存单帧事件的业务键、显示名称和类型化参数联合体。
    /// </summary>
    [Serializable]
    public sealed class SkillEventMarkerConfig : TimelineItemConfigBase
    {
        [SerializeField, ReadOnly, LabelText("内容 ID")] private string id = string.Empty;
        [SerializeField, Min(0)] private int frame;
        [FormerlySerializedAs("eventTypeName")]
        [SerializeField] private string eventKey = string.Empty;
        [SerializeField] private string displayName = "事件";
        [SerializeField] private SkillEventValueType valueType = SkillEventValueType.String;
        [SerializeField] private int intValue;
        [FormerlySerializedAs("parameterText")]
        [SerializeField, TextArea] private string stringValue = string.Empty;
        [SerializeField] private long longValue;
        [SerializeField] private bool boolValue;
        [SerializeField] private double doubleValue;
        [SerializeField] private float floatValue;
        [SerializeField] private UnityEngine.Object objectValue;

        public override string Id => id;
        public int Frame => frame;
        public override int StartFrame => frame;
        public override int DurationFrames => 1;
        public string EventKey => eventKey;
        public string DisplayName => displayName;
        public SkillEventValueType ValueType => valueType;
        public int IntValue => intValue;
        public string StringValue => stringValue;
        public long LongValue => longValue;
        public bool BoolValue => boolValue;
        public double DoubleValue => doubleValue;
        public float FloatValue => floatValue;
        public UnityEngine.Object ObjectValue => objectValue;
    }
}
