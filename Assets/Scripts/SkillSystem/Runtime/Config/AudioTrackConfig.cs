using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Serialization;

namespace RPG.SkillSystem
{
    /// <summary>
    /// 保存一条音频轨道的公共轨道数据和音频片段列表。
    /// </summary>
    [TimelineTrack("音频轨道", 30)]
    public sealed class AudioTrackConfig : TrackConfigBase
    {
        [SerializeField] private List<AudioSkillClipConfig> clips = new();

        public IReadOnlyList<AudioSkillClipConfig> Clips => clips;
        public override IReadOnlyList<TimelineItemConfigBase> Items => clips;
    }

    /// <summary>
    /// 保存音频素材、半开帧区间、音量和播放音调等运行时数据。
    /// </summary>
    [Serializable]
    public sealed class AudioSkillClipConfig : TimelineItemConfigBase
    {
        [SerializeField, ReadOnly, LabelText("内容 ID")] private string id = string.Empty;
        [FormerlySerializedAs("clip"), SerializeField] private AudioClip audioClip;
        [SerializeField, Min(0)] private int startFrame;
        [SerializeField, Min(1)] private int durationFrames = 1;
        [SerializeField, Range(0f, 1f)] private float volume = 1f;
        [FormerlySerializedAs("playbackSpeed"), SerializeField, Min(0.01f)] private float pitch = 1f;

        public override string Id => id;
        public AudioClip AudioClip => audioClip;
        public override int StartFrame => startFrame;
        public override int DurationFrames => durationFrames;
        public float Volume => volume;
        public float Pitch => pitch;
    }
}