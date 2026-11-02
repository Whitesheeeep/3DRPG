using System;
using UnityEngine;

namespace RPG.ConditionSystemNS
{
    /// <summary>提供可配置的固定结果，便于制作基础条件树和验证编辑流程。</summary>
    public sealed class ConstantConditionNode : ConditionNode
    {
        #region 配置字段
        [SerializeField] private bool value = true;
        [SerializeField, TextArea] private string failureMessage = "条件未满足";
        #endregion

        #region 属性与操作
        // 属性：固定值由配置或测试设置，求值不写回。
        /// <summary>获取或设置固定判断值；测试及配置工具使用，求值不修改该值。</summary>
        public bool Value { get => value; set => this.value = value; }

        // 操作：校验原因并返回当前配置值。
        /// <summary>检查未满足时必须提供原因。</summary>
        /// <exception cref="InvalidOperationException">说明为空时抛出。</exception>
        public override void Validate()
        {
            base.Validate();
            if (string.IsNullOrWhiteSpace(failureMessage))
                throw new InvalidOperationException($"{name} 必须配置未满足说明。");
        }

        /// <summary>读取固定值，不访问业务对象或改变配置。</summary>
        /// <param name="context">本次上下文；该节点无需读取其中的数据。</param>
        /// <returns>固定值对应的结果。</returns>
        public override ConditionResult Evaluate(ConditionContext context) => value
            ? ConditionResult.Met() : ConditionResult.NotMet(new ConditionReason(failureMessage));
        #endregion
    }
}
