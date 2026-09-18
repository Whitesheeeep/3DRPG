#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace RPG.RedDotSystemNS.Editor
{
    /// <summary>将运行时红点快照投影为 UI Toolkit TreeView 行数据。</summary>
    internal sealed class RedDotDebuggerNodeViewData
    {
        #region 构造与属性

        /// <summary>创建一行红点调试树数据。</summary>
        /// <param name="id">当前快照中的 TreeView 唯一标识。</param>
        /// <param name="snapshot">对应运行时节点快照。</param>
        public RedDotDebuggerNodeViewData(int id, DebugRedDotNodeSnapshot snapshot)
        {
            Id = id;
            Snapshot = snapshot;
        }

        /// <summary>获取当前 TreeView 快照中的唯一标识。</summary>
        public int Id { get; }

        /// <summary>获取对应运行时节点快照。</summary>
        public DebugRedDotNodeSnapshot Snapshot { get; }

        /// <summary>获取直接子节点行数据，顺序与运行时快照一致。</summary>
        public List<RedDotDebuggerNodeViewData> Children { get; } =
            new List<RedDotDebuggerNodeViewData>();

        #endregion

        #region 树投影

        /// <summary>把扁平运行时快照转换为 TreeView 根条目。</summary>
        /// <param name="snapshots">当前完整红点树快照。</param>
        /// <param name="nodeByKeyMap">接收按节点 Asset 引用索引的行数据。</param>
        /// <returns>TreeView 根条目集合。</returns>
        internal static IList<TreeViewItemData<RedDotDebuggerNodeViewData>> BuildTreeItems(
            IReadOnlyList<DebugRedDotNodeSnapshot> snapshots,
            out IReadOnlyDictionary<RedDotKey, RedDotDebuggerNodeViewData> nodeByKeyMap)
        {
            // key：RedDotKey Asset；value：本次快照对应的 TreeView 行数据。
            var mutableNodeByKeyMap =
                new Dictionary<RedDotKey, RedDotDebuggerNodeViewData>();
            for (int index = 0; index < snapshots.Count; index++)
            {
                DebugRedDotNodeSnapshot snapshot = snapshots[index];
                mutableNodeByKeyMap.Add(
                    snapshot.Key,
                    new RedDotDebuggerNodeViewData(index + 1, snapshot));
            }

            var roots = new List<RedDotDebuggerNodeViewData>();
            foreach (RedDotDebuggerNodeViewData node in mutableNodeByKeyMap.Values)
            {
                if (node.Snapshot.ParentKey == null)
                {
                    roots.Add(node);
                    continue;
                }

                if (mutableNodeByKeyMap.TryGetValue(
                        node.Snapshot.ParentKey,
                        out RedDotDebuggerNodeViewData parent))
                {
                    parent.Children.Add(node);
                }
            }

            // DebugGetSnapshot 已按稳定的派生路径返回；这里保留快照顺序，避免 UI 重排造成选中闪烁。
            roots.Sort(CompareNodeOrder);
            foreach (RedDotDebuggerNodeViewData node in mutableNodeByKeyMap.Values)
            {
                node.Children.Sort(CompareNodeOrder);
            }

            var rootItems = new List<TreeViewItemData<RedDotDebuggerNodeViewData>>();
            for (int index = 0; index < roots.Count; index++)
            {
                rootItems.Add(BuildTreeItem(roots[index]));
            }

            nodeByKeyMap = mutableNodeByKeyMap;
            return rootItems;
        }

        /// <summary>按照 TreeView 行标识保持运行时快照顺序。</summary>
        /// <param name="left">左侧行数据。</param>
        /// <param name="right">右侧行数据。</param>
        /// <returns>稳定顺序比较结果。</returns>
        private static int CompareNodeOrder(
            RedDotDebuggerNodeViewData left,
            RedDotDebuggerNodeViewData right)
        {
            return left.Id.CompareTo(right.Id);
        }

        /// <summary>递归转换一个节点及其子节点。</summary>
        /// <param name="node">当前行数据。</param>
        /// <returns>TreeView 条目。</returns>
        private static TreeViewItemData<RedDotDebuggerNodeViewData> BuildTreeItem(
            RedDotDebuggerNodeViewData node)
        {
            List<TreeViewItemData<RedDotDebuggerNodeViewData>> childItems = node.Children
                .Select(BuildTreeItem)
                .ToList();
            return new TreeViewItemData<RedDotDebuggerNodeViewData>(node.Id, node, childItems);
        }

        #endregion
    }
}
#endif
