using WS_Modules.GAS.GameplayEffect;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>描述一个 Ability 提交或结束的 Cooldown Runtime，供 UI 和其他观察者订阅。</summary>
    public sealed class GameplayAbilityCooldownEventArgs
    {
        #region 属性

        /// <summary>获取产生该 Cooldown 的 Ability Runtime。</summary>
        public GameplayAbilityRuntime AbilityRuntime { get; }
        /// <summary>获取产生该 Cooldown 的 Ability Spec。</summary>
        public GameplayAbilitySpec Spec { get; }
        /// <summary>获取产生该 Cooldown 的 Ability 配置。</summary>
        public GameplayAbilityData AbilityData { get; }
        /// <summary>获取产生该 Cooldown 的临时 Handle。</summary>
        public GameplayAbilityHandle Handle { get; }
        /// <summary>获取本次 Cooldown 的 GE Runtime。</summary>
        public GameEffectRuntime CooldownRuntime { get; }
        /// <summary>获取 Cooldown 的配置时长；Infinite 时为正无穷语义。</summary>
        public float Duration { get; }
        /// <summary>获取事件发送时的剩余时长。</summary>
        public float RemainingDuration { get; }
        /// <summary>获取该 Cooldown 是否为 Infinite。</summary>
        public bool IsInfinite { get; }

        #endregion

        #region 构造

        /// <summary>创建一个不可变的 Cooldown 生命周期事件参数。</summary>
        /// <param name="abilityRuntime">关联的 Ability Runtime。</param>
        /// <param name="cooldownRuntime">关联的 Cooldown GE Runtime。</param>
        public GameplayAbilityCooldownEventArgs(
            GameplayAbilityRuntime abilityRuntime,
            GameEffectRuntime cooldownRuntime)
        {
            AbilityRuntime = abilityRuntime;
            Spec = abilityRuntime.Spec;
            AbilityData = Spec.Data;
            Handle = Spec.Handle;
            CooldownRuntime = cooldownRuntime;
            Duration = cooldownRuntime.Data.DurationType == E_GameEffectDurationType.Infinite
                ? float.PositiveInfinity
                : cooldownRuntime.Data.Duration;
            RemainingDuration = cooldownRuntime.RemainingDuration;
            IsInfinite = cooldownRuntime.Data.DurationType == E_GameEffectDurationType.Infinite;
        }

        #endregion
    }
}
