#if UNITY_EDITOR
using System.Collections.Generic;

namespace RPG.ConditionSystemNS.Editor
{
    /// <summary>将公共校验结果交给编辑器展示，不另写一套连接或结构规则。</summary>
    internal static class ConditionGraphValidator
    {
        /// <summary>获取可点击定位的错误与警告。</summary>
        /// <param name="graph">当前编辑资产。</param>
        /// <returns>图模型生成的问题集合。</returns>
        internal static IReadOnlyList<ConditionValidationIssue> Validate(ConditionGraph_SO graph) => graph.GetValidationIssues();
    }
}
#endif
