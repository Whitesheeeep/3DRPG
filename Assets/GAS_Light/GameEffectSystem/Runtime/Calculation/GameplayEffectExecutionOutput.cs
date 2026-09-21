using System.Collections.Generic;
using WS_Modules.GAS.AttributeSystem;

namespace WS_Modules.GAS.GameplayEffect
{
    /// <summary>保存单个 Execution 的 Modifier 输出与可选业务元数据。</summary>
    public sealed class GameplayEffectExecutionOutput
    {
        #region 属性

        /// <summary>获取该 Execution 产生的最终 AttributeModifier 列表。</summary>
        public IReadOnlyList<AttributeModifier> Modifiers { get; }

        /// <summary>获取该 Execution 产生的业务结果；没有元数据时为 null。</summary>
        public GameplayEffectExecutionResult Result { get; }

        #endregion

        #region 构造

        /// <summary>创建一个 Execution 输出。</summary>
        /// <param name="modifiers">已经计算完成的 Modifier 集合。</param>
        /// <param name="result">可选业务结果。</param>
        public GameplayEffectExecutionOutput(
            IReadOnlyList<AttributeModifier> modifiers,
            GameplayEffectExecutionResult result = null)
        {
            Modifiers = modifiers ?? System.Array.Empty<AttributeModifier>();
            Result = result;
        }

        #endregion
    }
}
