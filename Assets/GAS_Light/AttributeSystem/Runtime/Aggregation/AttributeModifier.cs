namespace WS_Modules.GAS.AttributeSystem
{
    /// <summary>
    /// 运行时的 modifier，表示已应用到单个 Container 的运行时 Attribute Modifier，并以对象引用作为身份。
    /// </summary>
    public sealed class AttributeModifier
    {
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
        #endregion

        #region 构造
        // 仅由 Container 从已校验 Config 创建，避免外部伪造已应用 Modifier。
        internal AttributeModifier(IModifierSource source, AttributeModifierConfig config)
        {
            Source = source;
            Attribute = config.Attribute;
            Type = config.Type;
            Magnitude = config.Magnitude;
            Priority = config.Priority;
        }
        #endregion
    }
}