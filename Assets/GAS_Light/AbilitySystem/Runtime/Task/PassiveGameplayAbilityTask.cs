namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>保持 Passive Ability 活跃且等待外部 End 或 Cancel 的运行 Task。</summary>
    public sealed class PassiveGameplayAbilityTask : GameplayAbilityTask
    {
        #region 构造
        /// <summary>创建尚未启动的 Passive 保持 Task。</summary>
        /// <param name="runtime">承载该 Task 的异步 Ability Runtime。</param>
        public PassiveGameplayAbilityTask(AsynchronousGameplayAbilityRuntime runtime)
            : base(runtime)
        {
        }
        #endregion

        #region 生命周期
        // Passive Task 不注册 Tick，也不会自行完成；父 Runtime 终止时由基类传播 Stop 或 Cancel。
        protected override void OnStart()
        {
        }
        #endregion
    }
}