using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using WS_Modules.Pooling;
using WS_Modules.Extensions;
using WS_Modules.LogModule;
using WS_Modules.ResLoadModule;

namespace WS_Modules.AudioSystem
{
    public class AudioModule
    {
        // 资源加载器
        private IResLoad<string> _resLoader;
        /// <summary>
        /// 音频播放单元的预制体 Key，需要在 Resources 或相关加载路径下存在该预制体（挂载 AudioSource）
        /// </summary>
        private string _audioUnitKey = "AudioUnit";
        private const int DEFAULT_AUDIO_UNIT_COUNT = 5; // 默认预热的音频单元数量
        
        // 用于控制异步任务的生命周期，在 Dispose 时取消所有未完成的任务，避免内存泄漏和异常
        private CancellationTokenSource _cts;

        #region Volume Control Properties
        private float _globalVolume = 1f;
        /// <summary>
        /// 全局音量 (0~1)
        /// </summary>
        public float GlobalVolume
        {
            get => _globalVolume;
            set
            {
                if (Mathf.Approximately(_globalVolume, value)) return;
                _globalVolume = Mathf.Clamp01(value);
                // 更新所有音频播放器的音量
                UpdateAllAudioSourcesVolume();
            }
        }

        private float _bgVolume = 1f;
        /// <summary>
        /// 背景音乐音量 (0~1)
        /// </summary>
        public float BGVolume
        {
            get => _bgVolume;
            set
            {
                if (Mathf.Approximately(_bgVolume, value)) return;
                _bgVolume = Mathf.Clamp01(value);
                // 更新所有背景音乐播放器的音量
                UpdateBKAudioSourceVolume();
            }
        }

        private float _effectVolume = 1f;
        /// <summary>
        /// 音效音量 (0~1)
        /// </summary>
        public float EffectVolume
        {
            get => _effectVolume;
            set
            {
                if (Mathf.Approximately(_effectVolume, value)) return;
                _effectVolume = Mathf.Clamp01(value);
                // 更新所有音效播放器的音量
                UpdateEffectAudioSourcesVolume();
            }
        }

        private bool _isMute = false;
        /// <summary>
        /// 是否静音
        /// </summary>
        public bool IsMute
        {
            get => _isMute;
            set
            {
                if (_isMute == value) return;
                _isMute = value;
                // 更新所有音频播放器的静音状态
                UpdateMute();
            }
        }

        private bool _isLoop = false;
        /// <summary>
        /// 用于控制 BGM 是否循环播放
        /// </summary>
        public bool IsLoop
        {
            get => _isLoop;
            set
            {
                if (_isLoop == value) return;
                _isLoop = value;
                // 更新背景音乐的循环状态
                UpdateLoop();
            }
        }

        private bool _isPause = false;
        /// <summary>
        /// 是否暂停所有音频
        /// </summary>
        public bool IsPause
        {
            get => _isPause;
            set
            {
                if (_isPause == value) return;
                _isPause = value;
                // 更新所有音频播放器的暂停状态
                UpdatePause();
            }
        }
        #endregion

        private GameObjectPoolModule _audioSourcePoolModule;
        private List<AudioSource> _playingAudioSourcesList = new List<AudioSource>();
        private AudioSource _bgmAudioSource;

        public void Init(string audioUnitKey, Transform root, IResLoad<string> resLoad) =>
            Init(audioUnitKey, DEFAULT_AUDIO_UNIT_COUNT, root, resLoad);

