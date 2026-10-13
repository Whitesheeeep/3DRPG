using System;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Events;
using WS_Modules.LogModule;
using WS_Modules.ResLoadModule;
using WS_Modules.Singleton;

namespace WS_Modules.AudioSystem
{
    /// <summary>
    /// 作为 WSFrame 音频统一入口管理播放模块和专用 AudioMixer 音量。
    /// </summary>
    public sealed class AudioManager : SingletonBase<AudioManager>, IDisposable
    {
        #region 依赖字段

        // 音频播放、资源加载和 Voice 生命周期集中由一个模块负责。
        private readonly AudioModule audioModule;

        #endregion

        #region 配置与状态字段

        private AudioMixer audioMixer;
        private Transform audioSystemRoot;
        private string globalVolumeParameterName;
        private string bgVolumeParameterName;
        private string effectVolumeParameterName;
        private float globalVolume = 1f;
        private float bgVolume = 1f;
        private float effectVolume = 1f;
        private bool isMute;
        private bool hasAppliedMixerSettings;

        #endregion

        #region 创建与释放

        /// <summary>
        /// 创建音频管理器及统一播放模块。
        /// </summary>
        private AudioManager()
        {
            audioModule = new AudioModule();
        }

        /// <summary>
        /// 按 Setting 初始化音频播放与 Mixer 配置。
        /// </summary>
        /// <param name="audioSystemSetting">音频资源、Mixer 输出组与暴露参数配置。</param>
        /// <param name="root">音频系统父节点。</param>
        /// <param name="resLoader">WSFrame 资源加载器。</param>
        public void Initialize(AudioSystemSetting audioSystemSetting, Transform root, IResLoad<string> resLoader)
        {
            if (root == null)
                throw CreateConfigurationException("[AudioManager] 初始化音频系统时必须提供 WSFrameRoot Transform。");
            ValidateSetting(audioSystemSetting);

            // 重新初始化时先释放旧播放图，再创建新的根节点，Voice 版本计数保留在同一 Module 中。
            DisposeAudioRuntime();
            audioMixer = audioSystemSetting.audioMixer;
            globalVolumeParameterName = audioSystemSetting.globalVolumeParameterName;
            bgVolumeParameterName = audioSystemSetting.bgVolumeParameterName;
            effectVolumeParameterName = audioSystemSetting.effectVolumeParameterName;

            GameObject audioSystem = new("AudioSystem");
            audioSystemRoot = audioSystem.transform;
            audioSystemRoot.SetParent(root, false);
            audioModule.Init(audioSystemSetting.audioSourcePrefabPath,
                audioSystemSetting.audioSourceInitCount,
                audioSystemRoot,
                resLoader,
                audioSystemSetting.bgmMixerGroup,
                audioSystemSetting.effectMixerGroup);

            WSLog.Log("[AudioManager] 音频模块初始化完成，BGM 与所有 SFX 已绑定 WSFrame Mixer 输出组。");
        }

        /// <summary>
        /// 在框架根节点销毁或重新初始化时停止播放并释放音频对象。
        /// </summary>
        public void Dispose()
        {
            DisposeAudioRuntime();
            audioMixer = null;
            hasAppliedMixerSettings = false;
        }

        /// <summary>
        /// 释放当前播放模块和 Manager 所有的 AudioSystem 根节点。
        /// </summary>
        private void DisposeAudioRuntime()
        {
            audioModule.Dispose();
            if (audioSystemRoot != null)
            {
                WSLog.Log("[AudioManager] 释放音频播放模块与 AudioSystem 根节点。");
                UnityEngine.Object.Destroy(audioSystemRoot.gameObject);
                audioSystemRoot = null;
            }
            hasAppliedMixerSettings = false;
        }

        #endregion

        #region Mixer 音量状态

        /// <summary>
        /// 读取或设置 Master 的线性音量，范围为 0 到 1。
        /// </summary>
        public float GlobalVolume
        {
            get => globalVolume;
            set
            {
                float clampedValue = Mathf.Clamp01(value);
                if (Mathf.Approximately(globalVolume, clampedValue)) return;
                globalVolume = clampedValue;
                if (hasAppliedMixerSettings && !isMute)
                    SetMixerVolume(globalVolumeParameterName, globalVolume);
            }
        }

