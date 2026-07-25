using System.Collections.Generic;

namespace WS_Modules.GAS.TAG
{
    /// <summary>定义纯运行时 Gameplay Tag 容器的核心集合和查询操作。</summary>
    public interface IGameplayTagContainer
    {
        /// <summary>获取显式添加的标签只读集合。</summary>
        IReadOnlyCollection<GameplayTag> Tags { get; }
        /// <summary>获取由显式标签派生的隐式祖先只读集合。</summary>
        IReadOnlyCollection<GameplayTag> ParentTags { get; }
        /// <summary>获取显式标签数量。</summary>
        int Count { get; }
        /// <summary>获取容器是否不含任何显式标签。</summary>
        bool IsEmpty { get; }

        /// <summary>添加一个存在于数据库中的显式标签。</summary>
        bool AddTag(GameplayTag tag);

        /// <summary>删除一个显式标签。</summary>
        bool RemoveTag(GameplayTag tag);

        /// <summary>合并另一个容器的显式标签。</summary>
        void AppendTags(IGameplayTagContainer other);

        /// <summary>删除另一个容器中列出的显式标签。</summary>
        void RemoveTags(IGameplayTagContainer other);

        /// <summary>清空全部显式和隐式标签。</summary>
        void Reset();

        /// <summary>判断容器是否具有能满足查询的显式标签或其隐式祖先。</summary>
        bool HasTag(GameplayTag tag);

        /// <summary>判断容器是否显式包含查询标签。</summary>
        bool HasTagExact(GameplayTag tag);

        /// <summary>判断是否满足另一个容器任一显式查询标签。</summary>
        bool HasAny(IGameplayTagContainer other);

        /// <summary>判断是否精确包含另一个容器任一显式标签。</summary>
        bool HasAnyExact(IGameplayTagContainer other);

        /// <summary>判断是否满足另一个容器全部显式查询标签。</summary>
        bool HasAll(IGameplayTagContainer other);

        /// <summary>判断是否精确包含另一个容器全部显式标签。</summary>
        bool HasAllExact(IGameplayTagContainer other);
    }

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
        public void AppendTags(IGameplayTagContainer other)
        {
            if (other == null || ReferenceEquals(this, other)) return;
            foreach (GameplayTag tag in other.Tags) AddTag(tag);
        }

        /// <inheritdoc />
        public void RemoveTags(IGameplayTagContainer other)
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
        public bool HasAny(IGameplayTagContainer other)
        {
            if (other == null) return false;
            foreach (GameplayTag tag in other.Tags)
                if (HasTag(tag))
                    return true;
            return false;
        }

        /// <inheritdoc />
        public bool HasAnyExact(IGameplayTagContainer other)
        {
            if (other == null) return false;
            foreach (GameplayTag tag in other.Tags)
                if (HasTagExact(tag))
                    return true;
            return false;
        }

        /// <inheritdoc />
        public bool HasAll(IGameplayTagContainer other)
        {
            if (other == null) return true;
            foreach (GameplayTag tag in other.Tags)
                if (!HasTag(tag))
                    return false;
            return true;
        }

        /// <inheritdoc />
        public bool HasAllExact(IGameplayTagContainer other)
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