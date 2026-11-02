using System.Collections.Generic;

namespace RPG.ConditionSystemNS
{
    /// <summary>按配置顺序判断所有子条件，保留每个未满足原因。</summary>
    public sealed class AndConditionNode : ConditionNode
    {
        /// <summary>获取不限数量的子条件容量。</summary>
        public override int MaximumChildren => int.MaxValue;
        /// <summary>获取组合至少需要一个子条件。</summary>
        public override int MinimumChildren => 1;

        /// <summary>完整评估所有要求，供业务同时展示多个不足项。</summary>
        /// <param name="context">本次业务上下文。</param>
        /// <returns>所有要求通过时满足，否则保留 And 原因树。</returns>
        public override ConditionResult Evaluate(ConditionContext context)
        {
            var reasons = new List<ConditionReason>();
            // And 不短路，保证多个不足项的原始原因均可交给业务展示。
            foreach (ConditionNode child in Children)
            {
                ConditionResult result = child.Evaluate(context);
                if (!result.IsMet) reasons.Add(result.Reason);
            }
            return reasons.Count == 0 ? ConditionResult.Met() : ConditionResult.NotMet(
                new CompositeConditionReason(E_ConditionReasonOperator.And, "需要满足全部条件", reasons));
        }
    }
}
