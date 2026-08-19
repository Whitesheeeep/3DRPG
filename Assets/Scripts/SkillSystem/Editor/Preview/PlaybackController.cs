#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 控制技能时间轴的播放状态、当前帧和编辑器时钟更新。
    /// </summary>
    internal sealed class PlaybackController : IDisposable
    {
        #region Clock state and events

        private const float MinimumPlaybackSpeed = 0.1f;
        private const float MaximumPlaybackSpeed = 2f;
        private readonly IPreview preview;
        private SkillConfig config;
        private double lastUpdateTime;
        private double accumulatedFrames;
        private bool disposed;

        public event Action<int> FrameChanged;
        public event Action PlaybackChanged;
        public event Action<string> PreviewStatusChanged;

        public int CurrentFrame { get; private set; }
        public bool IsPlaying { get; private set; }
        public bool IsLooping { get; private set; }
        public float PlaybackSpeed { get; private set; } = 1f;

        #endregion

        #region Lifecycle and playback commands

        /// <summary>
        /// 创建并初始化 PlaybackController。
        /// </summary>
        public PlaybackController(IPreview preview = null)
        {
            this.preview = preview;
            if (this.preview != null) this.preview.StatusChanged += OnPreviewStatusChanged;
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
            if (preview != null) preview.StatusChanged -= OnPreviewStatusChanged;
            preview?.Dispose();
            FrameChanged = null;
            PlaybackChanged = null;
            PreviewStatusChanged = null;
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
            preview?.SampleFrame(CurrentFrame, PreviewSampleReason.PlaybackStart);
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
        /// 设置是否在到达技能末帧后从第 0 帧继续播放。
        /// </summary>
        /// <param name="value">是否循环播放整个技能。</param>
        public void SetLooping(bool value)
        {
            if (IsLooping == value) return;
            IsLooping = value;
            PlaybackChanged?.Invoke();
        }

        /// <summary>
        /// 设置窗口预览时钟倍率；该状态不写入技能资产或 Undo。
        /// </summary>
        /// <param name="value">目标倍率，最终夹紧到 0.1 至 2。</param>
        public void SetPlaybackSpeed(float value)
        {
            value = Mathf.Clamp(value, MinimumPlaybackSpeed, MaximumPlaybackSpeed);
            if (Mathf.Approximately(PlaybackSpeed, value)) return;

            PlaybackSpeed = value;
            // 播放中切换倍率时从当前时刻继续累计，避免旧倍率区间被新倍率重复计算。
            if (IsPlaying) lastUpdateTime = EditorApplication.timeSinceStartup;
            PlaybackChanged?.Invoke();
        }

        /// <summary>
        /// 有技能时按技能末帧夹紧；空技能时保存非负虚拟帧且不触发 Preview。
        /// </summary>
        public void Seek(int frame)
        {
            frame = config != null
                ? Mathf.Clamp(frame, 0, Mathf.Max(0, config.DurationFrames - 1))
                : Mathf.Max(0, frame);
            PreviewSampleReason reason = IsPlaying
                ? PreviewSampleReason.PlaybackStart
                : PreviewSampleReason.Scrub;
            ApplyFrame(frame, reason);
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

        /// <summary>
        /// 将演示角色设置转交给可选 Preview 实现。
        /// </summary>
        public void SetPreviewActor(GameObject actor) => preview?.SetPreviewActor(actor);

        /// <summary>
        /// 将 Root Motion 设置转交给可选 Preview 实现。
        /// </summary>
        public void SetApplyRootMotion(bool value) => preview?.SetApplyRootMotion(value);

        /// <summary>
        /// 通知 Preview 当前 SkillConfig 内容已变化，并清除其派生缓存。
        /// </summary>
        public void InvalidatePreviewContent() => preview?.InvalidateContent();

        /// <summary>
        /// 使用当前播放头立即重新采样 Preview，不改变播放状态或当前帧。
        /// </summary>
        public void RefreshPreview()
        {
            if (config == null) return;
            preview?.SampleFrame(CurrentFrame, IsPlaying
                ? PreviewSampleReason.PlaybackStart
                : PreviewSampleReason.Scrub);
        }

        /// <summary>
        /// 清理 Preview 持有的场景对象和轨道资源，不改变当前 SkillConfig。
        /// </summary>
        public void ClearPreview() => preview?.Clear();
        #endregion

        #region Preview 状态

        // 将 Preview 的去重状态消息转发给 ViewModel，不让具体 Preview 依赖界面层。
        private void OnPreviewStatusChanged(string message) => PreviewStatusChanged?.Invoke(message);

        #endregion

        #region Editor clock
        /// <summary>
        /// 根据编辑器时钟和窗口播放倍率推进整数帧；循环时完整显示末帧，并在下一次越界推进时回到开头。
        /// </summary>
        private void OnEditorUpdate()
        {
            if (!IsPlaying || config == null) return;
            double now = EditorApplication.timeSinceStartup;
            double delta = Math.Max(0d, now - lastUpdateTime);
            lastUpdateTime = now;
            accumulatedFrames += delta * config.FrameRate * PlaybackSpeed;
            int advance = (int)Math.Floor(accumulatedFrames);
            if (advance <= 0) return;
            accumulatedFrames -= advance;
            int target = CurrentFrame + advance;
            int frameCount = Mathf.Max(1, config.DurationFrames);
            int lastFrame = frameCount - 1;
            if (target < lastFrame)
            {
                ApplyFrame(target, PreviewSampleReason.PlaybackAdvance);
                return;
            }
            if (!IsLooping)
            {
                ApplyFrame(lastFrame, PreviewSampleReason.PlaybackAdvance);
                IsPlaying = false;
                accumulatedFrames = 0d;
                preview?.Stop();
                PlaybackChanged?.Invoke();
                return;
            }
            if (target == lastFrame)
            {
                ApplyFrame(lastFrame, PreviewSampleReason.PlaybackAdvance);
                return;
            }
            int wrappedFrame = target % frameCount;
            ApplyFrame(wrappedFrame, PreviewSampleReason.PlaybackStart);
        }

        #endregion

        #region 帧状态辅助

        // 提交权威整数帧并使用明确采样原因刷新预览，避免播放时钟被误判为手动跳帧。
        private void ApplyFrame(int frame, PreviewSampleReason reason)
        {
            bool changed = CurrentFrame != frame;
            CurrentFrame = frame;
            if (config != null) preview?.SampleFrame(frame, reason);
            if (changed) FrameChanged?.Invoke(frame);
        }

        #endregion
    }
}
#endif
