using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.SkillSystem
{
    /// <summary>
    /// 保存动画素材、半开帧区间、源偏移和播放速度。
    /// </summary>
    [Serializable]
    public sealed class AnimationSkillClipConfig : TimelineItemConfigBase
    {
        [SerializeField, ReadOnly, LabelText("内容 ID")] private string id = string.Empty;
        [SerializeField] private AnimationClip animationClip;
        [SerializeField, Min(0)] private int startFrame;
        [SerializeField, Min(1)] private int durationFrames = 1;
        [SerializeField, Min(0)] private int sourceStartFrame;
        [SerializeField, Min(0.01f)] private float playbackSpeed = 1f;
        [SerializeField, Min(0f)] private float fadeDuration = 0.1f;

        public override string Id => id;
        public AnimationClip AnimationClip => animationClip;
        public override int StartFrame => startFrame;
        public override int DurationFrames => durationFrames;
        public int SourceStartFrame => sourceStartFrame;
        public float PlaybackSpeed => playbackSpeed;
        public float FadeDuration => fadeDuration;
    }
}
