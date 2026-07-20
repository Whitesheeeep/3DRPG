#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 控制技能时间轴的播放状态、当前帧和编辑器时钟更新。
    /// </summary>
    internal sealed class SkillTimelinePlaybackController : IDisposable
    {
        #region Clock state and events

        private readonly ISkillTimelinePreview preview;
        private SkillConfig config;
        private double lastUpdateTime;
        private double accumulatedFrames;
        private bool disposed;

        public event Action<int> FrameChanged;
        public event Action PlaybackChanged;

        public int CurrentFrame { get; private set; }
        public bool IsPlaying { get; private set; }

        #endregion

        #region Lifecycle and playback commands

        /// <summary>
        /// 创建并初始化 SkillTimelinePlaybackController。
        /// </summary>
        public SkillTimelinePlaybackController(ISkillTimelinePreview preview = null)
        {
            this.preview = preview;
            EditorApplication.update += OnEditorUpdate;
        }

        /// <summary>
        /// 释放事件订阅和该对象持有的编辑器资源。
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            EditorApplication.update -= OnEditorUpdate;
            preview?.Dispose();
            FrameChanged = null;
            PlaybackChanged = null;
        }

        /// <summary>
        /// 切换播放控制器使用的配置并复位播放头。
        /// </summary>
        public void SetSkillConfig(SkillConfig skillConfig)
        {
            Pause();
            config = skillConfig;
            preview?.SetSkillConfig(skillConfig);
            Seek(0);
        }

        /// <summary>
        /// 从当前帧开始播放时间轴。
        /// </summary>
        public void Play()
        {
            if (config == null || IsPlaying) return;
            if (CurrentFrame >= config.DurationFrames - 1) Seek(0);
            IsPlaying = true;
            accumulatedFrames = 0d;
            lastUpdateTime = EditorApplication.timeSinceStartup;
            PlaybackChanged?.Invoke();
        }

        /// <summary>
        /// 暂停时间轴并保留当前帧。
        /// </summary>
        public void Pause()
        {
            if (!IsPlaying) return;
            IsPlaying = false;
            accumulatedFrames = 0d;
            preview?.Stop();
            PlaybackChanged?.Invoke();
        }

        /// <summary>
        /// 停止播放并将播放头复位到第 0 帧。
        /// </summary>
        public void Stop()
        {
            bool wasPlaying = IsPlaying;
            IsPlaying = false;
            accumulatedFrames = 0d;
            preview?.Stop();
            Seek(0);
            if (wasPlaying) PlaybackChanged?.Invoke();
        }

        /// <summary>
        /// 有技能时按技能末帧夹紧；空技能时保存非负虚拟帧且不触发 Preview。
        /// </summary>
        public void Seek(int frame)
        {
            frame = config != null
                ? Mathf.Clamp(frame, 0, Mathf.Max(0, config.DurationFrames - 1))
                : Mathf.Max(0, frame);
            if (CurrentFrame == frame)
            {
                if (config != null) preview?.SampleFrame(frame);
                return;
            }

            CurrentFrame = frame;
            if (config != null) preview?.SampleFrame(frame);
            FrameChanged?.Invoke(frame);
        }

        /// <summary>
        /// 将播放头移动到上一帧。
        /// </summary>
        public void StepPreviousFrame()
        {
            Pause();
            Seek(CurrentFrame - 1);
        }

        /// <summary>
        /// 将播放头移动到下一帧。
        /// </summary>
        public void StepNextFrame()
        {
            Pause();
            Seek(CurrentFrame + 1);
        }

        /// <summary>
        /// 根据当前配置总帧数夹紧播放头。
        /// </summary>
        public void ClampToDuration() => Seek(CurrentFrame);

        #endregion

        #region Editor clock

        // 根据编辑器时钟推进整数帧；到达技能末帧后停止且不循环。
        private void OnEditorUpdate()
        {
            if (!IsPlaying || config == null) return;
            double now = EditorApplication.timeSinceStartup;
            double delta = Math.Max(0d, now - lastUpdateTime);
            lastUpdateTime = now;
            accumulatedFrames += delta * config.FrameRate;
            int advance = (int)Math.Floor(accumulatedFrames);
            if (advance <= 0) return;
            accumulatedFrames -= advance;
            int target = CurrentFrame + advance;
            int lastFrame = Mathf.Max(0, config.DurationFrames - 1);
            if (target >= lastFrame)
            {
                CurrentFrame = lastFrame;
                preview?.SampleFrame(CurrentFrame);
                FrameChanged?.Invoke(CurrentFrame);
                IsPlaying = false;
                accumulatedFrames = 0d;
                preview?.Stop();
                PlaybackChanged?.Invoke();
                return;
            }
            Seek(target);
        }

        #endregion
    }
}
#endif
