using System;
using System.Collections.Generic;

namespace WS_Modules.GAS.TAG
{
    /// <summary>统计 Gameplay Tag 的显式来源次数和包含子标签贡献的层级次数。</summary>
    public sealed class GameplayTagCountContainer : IReadOnlyGameplayTagContainer
    {
        #region 字段
        private readonly Dictionary<GameplayTag, int> explicitCounts = new();
        private readonly Dictionary<GameplayTag, int> hierarchicalCounts = new();
        private readonly HashSet<GameplayTag> tags = new();
        private readonly HashSet<GameplayTag> parentTags = new();
        #endregion

        #region 事件
        /// <summary>任意标签的层级计数改变后触发，参数依次为标签、旧计数和新计数。</summary>
        public event Action<GameplayTag, int, int> TagCountChanged;
        /// <summary>任意标签在不存在与存在之间切换时触发。</summary>
        public event Action<GameplayTag, bool> TagPresenceChanged;
        #endregion

        #region 属性
        /// <inheritdoc />
        public IReadOnlyCollection<GameplayTag> Tags => tags;
        /// <inheritdoc />
        public IReadOnlyCollection<GameplayTag> ParentTags => parentTags;
        /// <inheritdoc />
        public int Count => tags.Count;
        /// <inheritdoc />
        public bool IsEmpty => tags.Count == 0;
        #endregion

        #region 公开操作
        /// <summary>取得指定标签被直接添加的次数，不包含子标签贡献。</summary>
        /// <param name="tag">待查询标签。</param>
        /// <returns>标签有效时返回显式计数，否则返回 0。</returns>
        public int GetExplicitTagCount(GameplayTag tag) =>
            GameplayTagManager.Instance.IsValidTag(tag) && explicitCounts.TryGetValue(tag, out int count) ? count : 0;

        /// <summary>取得指定标签自身和全部子标签共同贡献的层级计数。</summary>
        /// <param name="tag">待查询标签。</param>
        /// <returns>标签有效时返回层级计数，否则返回 0。</returns>
        public int GetTagCount(GameplayTag tag) =>
            GameplayTagManager.Instance.IsValidTag(tag) && hierarchicalCounts.TryGetValue(tag, out int count)
                ? count
                : 0;

        /// <summary>为一个显式标签增加或减少来源计数，并同步更新全部祖先。</summary>
        /// <param name="tag">需要修改的显式标签。</param>
        /// <param name="delta">计数增量；负数表示移除来源。</param>
        /// <returns>全部计数合法且修改成功时返回 true。</returns>
        public bool UpdateTagCount(GameplayTag tag, int delta)
        {
            if (delta == 0 || !GameplayTagManager.Instance.TryGetNode(tag, out GameplayTagNode node)) return false;
            int oldExplicit = GetStoredCount(explicitCounts, tag);
            if (!TryCalculateCount(oldExplicit, delta, out int newExplicit)) return false;

            var changes = new List<(GameplayTag Tag, int OldCount, int NewCount)>(node.Ancestors.Count + 1);
            if (!TryCollectHierarchicalChange(tag, delta, changes)) return false;
            for (int i = 0; i < node.Ancestors.Count; i++)
                if (!TryCollectHierarchicalChange(node.Ancestors[i], delta, changes))
                    return false;

            SetCount(explicitCounts, tag, newExplicit);
            SetMembership(tags, tag, newExplicit > 0);
            ApplyHierarchicalChanges(changes);
            RaiseChanges(changes);
            return true;
        }

        /// <summary>原子更新另一个容器中的全部显式标签，每个标签应用一次相同增量。</summary>
        /// <param name="source">提供显式标签集合的只读容器。</param>
        /// <param name="delta">每个显式标签的计数增量。</param>
        /// <returns>所有标签和计数均合法且已整体提交时返回 true。</returns>
        public bool UpdateTagCounts(IReadOnlyGameplayTagContainer source, int delta)
        {
            if (source == null || source.Tags.Count == 0 || delta == 0) return false;

            var explicitDeltas = new Dictionary<GameplayTag, long>();
            var hierarchicalDeltas = new Dictionary<GameplayTag, long>();
            foreach (GameplayTag tag in source.Tags)
            {
                if (!GameplayTagManager.Instance.TryGetNode(tag, out GameplayTagNode node)) return false;
                AddDelta(explicitDeltas, tag, delta);
                AddDelta(hierarchicalDeltas, tag, delta);
                for (int i = 0; i < node.Ancestors.Count; i++)
                    AddDelta(hierarchicalDeltas, node.Ancestors[i], delta);
            }

            var explicitChanges = new List<(GameplayTag Tag, int OldCount, int NewCount)>();
            if (!TryBuildChanges(explicitCounts, explicitDeltas, explicitChanges)) return false;
            var hierarchicalChanges = new List<(GameplayTag Tag, int OldCount, int NewCount)>();
            if (!TryBuildChanges(hierarchicalCounts, hierarchicalDeltas, hierarchicalChanges)) return false;

            SortChanges(explicitChanges);
            SortChanges(hierarchicalChanges);
            for (int i = 0; i < explicitChanges.Count; i++)
            {
                (GameplayTag tag, _, int newCount) = explicitChanges[i];
                SetCount(explicitCounts, tag, newCount);
                SetMembership(tags, tag, newCount > 0);
            }

            ApplyHierarchicalChanges(hierarchicalChanges);
            RaiseChanges(hierarchicalChanges);
            return true;
        }

