using System.Collections.Generic;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>定义由独立 Root Task 驱动完成或中断的异步 Ability。</summary>
    public abstract class AsynchronousGameplayAbilityData : GameplayAbilityData
    {
        #region 字段与属性
        [SerializeReference, Tooltip("每次激活都会据此创建独立的 Root Task 实例。")]
        private GameplayAbilityTaskConfig rootTask;

        /// <summary>获取异步 Ability 的 Root Task 作者配置。</summary>
        public GameplayAbilityTaskConfig RootTask => rootTask;

        // Root 与全部内置子配置必须有效，Controller 才能继续激活。
        internal override bool IsRuntimeConfigurationValid =>
            rootTask is { IsConfigurationValid: true };
        #endregion

        #region Runtime 工厂
        /// <summary>创建默认异步 Runtime；具体 Data 可直接覆写该唯一工厂入口。</summary>
        /// <param name="activationId">当前 Controller 分配的激活标识。</param>
        /// <param name="spec">本次激活使用的 Ability Spec。</param>
        /// <param name="source">释放 Ability 的 Source ASC。</param>
        /// <param name="setByCaller">本次激活的动态数值快照。</param>
        /// <returns>绑定当前 Data 与 Root Task 配置的异步 Runtime。</returns>
        protected override GameplayAbilityRuntime CreateRuntime(
            int activationId,
            GameplayAbilitySpec spec,
            GameplayAbilitySystemComponent source,
            IReadOnlyDictionary<GameplayTag, float> setByCaller) =>
            new AsynchronousGameplayAbilityRuntime(
                activationId, spec, source, setByCaller, this);

        // 仅供测试或具体 Data 的受控初始化使用。
        protected void SetRootTask(GameplayAbilityTaskConfig config) => rootTask = config;
        #endregion
    }
}
