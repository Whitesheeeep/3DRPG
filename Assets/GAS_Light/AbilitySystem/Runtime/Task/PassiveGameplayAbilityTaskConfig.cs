using System;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>配置一个保持运行、直到所属 Passive Ability 结束或取消的 Task。</summary>
    [Serializable]
    public sealed class PassiveGameplayAbilityTaskConfig : GameplayAbilityTaskConfig
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