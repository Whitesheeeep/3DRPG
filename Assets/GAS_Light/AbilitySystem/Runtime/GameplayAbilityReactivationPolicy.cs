namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>定义同一 Ability Spec 已有 Active Runtime 时再次激活的处理策略。</summary>
    public enum GameplayAbilityReactivationPolicy
    {
        /// <summary>允许同一 Spec 同时存在多个 Runtime。</summary>
        AllowMultiple = 0,
        /// <summary>已有 Active Runtime 时拒绝本次激活。</summary>
        RejectWhileActive = 1,
        /// <summary>已有 Active Runtime 时正常结束旧 Runtime，不创建新 Runtime。</summary>
        ToggleOff = 2
    }
}
