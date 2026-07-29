using System;
using System.Collections.Generic;
using UnityEngine;

namespace WS_Modules.GAS.AttributeSystem
{
    /// <summary>
    /// 以可序列化 List 保存少量 Attribute，并通过 Aggregator 与所属 Set 规则受控修改数值。
    /// </summary>
    [Serializable]
    public sealed class GameplayAttributeContainer :
        IGameplayAttributeContainer,
        ISerializationCallbackReceiver
    {
        #region 嵌套类型

        /// <summary>保存通过聚合与 Current Pre 校验、等待原子提交的数值变化。</summary>
        private readonly struct PreparedCurrentValueChange
        {
            // 捕获旧值，保证批量提交后仍能发送准确的 Post 回调和事件。
            internal PreparedCurrentValueChange(
                GameplayAttributeDefinition definition,
                float newValue)
            {
                Definition = definition;
                OldValue = definition.CurrentValue;
                NewValue = newValue;
            }

            internal GameplayAttributeDefinition Definition { get; }
            internal float OldValue { get; }
            internal float NewValue { get; }
        }

        /// <summary>保存按 Source 暂时移除的一组 Modifier，供失败事务整体恢复。</summary>
        private sealed class RemovedModifierGroup
        {
            // 每个运行时 Definition 对应一个独立 Aggregator 和恢复列表。
            internal RemovedModifierGroup(GameplayAttributeDefinition definition)
            {
                Definition = definition;
            }

            internal GameplayAttributeDefinition Definition { get; }
            internal List<AttributeModifier> Modifiers { get; } = new();
        }
        #endregion

        #region 字段
        [SerializeField, HideInInspector]
        private List<GameplayAttributeDefinition> attributes = new();

        // 事务对象统一管理 Post FIFO、重复排队、修改环和 Modifier 防重入状态。
        [NonSerialized]
        private GameplayAttributeChangeTransaction changeTransaction;
        #endregion

        #region 事件
        /// <summary>CurrentValue 实际改变后触发，参数依次为 Attribute、旧值和新值。</summary>
        public event Action<GameplayAttribute, float, float> AttributeChanged;
        #endregion

        #region 属性
        /// <inheritdoc />
        public int Count => attributes?.Count ?? 0;

        /// <inheritdoc />
        public IReadOnlyList<GameplayAttributeDefinition> Attributes =>
            attributes ??= new List<GameplayAttributeDefinition>();
        #endregion

        #region 初始化与生命周期
        /// <summary>原子导入多个 Set，并为每项 Definition 创建独立运行时副本。</summary>
        /// <param name="sets">待组合的 Set；允许空集合，不允许 null 元素。</param>
        /// <param name="error">失败时返回首个明确问题。</param>
        /// <returns>全部模板合法且已整体替换运行时 List 时返回 true。</returns>
        public bool TryInitialize(IEnumerable<GameplayAttributeSet> sets, out string error)
        {
            if (sets == null)
            {
                error = "AttributeSet 集合不能为 null。";
                return false;
            }

            var imported = new List<GameplayAttributeDefinition>();
            var ids = new HashSet<int>();
            var setReferences = new HashSet<GameplayAttributeSet>();
            foreach (GameplayAttributeSet set in sets)
            {
                if (set == null)
                {
                    error = "AttributeSet 集合包含 null。";
                    return false;
                }

                if (!setReferences.Add(set))
                {
                    error = $"AttributeSet '{set.name}' 被重复导入。";
                    return false;
                }

                IReadOnlyList<GameplayAttributeDefinition> definitions = set.Definitions;
                for (int i = 0; i < definitions.Count; i++)
                {
                    GameplayAttributeDefinition source = definitions[i];
                    if (source == null)
                    {
                        error = $"AttributeSet '{set.name}' 的第 {i} 项 Definition 为 null。";
                        return false;
                    }

                    if (!source.TryValidateTemplate(out error))
                    {
                        error = $"AttributeSet '{set.name}': {error}";
                        return false;
                    }

                    if (!ids.Add(source.Attribute.Id))
                    {
                        error = $"AttributeId {source.Attribute.Id} 在组合的 Set 中重复。";
                        return false;
                    }

                    imported.Add(GameplayAttributeDefinition.CreateRuntimeCopy(source, set));
                }
            }

            attributes = imported;
            ResetTransientState();
            error = string.Empty;
            return true;
        }

        /// <inheritdoc />
        public void Clear()
        {
            attributes ??= new List<GameplayAttributeDefinition>();
            attributes.Clear();
            ResetTransientState();
        }

        // Unity 序列化前无需生成并行索引，List 本身就是唯一持久数据源。
        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
        }

