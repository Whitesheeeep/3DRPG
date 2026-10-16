using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Events;
using WS_Modules.Extensions;
using WS_Modules.LogModule;
using WS_Modules.Pooling;
using WS_Modules.ResLoadModule;

namespace WS_Modules.AudioSystem
{
    /// <summary>
    /// 统一管理资源 Key 音效、直接 AudioClip 音效、BGM 及其 AudioSource 生命周期。
    /// </summary>
    public sealed class AudioModule
    {
        #region 常量与依赖字段

        // 依赖字段：WSFrame 资源加载器和对象池负责资源 Key SFX 的加载、预热与回收。
        private IResLoad<string> _resLoader;
        private GameObjectPoolModule _audioSourcePoolModule;

        // 依赖字段：根节点及输出组在初始化时由 AudioManager 按 Setting 注入。
        private Transform _directSfxRoot;
        private AudioMixerGroup _bgmMixerGroup;
        private AudioMixerGroup _effectMixerGroup;
        private CancellationTokenSource _cts;

        private string _audioUnitKey = "AudioUnit";

        #endregion

        #region 状态与活动音源

        // 资源 Key 路径的活动及待加载音源，用于暂停、停止和异步回收。
        private readonly List<AudioSource> _playingAudioSourcesList = new();
        // 直接 Clip 路径的活动 Voice 与可复用音源保持一一对应。
        private readonly List<Voice> activeVoices = new();
        private readonly Stack<AudioSource> availableSources = new();

        private AudioSource _bgmAudioSource;
        private float bgmFadeInDuration;
        private float bgmFadeElapsedSeconds;
        private bool _isLoop;
        private bool _isPause;
        private uint nextGeneration;

        #endregion

        #region 生命周期

        /// <summary>
        /// 依赖已注入的资源加载器初始化音频节点、播放池与输出组。
        /// </summary>
        /// <param name="audioUnitKey">资源 Key 音效 AudioSource 单元 Key。</param>
        /// <param name="initCount">资源 Key 音效池的预热数量。</param>
        /// <param name="root">音频系统根节点。</param>
        /// <param name="resLoader">WSFrame 资源加载器。</param>
        /// <param name="bgmMixerGroup">BGM 输出 Mixer 组。</param>
        /// <param name="effectMixerGroup">所有 SFX 共用的 Effect 输出 Mixer 组。</param>
        public void Init(string audioUnitKey, int initCount, Transform root, IResLoad<string> resLoader,
            AudioMixerGroup bgmMixerGroup, AudioMixerGroup effectMixerGroup)
        {
            _resLoader = resLoader;
            _audioUnitKey = audioUnitKey;
            _bgmMixerGroup = bgmMixerGroup;
            _effectMixerGroup = effectMixerGroup;
            _cts = new CancellationTokenSource();

            // Mixer 保存玩家分类音量，AudioSource.volume 仅承担本次播放的淡入倍率。
            GameObject bgmGo = new("BGM_Source");
            bgmGo.transform.SetParent(root, false);
            _bgmAudioSource = bgmGo.AddComponent<AudioSource>();
            _bgmAudioSource.loop = true;
            _bgmAudioSource.playOnAwake = false;
            _bgmAudioSource.volume = 1f;
            _bgmAudioSource.outputAudioMixerGroup = _bgmMixerGroup;

            // 两类 SFX 使用各自资源池，但统一绑定同一个 Effect Mixer 输出组。
            Transform poolRoot = new GameObject("SFX_Pool_Root").transform;
            poolRoot.SetParent(root, false);
            _audioSourcePoolModule = new GameObjectPoolModule(poolRoot, resLoader);

            GameObject audioSourcePrefab = new(_audioUnitKey);
            audioSourcePrefab.AddComponent<AudioSource>().outputAudioMixerGroup = _effectMixerGroup;
            audioSourcePrefab.AddComponent<PoolObjectIdentity>().ConfigureKey(_audioUnitKey);
            _audioSourcePoolModule.Prewarm(audioSourcePrefab, initCount, -1, true);

            GameObject directSfxRootObject = new("DirectSFXRoot");
            directSfxRootObject.transform.SetParent(root, false);
            _directSfxRoot = directSfxRootObject.transform;
            _isPause = false;
            _isLoop = false;
            WSLog.Log($"[AudioModule] 初始化完成，audioUnitKey={_audioUnitKey}，prewarmCount={initCount}。");
        }

