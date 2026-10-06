using System.Collections.Generic;

namespace WS_Modules.SceneModule
{
    /// <summary>场景流程校验得到的不可变诊断摘要。</summary>
    public sealed class SceneLoadValidationResult
    {
        #region 状态

        private readonly List<string> issues;
        /// <summary>获取全部配置错误。</summary>
        public IReadOnlyList<string> Issues => issues;
        /// <summary>获取配置是否可以安全进入执行阶段。</summary>
        public bool IsValid => issues.Count == 0;

        #endregion

        #region 生命周期

        /// <summary>创建校验结果。</summary>
        /// <param name="validationIssues">不可执行配置的路径化错误。</param>
        internal SceneLoadValidationResult(List<string> validationIssues) => issues = validationIssues;

        #endregion
    }
}
