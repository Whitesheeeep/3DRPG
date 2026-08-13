using Cinemachine;
using RPG.SkillSystem;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.CameraSystem
{
    /// <summary>
    /// 保存一类持续摄像机噪声的波形、倍率、生命周期与淡入淡出参数。
    /// </summary>
    [CreateAssetMenu(fileName = "CameraShakeProfile", menuName = "RPG/Camera/Shake Profile")]
    public sealed class CameraShakeProfile : ScriptableObject
    {
        #region 序列化配置

        [SerializeField, LabelText("噪声波形"), AssetsOnly]
        private NoiseSettings noiseSettings;

        [SerializeField, LabelText("幅度倍率"), MinValue(0f)]
        private float amplitudeGain = 1f;

        [SerializeField, LabelText("频率倍率"), MinValue(0f)]
        private float frequencyGain = 1f;

        [SerializeField, LabelText("生命周期")]
        private CameraShakeLifetime lifetime = CameraShakeLifetime.Timed;

        [SerializeField, LabelText("持续时间"), MinValue(0f), ShowIf(nameof(IsTimed))]
        private float duration = 0.4f;

        [SerializeField, LabelText("淡入时间"), MinValue(0f)]
        private float fadeInDuration = 0.05f;

        [SerializeField, LabelText("淡出时间"), MinValue(0f)]
        private float fadeOutDuration = 0.15f;

        [SerializeField, LabelText("混合方式")]
        private CameraModifierBlendMode blendMode = CameraModifierBlendMode.Additive;

        #endregion

        #region 属性

        public NoiseSettings NoiseSettings => noiseSettings;
        public float AmplitudeGain => amplitudeGain;
        public float FrequencyGain => frequencyGain;
        public CameraShakeLifetime Lifetime => lifetime;
        public float Duration => duration;
        public float FadeInDuration => fadeInDuration;
        public float FadeOutDuration => fadeOutDuration;
        public CameraModifierBlendMode BlendMode => blendMode;
        private bool IsTimed => lifetime == CameraShakeLifetime.Timed;

        #endregion

        #region Unity 生命周期

        /// <summary>
        /// 在 Inspector 修改后夹紧时间与倍率，保证运行时采样契约稳定。
        /// </summary>
        private void OnValidate()
        {
            amplitudeGain = Mathf.Max(0f, amplitudeGain);
            frequencyGain = Mathf.Max(0f, frequencyGain);
            duration = Mathf.Max(0f, duration);
            fadeInDuration = Mathf.Max(0f, fadeInDuration);
            fadeOutDuration = Mathf.Max(0f, fadeOutDuration);
        }

        #endregion
    }
}
