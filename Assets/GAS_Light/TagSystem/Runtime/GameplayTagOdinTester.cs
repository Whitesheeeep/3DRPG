#if UNITY_EDITOR
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.Tests
{
    /// <summary>通过 Odin Inspector 手动验证 Gameplay Tag、Container、计数容器和简化 Query。</summary>
    public sealed class GameplayTagOdinTester : MonoBehaviour
    {
        #region 测试参数
        [Title("数据库")]
        [SerializeField, Required] private GameplayTagDatabase database;

        [Title("基础标签")]
        [SerializeField] private GameplayTag actualTag;
        [SerializeField] private GameplayTag queryTag;

        [Title("组合查询")]
        [SerializeField] private GameplayTag[] ownedTags;
        [SerializeField] private GameplayTagQuery configuredQuery;
        #endregion

        #region 初始化
        /// <summary>初始化 Manager 并输出数据库状态。</summary>
        [Button("初始化 Tag Manager")]
        public void InitializeManager()
        {
            GameplayTagManager.Instance.Initialize(database);
            Debug.Log($"[GameplayTagTest] 初始化完成：database={database?.name}, count={database?.Count ?? 0}", this);
        }

        /// <summary>清理 Manager 状态，避免手动测试影响后续运行。</summary>
        [Button("重置 Tag Manager")]
        public void ResetManager()
        {
            GameplayTagManager.Instance.Reset();
            Debug.Log("[GameplayTagTest] Manager 已重置。", this);
        }
        #endregion

        #region 基础行为测试
        /// <summary>验证实际标签匹配自身或祖先、但父标签不匹配子标签的 UE 方向语义。</summary>
        [Button("测试 Tag 匹配")]
        public void TestMatching()
        {
            Debug.Log($"[GameplayTagTest] MatchesTag actual={actualTag.Id}, query={queryTag.Id}, result={actualTag.MatchesTag(queryTag)}, exact={actualTag.MatchesTagExact(queryTag)}", this);
        }

        /// <summary>验证 Container 的隐式父标签、精确查询和删除后缓存重建。</summary>
        [Button("测试 Container")]
        public void TestContainer()
        {
            var container = new GameplayTagContainer();
            bool added = container.AddTag(actualTag);
            bool hierarchical = container.HasTag(queryTag);
            bool exact = container.HasTagExact(queryTag);
            bool removed = container.RemoveTag(actualTag);
            Debug.Log($"[GameplayTagTest] Container add={added}, has={hierarchical}, exact={exact}, remove={removed}, empty={container.IsEmpty}", this);
        }
        #endregion

        #region CountContainer 测试
        /// <summary>验证重复来源计数、祖先聚合、零边界事件和逐次删除。</summary>
        [Button("测试 CountContainer 单标签")]
        public void TestCountContainer()
        {
            var container = new GameplayTagCountContainer();
            container.TagCountChanged += (tag, oldCount, newCount) =>
                Debug.Log($"[GameplayTagTest] CountChanged tag={tag.Id}, {oldCount}->{newCount}", this);
            container.TagPresenceChanged += (tag, present) =>
                Debug.Log($"[GameplayTagTest] PresenceChanged tag={tag.Id}, present={present}", this);

            bool firstAdd = container.UpdateTagCount(actualTag, 1);
            bool secondAdd = container.UpdateTagCount(actualTag, 1);
            int explicitCount = container.GetExplicitTagCount(actualTag);
            int ancestorCount = container.GetTagCount(queryTag);
            bool firstRemove = container.UpdateTagCount(actualTag, -1);
            bool secondRemove = container.UpdateTagCount(actualTag, -1);
            bool underflowRejected = !container.UpdateTagCount(actualTag, -1);
            Debug.Log($"[GameplayTagTest] CountContainer add=({firstAdd},{secondAdd}), explicit={explicitCount}, ancestor={ancestorCount}, remove=({firstRemove},{secondRemove}), underflowRejected={underflowRejected}, empty={container.IsEmpty}", this);
        }

        /// <summary>验证多标签批量更新在普通 Container 与 CountContainer 之间工作。</summary>
        [Button("测试 CountContainer 批量")]
        public void TestCountContainerBatch()
        {
            GameplayTagContainer source = CreateOwnedTagContainer();
            var counts = new GameplayTagCountContainer();
            bool added = counts.UpdateTagCounts(source, 1);
            bool matched = configuredQuery.Matches(counts);
            bool removed = counts.UpdateTagCounts(source, -1);
            Debug.Log($"[GameplayTagTest] CountContainerBatch sourceCount={source.Count}, add={added}, queryMatched={matched}, remove={removed}, empty={counts.IsEmpty}", this);
        }
        #endregion

        #region Query 测试
        /// <summary>使用 Inspector 配置的 All、Any、None Query 匹配普通 Container。</summary>
        [Button("测试 Query + Container")]
        public void TestConfiguredQuery()
        {
            GameplayTagContainer container = CreateOwnedTagContainer();
            bool result = configuredQuery.Matches(container);
            Debug.Log($"[GameplayTagTest] Query containerCount={container.Count}, isEmpty={configuredQuery.IsEmpty}, isValid={configuredQuery.IsValid}, result={result}", this);
        }

        /// <summary>验证完全空 Query 对任意非 null 容器表示不限制。</summary>
        [Button("测试空 Query")]
        public void TestEmptyQuery()
        {
            var emptyQuery = new GameplayTagQuery();
            var container = new GameplayTagContainer();
            bool result = emptyQuery.Matches(container);
            Debug.Log($"[GameplayTagTest] EmptyQuery isEmpty={emptyQuery.IsEmpty}, isValid={emptyQuery.IsValid}, result={result}, expected=True", this);
        }
        #endregion

        #region 内部辅助
        // 把 Inspector 中配置的已拥有标签加入普通 Container，无效和重复标签由真实 API 自行拒绝。
        private GameplayTagContainer CreateOwnedTagContainer()
        {
            var container = new GameplayTagContainer();
            if (ownedTags == null) return container;
            for (int i = 0; i < ownedTags.Length; i++) container.AddTag(ownedTags[i]);
            return container;
        }
        #endregion
    }
}
#endif