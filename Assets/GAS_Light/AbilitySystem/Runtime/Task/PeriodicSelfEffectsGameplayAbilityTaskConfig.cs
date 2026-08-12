using System;
using UnityEngine;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>配置按固定周期向 Source 重复应用 Ability Effects 的 Task。</summary>
    [Serializable]
    public sealed class PeriodicSelfEffectsGameplayAbilityTaskConfig : GameplayAbilityTaskConfig
    {
        #region 字段与属性

        [SerializeField, Tooltip("启用后忽略 Duration，直到外部 End 或 Cancel。")]
        private bool infinite;
        [SerializeField, Min(0f), Tooltip("有限引导的持续秒数。")]
        private float duration = 1f;
        [SerializeField, Min(0.001f), Tooltip("两次 Effects 结算之间的秒数。")]
        private float period = 1f;
        [SerializeField, Tooltip("启动 Task 时是否立即执行第一次结算。")]
        private bool executeOnStart = true;

        /// <summary>获取是否保持运行直到外部结束。</summary>
        public bool Infinite => infinite;
        /// <summary>获取有限引导的持续秒数。</summary>
        public float Duration => duration;
        /// <summary>获取周期秒数。</summary>
        public float Period => period;
        /// <summary>获取是否在启动时立即执行。</summary>
        public bool ExecuteOnStart => executeOnStart;

        // 配置来自 SerializeReference 边界，必须在 Cost/Cooldown 前拒绝非法时间参数。
        internal override bool IsConfigurationValid =>
            period > 0f && IsFinite(period) &&
            (infinite || duration >= 0f && IsFinite(duration));

        #endregion

        #region 构造与工厂

        /// <summary>创建默认持续一秒、每秒执行且启动时立即结算的配置。</summary>
        public PeriodicSelfEffectsGameplayAbilityTaskConfig()
        {
        }

        /// <summary>创建指定周期参数的自身结算配置。</summary>
        /// <param name="infinite">是否保持到外部结束。</param>
        /// <param name="duration">有限模式持续秒数。</param>
        /// <param name="period">周期秒数。</param>
        /// <param name="executeOnStart">是否启动时立即执行。</param>
        public PeriodicSelfEffectsGameplayAbilityTaskConfig(
            bool infinite,
            float duration,
            float period,
            bool executeOnStart)
        {
            this.infinite = infinite;
            this.duration = duration;
            this.period = period;
            this.executeOnStart = executeOnStart;
        }

        /// <summary>为本次激活创建独立的周期计时 Task。</summary>
        /// <param name="runtime">本次异步 Ability Runtime。</param>
        /// <returns>新的周期 Task。</returns>
        protected override GameplayAbilityTask CreateTask(
            AsynchronousGameplayAbilityRuntime runtime) =>
            new PeriodicSelfEffectsGameplayAbilityTask(
                runtime, infinite, duration, period, executeOnStart);

        #endregion

        #region 校验辅助

        /// <summary>判断指定浮点数是否为有限值。</summary>
        /// <param name="value">待检查数值。</param>
        /// <returns>不是 NaN 或 Infinity 时返回 true。</returns>
        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        #endregion
    }
}
