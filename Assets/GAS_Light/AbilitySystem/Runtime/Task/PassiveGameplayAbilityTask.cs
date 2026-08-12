namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>兼容旧 Passive 配置的持续自身效果 Task；新资产使用 PersistentSelfEffectsGameplayAbilityTask。</summary>
    [System.Obsolete("请使用 PersistentSelfEffectsGameplayAbilityTask。")]
    public sealed class PassiveGameplayAbilityTask : PersistentSelfEffectsGameplayAbilityTask
    {
        #region 构造
        /// <summary>创建尚未启动的 Passive 保持 Task。</summary>
        /// <param name="runtime">承载该 Task 的异步 Ability Runtime。</param>
        public PassiveGameplayAbilityTask(AsynchronousGameplayAbilityRuntime runtime)
            : base(runtime)
        {
        }
        #endregion
    }
}
