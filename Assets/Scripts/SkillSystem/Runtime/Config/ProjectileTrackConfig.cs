using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.GameplayAbilitySystem;

namespace RPG.SkillSystem
{
    /// <summary>保存技能时间轴中全部单帧 Projectile 发射事件。</summary>
    [TimelineTrack("投射物轨道", 15, false)]
    public sealed class ProjectileTrackConfig : TrackConfigBase
    {
        #region 字段与属性

        [SerializeField] private List<ProjectileSkillClipConfig> clips = new();

        /// <summary>获取按物理顺序保存的 Projectile 发射片段。</summary>
        public IReadOnlyList<ProjectileSkillClipConfig> Clips => clips;

        /// <inheritdoc />
        public override IReadOnlyList<TimelineItemConfigBase> Items => clips;

        #endregion
    }

    /// <summary>保存指定逻辑帧一次性释放整组投射物的时间轴内容。</summary>
    [Serializable]
    public sealed class ProjectileSkillClipConfig : TimelineItemConfigBase
    {
        #region 字段与属性

        [SerializeField, ReadOnly, LabelText("内容 ID")] private string id = string.Empty;
        [SerializeField, Min(0), LabelText("发射帧")] private int startFrame;
        [SerializeField, LabelText("投射物设置")] private ProjectileSpawnConfig spawnConfig = new();

        /// <inheritdoc />
        public override string Id => id;

        /// <inheritdoc />
        public override int StartFrame => startFrame;

        /// <inheritdoc />
        public override int DurationFrames => 1;

        /// <summary>获取当前帧一次性发射使用的 Projectile 配置。</summary>
        public ProjectileSpawnConfig SpawnConfig => spawnConfig;

        #endregion
    }
}
