using System.Collections.Generic;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>定义在当前调用内完成业务执行且不创建 Ability Task 的同步 Ability。</summary>
    public abstract class SynchronousGameplayAbilityData : GameplayAbilityData
    {
        #region 执行与工厂
        // Runtime 通过该包装入口执行具体 Data。
        internal void ExecuteRuntime(SynchronousGameplayAbilityRuntime runtime) => Execute(runtime);

        // 具体同步 Ability 在当前调用内完成同步业务。
        protected abstract void Execute(SynchronousGameplayAbilityRuntime runtime);

        // 需要专用运行状态的同步 Ability 可覆盖该工厂。
        protected virtual SynchronousGameplayAbilityRuntime CreateSynchronousRuntime(
            int activationId,
            GameplayAbilitySpec spec,
            GameplayAbilitySystemComponent source,
            IReadOnlyDictionary<GameplayTag, float> setByCaller) =>
            new SynchronousGameplayAbilityRuntime(
                activationId, spec, source, setByCaller, this);

        // 将公共多态工厂固定转发到同步 Runtime 工厂。
        protected sealed override GameplayAbilityRuntime CreateRuntime(
            int activationId,
            GameplayAbilitySpec spec,
            GameplayAbilitySystemComponent source,
            IReadOnlyDictionary<GameplayTag, float> setByCaller) =>
            CreateSynchronousRuntime(activationId, spec, source, setByCaller);
        #endregion
    }
}
