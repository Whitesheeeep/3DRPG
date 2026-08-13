using WS_Modules.GAS.GameplayCue;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>通过 ASC Tick 在有限或无限时段内周期结算 Source Effects。</summary>
    public sealed class PeriodicSelfEffectsGameplayAbilityTask : GameplayAbilityTask
    {
        #region 字段

        private readonly bool infinite;
        private readonly float duration;
        private readonly float period;
        private readonly bool executeOnStart;
        private float elapsed;
        private float periodElapsed;

        #endregion

        #region 构造

        /// <summary>创建一份独立的周期自身效果运行状态。</summary>
        /// <param name="runtime">承载该 Task 的异步 Ability Runtime。</param>
        /// <param name="infinite">是否保持到外部结束。</param>
        /// <param name="duration">有限模式持续秒数。</param>
        /// <param name="period">周期秒数。</param>
        /// <param name="executeOnStart">是否启动时立即执行。</param>
        public PeriodicSelfEffectsGameplayAbilityTask(
            AsynchronousGameplayAbilityRuntime runtime,
            bool infinite,
            float duration,
            float period,
            bool executeOnStart)
            : base(runtime)
        {
            this.infinite = infinite;
            this.duration = duration;
            this.period = period;
            this.executeOnStart = executeOnStart;
        }

        #endregion

        #region 生命周期

        /// <summary>处理首次立即结算，并让零时长有限 Task 在当前调用内完成。</summary>
        protected override void OnStart()
        {
            if (executeOnStart) ApplyCycle();
            if (!infinite && duration <= 0f) Complete();
        }

        #endregion

        #region Tick 更新

        /// <summary>只累计仍位于有限 Duration 内的时间，并补执行大步长跨过的完整周期。</summary>
        /// <param name="deltaTime">ASC 本帧推进秒数。</param>
        protected override void OnTick(float deltaTime)
        {
            float activeDelta = infinite
                ? deltaTime
                : UnityEngine.Mathf.Min(deltaTime, duration - elapsed);
            elapsed += activeDelta;
            periodElapsed += activeDelta;

            while (periodElapsed >= period && State == GameplayAbilityTaskState.Running)
            {
                periodElapsed -= period;
                ApplyCycle();
            }

            if (!infinite && elapsed >= duration) Complete();
        }

        /// <summary>对 Source 应用一次 Effects，并为本次周期发布 Execute Cue。</summary>
        private void ApplyCycle()
        {
            GameplayAbilityData data = Runtime.Data;
            data.ApplyConfiguredEffects(
                Runtime.SourceASC,
                Runtime.SourceASC,
                Runtime.Level,
                Runtime.SetByCaller);
            data.PublishConfiguredCues(
                GameplayCueEventType.Execute,
                Runtime.SourceASC,
                Runtime.SourceASC,
                abilityRuntime: Runtime);
        }

        #endregion
    }
}