        /// <summary>
        /// 取消资源加载并停止、清理两条 SFX 路径及 BGM 的运行时状态。
        /// </summary>
        public void Dispose()
        {
            bool wasInitialized = _cts != null || _audioSourcePoolModule != null ||
                                  _bgmAudioSource != null || _directSfxRoot != null;
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }

            StopBGM();
            StopAllSFX();
            _playingAudioSourcesList.Clear();
            activeVoices.Clear();
            availableSources.Clear();
            _audioSourcePoolModule?.ClearAll();
            _audioSourcePoolModule = null;
            _resLoader = null;
            _bgmAudioSource = null;
            _directSfxRoot = null;
            _bgmMixerGroup = null;
            _effectMixerGroup = null;
            _isPause = false;
            _isLoop = false;
            if (wasInitialized)
                WSLog.Log("[AudioModule] 已取消异步加载并清理 BGM、资源 Key SFX 与直接 AudioClip Voice。");
        }

        /// <summary>
        /// 每帧推进 BGM 淡入并回收自然结束的直接 AudioClip Voice；音频暂停时两者均暂停。
        /// </summary>
        public void Tick()
        {
            if (_isPause) return;
            UpdateBgmFade(Time.unscaledDeltaTime);
            for (int index = activeVoices.Count - 1; index >= 0; index--)
            {
                AudioSource source = activeVoices[index].Source;
                if (source == null)
                {
                    // Owner 销毁时 Unity 会一并销毁子音源，只移除失效记录。
                    activeVoices.RemoveAt(index);
                    continue;
                }
                if (!source.isPlaying) RecycleVoiceAt(index);
            }
        }

        #endregion

        #region 状态属性

        /// <summary>
        /// 读取或设置全局 BGM 循环开关。
        /// </summary>
        public bool IsLoop
        {
            get => _isLoop;
            set
            {
                if (_isLoop == value) return;
                _isLoop = value;
                UpdateLoop();
            }
        }

        /// <summary>
        /// 读取或设置所有音频路径的暂停状态。
        /// </summary>
        public bool IsPause
        {
            get => _isPause;
            set
            {
                if (_isPause == value) return;
                _isPause = value;
                UpdatePause();
                WSLog.Log($"[AudioModule] 更新全部音频暂停状态，paused={_isPause}。");
            }
        }

        #endregion

        #region BGM 操作

        /// <summary>
        /// 立即播放 BGM；保留原接口行为，分类音量由 AudioManager 写入 Mixer。
        /// </summary>
        /// <param name="clip">背景音乐素材。</param>
        /// <param name="loop">是否循环播放。</param>
        public void PlayBGM(AudioClip clip, bool loop)
        {
            PlayBGM(clip, loop, 0f);
        }

        /// <summary>播放 BGM 并以独立音源倍率线性淡入，替换播放时重置旧淡入。</summary>
        /// <param name="clip">背景音乐素材。</param>
        /// <param name="loop">是否循环播放。</param>
        /// <param name="fadeInDuration">非负有限淡入秒数，0 为立即播放。</param>
        /// <exception cref="ArgumentOutOfRangeException">淡入时间不是非负有限值。</exception>
        public void PlayBGM(AudioClip clip, bool loop, float fadeInDuration)
        {
            if (float.IsNaN(fadeInDuration) || float.IsInfinity(fadeInDuration) || fadeInDuration < 0f)
                throw new ArgumentOutOfRangeException(nameof(fadeInDuration), "[AudioModule] BGM 淡入时间必须是非负有限值。");
            if (_bgmAudioSource == null) return;
            bgmFadeInDuration = fadeInDuration;
            bgmFadeElapsedSeconds = 0f;
            IsLoop = loop;
            _bgmAudioSource.clip = clip;
            // 必须先设置初始倍率再 Play，避免第一帧以全音量输出。
            _bgmAudioSource.volume = fadeInDuration > 0f ? 0f : 1f;
            _bgmAudioSource.outputAudioMixerGroup = _bgmMixerGroup;
            _bgmAudioSource.loop = _isLoop;
            _bgmAudioSource.Play();
            if (_isPause) _bgmAudioSource.Pause();
            WSLog.Log($"[AudioModule] 播放 BGM，clip={clip?.name}，fadeIn={fadeInDuration:0.###}，loop={loop}。");
        }