        /// <summary>
        /// 读取或设置 BGM Mixer 组的线性音量，范围为 0 到 1。
        /// </summary>
        public float BGVolume
        {
            get => bgVolume;
            set
            {
                float clampedValue = Mathf.Clamp01(value);
                if (Mathf.Approximately(bgVolume, clampedValue)) return;
                bgVolume = clampedValue;
                if (hasAppliedMixerSettings)
                    SetMixerVolume(bgVolumeParameterName, bgVolume);
            }
        }

        /// <summary>
        /// 读取或设置所有 SFX 共用的 Effect Mixer 组线性音量，范围为 0 到 1。
        /// </summary>
        public float EffectVolume
        {
            get => effectVolume;
            set
            {
                float clampedValue = Mathf.Clamp01(value);
                if (Mathf.Approximately(effectVolume, clampedValue)) return;
                effectVolume = clampedValue;
                if (hasAppliedMixerSettings)
                    SetMixerVolume(effectVolumeParameterName, effectVolume);
            }
        }

        /// <summary>
        /// 读取或设置 Master 静音状态，不改变缓存的全局音量。
        /// </summary>
        public bool IsMute
        {
            get => isMute;
            set
            {
                if (isMute == value) return;
                isMute = value;
                if (hasAppliedMixerSettings)
                    SetMixerVolume(globalVolumeParameterName, isMute ? 0f : globalVolume);
                WSLog.Log($"[AudioManager] 更新全局静音状态，muted={isMute}，cachedVolume={globalVolume:0.###}。");
            }
        }

        /// <summary>
        /// 读取或设置 BGM 循环状态。
        /// </summary>
        public bool IsLoop
        {
            get => audioModule.IsLoop;
            set => audioModule.IsLoop = value;
        }

        /// <summary>
        /// 读取或设置全部音频播放状态。
        /// </summary>
        public bool IsPause
        {
            get => audioModule.IsPause;
            set => audioModule.IsPause = value;
        }

        #endregion

        #region Mixer 参数应用

        /// <summary>
        /// 在 WSFrameRoot.Start 中首次将缓存音量写入 Mixer。
        /// </summary>
        public void ApplyMixerSettings()
        {
            if (audioMixer == null)
                throw CreateConfigurationException("[AudioManager] 无法应用音量：尚未使用有效的 AudioSystemSetting 初始化。");

            // Start 阶段 Mixer 已离开 Awake/OnEnable 初始化流程，按 Master、BGM、Effect 顺序应用快照。
            SetMixerVolume(bgVolumeParameterName, bgVolume);
            SetMixerVolume(effectVolumeParameterName, effectVolume);
            SetMixerVolume(globalVolumeParameterName, isMute ? 0f : globalVolume);
            hasAppliedMixerSettings = true;
            WSLog.Log($"[AudioManager] Mixer 初始音量已应用，global={globalVolume:0.###}，bgm={bgVolume:0.###}，effect={effectVolume:0.###}，muted={isMute}。");
        }

        /// <summary>
        /// 将全局、BGM 与 SFX 线性音量写成 AudioMixer 使用的分贝值。
        /// </summary>
        /// <param name="parameterName">已暴露的 Mixer 参数名。</param>
        /// <param name="volume">0 到 1 的线性音量。</param>
        private void SetMixerVolume(string parameterName, float volume)
        {
            float decibels = volume <= 0f ? -80f : Mathf.Max(-80f, 20f * Mathf.Log10(volume));
            if (!audioMixer.GetFloat(parameterName, out _))
                throw CreateConfigurationException($"[AudioManager] AudioMixer 参数 '{parameterName}' 未暴露或当前不可读。");
            if (!audioMixer.SetFloat(parameterName, decibels))
                throw CreateConfigurationException($"[AudioManager] AudioMixer 参数 '{parameterName}' 写入失败，valueDb={decibels:0.##}。");
        }

        #endregion

        #region Setting 校验

