using System;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>兼容旧 Passive 资产的持续效果配置；新资产使用 PersistentSelfEffectsGameplayAbilityTaskConfig。</summary>
    [Serializable]
    [Obsolete("请使用 PersistentSelfEffectsGameplayAbilityTaskConfig。")]
    public sealed class PassiveGameplayAbilityTaskConfig : PersistentSelfEffectsGameplayAbilityTaskConfig
    {
        #region 构造与工厂
        /// <summary>创建不主动完成的 Passive 保持 Task 配置。</summary>
        public PassiveGameplayAbilityTaskConfig()
        {
        }

        // 每次激活创建独立的保持 Task，状态不会在不同 Runtime 之间共享。
        protected override GameplayAbilityTask CreateTask(
            AsynchronousGameplayAbilityRuntime runtime) =>
            new PassiveGameplayAbilityTask(runtime);
        #endregion
    }
}
