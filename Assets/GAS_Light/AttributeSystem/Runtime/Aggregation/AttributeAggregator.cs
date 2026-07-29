using System.Collections.Generic;

namespace WS_Modules.GAS.AttributeSystem
{
    /// <summary>为单个运行时 Attribute 保存 Modifier，并按优先级计算 CurrentValue。</summary>
    internal sealed class AttributeAggregator
    {
        #region 字段

        private readonly List<AttributeModifier> modifiers = new();

        #endregion

        #region Modifier 管理

        // 添加已经由 Container 校验的 Modifier，并保持 Priority 从小到大排列。
        internal void Add(AttributeModifier modifier)
        {
            modifiers.Add(modifier);
            SortByPriority();
        }

        // 按对象引用判断该 Modifier 是否属于当前 Aggregator。
        internal bool Contains(AttributeModifier modifier) => modifiers.Contains(modifier);

        // 按对象引用移除单个 Modifier。
        internal bool Remove(AttributeModifier modifier) => modifiers.Remove(modifier);

        // 移除指定 Source 的全部 Modifier，并返回实例供失败事务恢复。
        internal int RemoveBySource(
            IModifierSource source,
            List<AttributeModifier> removed)
        {
            int count = 0;
            for (int i = modifiers.Count - 1; i >= 0; i--)
            {
                AttributeModifier modifier = modifiers[i];
                if (!ReferenceEquals(modifier.Source, source)) continue;
                modifiers.RemoveAt(i);
                removed.Add(modifier);
                count++;
            }

            return count;
        }

        // 恢复原子操作中暂时移除的 Modifier；同 Priority 最多一个 Override，因此 List 顺序不影响结果。
        internal void Restore(IReadOnlyList<AttributeModifier> removed)
        {
            for (int i = 0; i < removed.Count; i++) modifiers.Add(removed[i]);
            SortByPriority();
        }

        #endregion

        #region 聚合计算

        // 按优先级从小到大执行；每级先计算 Add/Multiply，再由唯一 Override 覆盖。
        internal bool TryEvaluate(float baseValue, out float result)
        {
            if (!IsFinite(baseValue))
            {
                result = default;
                return false;
            }

            double value = baseValue;
            int modifierIndex = 0;
            while (modifierIndex < modifiers.Count)
            {
                int priority = modifiers[modifierIndex].Priority;
                double additive = 0d;
                double multiplier = 1d;
                AttributeModifier overrideModifier = null;

                // 处理同一个 Priority 层级的 Modifier
                while (modifierIndex < modifiers.Count &&
                       modifiers[modifierIndex].Priority == priority)
                {
                    AttributeModifier modifier = modifiers[modifierIndex];
                    switch (modifier.Type)
                    {
                        case AttributeModifierType.Add:
                            additive += modifier.Magnitude;
                            break;
                        case AttributeModifierType.Multiply:
                            multiplier *= modifier.Magnitude;
                            break;
                        case AttributeModifierType.Override:
                            // 只允许一个 Override 生效
                            if (overrideModifier != null)
                            {
                                break;
                            }

                            overrideModifier = modifier;
                            break;
                        default:
                            result = default;
                            return false;
                    }

                    modifierIndex++;
                }

                value = (value + additive) * multiplier;
                if (overrideModifier != null) value = overrideModifier.Magnitude;
                if (!IsFinite(value))
                {
                    result = default;
                    return false;
                }
            }

            float evaluated = (float)value;
            if (!IsFinite(evaluated))
            {
                result = default;
                return false;
            }

            result = evaluated;
            return true;
        }

        // Modifier 数量较少，直接排序比维护额外 Priority 集合更简单且不会产生 LINQ 分配。
        private void SortByPriority() =>
            modifiers.Sort((left, right) => left.Priority.CompareTo(right.Priority));

        // 聚合中间值使用 double 降低误差，但仍禁止超出有限数值范围。
        private static bool IsFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);

        #endregion
    }
}
