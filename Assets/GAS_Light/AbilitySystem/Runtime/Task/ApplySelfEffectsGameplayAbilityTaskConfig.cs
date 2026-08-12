using System;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>配置一个启动后立即对 Source 结算 Ability Effects 的 Task。</summary>
    [Serializable]
    public sealed class ApplySelfEffectsGameplayAbilityTaskConfig : GameplayAbilityTaskConfig
    {
        #region 工厂

        /// <summary>创建可由 Unity 反序列化的自身效果配置。</summary>
        public ApplySelfEffectsGameplayAbilityTaskConfig()
        {
        }

        /// <summary>为本次 Ability 激活创建独立的自身效果 Task。</summary>
        /// <param name="runtime">本次异步 Ability Runtime。</param>
        /// <returns>新的 Task 实例。</returns>
        protected override GameplayAbilityTask CreateTask(
            AsynchronousGameplayAbilityRuntime runtime) =>
            new ApplySelfEffectsGameplayAbilityTask(runtime);

        #endregion
    }
}