        /// <summary>
        /// 校验外部 Setting 边界中的 Mixer、输出组与暴露参数配置。
        /// </summary>
        /// <param name="setting">待验证的音频系统配置。</param>
        private static void ValidateSetting(AudioSystemSetting setting)
        {
            if (setting == null)
                throw CreateConfigurationException("[AudioManager] AudioSystemSetting 不能为空。");
            if (setting.audioMixer == null)
                throw CreateConfigurationException("[AudioManager] AudioSystemSetting 未指定 WSFrame AudioMixer。");
            if (setting.bgmMixerGroup == null || setting.effectMixerGroup == null)
                throw CreateConfigurationException("[AudioManager] AudioSystemSetting 必须指定 BGM 和共用 SFX 的 Effect 输出组。");
            if (setting.bgmMixerGroup.audioMixer != setting.audioMixer || setting.effectMixerGroup.audioMixer != setting.audioMixer)
                throw CreateConfigurationException("[AudioManager] BGM 与 Effect 输出组必须属于 AudioSystemSetting 指定的 AudioMixer。");

            string[] parameterNames =
            {
                setting.globalVolumeParameterName,
                setting.bgVolumeParameterName,
                setting.effectVolumeParameterName
            };
            for (int index = 0; index < parameterNames.Length; index++)
            {
                if (string.IsNullOrWhiteSpace(parameterNames[index]))
                    throw CreateConfigurationException($"[AudioManager] AudioMixer 音量参数名不能为空，index={index}。");
                for (int previousIndex = 0; previousIndex < index; previousIndex++)
                {
                    if (string.Equals(parameterNames[index], parameterNames[previousIndex], StringComparison.Ordinal))
                        throw CreateConfigurationException($"[AudioManager] AudioMixer 音量参数名必须互不重复，parameterName={parameterNames[index]}。");
                }
            }
        }

        /// <summary>
        /// 记录 Setting 或 Mixer 参数配置错误，并构造可直接抛出的初始化异常。
        /// </summary>
        /// <param name="message">包含音频流程上下文的错误内容。</param>
        /// <returns>供当前发现错误的方法立即抛出的异常。</returns>
        private static InvalidOperationException CreateConfigurationException(string message)
        {
            WSLog.LogError(message);
            return new InvalidOperationException(message);
        }

        #endregion

        #region 模块帧驱动

        /// <summary>
        /// 将 Unity 每帧回收职责转交给统一 AudioModule。
        /// </summary>
        public void Tick()
        {
            audioModule.Tick();
        }

        #endregion

        #region BGM 操作

        /// <summary>
        /// 播放直接 AudioClip BGM，并按旧接口约定可更新 BGM 分类音量。
        /// </summary>
        /// <param name="clip">BGM 素材。</param>
        /// <param name="volume">非负时更新 BGM 分类音量，负值时保留当前值。</param>
        /// <param name="loop">是否循环。</param>
        public void PlayBGM(AudioClip clip, float volume = 1f, bool loop = true)
        {
            if (volume >= 0f) BGVolume = volume;
            audioModule.PlayBGM(clip, loop);
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
        /// 通过资源 Key 播放跟随组件的 WSFrame SFX。
        /// </summary>
        /// <param name="name">音频资源 Key。</param>
        /// <param name="component">跟随组件。</param>
        /// <param name="is3D">是否为 3D 音频。</param>
        /// <param name="scale">单次播放音量倍率。</param>
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
        /// 通过资源 Key 在指定世界位置播放 WSFrame SFX。
        /// </summary>
        /// <param name="name">音频资源 Key。</param>
        /// <param name="position">世界位置。</param>
        /// <param name="is3D">是否为 3D 音频。</param>
        /// <param name="scale">单次播放音量倍率。</param>
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
        /// 直接播放已持有的 AudioClip，并返回版本安全的单个 Voice 控制句柄。
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
            return audioModule.PlayDirectClip(clip, owner, volume, pitch, spatial);
        }

        /// <summary>
        /// 停止资源 Key SFX 与直接 AudioClip Voice。
        /// </summary>
        public void StopAllSFX()
        {
            audioModule.StopAllSFX();
        }

        #endregion
    }
}
