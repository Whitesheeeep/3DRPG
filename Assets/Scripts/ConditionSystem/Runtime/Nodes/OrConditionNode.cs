using System.Collections.Generic;

namespace RPG.ConditionSystemNS
{
    /// <summary>按顺序寻找满足的分支，全部失败时保留所有候选要求。</summary>
    public sealed class OrConditionNode : ConditionNode
    {
        /// <summary>获取不限数量的子条件容量。</summary>
        public override int MaximumChildren => int.MaxValue;
        /// <summary>获取组合至少需要一个分支。</summary>
        public override int MinimumChildren => 1;

        /// <summary>在首个满足分支短路，失败时返回带 Or 关系的原始原因。</summary>
        /// <param name="context">本次业务上下文。</param>
        /// <returns>任一分支通过时满足，否则返回候选原因树。</returns>
        public override ConditionResult Evaluate(ConditionContext context)
        {
            var reasons = new List<ConditionReason>();
            foreach (ConditionNode child in Children)
            {
                ConditionResult result = child.Evaluate(context);
                if (result.IsMet) return ConditionResult.Met();
                reasons.Add(result.Reason);
            }
            return ConditionResult.NotMet(new CompositeConditionReason(
                E_ConditionReasonOperator.Or, "需要满足任意一个条件", reasons));
        }
    }
}