        /// <summary>
        /// 初始化音频模块
        /// </summary>
        /// <param name="audioUnitKey">放 audioSource 的预制体的 Key</param>
        /// <param name="initCount">初始化的数量</param>
        /// <param name="root">音频模块的根节点</param>
        /// <param name="resLoader">资源加载器</param>
        public void Init(string audioUnitKey, int initCount, Transform root, IResLoad<string> resLoader)
        {
            _resLoader = resLoader;
            _audioUnitKey = audioUnitKey;
            _cts = new CancellationTokenSource();

            // 初始化 BGM 播放器
            GameObject bgmGo = new GameObject("BGM_Source");
            bgmGo.transform.SetParent(root);
            if (root is null) GameObject.DontDestroyOnLoad(bgmGo);
            
            _bgmAudioSource = bgmGo.AddComponent<AudioSource>();
            _bgmAudioSource.loop = true;
            _bgmAudioSource.playOnAwake = false;

            // 初始化 SFX 对象池
            Transform poolRoot = new GameObject("SFX_Pool_Root").transform;
            poolRoot.SetParent(root);
            

            // 这里依赖 resLoader 加载 AudioUnit 预制体
            _audioSourcePoolModule = new GameObjectPoolModule(poolRoot, resLoader);

            // 预热对象池
            // 如果资源中不存在该 Key，会在日志中报错，提示开发者添加资源
            GameObject audioSourcePrefab = new GameObject(_audioUnitKey);
            audioSourcePrefab.AddComponent<AudioSource>();
            audioSourcePrefab.AddComponent<PoolObjectIdentity>().PoolKey = _audioUnitKey; // 确保预制体上有 PoolObjectIdentity 组件，并设置 PoolKey
            _audioSourcePoolModule.Prewarm(audioSourcePrefab,initCount, -1, true); // -1 代表无限容量
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            StopBGM();
            StopAllSFX();
            _playingAudioSourcesList.Clear();
            // 这里也可以清除对象池
            _audioSourcePoolModule?.ClearAll();
        }

        #region BGM Control
        public void PlayBGM(AudioClip clip, float volume = 1f, bool loop = true)
        {
            if (_bgmAudioSource == null) return;

            IsLoop = loop;
            if (volume >= 0) // 只有当外部传入有效音量时才更新
            {
                BGVolume = volume;
            }

            _bgmAudioSource.clip = clip;
            _bgmAudioSource.volume = _globalVolume * _bgVolume;
            _bgmAudioSource.loop = _isLoop;
            _bgmAudioSource.mute = _isMute;
            _bgmAudioSource.Play();
        }

        public void StopBGM()
        {
            if (_bgmAudioSource != null)
            {
                _bgmAudioSource.Stop();
                _bgmAudioSource.clip = null;
            }
        }

        public void PauseBGM()
        {
            IsPause = true;
        }

        public void ResumeBGM()
        {
            IsPause = false;
        }
        #endregion

        #region SFX Control
        /// <summary>
        /// 播放音效（跟随组件）
        /// </summary>
        /// <param name="name">音效资源名称/路径</param>
        /// <param name="component">挂载的组件（音效将成为其子对象）</param>
        /// <param name="is3D">是否为 3D 音效</param>
        /// <param name="scale">音量缩放倍率</param>
        /// <param name="loop">是否循环</param>
        /// <param name="autoRelease">播放结束后是否自动卸载 AudioClip 引用计数</param>
        /// <param name="onPlay">开始播放时的回调</param>
        /// <param name="onComplete">播放完成时的回调</param>
        public void PlaySFX(string name, Component component, bool is3D = false, float scale = 1f, bool loop = false,
            bool autoRelease = false, UnityAction<AudioSource> onPlay = null,
            UnityAction<AudioSource> onComplete = null)
        {
            if (_audioSourcePoolModule == null) return;

            // 1. 获取 AudioSource 载体
            var source = GetAudioSource(is3D);
            if (source == null) return;

            // 设置父对象为传入的组件
            source.transform.SetParent(component.transform);
            source.transform.localPosition = Vector3.zero;

            // 监听 Component 销毁
            // 如果 Component 被销毁，MonitorSFX 中的 source == null 检查会处理，或者 destroy token 会处理
            // 但为了更即时，我们可以保留 OnDestroy 监听作为双重保险，或依赖 MonitorSFX 的轮询
            
            // 之前的 OnDestroy 实现可能引起闭包 GC 问题，鉴于 MonitorSFX 已有 source == null 检查，
            // 且 Component 销毁时子物体 AudioSource 也会被销毁，
            // MonitorSFX 下一帧就能检测到 source == null，所以这里可以简化。
            
            // 启动播放任务
            LoadAndPlaySFX(name, source, scale, loop, autoRelease, onPlay, onComplete, _cts.Token).Forget();
        }