        // 反序列化后丢弃无法持久化的 Modifier，并让 CurrentValue 回到无 Modifier 的 BaseValue。
        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            if (attributes != null)
                for (int i = 0; i < attributes.Count; i++)
                    attributes[i]?.ResetAggregation();
            ResetTransientState();
        }
        #endregion

        #region 查询
        /// <inheritdoc />
        public bool Contains(GameplayAttribute attribute) => FindDefinitionIndex(attribute) >= 0;

        /// <inheritdoc />
        public bool TryGetDefinition(
            GameplayAttribute attribute,
            out GameplayAttributeDefinition definition)
        {
            int index = FindDefinitionIndex(attribute);
            if (index < 0)
            {
                definition = null;
                return false;
            }

            definition = attributes[index];
            return true;
        }

        /// <inheritdoc />
        public bool TryGetCurrentValue(GameplayAttribute attribute, out float value)
        {
            if (TryGetDefinition(attribute, out GameplayAttributeDefinition definition))
            {
                value = definition.CurrentValue;
                return true;
            }

            value = default;
            return false;
        }
        #endregion

        #region Instant 与内部结算
        /// <inheritdoc />
        public void ApplyInstantModifier(AttributeModifierConfig config)
        {
            if (config == null ||
                !config.IsValid() ||
                !TryGetDefinition(config.Attribute, out GameplayAttributeDefinition definition))
            {
                throw new Exception("Modifier config 为空或者 config Type、数值不对, 或者不存在该 Attribute.");
            }

            float inputValue;
            if (definition.Type == GameplayAttributeType.Stat)
                inputValue = definition.BaseValue;
            else
                inputValue = definition.CurrentValue;

            if (TryCalculateInstantValue(
                    inputValue,
                    config.Type,
                    config.Magnitude,
                    out float settledValue))
                TrySetBaseValue(config.Attribute, settledValue);
        }

        // 设置内部结算值并进入统一 FIFO；该入口不属于公共业务 API。
        internal bool TrySetBaseValue(GameplayAttribute attribute, float value)
        {
            if (!IsFinite(value) || !Contains(attribute)) return false;
            EnsureTransientState();

            if (changeTransaction.IsProcessing)
                return EnqueuePostBaseValueChange(attribute, value);

            return changeTransaction.TryScheduleBaseValueChange(attribute, value) && ProcessPendingChanges();
        }

        /// <inheritdoc />
        public void ResetToDefaultValues()
        {
            EnsureTransientState();
            if (changeTransaction.IsProcessing) return;

            for (int i = 0; i < attributes.Count; i++)
            {
                GameplayAttributeDefinition definition = attributes[i];
                if (definition == null) continue;
                changeTransaction.TryScheduleBaseValueChange(
                    definition.Attribute,
                    definition.DefaultValue);
            }

            ProcessPendingChanges();
        }

        // 仅供 Post Context 在当前事务中加入后续修改；重复或形成修改环的请求会被拒绝。
        internal bool EnqueuePostBaseValueChange(GameplayAttribute attribute, float value)
        {
            EnsureTransientState();
            if (!changeTransaction.IsProcessing ||
                !IsFinite(value) ||
                !Contains(attribute))
                return false;
            return changeTransaction.TryScheduleBaseValueChange(attribute, value);
        }
        #endregion

        #region Modifier 操作
        /// <inheritdoc />
        public bool TryAddModifier(
            IModifierSource source,
            AttributeModifierConfig config,
            out AttributeModifier modifier)
        {
            modifier = null;
            if (source == null ||
                config == null ||
                !config.IsValid() ||
                !TryGetDefinition(config.Attribute, out GameplayAttributeDefinition definition) ||
                definition.Type != GameplayAttributeType.Stat ||
                !TryBeginModifierTransaction())
                return false;

            var candidate = new AttributeModifier(source, config);
            bool keepMutation = false;
            try
            {
                definition.Aggregator.Add(candidate);
                if (!TryPrepareCurrentValueChange(
                        definition,
                        definition.BaseValue,
                        out PreparedCurrentValueChange change))
                    return false;

                keepMutation = true;
                CommitCurrentValueChange(change);
                // PostAttributeChange 可能会有通过 context.RequestSetValue 的后续修改请求，必须在当前事务中处理完毕。
                DrainPendingChanges();
                modifier = candidate;
                return true;
            }
            finally
            {
                if (!keepMutation) definition.Aggregator.Remove(candidate);
                changeTransaction.Complete();
            }
        }


        /// <inheritdoc />
        public bool TryRemoveModifier(AttributeModifier modifier)
        {
            if (!TryGetModifierDefinition(modifier, out GameplayAttributeDefinition definition) ||
                !TryBeginModifierTransaction())
                return false;

            bool keepMutation = false;
            try
            {
                if (!definition.Aggregator.Remove(modifier)) return false;
                if (!TryPrepareCurrentValueChange(
                        definition,
                        definition.BaseValue,
                        out PreparedCurrentValueChange change))
                    return false;

                keepMutation = true;
                CommitCurrentValueChange(change);
                DrainPendingChanges();
                return true;
            }
            finally
            {
                if (!keepMutation && !definition.Aggregator.Contains(modifier))
                    definition.Aggregator.Add(modifier);
                changeTransaction.Complete();
            }
        }

        /// <inheritdoc />
        public bool TryRemoveModifiers(IModifierSource source, out int removedCount)
        {
            removedCount = 0;
            EnsureTransientState();
            if (source == null || changeTransaction.IsProcessing) return false;

            var groups = new List<RemovedModifierGroup>();
            int total = 0;
            for (int i = 0; i < attributes.Count; i++)
            {
                GameplayAttributeDefinition definition = attributes[i];
                if (definition == null) continue;
                var group = new RemovedModifierGroup(definition);
                int count = definition.Aggregator.RemoveBySource(source, group.Modifiers);
                if (count == 0) continue;
                groups.Add(group);
                total += count;
            }

            if (total == 0) return false;

            if (!changeTransaction.TryBegin())
            {
                for (int i = 0; i < groups.Count; i++)
                    groups[i].Definition.Aggregator.Restore(groups[i].Modifiers);
                return false;
            }

            bool keepMutation = false;
            try
            {
                var changes = new List<PreparedCurrentValueChange>(groups.Count);
                for (int i = 0; i < groups.Count; i++)
                {
                    GameplayAttributeDefinition definition = groups[i].Definition;
                    if (!TryPrepareCurrentValueChange(
                            definition,
                            definition.BaseValue,
                            out PreparedCurrentValueChange change))
                        return false;
                    changes.Add(change);
                }

                keepMutation = true;
                CommitCurrentValueChanges(changes);
                DrainPendingChanges();
                removedCount = total;
                return true;
            }
            finally
            {
                // 回退 Aggregator
                if (!keepMutation)
                    for (int i = 0; i < groups.Count; i++)
                        groups[i].Definition.Aggregator.Restore(groups[i].Modifiers);
                changeTransaction.Complete();
            }
        }
        #endregion

        #region 修改事务
        /// <summary>
        /// 顺序处理当前内部结算事务，并返回队首请求的实际提交结果。
        /// </summary>
        /// <returns>队首请求提交成功时返回 true；队列为空时同样返回 true。</returns>
        private bool ProcessPendingChanges()
        {
            if (!changeTransaction.TryBegin()) return false;
            try
            {
                return DrainPendingChanges();
            }
            finally
            {
                changeTransaction.Complete();
            }
        }

        /// <summary>
        /// 按 FIFO 消费内部结算请求；Post 新请求在当前回调和事件完成后执行。
        /// </summary>
        /// <returns>队首请求提交成功时返回 true；后续关联请求不覆盖该结果。</returns>
        private bool DrainPendingChanges()
        {
            bool hasRootResult = false;
            bool rootResult = false;
            while (changeTransaction.TryDequeueBaseValueChange(
                       out GameplayAttribute attribute,
                       out float value))
            {
                bool applied = ApplyBaseValueChange(attribute, value);
                if (hasRootResult) continue;
                hasRootResult = true;
                rootResult = applied;
            }

            return !hasRootResult || rootResult;
        }

        // 内部结算值与最终 Current 先完成全部 Pre 校验，再一起提交并依次发送 Post。
        private bool ApplyBaseValueChange(GameplayAttribute attribute, float requestedValue)
        {
            if (!TryGetDefinition(attribute, out GameplayAttributeDefinition definition) ||
                definition.OwnerSet == null)
                return false;

            float newBaseValue = requestedValue;
            definition.OwnerSet.DispatchPreAttributeBaseChange(this, attribute, ref newBaseValue);
            // 拿到 CurrentValue 的变化，会经历一次对 CurrentValue 的 PreAttributeChange 的处理
            if (!IsFinite(newBaseValue) ||
                !TryPrepareCurrentValueChange(
                    definition,
                    newBaseValue,
                    out PreparedCurrentValueChange currentChange))
                return false;

            // Resource 将 Current Pre 后的最终值同步到 BaseValue；Stat 保留不含持续 Modifier 的新 BaseValue。
            float committedBaseValue = definition.Type == GameplayAttributeType.Resource
                ? currentChange.NewValue
                : newBaseValue;
            float oldBaseValue = definition.BaseValue;
            definition.SetBaseValue(committedBaseValue);
            definition.SetCurrentValue(currentChange.NewValue);

            var context = new GameplayAttributePostChangeContext(this);
            if (!Mathf.Approximately(oldBaseValue, committedBaseValue))
                definition.OwnerSet.DispatchPostAttributeBaseChange(
                    context,
                    attribute,
                    oldBaseValue,
                    committedBaseValue);
            NotifyCurrentValueChange(context, currentChange);
            return true;
        }

        // 建立 Modifier 事务；Modifier 操作不允许在其他修改回调中重入。
        private bool TryBeginModifierTransaction()
        {
            EnsureTransientState();
            return changeTransaction.TryBegin();
        }
        #endregion

        #region CurrentValue 准备与提交
        // Stat 使用内部 Base 聚合，Resource 直接结算；两者都执行 Current Pre。
        private bool TryPrepareCurrentValueChange(
            GameplayAttributeDefinition definition,
            float baseValue,
            out PreparedCurrentValueChange change)
        {
            change = default;
            if (definition == null || definition.OwnerSet == null) return false;

            float evaluatedValue;
            if (definition.Type == GameplayAttributeType.Stat)
            {
                // Stat 使用 Aggregator 进行结算
                if (!definition.Aggregator.TryEvaluate(baseValue, out evaluatedValue)) return false;
            }
            else if (definition.Type == GameplayAttributeType.Resource)
            {
                // Resource 直接使用 BaseValue 作为 CurrentValue
                evaluatedValue = baseValue;
            }
            else
            {
                return false;
            }

            definition.OwnerSet.DispatchPreAttributeChange(
                this,
                definition.Attribute,
                ref evaluatedValue);
            if (!IsFinite(evaluatedValue)) return false;

            change = new PreparedCurrentValueChange(definition, evaluatedValue);
            return true;
        }

        // 提交单个 CurrentValue，并在实际变化时发送 Post 和 AttributeChanged。
        private void CommitCurrentValueChange(PreparedCurrentValueChange change)
        {
            change.Definition.SetCurrentValue(change.NewValue);
            var context = new GameplayAttributePostChangeContext(this);
            NotifyCurrentValueChange(context, change);
        }

        // 批量 CurrentValue 先全部写入，再发送 Post，避免观察到部分提交状态。
        private void CommitCurrentValueChanges(IReadOnlyList<PreparedCurrentValueChange> changes)
        {
            for (int i = 0; i < changes.Count; i++)
                changes[i].Definition.SetCurrentValue(changes[i].NewValue);

            var context = new GameplayAttributePostChangeContext(this);
            for (int i = 0; i < changes.Count; i++)
                NotifyCurrentValueChange(context, changes[i]);
        }

        // 仅在 CurrentValue 确实变化时发送规则回调和公共事件。
        private void NotifyCurrentValueChange(
            GameplayAttributePostChangeContext context,
            PreparedCurrentValueChange change)
        {
            if (Mathf.Approximately(change.OldValue, change.NewValue)) return;
            GameplayAttributeDefinition definition = change.Definition;
            definition.OwnerSet.DispatchPostAttributeChange(
                context,
                definition.Attribute,
                change.OldValue,
                change.NewValue);
            AttributeChanged?.Invoke(
                definition.Attribute,
                change.OldValue,
                change.NewValue);
        }
        #endregion

        #region 内部辅助
        // 对少量 Attribute 使用线性查询，避免持久 Dictionary 的额外内存与同步成本。
        private int FindDefinitionIndex(GameplayAttribute attribute)
        {
            if (!attribute.IsValid || attributes == null) return -1;
            for (int i = 0; i < attributes.Count; i++)
            {
                GameplayAttributeDefinition definition = attributes[i];
                if (definition != null && definition.Attribute == attribute) return i;
            }

            return -1;
        }

        // 同时校验 Modifier 的 Attribute 和对象引用归属，拒绝跨 Container 操作。
        private bool TryGetModifierDefinition(
            AttributeModifier modifier,
            out GameplayAttributeDefinition definition)
        {
            definition = null;
            return modifier != null &&
                   TryGetDefinition(modifier.Attribute, out definition) &&
                   definition.Aggregator.Contains(modifier);
        }

        // 恢复事件事务所需的非序列化集合。
        private void EnsureTransientState()
        {
            changeTransaction ??= new GameplayAttributeChangeTransaction();
        }

        // 清除域重载前遗留的事务状态；Aggregator 生命周期由 Definition 管理。
        private void ResetTransientState()
        {
            changeTransaction = null;
        }

        // 对单项 Instant 运算使用 double 检查中间结果，避免 float 溢出后进入修改事务。
        private static bool TryCalculateInstantValue(
            float inputValue,
            AttributeModifierType type,
            float magnitude,
            out float result)
        {
            double calculatedValue;
            switch (type)
            {
                case AttributeModifierType.Add:
                    calculatedValue = (double)inputValue + magnitude;
                    break;
                case AttributeModifierType.Multiply:
                    calculatedValue = (double)inputValue * magnitude;
                    break;
                case AttributeModifierType.Override:
                    calculatedValue = magnitude;
                    break;
                default:
                    result = default;
                    return false;
            }

            result = (float)calculatedValue;
            return IsFinite(calculatedValue) && IsFinite(result);
        }

        // Attribute 运行时输入和聚合结果禁止 NaN 与 Infinity。
        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        // double 中间结果同样必须保持有限，避免转换到 float 后隐藏非法运算。
        private static bool IsFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);
        #endregion
    }
}