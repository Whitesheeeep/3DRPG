#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

/**************************************
 * 时间轴编辑请求相关类型定义，每次请求编辑时处理，避免直接操作数据
 */

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 描述从 Project 批量创建动画片段所需的稳定素材集合与时间轴落点帧。
    /// </summary>
    internal readonly struct AnimationCreateRequest : IItemCreateRequest
    {
        public IReadOnlyList<AnimationClip> Clips { get; }
        public int StartFrame { get; }

        /// <summary>
        /// 创建动画素材批量创建请求，并复制集合以隔离 DragAndDrop 生命周期。
        /// </summary>
        public AnimationCreateRequest(IReadOnlyList<AnimationClip> clips, int startFrame)
        {
            Clips = CopyItems(clips);
            StartFrame = startFrame;
        }

        // 复制拖拽资源引用，避免 Unity 在 DragPerform 结束后替换原始集合。
        private static AnimationClip[] CopyItems(IReadOnlyList<AnimationClip> clips)
        {
            if (clips == null || clips.Count == 0) return Array.Empty<AnimationClip>();
            AnimationClip[] copy = new AnimationClip[clips.Count];
            for (int index = 0; index < clips.Count; index++) copy[index] = clips[index];
            return copy;
        }
    }

    /// <summary>
    /// 描述从 Project 批量创建特效片段所需的 Prefab、时间轴落点帧和默认持续帧。
    /// </summary>
    internal readonly struct VfxCreateRequest : IItemCreateRequest
    {
        public IReadOnlyList<GameObject> Prefabs { get; }
        public int StartFrame { get; }
        public int DurationFrames { get; }

        /// <summary>
        /// 创建特效素材批量创建请求，并复制集合以隔离 DragAndDrop 生命周期。
        /// </summary>
        public VfxCreateRequest(IReadOnlyList<GameObject> prefabs, int startFrame, int durationFrames)
        {
            Prefabs = CopyItems(prefabs);
            StartFrame = startFrame;
            DurationFrames = durationFrames;
        }

        // 复制拖拽资源引用，避免 Unity 在 DragPerform 结束后替换原始集合。
        private static GameObject[] CopyItems(IReadOnlyList<GameObject> prefabs)
        {
            if (prefabs == null || prefabs.Count == 0) return Array.Empty<GameObject>();
            GameObject[] copy = new GameObject[prefabs.Count];
            for (int index = 0; index < prefabs.Count; index++) copy[index] = prefabs[index];
            return copy;
        }
    }

    /// <summary>
    /// 描述从 Project 批量创建音频片段所需的稳定素材集合、时间轴落点、默认音量和音调。
    /// </summary>
    internal readonly struct AudioCreateRequest : IItemCreateRequest
    {
        public IReadOnlyList<AudioClip> AudioClips { get; }
        public int StartFrame { get; }
        public float Volume { get; }
        public float Pitch { get; }

        /// <summary>
        /// 创建音频素材批量创建请求，并复制集合以隔离 DragAndDrop 生命周期。
        /// </summary>
        public AudioCreateRequest(IReadOnlyList<AudioClip> audioClips, int startFrame,
            float volume = 1f, float pitch = 1f)
        {
            AudioClips = CopyItems(audioClips);
            StartFrame = startFrame;
            Volume = volume;
            Pitch = pitch;
        }

        // 复制拖拽资源引用，避免 Unity 在 DragPerform 结束后替换原始集合。
        private static AudioClip[] CopyItems(IReadOnlyList<AudioClip> audioClips)
        {
            if (audioClips == null || audioClips.Count == 0) return Array.Empty<AudioClip>();
            AudioClip[] copy = new AudioClip[audioClips.Count];
            for (int index = 0; index < audioClips.Count; index++) copy[index] = audioClips[index];
            return copy;
        }
    }

    /// <summary>
    /// 描述动画片段 Inspector 提交的一次完整字段编辑请求。
    /// </summary>
    internal readonly struct AnimationEditRequest : IItemEditRequest
    {
        public AnimationClip AnimationClip { get; }
        public int StartFrame { get; }
        public int DurationFrames { get; }
        public int SourceStartFrame { get; }
        public float PlaybackSpeed { get; }

        /// <summary>
        /// 创建并初始化 AnimationEditRequest。
        /// </summary>
        public AnimationEditRequest(AnimationClip animationClip, int startFrame, int durationFrames,
            int sourceStartFrame, float playbackSpeed)
        {
            AnimationClip = animationClip;
            StartFrame = startFrame;
            DurationFrames = durationFrames;
            SourceStartFrame = sourceStartFrame;
            PlaybackSpeed = playbackSpeed;
        }
    }

    /// <summary>
    /// 描述特效片段 Inspector 提交的一次完整字段编辑请求。
    /// </summary>
    internal readonly struct VfxEditRequest : IItemEditRequest
    {
        public GameObject Prefab { get; }
        public int StartFrame { get; }
        public int DurationFrames { get; }
        public Vector3 LocalPosition { get; }
        public Vector3 LocalEulerAngles { get; }
        public Vector3 LocalScale { get; }
        public VfxFollowMode FollowMode { get; }
        public VfxStopMode StopMode { get; }

        /// <summary>
        /// 创建并初始化 VfxEditRequest。
        /// </summary>
        public VfxEditRequest(GameObject prefab, int startFrame, int durationFrames,
            Vector3 localPosition, Vector3 localEulerAngles, Vector3 localScale,
            VfxFollowMode followMode, VfxStopMode stopMode)
        {
            Prefab = prefab;
            StartFrame = startFrame;
            DurationFrames = durationFrames;
            LocalPosition = localPosition;
            LocalEulerAngles = localEulerAngles;
            LocalScale = localScale;
            FollowMode = followMode;
            StopMode = stopMode;
        }
    }

    /// <summary>
    /// 描述攻击检测片段 Inspector 提交的一次完整字段编辑请求。
    /// </summary>
    internal readonly struct AttackDetectionEditRequest : IItemEditRequest
    {
        public int StartFrame { get; }
        public int DurationFrames { get; }
        public int SampleIntervalFrames { get; }
        public AttackDetectionDataBase DetectionData { get; }

        /// <summary>
        /// 创建攻击检测编辑请求，并复制 managed reference 数据以隔离 Inspector 临时状态。
        /// </summary>
        public AttackDetectionEditRequest(int startFrame, int durationFrames,
            int sampleIntervalFrames, AttackDetectionDataBase detectionData)
        {
            StartFrame = startFrame;
            DurationFrames = durationFrames;
            SampleIntervalFrames = sampleIntervalFrames;
            DetectionData = AttackDetectionDataBase.Copy(detectionData);
        }
    }

    /// <summary>
    /// 描述事件标记 Inspector 提交的一次完整字段编辑请求。
    /// </summary>
    internal readonly struct EventEditRequest : IItemEditRequest
    {
        public int Frame { get; }
        public string EventTypeName { get; }
        public string DisplayName { get; }
        public string ParameterText { get; }

        /// <summary>
        /// 创建并初始化 EventEditRequest。
        /// </summary>
        public EventEditRequest(int frame, string eventTypeName, string displayName, string parameterText)
        {
            Frame = frame;
            EventTypeName = eventTypeName;
            DisplayName = displayName;
            ParameterText = parameterText;
        }
    }

    /// <summary>
    /// 描述音频片段 Inspector 提交的一次完整字段编辑请求。
    /// </summary>
    internal readonly struct AudioEditRequest : IItemEditRequest
    {
        public AudioClip AudioClip { get; }
        public int StartFrame { get; }
        public int DurationFrames { get; }
        public float Volume { get; }
        public float Pitch { get; }

        /// <summary>
        /// 创建并初始化 AudioEditRequest。
        /// </summary>
        public AudioEditRequest(AudioClip audioClip, int startFrame, int durationFrames, float volume, float pitch)
        {
            AudioClip = audioClip;
            StartFrame = startFrame;
            DurationFrames = durationFrames;
            Volume = volume;
            Pitch = pitch;
        }
    }


}
#endif
