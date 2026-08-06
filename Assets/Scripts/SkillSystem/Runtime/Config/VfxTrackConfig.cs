using System;
using System.Collections.Generic;
using RPG.Markers;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.SkillSystem
{
    /// <summary>
    /// 保存一条特效轨道的公共轨道数据和特效片段列表。
    /// </summary>
    [TimelineTrack("特效轨道", 20)]
    public sealed class VfxTrackConfig : TrackConfigBase
    {
        [SerializeField] private List<VfxSkillClipConfig> clips = new();

        public IReadOnlyList<VfxSkillClipConfig> Clips => clips;
        public override IReadOnlyList<TimelineItemConfigBase> Items => clips;
    }

    /// <summary>
    /// 指定特效在生成后是否持续跟随运行时传入的基准对象。
    /// </summary>
    public enum VfxFollowMode
    {
        FollowBinding,
        KeepWorldPosition
    }

    /// <summary>
    /// 指定特效到达 Clip 结束边界时的生命周期策略。
    /// </summary>
    public enum VfxStopMode
    {
        ReturnToPoolAtEnd,
        StopEmissionAtEnd,
        KeepAlive
    }

    /// <summary>
    /// 保存特效 Prefab、语义挂点、半开帧区间、局部变换、独立播放倍率和生命周期策略。
    /// </summary>
    [Serializable]
    public sealed class VfxSkillClipConfig : TimelineItemConfigBase
    {
        [SerializeField, ReadOnly, LabelText("内容 ID")] private string id = string.Empty;
        [SerializeField] private GameObject prefab;
        [SerializeField, LabelText("挂点")] private MarkerKey markerKey;
        [SerializeField, Min(0)] private int startFrame;
        [SerializeField, Min(1)] private int durationFrames = 1;
        [SerializeField] private Vector3 localPosition;
        [SerializeField] private Vector3 localEulerAngles;
        [SerializeField] private Vector3 localScale = Vector3.one;
        [SerializeField, Min(0.01f), LabelText("播放速度")] private float playbackSpeed = 1f;
        [SerializeField] private VfxFollowMode followMode;
        [SerializeField] private VfxStopMode stopMode;

        public override string Id => id;
        public GameObject Prefab => prefab;
        public MarkerKey MarkerKey => markerKey;
        public override int StartFrame => startFrame;
        public override int DurationFrames => durationFrames;
        public Vector3 LocalPosition => localPosition;
        public Vector3 LocalEulerAngles => localEulerAngles;
        public Vector3 LocalScale => localScale;
        public float PlaybackSpeed => playbackSpeed;
        public VfxFollowMode FollowMode => followMode;
        public VfxStopMode StopMode => stopMode;
    }
}