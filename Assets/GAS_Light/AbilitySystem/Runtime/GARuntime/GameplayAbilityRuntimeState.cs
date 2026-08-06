namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>描述单次 Gameplay Ability 激活从候选创建到终止的生命周期状态。</summary>
    public enum GameplayAbilityRuntimeState
    {
        Created,
        Active,
        Ended,
        Cancelled
    }
}
