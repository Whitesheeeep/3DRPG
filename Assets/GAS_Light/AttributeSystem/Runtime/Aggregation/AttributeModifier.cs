using System;

namespace WS_Modules.GAS.AttributeSystem
{
    /// <summary>
    /// 保存已经计算完成的不可变 Attribute Modifier；提交后以对象引用作为精确删除 Handle。
    /// </summary>
    public sealed class AttributeModifier
    {
        #region 字段

        private GameplayAttributeContainer owner;

        #endregion

        #region 属性

        /// <summary>获取产生该 Modifier 的运行时 Source。</summary>
        public IModifierSource Source { get; }

        /// <summary>获取目标 Attribute。</summary>
        public GameplayAttribute Attribute { get; }

        /// <summary>获取数值运算类型。</summary>
        public AttributeModifierType Type { get; }

        /// <summary>获取当前运算数值。</summary>
        public float Magnitude { get; }

        /// <summary>获取从小到大执行的优先级。</summary>
        public int Priority { get; }

        // 未提交或 Instant Modifier 没有 Owner；持续提交成功后由 Container 绑定。
        internal GameplayAttributeContainer Owner => owner;

        #endregion

        #region 构造

        /// <summary>创建已经计算完成、尚未提交到 Container 的 Attribute Modifier。</summary>
        /// <param name="source">产生该 Modifier 的 Active GE 等运行时身份。</param>
        /// <param name="attribute">目标 Attribute。</param>
        /// <param name="type">Add、Multiply 或 Override 运算。</param>
        /// <param name="magnitude">最终计算得到的运算数值。</param>
        /// <param name="priority">从小到大执行的优先级。</param>
        public AttributeModifier(
            IModifierSource source,
            GameplayAttribute attribute,
            AttributeModifierType type,
            float magnitude,
            int priority = 0)
        {
            Source = source;
            Attribute = attribute;
            Type = type;
            Magnitude = magnitude;
            Priority = priority;
        }

        #endregion

        #region Container 所有权

        // 持续提交前统一校验不可变字段；Instant 同样复用该边界。
        internal bool IsValid() =>
            Source != null &&
            Attribute.IsValid &&
            (Type is AttributeModifierType.Add or
                AttributeModifierType.Multiply or
                AttributeModifierType.Override) &&
            !float.IsNaN(Magnitude) &&
            !float.IsInfinity(Magnitude);

        // 只有未归属的候选才能绑定，避免同一 Handle 被多个 Container 共享。
        internal void Attach(GameplayAttributeContainer container)
        {
            if (container == null || owner != null)
                throw new InvalidOperationException("Modifier 必须处于未归属状态才能提交。");
            owner = container;
        }

        // 只有当前 Owner 可以解除归属，防止错误 Container 破坏 Handle 状态。
        internal void Detach(GameplayAttributeContainer container)
        {
            if (!ReferenceEquals(owner, container))
                throw new InvalidOperationException("只有当前 Owner Container 可以解除 Modifier 归属。");
            owner = null;
        }

        #endregion
    }
}
