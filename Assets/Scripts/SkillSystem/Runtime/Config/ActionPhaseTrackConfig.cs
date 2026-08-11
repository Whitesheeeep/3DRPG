using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.SkillSystem
{
    /// <summary>
    /// 表示动作在当前帧所处的逻辑阶段；时间轴空白区间由未来运行时解释为 None。
    /// </summary>
    public enum ActionPhaseType
    {
        None = 0,
        Startup = 1,
        Active = 2,
        Recovery = 3
    }

    /// <summary>
    /// 保存一个技能的动作阶段区间；每个 SkillConfig 最多允许创建一条该类型轨道。
    /// </summary>
    [TimelineTrack("动作阶段轨道", -10, false)]
    public sealed class ActionPhaseTrackConfig : TrackConfigBase
    {
        #region 序列化字段

        [SerializeField, LabelText("动作阶段")]
        private List<ActionPhaseSkillClipConfig> clips = new();

        #endregion

        #region 只读数据

        /// <summary>
        /// 当前轨道按起始帧排序的动作阶段区间。
        /// </summary>
        public IReadOnlyList<ActionPhaseSkillClipConfig> Clips => clips;

        /// <summary>
        /// 返回实际动作阶段配置，不创建表现层副本。
        /// </summary>
        public override IReadOnlyList<TimelineItemConfigBase> Items => clips;

        #endregion
    }

    /// <summary>
    /// 描述一个左闭右开动作阶段区间及该阶段是否允许被外部逻辑打断。
    /// </summary>
    [Serializable]
    public sealed class ActionPhaseSkillClipConfig : TimelineItemConfigBase
    {
        #region 序列化字段

        [SerializeField, ReadOnly, LabelText("内容 ID")]
        private string id = string.Empty;

        [SerializeField, Min(0), LabelText("起始帧")]
        private int startFrame;

        [SerializeField, Min(1), LabelText("持续帧")]
        private int durationFrames = 1;

        [SerializeField, LabelText("动作阶段")]
        private ActionPhaseType phase = ActionPhaseType.Startup;

        [SerializeField, LabelText("可被外部打断")]
        private bool canBeInterrupted;

        #endregion

        #region 只读数据

        public override string Id => id;
        public override int StartFrame => startFrame;
        public override int DurationFrames => durationFrames;

        /// <summary>
        /// 该区间描述的动作阶段。
        /// </summary>
        public ActionPhaseType Phase => phase;

        /// <summary>
        /// 当前动作在该区间内是否允许被外部动作或状态逻辑打断。
        /// </summary>
        public bool CanBeInterrupted => canBeInterrupted;

        #endregion
    }
}