namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>配置在准备动画冻结姿态上等待松开的蓄力阶段。</summary>
    [System.Serializable]
    public sealed class ChargeHoldGameplayAbilityTaskConfig : GameplayAbilityTaskConfig
    {
        #region 配置校验

        /// <summary>蓄力等待阶段不需要额外的序列化参数。</summary>
        internal override bool IsConfigurationValid => true;

        #endregion

        #region Task 工厂

        /// <summary>创建不覆盖准备姿态、尚未开始等待的 Task。</summary>
        /// <param name="runtime">拥有本 Task 的异步 Ability Runtime。</param>
        /// <returns>本次激活独立的蓄力等待 Task。</returns>
        protected override GameplayAbilityTask CreateTask(AsynchronousGameplayAbilityRuntime runtime) =>
            new ChargeHoldGameplayAbilityTask(runtime);

        #endregion
    }
}
