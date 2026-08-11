using UnityEngine;

namespace RPG.SkillSystem
{
    /// <summary>
    /// 保存技能事件发布时的不可变配置快照，供 WSFrame 全局类型事件直接传递。
    /// </summary>
    public readonly struct SkillConfigEventArgs
    {
        public SkillConfig Config { get; }
        public GameObject Owner { get; }
        public string EventKey { get; }
        public string MarkerId { get; }
        public int Frame { get; }
        public SkillEventValueType ValueType { get; }
        public int IntValue { get; }
        public string StringValue { get; }
        public long LongValue { get; }
        public bool BoolValue { get; }
        public double DoubleValue { get; }
        public float FloatValue { get; }
        public Object ObjectValue { get; }

        /// <summary>
        /// 从本次技能实例与 Marker 复制完整事件快照，避免监听者持有可变 Marker 引用。
        /// </summary>
        /// <param name="config">触发事件的技能配置。</param>
        /// <param name="owner">本次技能释放者，用于区分并发技能实例。</param>
        /// <param name="marker">到达当前帧的事件 Marker。</param>
        public SkillConfigEventArgs(SkillConfig config, GameObject owner, SkillEventMarkerConfig marker)
        {
            // 完整复制联合体，保证监听者可以按 ValueType 读取有效值，也能获得稳定诊断信息。
            Config = config;
            Owner = owner;
            EventKey = marker.EventKey;
            MarkerId = marker.Id;
            Frame = marker.Frame;
            ValueType = marker.ValueType;
            IntValue = marker.IntValue;
            StringValue = marker.StringValue;
            LongValue = marker.LongValue;
            BoolValue = marker.BoolValue;
            DoubleValue = marker.DoubleValue;
            FloatValue = marker.FloatValue;
            ObjectValue = marker.ObjectValue;
        }
    }
}
