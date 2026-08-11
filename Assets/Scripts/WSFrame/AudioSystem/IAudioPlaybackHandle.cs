using System.Collections.Generic;
using UnityEngine;

namespace WS_Modules.AudioSystem
{
    /// <summary>
    /// 提供单个直接 AudioClip 播放实例的安全控制句柄；池复用后旧句柄不会操作新的 Voice。
    /// </summary>
    public interface IAudioPlaybackHandle
    {
        bool IsPlaying { get; }

        /// <summary>
        /// 停止当前句柄仍然拥有的播放实例；实例已自然结束或被复用时不执行任何操作。
        /// </summary>
        void Stop();
    }

    /// <summary>
    /// 为 AudioManager 托管直接 AudioClip 的轻量 AudioSource 池、版本句柄和逐帧自然回收。
    /// </summary>
    internal sealed class DirectAudioPlaybackModule
    {
        #region 字段与状态

        private readonly List<Voice> activeVoices = new();
        private readonly Stack<AudioSource> availableSources = new();
        private readonly Transform root;
        private uint nextGeneration;
        private float globalVolume = 1f;
        private float effectVolume = 1f;
        private bool muted;
        private bool paused;

        #endregion

        #region 创建与释放

        /// <summary>
        /// 创建直接音频模块及其 Update 驱动节点。
        /// </summary>
        /// <param name="parent">AudioManager 创建的音频系统根节点。</param>
        public DirectAudioPlaybackModule(Transform parent)
        {
            GameObject rootObject = new("DirectSFXRoot");
            rootObject.transform.SetParent(parent, false);
            root = rootObject.transform;
            DirectAudioPlaybackDriver driver = rootObject.AddComponent<DirectAudioPlaybackDriver>();
            driver.Initialize(this);
        }

        /// <summary>
        /// 停止所有 Voice 并销毁本模块创建的 AudioSource 根节点。
        /// </summary>
        public void Dispose()
        {
            StopAll();
            if (root != null) Object.Destroy(root.gameObject);
        }

        #endregion

        #region 音量与暂停

        /// <summary>
        /// 更新 WSFrame 全局音量并刷新全部活动 Voice。
        /// </summary>
        /// <param name="value">0 到 1 的音量。</param>
        public void SetGlobalVolume(float value)
        {
            globalVolume = Mathf.Clamp01(value);
            RefreshVolumes();
        }

        /// <summary>
        /// 更新 WSFrame 音效分类音量并刷新全部活动 Voice。
        /// </summary>
        /// <param name="value">0 到 1 的音量。</param>
        public void SetEffectVolume(float value)
        {
            effectVolume = Mathf.Clamp01(value);
            RefreshVolumes();
        }

        /// <summary>
        /// 更新全部直接音频 Voice 的静音状态。
        /// </summary>
        /// <param name="value">是否静音。</param>
        public void SetMuted(bool value)
        {
            muted = value;
            for (int index = 0; index < activeVoices.Count; index++)
                activeVoices[index].Source.mute = value;
        }

        /// <summary>
        /// 暂停或恢复全部直接音频 Voice。
        /// </summary>
        /// <param name="value">是否暂停。</param>
        public void SetPaused(bool value)
        {
            if (paused == value) return;
            paused = value;
            for (int index = 0; index < activeVoices.Count; index++)
            {
                if (value) activeVoices[index].Source.Pause();
                else activeVoices[index].Source.UnPause();
            }
        }

        #endregion

        #region 播放控制

        /// <summary>
        /// 取得或创建 AudioSource，配置独立音量、Pitch 与空间模式并返回版本安全句柄。
        /// </summary>
        /// <param name="clip">直接持有的 AudioClip。</param>
        /// <param name="owner">3D 音频跟随组件。</param>
        /// <param name="volume">Clip 独立音量。</param>
        /// <param name="pitch">Clip 独立 Pitch。</param>
        /// <param name="spatial">是否使用 3D 空间音频。</param>
        /// <returns>控制本次播放的句柄。</returns>
        public IAudioPlaybackHandle Play(AudioClip clip, Component owner,
            float volume, float pitch, bool spatial)
        {
            AudioSource source = GetSource();
            source.transform.SetParent(owner.transform, false);
            source.transform.localPosition = Vector3.zero;
            source.transform.localRotation = Quaternion.identity;
            source.clip = clip;
            source.pitch = Mathf.Max(0.01f, pitch);
            source.spatialBlend = spatial ? 1f : 0f;
            source.loop = false;
            source.mute = muted;

            uint generation = ++nextGeneration;
            Voice voice = new(source, generation, Mathf.Clamp01(volume));
            activeVoices.Add(voice);
            ApplyVolume(voice);
            source.Play();
            if (paused) source.Pause();
            return new AudioPlaybackHandle(this, source, generation);
        }

        /// <summary>
        /// 停止并回收全部直接 AudioClip Voice。
        /// </summary>
        public void StopAll()
        {
            for (int index = activeVoices.Count - 1; index >= 0; index--)
                RecycleAt(index);
        }

        /// <summary>
        /// 在 Update 中回收已经自然播放完毕的非暂停 Voice。
        /// </summary>
        public void Tick()
        {
            if (paused) return;
            for (int index = activeVoices.Count - 1; index >= 0; index--)
            {
                AudioSource source = activeVoices[index].Source;
                if (source == null)
                {
                    // 跟随 Owner 被销毁时子 AudioSource 会一并销毁，只需移除已失效的 Voice 记录。
                    activeVoices.RemoveAt(index);
                    continue;
                }
                if (!source.isPlaying) RecycleAt(index);
            }
        }

