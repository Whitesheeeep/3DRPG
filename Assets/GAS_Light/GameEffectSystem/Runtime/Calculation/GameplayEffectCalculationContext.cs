using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.AttributeSystem;

namespace WS_Modules.GAS.GameplayEffect
{
    /// <summary>提供一次 GE 计算所需的 Spec、Source、Target 和层数上下文。</summary>
    public sealed class GameplayEffectCalculationContext
    {
        #region 属性

        /// <summary>获取本次计算使用的封存 Spec。</summary>
        public GameplayEffectSpec Spec { get; }

        /// <summary>获取 Spec 固定的 Source ASC。</summary>
        public GameplayAbilitySystemComponent Source { get; }

        /// <summary>获取本次应用的 Target ASC。</summary>
        public GameplayAbilitySystemComponent Target { get; }

        /// <summary>获取 Spec 固定的应用等级。</summary>
        public int Level => Spec.Level;

        /// <summary>获取当前 Active Runtime 的层数；Instant GE 为 1。</summary>
        public int StackCount { get; }

        #endregion

        #region 字段

        // 依赖字段：AttributeModifier 必须绑定到本次 Spec 或 Active Runtime 的 Source Handle。
        private readonly IModifierSource modifierSource;

        #endregion

        #region 构造

        /// <summary>创建一次计算上下文。</summary>
        /// <param name="spec">已封存的 GE Spec。</param>
        /// <param name="target">本次应用的 Target ASC。</param>
        /// <param name="stackCount">本次计算使用的层数。</param>
        /// <param name="modifierSource">最终 AttributeModifier 的 Source Handle。</param>
        internal GameplayEffectCalculationContext(
            GameplayEffectSpec spec,
            GameplayAbilitySystemComponent target,
            int stackCount,
            IModifierSource modifierSource)
        {
            Spec = spec;
            Source = spec.Source;
            Target = target;
            StackCount = stackCount;
            this.modifierSource = modifierSource;
        }

        #endregion

        #region 查询与输出

        /// <summary>读取 Source 当前 Attribute 值，不使用历史快照。</summary>
        /// <param name="attribute">Source Attribute。</param>
        /// <param name="value">读取成功时返回当前值。</param>
        /// <returns>Source 有效且包含该 Attribute 时返回 true。</returns>
        public bool TryGetSourceAttribute(GameplayAttribute attribute, out float value)
        {
            if (Source != null)
                return Source.TryGetCurrentValue(attribute, out value);

            // Source 不存在时仍必须为 out 参数赋值，避免短路返回造成未赋值编译错误。
            value = default;
            return false;
        }

        /// <summary>读取 Target 当前 Attribute 值，不使用历史快照。</summary>
        /// <param name="attribute">Target Attribute。</param>
        /// <param name="value">读取成功时返回当前值。</param>
        /// <returns>Target 有效且包含该 Attribute 时返回 true。</returns>
        public bool TryGetTargetAttribute(GameplayAttribute attribute, out float value)
        {
            if (Target != null)
                return Target.TryGetCurrentValue(attribute, out value);

            // Target 不存在时返回失败，并提供确定的默认输出值。
            value = default;
            return false;
        }

        /// <summary>读取 Spec 中封存的 SetByCaller 值。</summary>
        /// <param name="key">稳定的 SetByCaller Tag Key。</param>
        /// <param name="value">找到时返回对应值。</param>
        /// <returns>Key 存在且值有效时返回 true。</returns>
        public bool TryGetSetByCaller(
            WS_Modules.GAS.TAG.GameplayTag key,
            out float value) =>
            Spec.SetByCaller.TryGetValue(key, out value);

        /// <summary>创建绑定到当前 GE Runtime 或 Spec 的最终 AttributeModifier。</summary>
        /// <param name="attribute">目标 Attribute。</param>
        /// <param name="type">数值运算类型。</param>
        /// <param name="magnitude">已计算的有限 Magnitude。</param>
        /// <param name="priority">Modifier 优先级。</param>
        /// <returns>尚未提交到 AttributeContainer 的 Modifier。</returns>
        public AttributeModifier CreateModifier(
            GameplayAttribute attribute,
            AttributeModifierType type,
            float magnitude,
            int priority = 0) =>
            new(modifierSource, attribute, type, magnitude, priority);

        #endregion
    }
}
