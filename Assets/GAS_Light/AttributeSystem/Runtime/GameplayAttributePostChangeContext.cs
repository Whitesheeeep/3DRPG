using System;

namespace WS_Modules.GAS.AttributeSystem
{
    /// <summary>向 AttributeSet Post 回调提供只读查询和受控后续修改请求。</summary>
    public readonly struct GameplayAttributePostChangeContext
    {
        #region 字段与属性

        private readonly GameplayAttributeContainer container;

        /// <summary>获取当前 Container 的只读查询接口。</summary>
        public IReadOnlyGameplayAttributeContainer Attributes => container;

        #endregion

        #region 构造与请求

        // 仅由 Container 为一次提交创建，避免外部伪造修改事务。
        internal GameplayAttributePostChangeContext(GameplayAttributeContainer container) =>
            this.container = container;

        /// <summary>请求在当前回调与事件完成后修改另一个 Attribute 的内部结算值。</summary>
        /// <param name="attribute">待修改 Attribute。</param>
        /// <param name="value">候选结算值。</param>
        /// <returns>请求合法且已加入当前 FIFO 事务时返回 true。</returns>
        public bool RequestSetValue(GameplayAttribute attribute, float value) =>
            container != null && container.EnqueuePostBaseValueChange(attribute, value);

        #endregion
    }
}
