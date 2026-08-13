using System;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>通过 ASC Tick 累计有限秒数后完成的 Ability Task。</summary>
    public sealed class WaitDurationGameplayAbilityTask : GameplayAbilityTask
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
        /// <summary>零时长等待在启动调用内立即完成。</summary>
        protected override void OnStart()
        {
            if (duration <= 0f) Complete();
        }
        #endregion

        #region Tick 更新
        /// <summary>累计 ASC 普通更新阶段提供的时间，达到时长后完成。</summary>
        /// <param name="deltaTime">本帧普通更新时间。</param>
        protected override void OnTick(float deltaTime)
        {
            elapsed += deltaTime;
            if (elapsed >= duration) Complete();
        }
        #endregion
    }
}
