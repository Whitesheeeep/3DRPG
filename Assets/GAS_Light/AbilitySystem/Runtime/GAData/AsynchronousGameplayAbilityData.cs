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
        // 需要专用运行状态的 Ability 可覆盖该工厂。
        protected virtual AsynchronousGameplayAbilityRuntime CreateAsynchronousRuntime(
            int activationId,
            GameplayAbilitySpec spec,
            GameplayAbilitySystemComponent source,
            IReadOnlyDictionary<GameplayTag, float> setByCaller) =>
            new(
                activationId, spec, source, setByCaller, this);

        // 将公共多态工厂固定转发到异步 Runtime 工厂。
        protected sealed override GameplayAbilityRuntime CreateRuntime(
            int activationId,
            GameplayAbilitySpec spec,
            GameplayAbilitySystemComponent source,
            IReadOnlyDictionary<GameplayTag, float> setByCaller) =>
            CreateAsynchronousRuntime(activationId, spec, source, setByCaller);

        // 仅供测试或具体 Data 的受控初始化使用。
        protected void SetRootTask(GameplayAbilityTaskConfig config) => rootTask = config;
        #endregion
    }
}
