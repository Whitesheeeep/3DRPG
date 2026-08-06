using System.Collections.Generic;
using UnityEngine;

namespace RPG.SkillSystem
{
    /// <summary>
    /// 保存一条动画轨道的公共轨道数据和动画片段列表。
    /// </summary>
    [TimelineTrack("动画轨道", 0)]
    public sealed class AnimationTrackConfig : TrackConfigBase
    {
        [SerializeField] private List<AnimationSkillClipConfig> clips = new();

        public IReadOnlyList<AnimationSkillClipConfig> Clips => clips;
        public override IReadOnlyList<TimelineItemConfigBase> Items => clips;
    }
}