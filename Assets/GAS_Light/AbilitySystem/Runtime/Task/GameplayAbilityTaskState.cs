namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>描述 Ability Task 从未启动到完成、停止或取消的生命周期状态。</summary>
    public enum GameplayAbilityTaskState
    {
        Inactive,
        Running,
        Completed,
        Stopped,
        Cancelled
    }
}