        /// <summary>
        /// 判断句柄是否仍拥有当前 AudioSource 版本且正在播放或暂停。
        /// </summary>
        /// <param name="source">句柄记录的 AudioSource。</param>
        /// <param name="generation">句柄记录的版本。</param>
        /// <returns>版本仍活动时返回 true。</returns>
        internal bool IsPlaying(AudioSource source, uint generation) =>
            FindVoice(source, generation) >= 0;

        /// <summary>
        /// 仅在句柄版本仍匹配时停止并回收对应 Voice。
        /// </summary>
        /// <param name="source">句柄记录的 AudioSource。</param>
        /// <param name="generation">句柄记录的版本。</param>
        internal void Stop(AudioSource source, uint generation)
        {
            int index = FindVoice(source, generation);
            if (index >= 0) RecycleAt(index);
        }

        #endregion

        #region 池与内部辅助

        /// <summary>
        /// 从本地池取得 AudioSource；池为空时在固定根节点下创建新来源。
        /// </summary>
        /// <returns>已激活且等待配置的 AudioSource。</returns>
        private AudioSource GetSource()
        {
            AudioSource source;
            if (availableSources.Count > 0)
            {
                source = availableSources.Pop();
                source.gameObject.SetActive(true);
            }
            else
            {
                GameObject gameObject = new("DirectSFXVoice");
                gameObject.transform.SetParent(root, false);
                source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
            }
            return source;
        }

        /// <summary>
        /// 停止 Voice、重置全部会污染下次复用的字段并放回本地池。
        /// </summary>
        /// <param name="index">活动 Voice 索引。</param>
        private void RecycleAt(int index)
        {
            AudioSource source = activeVoices[index].Source;
            activeVoices.RemoveAt(index);
            if (source == null) return;
            source.Stop();
            source.clip = null;
            source.pitch = 1f;
            source.volume = 1f;
            source.spatialBlend = 0f;
            source.loop = false;
            source.mute = false;
            source.transform.SetParent(root, false);
            source.transform.localPosition = Vector3.zero;
            source.transform.localRotation = Quaternion.identity;
            source.transform.localScale = Vector3.one;
            source.gameObject.SetActive(false);
            availableSources.Push(source);
        }

        /// <summary>
        /// 查找与 AudioSource 和版本同时匹配的活动 Voice。
        /// </summary>
        /// <param name="source">待查找 AudioSource。</param>
        /// <param name="generation">待查找版本。</param>
        /// <returns>活动索引；不存在时返回 -1。</returns>
        private int FindVoice(AudioSource source, uint generation)
        {
            for (int index = 0; index < activeVoices.Count; index++)
            {
                Voice voice = activeVoices[index];
                if (voice.Source == source && voice.Generation == generation) return index;
            }
            return -1;
        }

        /// <summary>
        /// 根据全局、音效分类和 Clip 独立音量刷新全部 Voice。
        /// </summary>
        private void RefreshVolumes()
        {
            for (int index = 0; index < activeVoices.Count; index++) ApplyVolume(activeVoices[index]);
        }

        /// <summary>
        /// 应用单个 Voice 的最终线性音量。
        /// </summary>
        /// <param name="voice">待更新 Voice。</param>
        private void ApplyVolume(Voice voice)
        {
            voice.Source.volume = globalVolume * effectVolume * voice.Volume;
        }

        #endregion

        #region 嵌套类型

        /// <summary>
        /// 保存活动 AudioSource 的版本和 Clip 独立音量。
        /// </summary>
        private sealed class Voice
        {
            public AudioSource Source { get; }
            public uint Generation { get; }
            public float Volume { get; }

            /// <summary>
            /// 创建活动 Voice 记录。
            /// </summary>
            /// <param name="source">本次播放使用的 AudioSource。</param>
            /// <param name="generation">用于拒绝旧句柄的版本。</param>
            /// <param name="volume">Clip 独立音量。</param>
            public Voice(AudioSource source, uint generation, float volume)
            {
                Source = source;
                Generation = generation;
                Volume = volume;
            }
        }

        /// <summary>
        /// 将外部句柄操作路由到所属模块，并使用版本阻止池复用后的误操作。
        /// </summary>
        private sealed class AudioPlaybackHandle : IAudioPlaybackHandle
        {
            private readonly DirectAudioPlaybackModule module;
            private readonly AudioSource source;
            private readonly uint generation;

            public bool IsPlaying => module.IsPlaying(source, generation);

            /// <summary>
            /// 创建一个版本绑定的播放句柄。
            /// </summary>
            /// <param name="module">拥有 Voice 的模块。</param>
            /// <param name="source">本次播放的 AudioSource。</param>
            /// <param name="generation">本次播放版本。</param>
            public AudioPlaybackHandle(DirectAudioPlaybackModule module,
                AudioSource source, uint generation)
            {
                this.module = module;
                this.source = source;
                this.generation = generation;
            }

            /// <summary>
            /// 仅停止仍与当前版本匹配的 Voice。
            /// </summary>
            public void Stop()
            {
                module.Stop(source, generation);
            }
        }

        #endregion
    }

    /// <summary>
    /// 将 Unity Update 生命周期转发给纯 C# DirectAudioPlaybackModule。
    /// </summary>
    internal sealed class DirectAudioPlaybackDriver : MonoBehaviour
    {
        private DirectAudioPlaybackModule module;

        /// <summary>
        /// 绑定需要驱动的直接音频模块。
        /// </summary>
        /// <param name="module">所属模块。</param>
        public void Initialize(DirectAudioPlaybackModule module)
        {
            this.module = module;
        }

        /// <summary>
        /// 每帧检查直接 AudioClip 是否自然结束并完成回收。
        /// </summary>
        private void Update()
        {
            module.Tick();
        }
    }
}
