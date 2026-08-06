using System;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>通过 ASC Tick 累计有限秒数后完成的 Ability Task。</summary>
    public sealed class WaitDurationGameplayAbilityTask : GameplayAbilityTickTask
    {
        #region 字段
        private readonly float duration;
        private float elapsed;
        #endregion

        #region 构造
        /// <summary>创建一次独立的等待运行实例。</summary>
        /// <param name="runtime">承载该 Task 的异步 Ability Runtime。</param>
        /// <param name="duration">需要等待的秒数。</param>
        public WaitDurationGameplayAbilityTask(
            AsynchronousGameplayAbilityRuntime runtime,
            float duration)
            : base(runtime)
        {
            this.duration = duration;
        }
        #endregion

        #region 生命周期
        // 0 秒在 Tick 注册完成后立即结束；正数等待 ASC Tick 推进。
        protected override void OnTickStarted()
        {
            if (duration <= 0f) Complete();
        }
        #endregion

        #region Tick 更新
        // 累计 ASC Tick 提供的 deltaTime，达到时长后完成并自动注销。
        protected override void OnTick(float deltaTime)
        {
            elapsed += deltaTime;
            if (elapsed >= duration) Complete();
        }
        #endregion
    }
}
