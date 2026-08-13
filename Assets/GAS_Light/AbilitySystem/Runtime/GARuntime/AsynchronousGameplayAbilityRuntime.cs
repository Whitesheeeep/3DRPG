using System.Collections.Generic;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>持有独立 Root Task 并由其完成通知驱动终态的异步 Ability Runtime。</summary>
    public class AsynchronousGameplayAbilityRuntime : GameplayAbilityRuntime
    {
        #region 属性与构造
        /// <summary>获取本次运行使用的异步 Ability Data。</summary>
        public AsynchronousGameplayAbilityData Data { get; }
        /// <summary>获取本次激活独享的 Root Task。</summary>
        public GameplayAbilityTask RootTask { get; }

        // 同程序集 Data 工厂或外部专用 Runtime 子类可构造该基础运行状态。
        /// <summary>创建异步 Runtime，并立即从 Definition 构造独立 Root Task。</summary>
        protected internal AsynchronousGameplayAbilityRuntime(
            int activationId,
            GameplayAbilitySpec spec,
            GameplayAbilitySystemComponent source,
            IReadOnlyDictionary<GameplayTag, float> setByCaller,
            AsynchronousGameplayAbilityData data)
            : base(activationId, spec, source, setByCaller)
        {
            Data = data ?? throw new System.ArgumentNullException(nameof(data));
            RootTask = Data.RootTask.CreateTaskInstance(this);
        }
        #endregion

        #region 生命周期
        // 先订阅完成事件再启动，以支持 Root Task 在 Start 中同步完成。
        protected override void OnStart()
        {
            RootTask.Completed += OnRootTaskCompleted;
            RootTask.Start();
        }

        // 外部正常 End 时把仍在运行的 Root 标记为 Stopped。
        protected override void OnEnd()
        {
            RootTask.Completed -= OnRootTaskCompleted;
            RootTask.Stop();
        }

        // 外部 Cancel 或 Clear 时向 Root 传播 Cancelled。
        protected override void OnCancel()
        {
            RootTask.Completed -= OnRootTaskCompleted;
            RootTask.Cancel();
        }

        /// <summary>将普通更新阶段转发给 Root Task。</summary>
        /// <param name="deltaTime">普通更新阶段的秒数。</param>
        internal override void Tick(float deltaTime) => RootTask.Tick(deltaTime);

        /// <summary>将固定更新阶段转发给 Root Task。</summary>
        /// <param name="fixedDeltaTime">固定更新阶段的秒数。</param>
        internal override void FixedTick(float fixedDeltaTime) => RootTask.FixedTick(fixedDeltaTime);

        /// <summary>将延迟更新阶段转发给 Root Task。</summary>
        /// <param name="deltaTime">延迟更新阶段使用的秒数。</param>
        internal override void LateTick(float deltaTime) => RootTask.LateTick(deltaTime);

        // Root 正常完成后解除订阅并结束 Runtime。
        private void OnRootTaskCompleted(GameplayAbilityTask task)
        {
            RootTask.Completed -= OnRootTaskCompleted;
            Complete();
        }
        #endregion
    }
}
