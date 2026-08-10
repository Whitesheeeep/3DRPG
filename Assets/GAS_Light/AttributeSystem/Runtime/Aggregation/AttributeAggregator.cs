using System;
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

        /// <summary>添加已经由 Container 校验的 Modifier，并保持 Priority 从小到大排列。</summary>
        /// <param name="modifier">需要加入聚合器的运行时 Modifier。</param>
        internal void Add(AttributeModifier modifier)
        {
            modifiers.Add(modifier);
            SortByPriority();
        }

        /// <summary>按对象引用判断该 Modifier 是否属于当前 Aggregator。</summary>
        /// <param name="modifier">需要查询的运行时 Modifier。</param>
        /// <returns>当前聚合器持有该对象时返回 true。</returns>
        internal bool Contains(AttributeModifier modifier) => modifiers.Contains(modifier);

        /// <summary>按对象引用移除单个 Modifier。</summary>
        /// <param name="modifier">需要移除的运行时 Modifier。</param>
        /// <returns>成功移除时返回 true。</returns>
        internal bool Remove(AttributeModifier modifier) => modifiers.Remove(modifier);

        /// <summary>检查指定 Priority 是否已有其他 Source 提供的 Override。</summary>
        /// <param name="priority">需要检查的运算优先级。</param>
        /// <param name="ignoredSource">替换事务中即将整体移除、因此应忽略的 Source。</param>
        /// <returns>存在冲突 Override 时返回 true。</returns>
        internal bool HasOverride(int priority, IModifierSource ignoredSource = null)
        {
            for (int i = 0; i < modifiers.Count; i++)
            {
                AttributeModifier modifier = modifiers[i];
                if (modifier.Priority != priority ||
                    modifier.Type != AttributeModifierType.Override ||
                    ReferenceEquals(modifier.Source, ignoredSource))
                    continue;
                return true;
            }

            return false;
        }

        /// <summary>移除指定 Source 的全部 Modifier，并返回实例供失败事务恢复。</summary>
        /// <param name="source">需要移除的 Modifier 来源。</param>
        /// <param name="removed">接收被移除对象的列表。</param>
        /// <returns>实际移除数量。</returns>
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

        /// <summary>恢复原子操作中暂时移除的 Modifier。</summary>
        /// <param name="removed">需要恢复的运行时 Modifier。</param>
        internal void Restore(IReadOnlyList<AttributeModifier> removed)
        {
            for (int i = 0; i < removed.Count; i++) modifiers.Add(removed[i]);
            SortByPriority();
        }

        /// <summary>在 Container 清空或重新初始化时解除全部 Handle，并清空聚合集合。</summary>
        /// <param name="owner">当前绑定的 Attribute Container。</param>
        internal void DetachAll(GameplayAttributeContainer owner)
        {
            for (int i = 0; i < modifiers.Count; i++) modifiers[i].Detach(owner);
            modifiers.Clear();
        }

        #endregion

        #region 聚合计算

        /// <summary>按优先级从小到大执行；每级先计算 Add/Multiply，再由唯一 Override 覆盖。</summary>
        /// <param name="baseValue">进入聚合器的内部基础值。</param>
        /// <param name="result">接收最终有限数值。</param>
        /// <returns>全部运算合法且不存在 Override 冲突时返回 true。</returns>
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
                            // Container 应保证唯一性；这里再次拒绝被破坏的内部不变量，禁止结果依赖排序。
                            if (overrideModifier != null)
                            {
                                throw new InvalidOperationException("同一 Attribute、同一 Priority 层级不允许存在多个 Override Modifier。");
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

        /// <summary>按 Priority 排列少量 Modifier，不维护额外分组结构或产生 LINQ 分配。</summary>
        private void SortByPriority() =>
            modifiers.Sort((left, right) => left.Priority.CompareTo(right.Priority));

        /// <summary>检查聚合中间值是否仍处于有限数值范围。</summary>
        /// <param name="value">需要检查的双精度中间值。</param>
        /// <returns>不是 NaN 或 Infinity 时返回 true。</returns>
        private static bool IsFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);

        #endregion
    }
}
