using System.Collections.Generic;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>执行同步 Data 并在当前调用内自动进入 Ended 的 Ability Runtime。</summary>
    public class SynchronousGameplayAbilityRuntime : GameplayAbilityRuntime
    {
        #region 属性与构造
        /// <summary>获取本次运行使用的同步 Ability Data。</summary>
        public SynchronousGameplayAbilityData Data { get; }

        // 同程序集 Data 工厂或外部专用 Runtime 子类可构造该基础运行状态。
        /// <summary>创建同步 Ability Runtime。</summary>
        protected internal SynchronousGameplayAbilityRuntime(
            int activationId,
            GameplayAbilitySpec spec,
            GameplayAbilitySystemComponent source,
            IReadOnlyDictionary<GameplayTag, float> setByCaller,
            SynchronousGameplayAbilityData data)
            : base(activationId, spec, source, setByCaller)
        {
            Data = data ?? throw new System.ArgumentNullException(nameof(data));
        }
        #endregion

        #region 生命周期
        // 同步业务返回即表示执行完成；若业务已取消，Complete 不会重复改变状态。
        protected override void OnStart()
        {
            Data.ExecuteRuntime(this);
            Complete();
        }
        #endregion
    }
}