        /// <inheritdoc />
        public bool HasTag(GameplayTag tag) => GetTagCount(tag) > 0;

        /// <inheritdoc />
        public bool HasTagExact(GameplayTag tag) => GetExplicitTagCount(tag) > 0;

        /// <summary>清空全部计数，并在状态清理完成后发送归零事件。</summary>
        public void Reset()
        {
            if (hierarchicalCounts.Count == 0)
            {
                explicitCounts.Clear();
                tags.Clear();
                parentTags.Clear();
                return;
            }

            var changes = new List<(GameplayTag Tag, int OldCount, int NewCount)>(hierarchicalCounts.Count);
            foreach (KeyValuePair<GameplayTag, int> pair in hierarchicalCounts)
                changes.Add((pair.Key, pair.Value, 0));
            SortChanges(changes);

            explicitCounts.Clear();
            hierarchicalCounts.Clear();
            tags.Clear();
            parentTags.Clear();
            RaiseChanges(changes);
        }
        #endregion

        #region 计数提交
        // 收集一次层级计数变化；任何下溢或溢出都会使整个调用失败。
        private bool TryCollectHierarchicalChange(GameplayTag tag, int delta,
            ICollection<(GameplayTag Tag, int OldCount, int NewCount)> changes)
        {
            int oldCount = GetStoredCount(hierarchicalCounts, tag);
            if (!TryCalculateCount(oldCount, delta, out int newCount)) return false;
            changes.Add((tag, oldCount, newCount));
            return true;
        }

        // 把预先验证的层级变化写入字典，并同步派生的父标签存在集合。
        private void ApplyHierarchicalChanges(
            IReadOnlyList<(GameplayTag Tag, int OldCount, int NewCount)> changes)
        {
            for (int i = 0; i < changes.Count; i++)
            {
                (GameplayTag tag, _, int newCount) = changes[i];
                SetCount(hierarchicalCounts, tag, newCount);
                int explicitCount = GetStoredCount(explicitCounts, tag);
                SetMembership(parentTags, tag, newCount > explicitCount);
            }
        }

        // 在全部内部状态提交后发送层级计数事件；存在事件只跨越零边界时发送。
        private void RaiseChanges(IReadOnlyList<(GameplayTag Tag, int OldCount, int NewCount)> changes)
        {
            for (int i = 0; i < changes.Count; i++)
            {
                (GameplayTag tag, int oldCount, int newCount) = changes[i];
                TagCountChanged?.Invoke(tag, oldCount, newCount);
                if ((oldCount == 0) != (newCount == 0))
                    TagPresenceChanged?.Invoke(tag, newCount > 0);
            }
        }
        #endregion

        #region 校验与内部辅助
        // 将相同标签的多个批量增量合并为 long，避免合并阶段发生 int 溢出。
        private static void AddDelta(IDictionary<GameplayTag, long> deltas, GameplayTag tag, int delta)
        {
            deltas.TryGetValue(tag, out long current);
            deltas[tag] = current + delta;
        }

        // 根据增量表构建完整变化列表，不在校验阶段修改任何容器状态。
        private static bool TryBuildChanges(IReadOnlyDictionary<GameplayTag, int> counts,
            IReadOnlyDictionary<GameplayTag, long> deltas,
            ICollection<(GameplayTag Tag, int OldCount, int NewCount)> changes)
        {
            foreach (KeyValuePair<GameplayTag, long> pair in deltas)
            {
                int oldCount = GetStoredCount(counts, pair.Key);
                if (!TryCalculateCount(oldCount, pair.Value, out int newCount)) return false;
                changes.Add((pair.Key, oldCount, newCount));
            }

            return true;
        }

        // 计算新计数并拒绝负数和超过 int 上限的结果。
        private static bool TryCalculateCount(int current, long delta, out int result)
        {
            long value = current + delta;
            if (value is < 0 or > int.MaxValue)
            {
                result = current;
                return false;
            }

            result = (int)value;
            return true;
        }

        // 查询内部字典计数；不存在的键按零处理。
        private static int GetStoredCount(IReadOnlyDictionary<GameplayTag, int> counts, GameplayTag tag) =>
            counts.TryGetValue(tag, out int count) ? count : 0;

        // 写入正计数，零计数直接移除键以保持集合紧凑。
        private static void SetCount(IDictionary<GameplayTag, int> counts, GameplayTag tag, int count)
        {
            if (count == 0) counts.Remove(tag);
            else counts[tag] = count;
        }

        // 同步 HashSet 成员状态，避免把内部可变集合暴露给调用方。
        private static void SetMembership(ISet<GameplayTag> set, GameplayTag tag, bool present)
        {
            if (present) set.Add(tag);
            else set.Remove(tag);
        }

        // 按稳定 TagId 排序批量事件，保证同一输入产生确定的通知顺序。
        private static void SortChanges(List<(GameplayTag Tag, int OldCount, int NewCount)> changes) =>
            changes.Sort((left, right) => left.Tag.Id.CompareTo(right.Tag.Id));
        #endregion
    }
}