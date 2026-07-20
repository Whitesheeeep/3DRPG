#if UNITY_EDITOR
using UnityEngine;

/**************************************
 * 时间轴编辑请求相关类型定义，每次请求编辑时处理，避免直接操作数据
 */

namespace RPG.SkillSystem.Editor
{
    internal readonly struct AnimationClipEditRequest
    {
        public AnimationClip AnimationClip { get; }
        public int StartFrame { get; }
        public int DurationFrames { get; }
        public int SourceStartFrame { get; }
        public float PlaybackSpeed { get; }

        /// <summary>
        /// 创建并初始化 AnimationClipEditRequest。
        /// </summary>
        public AnimationClipEditRequest(AnimationClip animationClip, int startFrame, int durationFrames,
            int sourceStartFrame, float playbackSpeed)
        {
            AnimationClip = animationClip;
            StartFrame = startFrame;
            DurationFrames = durationFrames;
            SourceStartFrame = sourceStartFrame;
            PlaybackSpeed = playbackSpeed;
        }
    }

    internal readonly struct VfxClipEditRequest
    {
        public GameObject Prefab { get; }
        public int StartFrame { get; }
        public int DurationFrames { get; }
        public string BindingPath { get; }
        public Vector3 LocalPosition { get; }
        public Vector3 LocalEulerAngles { get; }
        public Vector3 LocalScale { get; }
        public VfxFollowMode FollowMode { get; }
        public VfxStopMode StopMode { get; }

        /// <summary>
        /// 创建并初始化 VfxClipEditRequest。
        /// </summary>
        public VfxClipEditRequest(GameObject prefab, int startFrame, int durationFrames, string bindingPath,
            Vector3 localPosition, Vector3 localEulerAngles, Vector3 localScale,
            VfxFollowMode followMode, VfxStopMode stopMode)
        {
            Prefab = prefab;
            StartFrame = startFrame;
            DurationFrames = durationFrames;
            BindingPath = bindingPath;
            LocalPosition = localPosition;
            LocalEulerAngles = localEulerAngles;
            LocalScale = localScale;
            FollowMode = followMode;
            StopMode = stopMode;
        }
    }

    internal readonly struct EventMarkerEditRequest
    {
        public int Frame { get; }
        public string EventTypeName { get; }
        public string DisplayName { get; }
        public string ParameterText { get; }

        /// <summary>
        /// 创建并初始化 EventMarkerEditRequest。
        /// </summary>
        public EventMarkerEditRequest(int frame, string eventTypeName, string displayName, string parameterText)
        {
            Frame = frame;
            EventTypeName = eventTypeName;
            DisplayName = displayName;
            ParameterText = parameterText;
        }
    }
}
#endif
