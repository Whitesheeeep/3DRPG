namespace WS_Modules.GAS.GameplayEffect
{
    /// <summary>
    /// 定义 Gameplay Effect 的持续时间类型。
    /// </summary>
    public enum E_GameEffectDurationType
    {
        /// <summary>
        /// 立即执行且不进入 Active Gameplay Effect 列表。
        /// </summary>
        Instant,

        /// <summary>
        /// 在指定持续时间内生效，例如 Poison。
        /// </summary>
        Duration,

        /// <summary>
        /// 无限期生效，直到被主动移除，例如装备提供的效果。
        /// </summary>
        Infinite
    }

    /// <summary>
    /// 定义非 Instant Gameplay Effect 再次应用时与哪个 Active Gameplay Effect 合并。
    /// </summary>
    public enum E_GameEffectStackingType
    {
        /// <summary>
        /// 不合并叠层，每次应用都创建独立的 Active Gameplay Effect。
        /// </summary>
        None,

        /// <summary>
        /// 相同 Gameplay Effect 仅在 Source 与 Target 都相同时合并叠层。
        /// </summary>
        AggregateBySource,

        /// <summary>
        /// 相同 Gameplay Effect 在同一 Target 上合并叠层，不区分 Source。
        /// </summary>
        AggregateByTarget
    }

    /// <summary>
    /// 定义 Gameplay Effect 成功增加叠层时如何处理剩余持续时间。
    /// </summary>
    public enum E_GameEffectStackingDurationPolicy
    {
        /// <summary>
        /// 每次成功应用都会把剩余时间重置为完整持续时间。
        /// </summary>
        RefreshOnSuccessfulApplication,

        /// <summary>
        /// 成功增加叠层时不改变当前剩余时间。
        /// </summary>
        NeverRefresh,

        /// <summary>
        /// 每次成功应用都把本次配置的持续时间追加到当前剩余时间。
        /// </summary>
        ExtendDuration
    }

    /// <summary>
    /// 定义周期 Gameplay Effect 成功增加叠层时如何处理下一次执行计时。
    /// </summary>
    public enum E_GameEffectStackingPeriodPolicy
    {
        /// <summary>
        /// 成功应用后丢弃当前周期进度，从完整 Period 重新计时。
        /// </summary>
        ResetOnSuccessfulApplication,

        /// <summary>
        /// 成功增加叠层时保留当前周期进度。
        /// </summary>
        NeverReset
    }

    /// <summary>
    /// 定义 Duration Gameplay Effect 到期时如何处理现有叠层。
    /// </summary>
    public enum E_GameEffectStackingExpirationPolicy
    {
        /// <summary>
        /// 到期时清空全部层数并移除 Active Gameplay Effect。
        /// </summary>
        ClearEntireStack,

        /// <summary>
        /// 到期时移除一层；仍有剩余层数时刷新完整持续时间。
        /// </summary>
        RemoveSingleStackAndRefreshDuration,

        /// <summary>
        /// 到期时不减少层数，仅刷新完整持续时间。
        /// </summary>
        RefreshDuration
    }

    /// <summary>
    /// 定义单项 Gameplay Effect Modifier 是否根据 Active Gameplay Effect 的层数重复计算。
    /// </summary>
    public enum E_GameEffectModifierStackPolicy
    {
        /// <summary>
        /// Modifier 始终只计算一次，StackCount 不影响其 Magnitude。
        /// </summary>
        IgnoreStackCount,

        /// <summary>
        /// 按 StackCount 重复同一运算；Add 线性累加，Multiply 连乘。
        /// </summary>
        RepeatPerStack
    }
}