        /// <summary>按未缩放时间推进单次淡入；调用方在显式音频暂停时不调用此方法。</summary>
        /// <param name="deltaTime">本帧未缩放秒数。</param>
        private void UpdateBgmFade(float deltaTime)
        {
            if (bgmFadeInDuration <= 0f || _bgmAudioSource == null)
                return;

            bgmFadeElapsedSeconds += deltaTime;
            _bgmAudioSource.volume = Mathf.Clamp01(bgmFadeElapsedSeconds / bgmFadeInDuration);
            if (bgmFadeElapsedSeconds < bgmFadeInDuration)
                return;

            bgmFadeInDuration = 0f;
            WSLog.Log($"[AudioModule] BGM 淡入完成，clip={_bgmAudioSource.clip?.name}。");
        }

        /// <summary>
        /// 停止 BGM 并清除当前 Clip 引用。
        /// </summary>
        public void StopBGM()
        {
            bgmFadeInDuration = 0f;
            bgmFadeElapsedSeconds = 0f;
            if (_bgmAudioSource == null) return;
            _bgmAudioSource.Stop();
            _bgmAudioSource.clip = null;
            _bgmAudioSource.volume = 1f;
        }

        /// <summary>
        /// 暂停全部音频，包括 BGM。
        /// </summary>
        public void PauseBGM() => IsPause = true;

        /// <summary>
        /// 恢复全部音频，包括 BGM。
        /// </summary>
        public void ResumeBGM() => IsPause = false;

        #endregion

        #region 资源 Key SFX 操作

        /// <summary>
        /// 播放跟随组件的资源 Key SFX，并异步加载其 AudioClip。
        /// </summary>
        /// <param name="name">音效资源 Key。</param>
        /// <param name="component">音效跟随的组件。</param>
        /// <param name="is3D">是否为 3D 音频。</param>
        /// <param name="scale">该次播放的音量倍率。</param>
        /// <param name="loop">是否循环。</param>
        /// <param name="autoRelease">播放完成后是否释放 Clip 资源引用。</param>
        /// <param name="onPlay">开始播放回调。</param>
        /// <param name="onComplete">播放结束回调。</param>
        public void PlaySFX(string name, Component component, bool is3D = false, float scale = 1f,
            bool loop = false, bool autoRelease = false,
            UnityAction<AudioSource> onPlay = null, UnityAction<AudioSource> onComplete = null)
        {
            if (_audioSourcePoolModule == null) return;
            AudioSource source = GetAudioSource(is3D);
            if (source == null) return;

            source.transform.SetParent(component.transform, false);
            source.transform.localPosition = Vector3.zero;
            LoadAndPlaySFX(name, source, scale, loop, autoRelease, onPlay, onComplete, _cts.Token).Forget();
        }

        /// <summary>
        /// 在指定世界坐标播放资源 Key SFX，并异步加载其 AudioClip。
        /// </summary>
        /// <param name="name">音效资源 Key。</param>
        /// <param name="position">播放世界坐标。</param>
        /// <param name="is3D">是否为 3D 音频。</param>
        /// <param name="scale">该次播放的音量倍率。</param>
        /// <param name="loop">是否循环。</param>
        /// <param name="autoRelease">播放完成后是否释放 Clip 资源引用。</param>
        /// <param name="onPlay">开始播放回调。</param>
        /// <param name="onComplete">播放结束回调。</param>
        public void PlaySFX(string name, Vector3 position, bool is3D = false, float scale = 1f,
            bool loop = false, bool autoRelease = false,
            UnityAction<AudioSource> onPlay = null, UnityAction<AudioSource> onComplete = null)
        {
            if (_audioSourcePoolModule == null) return;
            AudioSource source = GetAudioSource(is3D);
            if (source == null) return;

            source.transform.SetParent(null);
            source.transform.position = position;
            LoadAndPlaySFX(name, source, scale, loop, autoRelease, onPlay, onComplete, _cts.Token).Forget();
        }

