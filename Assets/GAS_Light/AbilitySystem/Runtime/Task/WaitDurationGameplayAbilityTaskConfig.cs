using System;
using UnityEngine;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>配置一个等待有限秒数后正常完成的 Ability Task。</summary>
    [Serializable]
    public sealed class WaitDurationGameplayAbilityTaskConfig : GameplayAbilityTaskConfig
    {
        #region 字段与属性
        [SerializeField, Tooltip("等待秒数；0 表示启动后立即完成。")]
        private float duration;

        /// <summary>获取等待秒数。</summary>
        public float Duration => duration;

        // 在任何 Cost/Cooldown 副作用前拒绝负数与非有限时长。
        internal override bool IsConfigurationValid =>
            duration >= 0f && !float.IsNaN(duration) && !float.IsInfinity(duration);
        #endregion

        #region 构造与工厂
        /// <summary>创建默认 0 秒等待配置。</summary>
        public WaitDurationGameplayAbilityTaskConfig()
        {
        }

        /// <summary>创建指定秒数的等待配置。</summary>
        public WaitDurationGameplayAbilityTaskConfig(float duration)
        {
            this.duration = duration;
        }

        // 每次激活创建独立计时状态。
        protected override GameplayAbilityTask CreateTask(
            AsynchronousGameplayAbilityRuntime runtime) =>
            new WaitDurationGameplayAbilityTask(runtime, duration);
        #endregion
    }
}
