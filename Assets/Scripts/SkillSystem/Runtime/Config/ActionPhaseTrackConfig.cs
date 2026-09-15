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
    /// 描述当前技能动作阶段开放的外部转换窗口。
    /// 该掩码只表达候选动作可以尝试转换，不负责执行取消、激活或状态切换。
    /// </summary>
    [Flags]
    public enum SkillTransitionMask
    {
        /// <summary>当前阶段不开放外部转换。</summary>
        None = 0,

        /// <summary>允许移动逻辑尝试转换。</summary>
        Move = 1 << 0,

        /// <summary>允许跳跃逻辑尝试转换。</summary>
        Jump = 1 << 1,

        /// <summary>允许其他 Gameplay Ability 尝试转换。</summary>
        Ability = 1 << 2
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
    /// 描述一个左闭右开动作阶段区间及该阶段开放的外部转换窗口。
    /// </summary>
    [Serializable]
    public sealed class ActionPhaseSkillClipConfig : TimelineItemConfigBase
    {
        #region 序列化字段

        [SerializeField, ReadOnly, LabelText("内容 ID")]
        private string id = string.Empty;

        [SerializeField, MinValue(0), LabelText("起始帧")]
        private int startFrame;

        [SerializeField, MinValue(1), LabelText("持续帧")]
        private int durationFrames = 1;

        [SerializeField, LabelText("动作阶段")]
        private ActionPhaseType phase = ActionPhaseType.Startup;

        [SerializeField, EnumToggleButtons, LabelText("允许转换"),
         Tooltip("只声明本阶段允许哪些动作尝试转换，不直接取消技能或启动目标动作。")]
        private SkillTransitionMask allowedTransitions;

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
        /// 当前阶段允许外部逻辑尝试的转换类型。
        /// </summary>
        public SkillTransitionMask AllowedTransitions => allowedTransitions;

        #endregion
    }
}
