#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 创建窗口私有的 Audio 轨道预览处理器。
    /// </summary>
    internal sealed class AudioPreviewFactory : ITrackPreviewFactory
    {
        /// <summary>
        /// 每次调用都创建独立处理器，避免多个时间轴窗口共享音频 Graph。
        /// </summary>
        public ITrackPreviewHandler Create() => new AudioPreviewHandler();
    }

    /// <summary>
    /// 根据采样原因重建或推进窗口私有音频 Voice，手动拖帧期间始终保持静音。
    /// </summary>
    internal sealed class AudioPreviewHandler : ITrackPreviewHandler
    {
        #region 播放状态

        private readonly AudioPreviewGraph previewGraph = new();
        private readonly HashSet<string> audibleIds = new();
        private readonly List<string> removalBuffer = new();
        private int previousFrame = -1;
        private bool playing;
        private bool disposed;

        #endregion

        #region 生命周期

        /// <summary>
        /// 销毁 PlayableGraph、AudioSource 和全部 Voice。
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            previewGraph.Dispose();
            audibleIds.Clear();
            removalBuffer.Clear();
        }

        #endregion

        #region 预览操作

        /// <summary>
        /// 配置内容变化后停止旧音频；播放中的刷新会以 PlaybackStart 从当前帧重建。
        /// </summary>
        public void Invalidate() => Stop();

        /// <summary>
        /// Scrub 保持静音，PlaybackStart 重建有效 Voice，PlaybackAdvance 仅同步跨过的边界。
        /// </summary>
        public void SampleFrame(in PreviewFrameContext context)
        {
            if (disposed || context.Config == null) return;
            switch (context.Reason)
            {
                case PreviewSampleReason.Scrub:
                    Stop();
                    break;
                case PreviewSampleReason.PlaybackStart:
                    RebuildPlayback(context);
                    break;
                case PreviewSampleReason.PlaybackAdvance:
                    if (!playing || context.Frame < previousFrame)
                        RebuildPlayback(context);
                    else
                        AdvancePlayback(context);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        /// <summary>
        /// 停止并释放全部声音资源，但保留 Handler 以便再次播放。
        /// </summary>
        public void Stop()
        {
            playing = false;
            previousFrame = -1;
            previewGraph.StopAll();
            audibleIds.Clear();
            removalBuffer.Clear();
        }

        /// <summary>
        /// 清理行为与停止一致，确保切换角色或 Config 时没有声音残留。
        /// </summary>
        public void Clear() => Stop();

        #endregion

        #region Voice 同步

        // 从当前播放帧重新创建全部有效音频，使中途播放和手动重新定位使用准确源偏移。
        private void RebuildPlayback(in PreviewFrameContext context)
        {
            previewGraph.StopAll();
            audibleIds.Clear();
            StartAudibleClips(context, false);
            previousFrame = context.Frame;
            playing = true;
        }

        // 保留仍有效的 Voice，停止越过结束边界的 Voice，并启动本次跨帧范围内的新 Clip。
        private void AdvancePlayback(in PreviewFrameContext context)
        {
            audibleIds.Clear();
            StartAudibleClips(context, true);
            removalBuffer.Clear();
            foreach (string key in previewGraph.VoiceKeys)
            {
                if (!audibleIds.Contains(key)) removalBuffer.Add(key);
            }
            foreach (string key in removalBuffer)
                previewGraph.RemoveVoice(key);

            previousFrame = context.Frame;
            playing = true;
        }

        // 枚举未静音轨道；重建时启动全部当前有效 Clip，推进时只启动跨过起始帧的新 Clip。
        private void StartAudibleClips(in PreviewFrameContext context, bool onlyCrossedStarts)
        {
            foreach (AudioTrackConfig track in context.Config.AudioTracks)
            {
                if (track?.Header == null || track.Header.Muted) continue;
                foreach (AudioSkillClipConfig clip in track.Clips)
                {
                    if (!IsAudibleAt(clip, context.Frame, context.Config.FrameRate)) continue;
                    string key = GetStableKey(clip);
                    audibleIds.Add(key);
                    if (previewGraph.ContainsVoice(key)) continue;
                    if (onlyCrossedStarts &&
                        (clip.StartFrame <= previousFrame || clip.StartFrame > context.Frame))
                        continue;
                    previewGraph.StartVoice(key, clip, context.Frame, context.Config.FrameRate);
                }
            }
        }

        // 同时遵守时间轴半开区间和源 AudioClip 经 Pitch 换算后的实际末尾。
        private static bool IsAudibleAt(AudioSkillClipConfig clip, int frame, int frameRate)
        {
            if (clip?.AudioClip == null || frame < clip.StartFrame || frame >= clip.EndFrame) return false;
            double sourceTime = CalculateSourceTime(clip, frame, frameRate);
            return sourceTime < clip.AudioClip.length;
        }

        // 时间轴经过秒数乘以 Pitch 得到 AudioClip 中的绝对源时间。
        internal static double CalculateSourceTime(AudioSkillClipConfig clip, int frame, int frameRate) =>
            Mathf.Max(0, frame - clip.StartFrame) / (double)Mathf.Max(1, frameRate) *
            Math.Max(0.01f, clip.Pitch);

        // 正常资产使用稳定 GUID；异常空 GUID 仅生成窗口内临时键，不修改 SkillConfig。
        private static string GetStableKey(AudioSkillClipConfig clip) =>
            !string.IsNullOrEmpty(clip.Id)
                ? clip.Id
                : $"runtime:{RuntimeHelpers.GetHashCode(clip)}";

        #endregion
    }

    /// <summary>
    /// 管理一个 DSPClock PlayableGraph、隐藏 AudioSource、Mixer 和当前全部 Audio Voice。
    /// </summary>
    internal sealed class AudioPreviewGraph : IDisposable
    {
        #region Graph 状态

        private readonly Dictionary<string, AudioPreviewVoice> voices = new();
        private GameObject host;
        private AudioSource audioSource;
        private PlayableGraph graph;
        private AudioMixerPlayable mixer;
        private bool disposed;

        internal IEnumerable<string> VoiceKeys => voices.Keys;

        #endregion

        #region 生命周期

        /// <summary>
        /// 销毁 Graph 和不可保存 AudioSource 宿主。
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            StopAll();
        }

        // 延迟创建 Graph，避免仅拖动播放头时产生任何音频对象。
        private void EnsureGraph()
        {
            if (graph.IsValid()) return;
            host = new GameObject("技能时间轴音频预览")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            audioSource = host.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f;
            audioSource.volume = 1f;

            graph = PlayableGraph.Create("Skill Timeline Audio Preview");
            graph.SetTimeUpdateMode(DirectorUpdateMode.DSPClock);
            mixer = AudioMixerPlayable.Create(graph, 0, true);
            AudioPlayableOutput output = AudioPlayableOutput.Create(graph, "Skill Timeline Audio", audioSource);
            output.SetSourcePlayable(mixer);
            graph.Play();
        }

        #endregion

        #region Voice 操作

        // 查询指定 GUID 是否已经拥有正在管理的 Voice。
        internal bool ContainsVoice(string key) => voices.ContainsKey(key);

        // 创建带独立 Pitch、源偏移和 Mixer 音量权重的 AudioClipPlayable。
        internal void StartVoice(string key, AudioSkillClipConfig clip, int frame, int frameRate)
        {
            if (disposed || clip?.AudioClip == null || voices.ContainsKey(key)) return;
            EnsureGraph();
            int inputPort = mixer.GetInputCount();
            mixer.SetInputCount(inputPort + 1);
            AudioClipPlayable playable = AudioClipPlayable.Create(graph, clip.AudioClip, false);
            playable.SetTime(AudioPreviewHandler.CalculateSourceTime(clip, frame, frameRate));
            playable.SetSpeed(Mathf.Max(0.01f, clip.Pitch));
            graph.Connect(playable, 0, mixer, inputPort);
            mixer.SetInputWeight(inputPort, Mathf.Clamp01(clip.Volume));
            playable.Play();
            voices.Add(key, new AudioPreviewVoice(playable, inputPort));
        }

        // 断开一个 Voice 并销毁其子图，Mixer 的空端口留待整个 Graph 重建时回收。
        internal void RemoveVoice(string key)
        {
            if (!voices.Remove(key, out AudioPreviewVoice voice) || !graph.IsValid()) return;
            if (mixer.IsValid() && voice.InputPort < mixer.GetInputCount())
                graph.Disconnect(mixer, voice.InputPort);
            if (voice.Playable.IsValid()) graph.DestroySubgraph(voice.Playable);
        }

        // 先销毁 PlayableGraph 再销毁 AudioSource 宿主，保证暂停、切换和关闭窗口时不残留声音。
        internal void StopAll()
        {
            voices.Clear();
            if (graph.IsValid()) graph.Destroy();
            graph = default;
            mixer = default;
            audioSource = null;
            if (host != null) Object.DestroyImmediate(host);
            host = null;
        }

        #endregion
    }

    /// <summary>
    /// 保存一个 AudioClipPlayable 及其在共享 Mixer 中的输入端口。
    /// </summary>
    internal readonly struct AudioPreviewVoice
    {
        public AudioClipPlayable Playable { get; }
        public int InputPort { get; }

        // 创建不可变 Voice 句柄，实际资源生命周期由 AudioPreviewGraph 统一管理。
        internal AudioPreviewVoice(AudioClipPlayable playable, int inputPort)
        {
            Playable = playable;
            InputPort = inputPort;
        }
    }
}
#endif