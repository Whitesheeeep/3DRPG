namespace WS_Modules.GAS.AttributeSystem
{
    /// <summary>定义 Attribute Modifier 在单个优先级中的数值运算。</summary>
    public enum AttributeModifierType
    {
        /// <summary>将 Magnitude 加到当前聚合值。</summary>
        Add,

        /// <summary>将当前聚合值乘以 Magnitude。</summary>
        Multiply,

        /// <summary>将当前优先级的聚合结果覆盖为 Magnitude。</summary>
        Override
    }
}