        /// <summary>
        /// 播放音效（指定位置）
        /// </summary>
        /// <param name="name">音效资源名称/路径</param>
        /// <param name="position">世界坐标位置</param>
        /// <param name="is3D">是否为 3D 音效</param>
        /// <param name="scale">音量缩放倍率</param>
        /// <param name="loop">是否循环</param>
        /// <param name="autoRelease">播放结束后是否自动卸载 AudioClip 引用计数</param>
        /// <param name="onPlay">开始播放时的回调</param>
        /// <param name="onComplete">播放完成时的回调</param>
        public void PlaySFX(string name, Vector3 position, bool is3D = false, float scale = 1f, bool loop = false,
            bool autoRelease = false, UnityAction<AudioSource> onPlay = null,
            UnityAction<AudioSource> onComplete = null)
        {
            if (_audioSourcePoolModule == null) return;

            // 1. 获取 AudioSource
            var source = GetAudioSource(is3D);
            if (source == null) return;

            source.transform.SetParent(null); // 世界空间
            source.transform.position = position;

            LoadAndPlaySFX(name, source, scale, loop, 
                autoRelease, onPlay, onComplete, _cts.Token).Forget();
        }

        private async UniTaskVoid LoadAndPlaySFX(string name, AudioSource source, float scale, bool loop,
            bool autoRelease, UnityAction<AudioSource> onPlay, UnityAction<AudioSource> onComplete, CancellationToken token)
        {
            // 2. 加载 Clip 并播放
            // 注意：LoadAsync 可能会因为 token 取消而抛出异常，需要处理
            AudioClip clip;
            try 
            {
                clip = await _resLoader.LoadAsync<AudioClip>(name);
            }
            catch (System.OperationCanceledException)
            {
                // 如果取消，直接回收 source
               if(source != null) _audioSourcePoolModule.Recycle(_audioUnitKey, source.gameObject);
               return;
            }

            if (token.IsCancellationRequested)
            {
                if(clip != null) _resLoader.UnLoad<AudioClip>(name, false); // 即使加载出来也要卸载
                if(source != null) _audioSourcePoolModule.Recycle(_audioUnitKey, source.gameObject);
                return;
            }

            if (clip == null)
            {
                WSLog.LogWarning($"[AudioModule] Failed to load clip: {name}");
                if(source != null) _audioSourcePoolModule.Recycle(_audioUnitKey, source.gameObject);
                onComplete?.Invoke(null);
                return;
            }
            
            // source 可能在加载过程中被销毁
            if (source == null)
            {
                _resLoader.UnLoad<AudioClip>(name, false);
                onComplete?.Invoke(null);
                return;
            }

            source.clip = clip;
            source.volume = _globalVolume * _effectVolume * scale;
            source.mute = _isMute;
            source.loop = _isLoop && loop; 

            if (IsPause) 
            {
                source.Pause();
            }
            else
            {
                source.Play();
            }

            // 加入列表管理，方便全局控制
            if (!_playingAudioSourcesList.Contains(source))
            {
                _playingAudioSourcesList.Add(source);
            }

            onPlay?.Invoke(source);

            // 启动监控任务
            MonitorSFX(name, source, autoRelease, onComplete, token).Forget();
        }

        private AudioSource GetAudioSource(bool is3D = false)
        {
            var go = _audioSourcePoolModule.Get(_audioUnitKey);
            if (go == null) return null;

            var source = go.GetOrAddComponent<AudioSource>();
            // 0 是 2D, 1 是 3D
            source.spatialBlend = is3D ? 1 : 0;
            _playingAudioSourcesList.Add(source);

            return source;
        }

