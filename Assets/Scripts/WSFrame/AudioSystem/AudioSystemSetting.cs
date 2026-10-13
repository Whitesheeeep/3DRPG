using System;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Audio;

namespace WS_Modules.AudioSystem
{
    /// <summary>
    /// 保存 WSFrame 音频资源、输出组与 Mixer 暴露参数配置。
    /// </summary>
    [Serializable]
    public class AudioSystemSetting
    {
        [LabelText("音频单元预热数量")]
        [MinValue(0)]
        public int audioSourceInitCount = 5;

        [LabelText("音频单元资源 Key"), WSAddressableKey]
        [InfoBox("用于资源 Key 音效对象池的 AudioSource 预制体资源 Key。")]
        public string audioSourcePrefabPath;

        [LabelText("WSFrame 音频 Mixer")]
        [Required]
        [InfoBox("必须指定 WSFrame 专用 Mixer，并配置 Master、BGM、Effect 三个暴露音量参数。")]
        public AudioMixer audioMixer;

        [LabelText("BGM 输出组")]
        [Required]
        public AudioMixerGroup bgmMixerGroup;

        [LabelText("Effect 输出组（所有 SFX 共用）")]
        [Required]
        public AudioMixerGroup effectMixerGroup;

        [LabelText("全局音量参数名")]
        public string globalVolumeParameterName = "MasterVolume";

        [LabelText("BGM 音量参数名")]
        public string bgVolumeParameterName = "BGMVolume";

        [LabelText("Effect 音量参数名")]
        public string effectVolumeParameterName = "EffectVolume";
    }
}
