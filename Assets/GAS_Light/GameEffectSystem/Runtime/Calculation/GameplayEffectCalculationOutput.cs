using System;
using System.Collections.Generic;
using WS_Modules.GAS.AttributeSystem;

namespace WS_Modules.GAS.GameplayEffect
{
    /// <summary>保存普通 Modifier 与全部 Execution 合并后的原子结算输出。</summary>
    public sealed class GameplayEffectCalculationOutput
    {
        #region 属性

        /// <summary>获取本次需要原子提交的全部 Modifier。</summary>
        public IReadOnlyList<AttributeModifier> Modifiers { get; }

        /// <summary>获取按 Execution 顺序收集的非空业务结果。</summary>
        public IReadOnlyList<GameplayEffectExecutionResult> ExecutionResults { get; }

        /// <summary>获取合法但没有任何数值或业务输出的空结果。</summary>
        public static GameplayEffectCalculationOutput Empty { get; } =
            new GameplayEffectCalculationOutput(
                Array.Empty<AttributeModifier>(),
                Array.Empty<GameplayEffectExecutionResult>());

        #endregion

        #region 构造

        /// <summary>创建不可变的聚合计算输出。</summary>
        /// <param name="modifiers">待提交 Modifier。</param>
        /// <param name="executionResults">Execution 业务结果。</param>
        public GameplayEffectCalculationOutput(
            IReadOnlyList<AttributeModifier> modifiers,
            IReadOnlyList<GameplayEffectExecutionResult> executionResults)
        {
            Modifiers = modifiers ?? Array.Empty<AttributeModifier>();
            ExecutionResults = executionResults ?? Array.Empty<GameplayEffectExecutionResult>();
        }

        #endregion
    }
}