        public void StopAllSFX()
        {
            for (int i = _playingAudioSourcesList.Count - 1; i >= 0; i--)
            {
                var source = _playingAudioSourcesList[i];
                if (source != null)
                {
                    source.Stop();
                    // MonitorSFX 会检测到 isPlaying false 并回收，或者我们可以手动回收。
                    // 了安全起见，手动触发回收需要处理 MonitorSFX 的竞态。
                    // 简单做法：Stop 后，MonitorSFX 下一帧会检测到并回收。
                }
            }
        }

        /// <summary>
        /// 监控 AudioSource 状态，负责自动回收和资源卸载
        /// </summary>
        private async UniTaskVoid MonitorSFX(string clipName, AudioSource source, bool autoRelease,
            UnityAction<AudioSource> onComplete, CancellationToken token)
        {
            string audioUnitKey = _audioUnitKey;

            while (true)
            {
                if (token.IsCancellationRequested) return;

                // 1. 如果 AudioSource 对象被意外销毁 (source == null)
                if (source == null)
                {
                    if (_playingAudioSourcesList.Contains(source))
                        _playingAudioSourcesList.Remove(source);

                    // 仅卸载 Asset 引用，无法回收 GameObject
                    if (autoRelease) _resLoader.UnLoad<AudioClip>(clipName, false);

                    onComplete?.Invoke(null);
                    return;
                }

                // 2. 如果全局暂停，什么都不做，继续等待
                if (IsPause)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                    continue;
                }

                // 3. 如果播放停止 (非暂停状态下)
                if (!source.isPlaying)
                {
                    if (_playingAudioSourcesList.Contains(source))
                        _playingAudioSourcesList.Remove(source);

                    source.clip = null; // 清理 AudioClip 引用

                    // 回收 AudioSource 到对象池
                    _audioSourcePoolModule?.Recycle(audioUnitKey, source.gameObject);

                    // 如果开启自动释放，卸载 AudioClip 引用计数
                    if (autoRelease) _resLoader.UnLoad<AudioClip>(clipName, false);

                    onComplete?.Invoke(source);
                    return;
                }

                // 低频检测 (每 0.5 秒检测一次状态)
                await UniTask.Delay(500, cancellationToken: token);
            }
        }
        #endregion

        #region State Updates
        private void UpdatePause()
        {
            // Update BGM
            if (_bgmAudioSource != null)
            {
                if (_isPause) _bgmAudioSource.Pause();
                else _bgmAudioSource.UnPause();
            }

            // Update SFX
            // 倒序遍历以防列表变动（虽然这里不删除元素）
            for (int i = _playingAudioSourcesList.Count - 1; i >= 0; i--)
            {
                var source = _playingAudioSourcesList[i];
                if (source == null) continue;

                if (_isPause) source.Pause();
                else source.UnPause();
            }
        }

        private void UpdateLoop()
        {
            if (_bgmAudioSource != null)
            {
                _bgmAudioSource.loop = _isLoop;
            }
            // SFX 的 Loop 通常由 PlaySFX 时的参数决定，不随全局 IsLoop 变化
        }

        private void UpdateAllAudioSourcesVolume()
        {
            UpdateBKAudioSourceVolume();
            UpdateEffectAudioSourcesVolume();
        }

        private void UpdateBKAudioSourceVolume()
        {
            if (_bgmAudioSource != null)
            {
                _bgmAudioSource.volume = _globalVolume * _bgVolume;
            }
        }

        private void UpdateEffectAudioSourcesVolume()
        {
            for (int i = 0; i < _playingAudioSourcesList.Count; i++)
            {
                var source = _playingAudioSourcesList[i];
                if (source != null)
                {
                    source.volume = _globalVolume * _effectVolume;
                }
            }
        }

        private void UpdateMute()
        {
            if (_bgmAudioSource != null) _bgmAudioSource.mute = _isMute;

            foreach (var source in _playingAudioSourcesList)
            {
                if (source != null) source.mute = _isMute;
            }
        }
        #endregion
    }
}

