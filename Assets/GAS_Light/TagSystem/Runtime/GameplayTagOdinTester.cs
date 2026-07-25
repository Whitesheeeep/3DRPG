#if UNITY_EDITOR
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules;
using WS_Modules.GAS.TAG;

namespace WSFrame.GAS.Tests
{
    /// <summary>通过 Odin Inspector 手动验证 Gameplay Tag 匹配和 Container 行为。</summary>
    public sealed class GameplayTagOdinTester : MonoBehaviour
    {
        #region 测试参数
        [Title("数据库")]
        [SerializeField, Required] private GameplayTagDatabase database;
        [Title("标签")]
        [SerializeField] private GameplayTag actualTag;
        [SerializeField] private GameplayTag queryTag;
        #endregion

        /// <summary>初始化 Manager 并输出数据库状态。</summary>
        [Button("初始化 Tag Manager")]
        public void InitializeManager()
        {
            GameplayTagManager.Instance.Initialize(database);
            Debug.Log($"[GameplayTagTest] 初始化完成：database={database?.name}, count={database?.Count ?? 0}", this);
        }

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

        /// <summary>清理 Manager 状态，避免手动测试影响后续运行。</summary>
        [Button("重置 Tag Manager")]
        public void ResetManager()
        {
            GameplayTagManager.Instance.Reset();
            Debug.Log("[GameplayTagTest] Manager 已重置。", this);
        }
    }
}
#endif
