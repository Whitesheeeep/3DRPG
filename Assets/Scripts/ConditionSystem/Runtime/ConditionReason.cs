using System;
using System.Collections.Generic;

namespace RPG.ConditionSystemNS
{
    /// <summary>提供可继承的原因数据，业务可追加标识和数量；不包含交互回调或窗口引用。</summary>
    public class ConditionReason
    {
        /// <summary>获取可读说明。</summary>
        public string Message { get; }

        /// <summary>保存本次判断产生的说明。</summary>
        /// <param name="message">面向调用方的原因说明。</param>
        public ConditionReason(string message) { Message = message; }
    }

    /// <summary>区分组合原因的逻辑关系，便于业务按实际含义展示要求。</summary>
    public enum E_ConditionReasonOperator
    {
        /// <summary>所有子要求均需满足。</summary>
        And,
        /// <summary>子要求至少满足一个。</summary>
        Or,
        /// <summary>被否定的条件已经满足。</summary>
        Not
    }

    /// <summary>保留组合语义以及原始派生原因对象，不把原因树压平成文本。</summary>
    public sealed class CompositeConditionReason : ConditionReason
    {
        /// <summary>获取逻辑关系。</summary>
        public E_ConditionReasonOperator Operator { get; }
        /// <summary>获取不可修改的子原因快照；Not 的子原因集合为空。</summary>
        public IReadOnlyList<ConditionReason> Children { get; }

        /// <summary>复制原因集合而保留各原因对象的具体类型。</summary>
        /// <param name="operation">组合关系。</param>
        /// <param name="message">组合说明。</param>
        /// <param name="children">未满足的子原因。</param>
        public CompositeConditionReason(E_ConditionReasonOperator operation, string message,
            IEnumerable<ConditionReason> children) : base(message)
        {
            Operator = operation;
            Children = Array.AsReadOnly(new List<ConditionReason>(children).ToArray());
        }
    }
}
