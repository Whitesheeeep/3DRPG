using System.Collections.Generic;

namespace WS_Modules.GAS.TAG
{
    /// <summary>保存显式 Gameplay Tag，并缓存由其派生的全部祖先标签。</summary>
    public sealed class GameplayTagContainer : IGameplayTagContainer
    {
        #region 字段与属性
        private readonly HashSet<GameplayTag> tags = new();
        private readonly HashSet<GameplayTag> parentTags = new();
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
        /// <inheritdoc />
        public bool AddTag(GameplayTag tag)
        {
            if (!GameplayTagManager.Instance.TryGetNode(tag, out GameplayTagNode node) || !tags.Add(tag)) return false;
            AddAncestors(node);
            return true;
        }

        /// <inheritdoc />
        public bool RemoveTag(GameplayTag tag)
        {
            if (!tags.Remove(tag)) return false;
            RebuildParentTags();
            return true;
        }

        /// <inheritdoc />
        public void AppendTags(IReadOnlyGameplayTagContainer other)
        {
            if (other == null || ReferenceEquals(this, other)) return;
            foreach (GameplayTag tag in other.Tags) AddTag(tag);
        }

        /// <inheritdoc />
        public void RemoveTags(IReadOnlyGameplayTagContainer other)
        {
            if (other == null) return;
            if (ReferenceEquals(this, other))
            {
                Reset();
                return;
            }

            bool changed = false;
            foreach (GameplayTag tag in other.Tags) changed |= tags.Remove(tag);
            if (changed) RebuildParentTags();
        }

        /// <inheritdoc />
        public void Reset()
        {
            tags.Clear();
            parentTags.Clear();
        }
        #endregion

        #region 查询
        /// <inheritdoc />
        public bool HasTag(GameplayTag tag) => GameplayTagManager.Instance.IsValidTag(tag) &&
                                               (tags.Contains(tag) || parentTags.Contains(tag));

        /// <inheritdoc />
        public bool HasTagExact(GameplayTag tag) => GameplayTagManager.Instance.IsValidTag(tag) && tags.Contains(tag);

        /// <inheritdoc />
        public bool HasAny(IReadOnlyGameplayTagContainer other)
        {
            if (other == null) return false;
            foreach (GameplayTag tag in other.Tags)
                if (HasTag(tag))
                    return true;
            return false;
        }

        /// <inheritdoc />
        public bool HasAnyExact(IReadOnlyGameplayTagContainer other)
        {
            if (other == null) return false;
            foreach (GameplayTag tag in other.Tags)
                if (HasTagExact(tag))
                    return true;
            return false;
        }

        /// <inheritdoc />
        public bool HasAll(IReadOnlyGameplayTagContainer other)
        {
            if (other == null) return true;
            foreach (GameplayTag tag in other.Tags)
                if (!HasTag(tag))
                    return false;
            return true;
        }

        /// <inheritdoc />
        public bool HasAllExact(IReadOnlyGameplayTagContainer other)
        {
            if (other == null) return true;
            foreach (GameplayTag tag in other.Tags)
                if (!HasTagExact(tag))
                    return false;
            return true;
        }
        #endregion

        #region 内部辅助
        // 将节点祖先快照并入父级缓存。
        private void AddAncestors(GameplayTagNode node)
        {
            IReadOnlyList<GameplayTag> list = node.Ancestors;
            for (int i = 0; i < list.Count; i++) parentTags.Add(list[i]);
        }

        // 从剩余显式标签重建缓存，避免共享祖先被提前移除。
        private void RebuildParentTags()
        {
            parentTags.Clear();
            foreach (GameplayTag tag in tags)
                if (GameplayTagManager.Instance.TryGetNode(tag, out GameplayTagNode node))
                    AddAncestors(node);
        }
        #endregion
    }
}