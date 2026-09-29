using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.GameplayCue;

namespace RPG.Character.Combat
{
    /// <summary>
    /// 配置命中音效资源 Key、音量和空间音频选项。
    /// </summary>
    [CreateAssetMenu(fileName = "HitSoundCueData", menuName = "RPG/Gameplay Cue/Hit Sound")]
    public sealed class HitSoundCueData : GameplayCueData
    {
        #region 配置字段

        [SerializeField, Tooltip("通过项目资源加载器解析的音频资源 Key。")]
        private string audioKey;
        [SerializeField, MinValue(0f), Tooltip("命中音效音量倍率。")]
        private float volume = 1f;
        [SerializeField, Tooltip("启用后按命中世界位置播放 3D 音频。")]
        private bool spatial = true;

        #endregion

        #region 属性

        /// <summary>获取音频资源 Key。</summary>
        public string AudioKey => audioKey;
        /// <summary>获取命中音效音量倍率。</summary>
        public float Volume => volume;
        /// <summary>获取是否按世界位置进行 3D 播放。</summary>
        public bool Spatial => spatial;

        #endregion

#if UNITY_EDITOR
        #region 编辑器校验

        /// <summary>检查命中音效 Key 非空且音量为非负有限值。</summary>
        protected override void OnValidate()
        {
            base.OnValidate();
            if (string.IsNullOrWhiteSpace(audioKey))
                Debug.LogError($"HitSoundCueData '{name}' 的 Audio Key 不能为空。", this);
            if (volume < 0f || float.IsNaN(volume) || float.IsInfinity(volume))
                Debug.LogError($"HitSoundCueData '{name}' 的音量必须是有限非负数。", this);
        }

        #endregion
#endif
    }
}
