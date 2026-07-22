using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace RPG.SkillSystem
{
    #region Skill asset

    /// <summary>
    /// 保存技能时间轴的运行时帧参数和各类强类型轨道数据。
    /// </summary>
    [CreateAssetMenu(fileName = "SkillConfig", menuName = "RPG/Skill/Skill Config")]
    public sealed class SkillConfig : ScriptableObject
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField, Min(1)] private int frameRate = 30;
        [SerializeField, Min(1)] private int durationFrames = 1;
        [SerializeField] private List<AnimationTrackConfig> animationTracks = new();
        [SerializeField] private List<VfxTrackConfig> vfxTracks = new();
        [SerializeField] private List<AudioTrackConfig> audioTracks = new();
        [SerializeField] private List<EventTrackConfig> eventTracks = new();

        public string Id => id;
        public int FrameRate => frameRate;
        public int DurationFrames => durationFrames;
        public IReadOnlyList<AnimationTrackConfig> AnimationTracks => animationTracks;
        public IReadOnlyList<VfxTrackConfig> VfxTracks => vfxTracks;
        public IReadOnlyList<AudioTrackConfig> AudioTracks => audioTracks;
        public IReadOnlyList<EventTrackConfig> EventTracks => eventTracks;
    }

    #endregion

    #region Track configurations

    [Serializable]
    public sealed class SkillTrackHeader
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField] private string displayName = "新轨道";
        [SerializeField] private bool muted;

#if UNITY_EDITOR
        [SerializeField] private bool editorLocked;
        [SerializeField] private Color editorColor = Color.white;
#endif

        public string Id => id;
        public string DisplayName => displayName;
        public bool Muted => muted;

#if UNITY_EDITOR
        public bool EditorLocked => editorLocked;
        public Color EditorColor => editorColor;
#endif
    }

    [Serializable]
    public sealed class AnimationTrackConfig
    {
        [SerializeField] private SkillTrackHeader header = new();
        [SerializeField] private List<AnimationSkillClipConfig> clips = new();

        public SkillTrackHeader Header => header;
        public IReadOnlyList<AnimationSkillClipConfig> Clips => clips;
    }

    [Serializable]
    public sealed class VfxTrackConfig
    {
        [SerializeField] private SkillTrackHeader header = new();
        [SerializeField] private List<VfxSkillClipConfig> clips = new();

        public SkillTrackHeader Header => header;
        public IReadOnlyList<VfxSkillClipConfig> Clips => clips;
    }

    /// <summary>
    /// 保存一条音频轨道的公共轨道头和音频片段列表。
    /// </summary>
    [Serializable]
    public sealed class AudioTrackConfig
    {
        [SerializeField] private SkillTrackHeader header = new();
        [SerializeField] private List<AudioSkillClipConfig> clips = new();

        public SkillTrackHeader Header => header;
        public IReadOnlyList<AudioSkillClipConfig> Clips => clips;
    }

    [Serializable]
    public sealed class EventTrackConfig
    {
        [SerializeField] private SkillTrackHeader header = new();
        [SerializeField] private List<SkillEventMarkerConfig> markers = new();

        public SkillTrackHeader Header => header;
        public IReadOnlyList<SkillEventMarkerConfig> Markers => markers;
    }
    #endregion

    #region Timeline content

    [Serializable]
    public sealed class AnimationSkillClipConfig
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField] private AnimationClip animationClip;
        [SerializeField, Min(0)] private int startFrame;
        [SerializeField, Min(1)] private int durationFrames = 1;
        [SerializeField, Min(0)] private int sourceStartFrame;
        [SerializeField, Min(0.01f)] private float playbackSpeed = 1f;

        public string Id => id;
        public AnimationClip AnimationClip => animationClip;
        public int StartFrame => startFrame;
        public int DurationFrames => durationFrames;
        public int EndFrame => startFrame + durationFrames;
        public int SourceStartFrame => sourceStartFrame;
        public float PlaybackSpeed => playbackSpeed;
    }

    public enum VfxFollowMode
    {
        FollowBinding,
        KeepWorldPosition
    }

    public enum VfxStopMode
    {
        ReturnToPoolAtEnd,
        StopEmissionAtEnd,
        KeepAlive
    }

    [Serializable]
    public sealed class VfxSkillClipConfig
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField] private GameObject prefab;
        [SerializeField, Min(0)] private int startFrame;
        [SerializeField, Min(1)] private int durationFrames = 1;
        [SerializeField] private string bindingPath = string.Empty;
        [SerializeField] private Vector3 localPosition;
        [SerializeField] private Vector3 localEulerAngles;
        [SerializeField] private Vector3 localScale = Vector3.one;
        [SerializeField] private VfxFollowMode followMode;
        [SerializeField] private VfxStopMode stopMode;

        public string Id => id;
        public GameObject Prefab => prefab;
        public int StartFrame => startFrame;
        public int DurationFrames => durationFrames;
        public int EndFrame => startFrame + durationFrames;
        public string BindingPath => bindingPath;
        public Vector3 LocalPosition => localPosition;
        public Vector3 LocalEulerAngles => localEulerAngles;
        public Vector3 LocalScale => localScale;
        public VfxFollowMode FollowMode => followMode;
        public VfxStopMode StopMode => stopMode;
    }

    /// <summary>
    /// 保存音频素材、半开帧区间、音量和播放音调等运行时数据。
    /// </summary>
    [Serializable]
    public sealed class AudioSkillClipConfig
    {
        [SerializeField] private string id = string.Empty;
        [FormerlySerializedAs("clip"), SerializeField] private AudioClip audioClip;
        [SerializeField, Min(0)] private int startFrame;
        [SerializeField, Min(1)] private int durationFrames = 1;
        [SerializeField, Range(0f, 1f)] private float volume = 1f;
        [FormerlySerializedAs("playbackSpeed"), SerializeField, Min(0.01f)] private float pitch = 1f;

        public string Id => id;
        public AudioClip AudioClip => audioClip;
        public int StartFrame => startFrame;
        public int DurationFrames => durationFrames;
        public int EndFrame => startFrame + durationFrames;
        public float Volume => volume;
        public float Pitch => pitch;
    }

    [Serializable]
    public sealed class SkillEventMarkerConfig
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField, Min(0)] private int frame;
        [SerializeField] private string eventTypeName = string.Empty;
        [SerializeField] private string displayName = "事件";
        [SerializeField, TextArea] private string parameterText = string.Empty;

        public string Id => id;
        public int Frame => frame;
        public string EventTypeName => eventTypeName;
        public string DisplayName => displayName;
        public string ParameterText => parameterText;
    }

    #endregion
}
