namespace WS_Modules.GAS.TAG
{
    /// <summary>提供面向调用点的 Gameplay Tag 容器查询快捷方法。</summary>
    public static class GameplayTagChecker
    {
        /// <summary>判断容器是否满足指定标签查询。</summary>
        public static bool HasTag(GameplayTagContainer container, GameplayTag tag) => container != null && container.HasTag(tag);
        /// <summary>判断容器是否满足查询容器中的任一显式标签。</summary>
        public static bool HasAnyTag(GameplayTagContainer container, IReadOnlyGameplayTagContainer queries) => container != null && container.HasAny(queries);
        /// <summary>判断容器是否满足查询容器中的全部显式标签。</summary>
        public static bool HasAllTags(GameplayTagContainer container, IReadOnlyGameplayTagContainer queries) => container != null && container.HasAll(queries);
    }
}
