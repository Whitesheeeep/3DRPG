namespace WS_Modules.GAS.GameplayCue
{
    /// <summary>描述 GameplayCue 本次需要执行的生命周期动作。</summary>
    public enum GameplayCueEventType
    {
        /// <summary>执行一次性表现。</summary>
        Execute,
        /// <summary>创建并启动持续表现。</summary>
        Active,
        /// <summary>停止并移除持续表现。</summary>
        Remove
    }
}