        /// <summary>
        /// 加载资源 Key 对应的 Clip，按单次倍率播放并启动完成监控。
        /// </summary>
        /// <param name="name">Clip 资源 Key。</param>
        /// <param name="source">已从资源 Key 音源池取得的音源。</param>
        /// <param name="scale">该次播放的音量倍率。</param>
        /// <param name="loop">是否循环。</param>
        /// <param name="autoRelease">完成后是否卸载 Clip。</param>
        /// <param name="onPlay">开始播放回调。</param>
        /// <param name="onComplete">播放结束回调。</param>
        /// <param name="token">本次初始化周期的取消令牌。</param>
        private async UniTaskVoid LoadAndPlaySFX(string name, AudioSource source, float scale, bool loop,
            bool autoRelease, UnityAction<AudioSource> onPlay,
            UnityAction<AudioSource> onComplete, CancellationToken token)
        {
            IResLoad<string> resourceLoader = _resLoader;
            AudioClip clip;
            try
            {
                clip = await resourceLoader.LoadAsync<AudioClip>(name);
            }
            catch (OperationCanceledException)
            {
                // Dispose 已清理该初始化周期的对象池，取消任务不再把旧音源回收到新周期。
                return;
            }

            if (token.IsCancellationRequested)
            {
                if (clip != null) resourceLoader.UnLoad<AudioClip>(name, false);
                return;
            }
            if (clip == null)
            {
                WSLog.LogWarning($"[AudioModule] 资源 Key SFX 加载失败，audioKey={name}。");
                if (source != null) _audioSourcePoolModule?.Recycle(_audioUnitKey, source.gameObject);
                onComplete?.Invoke(null);
                return;
            }
            if (source == null)
            {
                resourceLoader.UnLoad<AudioClip>(name, false);
                onComplete?.Invoke(null);
                return;
            }

            source.clip = clip;
            source.volume = Mathf.Clamp01(scale);
            source.outputAudioMixerGroup = _effectMixerGroup;
            source.loop = _isLoop && loop;
            source.Play();
            if (_isPause) source.Pause();

            if (!_playingAudioSourcesList.Contains(source)) _playingAudioSourcesList.Add(source);
            onPlay?.Invoke(source);
            MonitorSFX(name, source, autoRelease, onComplete, token).Forget();
        }

        /// <summary>
        /// 从资源 Key 对象池取出音源并重新绑定共用 Effect 输出组。
        /// </summary>
        /// <param name="is3D">是否使用 3D 空间混合。</param>
        /// <returns>配置完成且已登记的音源。</returns>
        private AudioSource GetAudioSource(bool is3D)
        {
            GameObject sourceObject = _audioSourcePoolModule.Get(_audioUnitKey);
            if (sourceObject == null) return null;

            AudioSource source = sourceObject.GetOrAddComponent<AudioSource>();
            source.outputAudioMixerGroup = _effectMixerGroup;
            source.spatialBlend = is3D ? 1f : 0f;
            if (!_playingAudioSourcesList.Contains(source)) _playingAudioSourcesList.Add(source);
            return source;
        }

