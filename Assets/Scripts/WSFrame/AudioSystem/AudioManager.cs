using System;
using UnityEngine;
using UnityEngine.Events;
using WS_Modules.ResLoadModule;
using WS_Modules.Singleton;

namespace WS_Modules.AudioSystem
{
    /// <summary>
    /// 作为 WSFrame 音频统一入口管理资源 Key 音频、BGM 与直接 AudioClip 播放。
    /// </summary>
    public sealed class AudioManager : SingletonBase<AudioManager>, IDisposable
    {
        #region 模块

        private readonly AudioModule audioModule;
        private DirectAudioPlaybackModule directAudioModule;

        #endregion

        #region 创建与释放

        /// <summary>
        /// 创建音频管理器及原有资源加载模块。
        /// </summary>
        private AudioManager()
        {
            audioModule = new AudioModule();
        }

        /// <summary>
        /// 按 AudioSystemSetting 初始化音频系统。
        /// </summary>
        /// <param name="audioSystemSetting">音频单元资源路径与预热数量。</param>
        /// <param name="root">音频系统父节点。</param>
        /// <param name="resLoader">WSFrame 资源加载器。</param>
        public void Initialize(AudioSystemSetting audioSystemSetting, Transform root, IResLoad<string> resLoader)
        {
            Initialize(audioSystemSetting.audioSourcePrefabPath,
                audioSystemSetting.audioSourceInitCount, root, resLoader);
        }

        /// <summary>
        /// 使用明确音频单元参数初始化原有模块和直接 AudioClip 模块。
        /// </summary>
        /// <param name="audioUnitKey">AudioSource 单元资源 Key。</param>
        /// <param name="initCount">原有 SFX 池预热数量。</param>
        /// <param name="root">音频系统父节点。</param>
        /// <param name="resLoader">WSFrame 资源加载器。</param>
        public void Initialize(string audioUnitKey, int initCount, Transform root, IResLoad<string> resLoader)
        {
            GameObject audioSystem = new("AudioSystem");
            audioSystem.transform.SetParent(root, false);
            audioModule.Init(audioUnitKey, initCount, audioSystem.transform, resLoader);
            directAudioModule = new DirectAudioPlaybackModule(audioSystem.transform);
        }

        /// <summary>
        /// 停止并释放原有音频与直接 AudioClip 模块。
        /// </summary>
        public void Dispose()
        {
            directAudioModule?.Dispose();
            directAudioModule = null;
            audioModule.Dispose();
        }

        #endregion

        #region 全局状态

        public float GlobalVolume
        {
            get => audioModule.GlobalVolume;
            set
            {
                audioModule.GlobalVolume = value;
                directAudioModule?.SetGlobalVolume(value);
            }
        }

        public float BGVolume
        {
            get => audioModule.BGVolume;
            set => audioModule.BGVolume = value;
        }

        public float EffectVolume
        {
            get => audioModule.EffectVolume;
            set
            {
                audioModule.EffectVolume = value;
                directAudioModule?.SetEffectVolume(value);
            }
        }

        public bool IsMute
        {
            get => audioModule.IsMute;
            set
            {
                audioModule.IsMute = value;
                directAudioModule?.SetMuted(value);
            }
        }

        public bool IsLoop
        {
            get => audioModule.IsLoop;
            set => audioModule.IsLoop = value;
        }

        public bool IsPause
        {
            get => audioModule.IsPause;
            set
            {
                audioModule.IsPause = value;
                directAudioModule?.SetPaused(value);
            }
        }

        #endregion

        #region BGM 操作

        /// <summary>
        /// 播放直接 AudioClip BGM。
        /// </summary>
        /// <param name="clip">BGM 素材。</param>
        /// <param name="volume">BGM 分类音量。</param>
        /// <param name="loop">是否循环。</param>
        public void PlayBGM(AudioClip clip, float volume = 1f, bool loop = true)
        {
            audioModule.PlayBGM(clip, volume, loop);
        }

        /// <summary>
        /// 停止 BGM。
        /// </summary>
        public void StopBGM() => audioModule.StopBGM();

        /// <summary>
        /// 暂停 BGM。
        /// </summary>
        public void PauseBGM() => audioModule.PauseBGM();

        /// <summary>
        /// 恢复 BGM。
        /// </summary>
        public void ResumeBGM() => audioModule.ResumeBGM();

        #endregion

        #region SFX 操作

        /// <summary>
        /// 通过资源 Key 播放跟随组件的原有 WSFrame SFX。
        /// </summary>
        /// <param name="name">音频资源 Key。</param>
        /// <param name="component">跟随组件。</param>
        /// <param name="is3D">是否为 3D 音频。</param>
        /// <param name="scale">音量倍率。</param>
        /// <param name="loop">是否循环。</param>
        /// <param name="autoRelease">完成后是否释放资源引用。</param>
        /// <param name="onPlay">开始回调。</param>
        /// <param name="onComplete">结束回调。</param>
        public void PlaySFX(string name, Component component, bool is3D = false, float scale = 1f,
            bool loop = false, bool autoRelease = false,
            UnityAction<AudioSource> onPlay = null, UnityAction<AudioSource> onComplete = null)
        {
            audioModule.PlaySFX(name, component, is3D, scale, loop, autoRelease, onPlay, onComplete);
        }

        /// <summary>
        /// 通过资源 Key 在指定世界位置播放原有 WSFrame SFX。
        /// </summary>
        /// <param name="name">音频资源 Key。</param>
        /// <param name="position">世界位置。</param>
        /// <param name="is3D">是否为 3D 音频。</param>
        /// <param name="scale">音量倍率。</param>
        /// <param name="loop">是否循环。</param>
        /// <param name="autoRelease">完成后是否释放资源引用。</param>
        /// <param name="onPlay">开始回调。</param>
        /// <param name="onComplete">结束回调。</param>
        public void PlaySFX(string name, Vector3 position, bool is3D = false, float scale = 1f,
            bool loop = false, bool autoRelease = false,
            UnityAction<AudioSource> onPlay = null, UnityAction<AudioSource> onComplete = null)
        {
            audioModule.PlaySFX(name, position, is3D, scale, loop, autoRelease, onPlay, onComplete);
        }

        /// <summary>
        /// 直接播放已持有的 AudioClip，并返回可安全停止单个池化 Voice 的版本句柄。
        /// </summary>
        /// <param name="clip">直接 AudioClip 资源。</param>
        /// <param name="owner">跟随组件。</param>
        /// <param name="volume">Clip 独立音量。</param>
        /// <param name="pitch">Clip 独立 Pitch。</param>
        /// <param name="spatial">是否使用 3D 空间音频。</param>
        /// <returns>本次播放的控制句柄。</returns>
        public IAudioPlaybackHandle PlaySfx(AudioClip clip, Component owner,
            float volume, float pitch, bool spatial = true)
        {
            if (directAudioModule == null)
                throw new InvalidOperationException("AudioManager 尚未 Initialize，不能播放直接 AudioClip。");
            return directAudioModule.Play(clip, owner, volume, pitch, spatial);
        }

        /// <summary>
        /// 停止原有资源 Key SFX 与全部直接 AudioClip Voice。
        /// </summary>
        public void StopAllSFX()
        {
            audioModule.StopAllSFX();
            directAudioModule?.StopAll();
        }

        #endregion
    }
}
