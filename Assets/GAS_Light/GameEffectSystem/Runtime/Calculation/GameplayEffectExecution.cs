using System;
using System.Collections.Generic;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayEffect
{
    /// <summary>定义在 GE 应用边界读取实时上下文并返回业务计算结果的 Execution。</summary>
    [Serializable]
    public abstract class GameplayEffectExecution
    {
        #region 运行时计算

        /// <summary>使用当前 Source、Target 和 Spec 计算一次 Execution 输出。</summary>
        /// <param name="context">本次应用的实时计算上下文。</param>
        /// <returns>成功时返回输出；失败时返回 null。</returns>
        public abstract GameplayEffectExecutionOutput Calculate(
            GameplayEffectCalculationContext context);

        #endregion

        #region 配置契约

        /// <summary>登记该 Execution 必需的 SetByCaller Key。</summary>
        /// <param name="keys">由 GE 汇总的 Key 集合。</param>
        protected internal virtual void CollectRequiredSetByCallerKeys(
            ISet<GameplayTag> keys)
        {
        }

        /// <summary>登记该 Execution 必需的 Source Attribute。</summary>
        /// <param name="attributes">由编辑器和运行时校验的 Attribute 集合。</param>
        protected internal virtual void CollectRequiredSourceAttributes(
            ISet<GameplayAttribute> attributes)
        {
        }

        /// <summary>登记该 Execution 必需的 Target Attribute。</summary>
        /// <param name="attributes">由编辑器和运行时校验的 Attribute 集合。</param>
        protected internal virtual void CollectRequiredTargetAttributes(
            ISet<GameplayAttribute> attributes)
        {
        }

        #endregion
    }
}