        /// <summary>
        /// 监控资源 Key SFX 完成状态并按原资源所有权规则回收音源。
        /// </summary>
        /// <param name="clipName">Clip 资源 Key。</param>
        /// <param name="source">本次播放音源。</param>
        /// <param name="autoRelease">完成后是否卸载 Clip。</param>
        /// <param name="onComplete">播放结束回调。</param>
        /// <param name="token">本次初始化周期的取消令牌。</param>
        private async UniTaskVoid MonitorSFX(string clipName, AudioSource source, bool autoRelease,
            UnityAction<AudioSource> onComplete, CancellationToken token)
        {
            string audioUnitKey = _audioUnitKey;
            IResLoad<string> resourceLoader = _resLoader;
            while (true)
            {
                if (token.IsCancellationRequested) return;
                if (source == null)
                {
                    _playingAudioSourcesList.Remove(source);
                    if (autoRelease) resourceLoader.UnLoad<AudioClip>(clipName, false);
                    onComplete?.Invoke(null);
                    return;
                }
                if (_isPause)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                    continue;
                }
                if (!source.isPlaying)
                {
                    _playingAudioSourcesList.Remove(source);
                    source.clip = null;
                    _audioSourcePoolModule?.Recycle(audioUnitKey, source.gameObject);
                    if (autoRelease) resourceLoader.UnLoad<AudioClip>(clipName, false);
                    onComplete?.Invoke(source);
                    return;
                }
                await UniTask.Delay(500, cancellationToken: token);
            }
        }

        #endregion

        #region 直接 AudioClip SFX 与播放句柄

        // 直接 Clip 播放入口从本地音源池取出 Voice，并立即绑定单次音量和 Mixer 组。
        /// <summary>
        /// 播放已持有的 AudioClip，使用本地池复用音源并创建版本安全句柄。
        /// </summary>
        /// <param name="clip">直接持有的音频素材。</param>
        /// <param name="owner">音效跟随组件。</param>
        /// <param name="volume">该 Voice 的单次播放音量。</param>
        /// <param name="pitch">该 Voice 的播放音高。</param>
        /// <param name="spatial">是否使用 3D 空间混合。</param>
        /// <returns>控制本次播放的版本句柄。</returns>
        public IAudioPlaybackHandle PlayDirectClip(AudioClip clip, Component owner,
            float volume, float pitch, bool spatial)
        {
            if (_directSfxRoot == null)
                throw new InvalidOperationException("[AudioModule] 直接 AudioClip 播放前必须初始化音频模块。");
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            if (owner == null) throw new ArgumentNullException(nameof(owner));

            AudioSource source = GetDirectAudioSource();
            source.transform.SetParent(owner.transform, false);
            source.transform.localPosition = Vector3.zero;
            source.transform.localRotation = Quaternion.identity;
            source.clip = clip;
            source.pitch = Mathf.Max(0.01f, pitch);
            source.volume = Mathf.Clamp01(volume);
            source.spatialBlend = spatial ? 1f : 0f;
            source.loop = false;
            source.mute = false;
            source.outputAudioMixerGroup = _effectMixerGroup;

            uint generation = ++nextGeneration;
            Voice voice = new(source, generation);
            activeVoices.Add(voice);
            source.Play();
            if (_isPause) source.Pause();
            return new AudioPlaybackHandle(this, source, generation);
        }

        /// <summary>
        /// 取得可复用的直接播放音源，并确保它连接到本轮 Setting 的 Effect 输出组。
        /// </summary>
        /// <returns>待配置的 AudioSource。</returns>
        private AudioSource GetDirectAudioSource()
        {
            AudioSource source;
            if (availableSources.Count > 0)
            {
                source = availableSources.Pop();
                source.gameObject.SetActive(true);
            }
            else
            {
                GameObject sourceObject = new("DirectSFXVoice");
                sourceObject.transform.SetParent(_directSfxRoot, false);
                source = sourceObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
            }
            source.outputAudioMixerGroup = _effectMixerGroup;
            return source;
        }

        // 句柄查询和停止均校验播放版本，回收后重置音源再放回本地池。
        /// <summary>
        /// 判断版本句柄是否仍拥有对应的直接播放 Voice。
        /// </summary>
        /// <param name="source">句柄记录的音源。</param>
        /// <param name="generation">句柄记录的播放版本。</param>
        /// <returns>版本仍存在时返回 true。</returns>
        private bool IsVoicePlaying(AudioSource source, uint generation) => FindVoice(source, generation) >= 0;

        /// <summary>
        /// 仅在句柄版本与活动 Voice 仍匹配时停止其播放。
        /// </summary>
        /// <param name="source">句柄记录的音源。</param>
        /// <param name="generation">句柄记录的播放版本。</param>
        private void StopVoice(AudioSource source, uint generation)
        {
            int index = FindVoice(source, generation);
            if (index >= 0) RecycleVoiceAt(index);
        }

        /// <summary>
        /// 查找音源引用和播放版本同时匹配的活动 Voice。
        /// </summary>
        /// <param name="source">待查找音源。</param>
        /// <param name="generation">待查找版本。</param>
        /// <returns>活动 Voice 索引；不存在时返回 -1。</returns>
        private int FindVoice(AudioSource source, uint generation)
        {
            for (int index = 0; index < activeVoices.Count; index++)
            {
                Voice voice = activeVoices[index];
                if (ReferenceEquals(voice.Source, source) && voice.Generation == generation) return index;
            }
            return -1;
        }

        /// <summary>
        /// 停止并重置一个 Voice，再将音源放回直接 Clip 本地池。
        /// </summary>
        /// <param name="index">活动 Voice 索引。</param>
        private void RecycleVoiceAt(int index)
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
            source.outputAudioMixerGroup = _effectMixerGroup;
            source.transform.SetParent(_directSfxRoot, false);
            source.transform.localPosition = Vector3.zero;
            source.transform.localRotation = Quaternion.identity;
            source.transform.localScale = Vector3.one;
            source.gameObject.SetActive(false);
            availableSources.Push(source);
        }

        #endregion

        #region 暂停、停止与内部状态刷新

        // 两类 SFX 使用不同资源池，因此统一停止入口分别执行各自的回收策略。
        /// <summary>
        /// 停止资源 Key SFX 并回收全部直接 Clip Voice。
        /// </summary>
        public void StopAllSFX()
        {
            for (int index = _playingAudioSourcesList.Count - 1; index >= 0; index--)
            {
                AudioSource source = _playingAudioSourcesList[index];
                if (source != null) source.Stop();
            }
            for (int index = activeVoices.Count - 1; index >= 0; index--)
                RecycleVoiceAt(index);
        }

        // 暂停状态按 BGM、资源 Key 音源、直接 Clip Voice 顺序同步到当前播放源。
        /// <summary>
        /// 将暂停或恢复状态应用到 BGM、资源 Key 音源和直接 Clip Voice。
        /// </summary>
        private void UpdatePause()
        {
            if (_bgmAudioSource != null)
            {
                if (_isPause) _bgmAudioSource.Pause();
                else _bgmAudioSource.UnPause();
            }
            for (int index = _playingAudioSourcesList.Count - 1; index >= 0; index--)
            {
                AudioSource source = _playingAudioSourcesList[index];
                if (source == null) continue;
                if (_isPause) source.Pause();
                else source.UnPause();
            }
            for (int index = activeVoices.Count - 1; index >= 0; index--)
            {
                AudioSource source = activeVoices[index].Source;
                if (source == null) continue;
                if (_isPause) source.Pause();
                else source.UnPause();
            }
        }

        // BGM 循环状态只影响当前和后续 BGM，不改写 SFX 的单次循环参数。
        /// <summary>
        /// 将全局 BGM 循环选项应用到当前 BGM 音源。
        /// </summary>
        private void UpdateLoop()
        {
            if (_bgmAudioSource != null) _bgmAudioSource.loop = _isLoop;
        }

        #endregion

        #region 嵌套类型

        /// <summary>
        /// 保存一个直接 AudioClip 播放实例及其单调递增版本号。
        /// </summary>
        private sealed class Voice
        {
            public AudioSource Source { get; }
            public uint Generation { get; }

            /// <summary>
            /// 创建与单次播放音源绑定的 Voice 记录。
            /// </summary>
            /// <param name="source">本次播放使用的 AudioSource。</param>
            /// <param name="generation">用于拒绝旧句柄的播放版本。</param>
            public Voice(AudioSource source, uint generation)
            {
                Source = source;
                Generation = generation;
            }
        }

        /// <summary>
        /// 将公开控制操作转发到所属 Module，并以版本保护对象池复用后的新播放。
        /// </summary>
        private sealed class AudioPlaybackHandle : IAudioPlaybackHandle
        {
            private readonly AudioModule module;
            private readonly AudioSource source;
            private readonly uint generation;

            /// <summary>
            /// 创建绑定到单次 Voice 版本的播放句柄。
            /// </summary>
            /// <param name="module">拥有 Voice 的统一播放模块。</param>
            /// <param name="source">本次播放使用的音源。</param>
            /// <param name="generation">本次播放版本。</param>
            public AudioPlaybackHandle(AudioModule module, AudioSource source, uint generation)
            {
                this.module = module;
                this.source = source;
                this.generation = generation;
            }

            /// <summary>
            /// 判断句柄拥有的 Voice 是否仍在活动状态。
            /// </summary>
            public bool IsPlaying => module.IsVoicePlaying(source, generation);

            /// <summary>
            /// 仅停止该句柄版本仍拥有的 Voice。
            /// </summary>
            public void Stop() => module.StopVoice(source, generation);
        }

        #endregion
    }
}
