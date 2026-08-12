using WS_Modules.GAS.GameplayCue;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>在启动时对 Source 应用一次 Ability Effects 并发布 Execute Cue。</summary>
    public sealed class ApplySelfEffectsGameplayAbilityTask : GameplayAbilityTask
    {
        #region 构造

        /// <summary>创建一次自身效果结算 Task。</summary>
        /// <param name="runtime">承载该 Task 的异步 Ability Runtime。</param>
        public ApplySelfEffectsGameplayAbilityTask(AsynchronousGameplayAbilityRuntime runtime)
            : base(runtime)
        {
        }

        #endregion

        #region 生命周期

        /// <summary>同步应用 Effects 和 Cue，然后立即完成当前 Task。</summary>
        protected override void OnStart()
        {
            GameplayAbilityData data = Runtime.Data;
            data.ApplyConfiguredEffects(
                Runtime.Source,
                Runtime.Source,
                Runtime.Level,
                Runtime.SetByCaller);
            data.PublishConfiguredCues(
                GameplayCueEventType.Execute,
                Runtime.Source,
                Runtime.Source,
                abilityRuntime: Runtime);
            Complete();
        }

        #endregion
    }
}
