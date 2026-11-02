using System;
using UnityEngine;

namespace RPG.ConditionSystemNS
{
    /// <summary>反转唯一子条件的满足状态，不捕获或反转配置异常。</summary>
    public sealed class NotConditionNode : ConditionNode
    {
        #region 配置字段
        [SerializeField, TextArea] private string failureMessage = "当前条件不允许满足";
        #endregion

        #region 属性与求值
        // 属性：否定节点始终恰好包含一个子条件。
        /// <summary>获取仅允许一个子条件的容量。</summary>
        public override int MaximumChildren => 1;
        /// <summary>获取必须配置一个子条件。</summary>
        public override int MinimumChildren => 1;

        // 校验与求值：只对有效条件的满足状态取反。
        /// <summary>检查否定失败时所需的独立说明。</summary>
        /// <exception cref="InvalidOperationException">子条件或说明不合法时抛出。</exception>
        public override void Validate()
        {
            base.Validate();
            if (string.IsNullOrWhiteSpace(failureMessage))
                throw new InvalidOperationException($"{name} 必须配置否定条件的未满足说明。");
        }

        /// <summary>反转子结果；子条件满足时使用独立原因，不伪造子条件的失败原因。</summary>
        /// <param name="context">本次业务上下文。</param>
        /// <returns>否定后的结果。</returns>
        public override ConditionResult Evaluate(ConditionContext context) =>
            Children[0].Evaluate(context).IsMet
                ? ConditionResult.NotMet(new CompositeConditionReason(E_ConditionReasonOperator.Not,
                    failureMessage, Array.Empty<ConditionReason>()))
                : ConditionResult.Met();
        #endregion
    }
}
