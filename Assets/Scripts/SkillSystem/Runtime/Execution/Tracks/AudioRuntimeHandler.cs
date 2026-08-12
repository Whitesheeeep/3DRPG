using System.Collections.Generic;
using WS_Modules.AudioSystem;

namespace RPG.SkillSystem
{
    /// <summary>
    /// 通过 AudioManager 直接 AudioClip API 播放技能音频，并按 Clip 排他结束边界停止长音频。
    /// </summary>
    internal sealed class AudioRuntimeHandler : TrackRuntimeHandler<AudioTrackConfig>
    {
        private readonly List<AudioRuntimeVoice> voices = new();

        /// <summary>
        /// 在起始帧播放直接 AudioClip，在排他结束帧停止仍未自然结束的 Voice。
        /// </summary>
        /// <param name="frame">当前整数帧。</param>
        public override void ProcessFrame(int frame)
        {
            RemoveCompletedVoices();
            for (int trackIndex = 0; trackIndex < Tracks.Count; trackIndex++)
            {
                AudioTrackConfig track = Tracks[trackIndex];
                for (int clipIndex = 0; clipIndex < track.Clips.Count; clipIndex++)
                {
                    AudioSkillClipConfig clip = track.Clips[clipIndex];
                    if (clip.StartFrame == frame && clip.AudioClip != null)
                    {
                        IAudioPlaybackHandle handle = AudioManager.Instance.PlaySfx(
                            clip.AudioClip, Context.Actor.Owner.transform,
                            clip.Volume, clip.Pitch, spatial: true);
                        voices.Add(new AudioRuntimeVoice(clip, handle));
                    }

                    if (clip.EndFrame == frame) StopClip(clip);
                }
            }
        }

        /// <summary>
        /// 音频播放由 Unity Audio 更新，不依赖 LateUpdate 姿态。
        /// </summary>
        /// <param name="frame">当前整数帧。</param>
        public override void ProcessLateFrame(int frame)
        {
        }

        /// <summary>
        /// Stop 允许已开始音频自然结束；Cancel 和自然技能边界停止仍由本执行持有的长音频。
        /// </summary>
        /// <param name="reason">技能结束原因。</param>
        public override void Complete(SkillCompletionReason reason)
        {
            if (reason != SkillCompletionReason.Stopped)
            {
                for (int index = 0; index < voices.Count; index++) voices[index].Handle.Stop();
            }
            voices.Clear();
        }

        /// <summary>
        /// 停止属于指定 Clip 且仍活动的全部 Voice。
        /// </summary>
        /// <param name="clip">到达结束边界的音频 Clip。</param>
        private void StopClip(AudioSkillClipConfig clip)
        {
            for (int index = voices.Count - 1; index >= 0; index--)
            {
                AudioRuntimeVoice voice = voices[index];
                if (voice.Clip != clip) continue;
                voice.Handle.Stop();
                voices.RemoveAt(index);
            }
        }

        /// <summary>
        /// 移除 AudioManager 已自然回收的 Voice 句柄记录。
        /// </summary>
        private void RemoveCompletedVoices()
        {
            for (int index = voices.Count - 1; index >= 0; index--)
            {
                if (!voices[index].Handle.IsPlaying) voices.RemoveAt(index);
            }
        }

        /// <summary>
        /// 关联 Audio Clip 配置与版本安全播放句柄。
        /// </summary>
        private readonly struct AudioRuntimeVoice
        {
            public AudioSkillClipConfig Clip { get; }
            public IAudioPlaybackHandle Handle { get; }

            /// <summary>
            /// 创建活动技能音频记录。
            /// </summary>
            /// <param name="clip">所属音频 Clip。</param>
            /// <param name="handle">AudioManager 返回的播放句柄。</param>
            public AudioRuntimeVoice(AudioSkillClipConfig clip, IAudioPlaybackHandle handle)
            {
                Clip = clip;
                Handle = handle;
            }
        }
    }
}
