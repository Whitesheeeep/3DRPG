using System;
using System.Collections.Generic;
using UnityEngine;

namespace WS_Modules.GAS.TAG
{
    /// <summary>使用 All、Any 和 None 三组标签描述可直接序列化的简化组合查询。</summary>
    [Serializable]
    public struct GameplayTagQuery
    {
        #region 字段
        [SerializeField, Tooltip("必须全部满足的标签；空数组表示没有 All 限制。")]
        private GameplayTag[] allTags;
        [SerializeField, Tooltip("至少满足一个的标签；空数组表示没有 Any 限制。")]
        private GameplayTag[] anyTags;
        [SerializeField, Tooltip("一个都不能满足的标签；空数组表示没有排除限制。")]
        private GameplayTag[] noneTags;
        #endregion

        #region 属性
        /// <summary>获取必须全部满足的标签。</summary>
        public IReadOnlyList<GameplayTag> AllTags => allTags ?? Array.Empty<GameplayTag>();
        /// <summary>获取非空时至少满足一个的标签。</summary>
        public IReadOnlyList<GameplayTag> AnyTags => anyTags ?? Array.Empty<GameplayTag>();
        /// <summary>获取一个都不能满足的标签。</summary>
        public IReadOnlyList<GameplayTag> BanedTags => noneTags ?? Array.Empty<GameplayTag>();
        /// <summary>获取查询是否完全不包含条件。</summary>
        public bool IsEmpty => AllTags.Count == 0 && AnyTags.Count == 0 && BanedTags.Count == 0;
        /// <summary>获取查询中的所有标签是否存在于当前 GameplayTagDatabase。</summary>
        public bool IsValid => AreTagsValid(AllTags) && AreTagsValid(AnyTags) && AreTagsValid(BanedTags);
        #endregion

        #region 构造
        /// <summary>使用三组标签创建查询，并复制输入数组避免外部后续修改。</summary>
        /// <param name="allTags">必须全部满足的标签。</param>
        /// <param name="anyTags">非空时至少满足一个的标签。</param>
        /// <param name="noneTags">一个都不能满足的标签。</param>
        public GameplayTagQuery(GameplayTag[] allTags, GameplayTag[] anyTags, GameplayTag[] noneTags)
        {
            this.allTags = CloneTags(allTags);
            this.anyTags = CloneTags(anyTags);
            this.noneTags = CloneTags(noneTags);
        }
        #endregion

        #region 公开操作
        /// <summary>判断目标容器是否同时满足 All、Any 和 None 三组条件。</summary>
        /// <param name="container">待匹配的只读标签容器。</param>
        /// <returns>容器非空、查询有效且全部条件满足时返回 true。</returns>
        public bool Matches(IReadOnlyGameplayTagContainer container)
        {
            if (container == null || !IsValid) return false;

            IReadOnlyList<GameplayTag> requiredAll = AllTags;
            for (int i = 0; i < requiredAll.Count; i++)
                if (!container.HasTag(requiredAll[i]))
                    return false;

            IReadOnlyList<GameplayTag> requiredAny = AnyTags;
            if (requiredAny.Count > 0)
            {
                bool hasAny = false;
                for (int i = 0; i < requiredAny.Count; i++)
                    if (container.HasTag(requiredAny[i]))
                    {
                        hasAny = true;
                        break;
                    }
                if (!hasAny) return false;
            }

            IReadOnlyList<GameplayTag> blocked = BanedTags;
            for (int i = 0; i < blocked.Count; i++)
                if (container.HasTag(blocked[i]))
                    return false;

            return true;
        }

        /// <summary>清空全部条件，使查询恢复为不限制任何非 null 容器。</summary>
        public void Clear()
        {
            allTags = Array.Empty<GameplayTag>();
            anyTags = Array.Empty<GameplayTag>();
            noneTags = Array.Empty<GameplayTag>();
        }

        /// <summary>创建要求全部标签都满足的查询。</summary>
        /// <param name="tags">必须全部满足的标签。</param>
        /// <returns>仅设置 AllTags 的新查询。</returns>
        public static GameplayTagQuery MatchAll(params GameplayTag[] tags) =>
            new(tags, Array.Empty<GameplayTag>(), Array.Empty<GameplayTag>());

        /// <summary>创建要求至少一个标签满足的查询。</summary>
        /// <param name="tags">候选标签。</param>
        /// <returns>仅设置 AnyTags 的新查询。</returns>
        public static GameplayTagQuery MatchAny(params GameplayTag[] tags) =>
            new(Array.Empty<GameplayTag>(), tags, Array.Empty<GameplayTag>());

        /// <summary>创建要求所有标签均不满足的查询。</summary>
        /// <param name="tags">禁止满足的标签。</param>
        /// <returns>仅设置 NoneTags 的新查询。</returns>
        public static GameplayTagQuery MatchNone(params GameplayTag[] tags) =>
            new(Array.Empty<GameplayTag>(), Array.Empty<GameplayTag>(), tags);
        #endregion

        #region 内部辅助
        // 检查数组中的每个标签是否已经烘焙并存在于当前数据库。
        private static bool AreTagsValid(IReadOnlyList<GameplayTag> tags)
        {
            for (int i = 0; i < tags.Count; i++)
                if (!GameplayTagManager.Instance.IsValidTag(tags[i]))
                    return false;
            return true;
        }

        // 复制构造输入；null 和空数组统一为共享空数组。
        private static GameplayTag[] CloneTags(GameplayTag[] tags)
        {
            if (tags == null || tags.Length == 0) return Array.Empty<GameplayTag>();
            var result = new GameplayTag[tags.Length];
            Array.Copy(tags, result, tags.Length);
            return result;
        }
        #endregion
    }
}