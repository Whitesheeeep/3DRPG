using RPG.Markers;
using RPG.SkillSystem;
using Sirenix.OdinInspector;
using UnityEngine;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>配置蓄力伤害结果、释放 SkillConfig 和可选释放表现。</summary>
    [System.Serializable]
    public sealed class ChargedDashReleaseGameplayAbilityTaskConfig : GameplayAbilityTaskConfig
    {
        #region 配置字段

        [SerializeField, Required, Tooltip("只在蓄力释放阶段启动的现有 SkillConfig。")]
        private SkillConfig skillConfig;
        [SerializeField, MinValue(0.01f), Tooltip("达到该真实按住时间后，伤害倍率封顶；继续按住仍等待松开。")]
        private float maximumChargeDuration = 3f;
        [SerializeField, MinValue(0f), Tooltip("零蓄力时的伤害倍率。")]
        private float minimumDamageMultiplier = 1f;
        [SerializeField, MinValue(0f), Tooltip("满蓄力时的伤害倍率。")]
        private float maximumDamageMultiplier = 2f;

        [SerializeField, Tooltip("可选释放音频资源 Key；留空时跳过播放。")]
        private string releaseAudioKey;
        [SerializeField, Tooltip("是否按世界位置播放 3D 释放音效。")]
        private bool releaseAudioIs3D = true;
        [SerializeField, MinValue(0f), Tooltip("释放音效音量倍率。")]
        private float releaseAudioVolumeScale = 1f;
        [SerializeField, Tooltip("右手释放特效解析使用的语义 MarkerKey。")]
        private MarkerKey rightHandMarkerKey;
        [SerializeField, Tooltip("可选右手释放特效 Prefab；留空时跳过播放。")]
        private GameObject releaseVfxPrefab;
        [SerializeField, Tooltip("相对 RightHand Marker 的本地位置偏移。")]
        private Vector3 releaseVfxLocalPosition;
        [SerializeField, Tooltip("相对 RightHand Marker 的本地欧拉角偏移。")]
        private Vector3 releaseVfxLocalEulerAngles;
        [SerializeField, MinValue(0f), Tooltip("释放特效相对 Prefab 根节点缩放的倍率。")]
        private float releaseVfxScale = 1f;

        #endregion

        #region 配置校验与工厂

        /// <summary>验证蓄力时长、伤害倍率和可选释放表现参数。</summary>
        internal override bool IsConfigurationValid =>
            skillConfig != null && IsFinite(maximumChargeDuration) && maximumChargeDuration > 0f &&
            IsFinite(minimumDamageMultiplier) && IsFinite(maximumDamageMultiplier) &&
            minimumDamageMultiplier >= 0f && maximumDamageMultiplier >= minimumDamageMultiplier &&
            IsFinite(releaseAudioVolumeScale) && releaseAudioVolumeScale >= 0f &&
            IsFinite(releaseVfxScale) && releaseVfxScale >= 0f;

        /// <summary>创建只保存释放配置快照、尚未确定方向或启动时间轴的 Task。</summary>
        /// <param name="runtime">拥有该 Task 的异步 Ability Runtime。</param>
        /// <returns>本次激活独立的释放 Task。</returns>
        protected override GameplayAbilityTask CreateTask(AsynchronousGameplayAbilityRuntime runtime) =>
            new ChargedDashReleaseGameplayAbilityTask(
                runtime,
                skillConfig,
                maximumChargeDuration,
                minimumDamageMultiplier,
                maximumDamageMultiplier,
                releaseAudioKey,
                releaseAudioIs3D,
                releaseAudioVolumeScale,
                rightHandMarkerKey,
                releaseVfxPrefab,
                releaseVfxLocalPosition,
                releaseVfxLocalEulerAngles,
                releaseVfxScale);

        #endregion

        #region 数值校验

        /// <summary>判断配置数值是否可安全用于插值和 Spec。</summary>
        /// <param name="value">待判断的浮点值。</param>
        /// <returns>值不是 NaN 或 Infinity 时返回 true。</returns>
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        #endregion
    }
}
