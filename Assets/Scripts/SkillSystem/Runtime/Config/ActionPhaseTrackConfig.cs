using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.TAG;

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
    /// 描述一个左闭右开动作阶段区间及该阶段的 RuntimeTag、阻断和取消策略。
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

        [SerializeField, LabelText("允许取消"),
         Tooltip("本阶段是否接受普通取消；系统清理仍可强制回收。")]
        private bool isCancelable;

        [SerializeField, LabelText("Runtime Tags"),
         Tooltip("本阶段贡献给当前 Ability Runtime 的临时标签；进入下一 Phase 或空白区间时撤销。")]
        private GameplayTag[] runtimeTags = Array.Empty<GameplayTag>();

        [SerializeField, LabelText("Block Ability Tags"),
         Tooltip("本阶段完整替换 Runtime 的 Ability 阻断标签；空白区间恢复 GA 初始值。")]
        private GameplayTag[] blockAbilityTags = Array.Empty<GameplayTag>();

        #endregion

        #region 只读数据

        public override string Id => id;
        public override int StartFrame => startFrame;
        public override int DurationFrames => durationFrames;

        /// <summary>
        /// 该区间描述的动作阶段。
        /// </summary>
        public ActionPhaseType Phase => phase;

        /// <summary>获取本阶段是否接受普通取消。</summary>
        public bool IsCancelable => isCancelable;

        /// <summary>获取本阶段贡献给 Runtime 的临时标签。</summary>
        public IReadOnlyList<GameplayTag> RuntimeTags => runtimeTags ?? Array.Empty<GameplayTag>();

        /// <summary>获取本阶段完整采用的 Ability 阻断标签快照。</summary>
        public IReadOnlyList<GameplayTag> BlockAbilityTags =>
            blockAbilityTags ?? Array.Empty<GameplayTag>();

        #endregion
    }
}
