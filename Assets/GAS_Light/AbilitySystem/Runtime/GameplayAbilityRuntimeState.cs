namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>标识单次 Gameplay Ability 激活实例的生命周期状态。</summary>
    public enum GameplayAbilityRuntimeState
    {
        /// <summary>Runtime 已成功激活，允许执行 Effect、正常结束或取消。</summary>
        Active,
        /// <summary>Runtime 已由外部逻辑正常结束。</summary>
        Ended,
        /// <summary>Runtime 已由外部逻辑取消或由 Controller 清理。</summary>
        Cancelled
    }
}
