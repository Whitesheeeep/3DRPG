using System;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>保存 Ability Task 作者配置，并为每次异步激活创建独立运行实例。</summary>
    [Serializable]
    public abstract class GameplayAbilityTaskConfig
    {
        // 内置 Config 覆盖该属性，在 Cost/Cooldown 前拒绝非法配置。
        internal virtual bool IsConfigurationValid => true;

        // Runtime 使用统一包装入口创建 Task。
        internal GameplayAbilityTask CreateTaskInstance(
            AsynchronousGameplayAbilityRuntime runtime) => CreateTask(runtime);

        // 具体 Config 根据配置创建全新的 Task 运行实例。
        protected abstract GameplayAbilityTask CreateTask(
            AsynchronousGameplayAbilityRuntime runtime);
    }
}
