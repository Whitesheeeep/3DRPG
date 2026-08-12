using System;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>配置一个持续持有 Source Effects，直到 Ability 结束或取消的 Task。</summary>
    [Serializable]
    public class PersistentSelfEffectsGameplayAbilityTaskConfig : GameplayAbilityTaskConfig
    {
        #region 构造与工厂

        /// <summary>创建可由 Unity 反序列化的持续自身效果配置。</summary>
        public PersistentSelfEffectsGameplayAbilityTaskConfig()
        {
        }

        /// <summary>为本次激活创建独立持续效果 Task。</summary>
        /// <param name="runtime">本次异步 Ability Runtime。</param>
        /// <returns>新的持续效果 Task。</returns>
        protected override GameplayAbilityTask CreateTask(
            AsynchronousGameplayAbilityRuntime runtime) =>
            new PersistentSelfEffectsGameplayAbilityTask(runtime);

        #endregion
    }
}
