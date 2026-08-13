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

        /// <summary>创建默认同步 Runtime；具体 Data 可直接覆写该唯一工厂入口。</summary>
        /// <param name="activationId">当前 Controller 分配的激活标识。</param>
        /// <param name="spec">本次激活使用的 Ability Spec。</param>
        /// <param name="source">释放 Ability 的 Source ASC。</param>
        /// <param name="setByCaller">本次激活的动态数值快照。</param>
        /// <returns>绑定当前 Data 的同步 Runtime。</returns>
        protected override GameplayAbilityRuntime CreateRuntime(
            int activationId,
            GameplayAbilitySpec spec,
            GameplayAbilitySystemComponent source,
            IReadOnlyDictionary<GameplayTag, float> setByCaller) =>
            new SynchronousGameplayAbilityRuntime(
                activationId, spec, source, setByCaller, this);
        #endregion
    }
}
