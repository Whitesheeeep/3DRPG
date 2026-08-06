namespace WS_Modules.GAS.TAG
{
    /// <summary>定义纯运行时 Gameplay Tag 容器的核心集合和查询操作。</summary>
    public interface IGameplayTagContainer : IReadOnlyGameplayTagContainer
    {
        /// <summary>添加一个存在于数据库中的显式标签。</summary>
        bool AddTag(GameplayTag tag);

        /// <summary>删除一个显式标签。</summary>
        bool RemoveTag(GameplayTag tag);

        /// <summary>合并另一个容器的显式标签。</summary>
        void AppendTags(IReadOnlyGameplayTagContainer other);

        /// <summary>删除另一个容器中列出的显式标签。</summary>
        void RemoveTags(IReadOnlyGameplayTagContainer other);

        /// <summary>清空全部显式和隐式标签。</summary>
        void Reset();

        /// <summary>判断是否满足另一个容器任一显式查询标签。</summary>
        bool HasAny(IReadOnlyGameplayTagContainer other);

        /// <summary>判断是否精确包含另一个容器任一显式标签。</summary>
        bool HasAnyExact(IReadOnlyGameplayTagContainer other);

        /// <summary>判断是否满足另一个容器全部显式查询标签。</summary>
        bool HasAll(IReadOnlyGameplayTagContainer other);

        /// <summary>判断是否精确包含另一个容器全部显式标签。</summary>
        bool HasAllExact(IReadOnlyGameplayTagContainer other);
    }
}