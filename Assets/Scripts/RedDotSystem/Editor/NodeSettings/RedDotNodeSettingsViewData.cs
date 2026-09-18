#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace RPG.RedDotSystemNS.Editor
{
    /// <summary>
    /// 将 RedDotKey Asset 投影为节点设置页 TreeView 使用的轻量行数据。
    /// </summary>
    internal sealed class RedDotNodeSettingsViewData
    {
        #region 构造与属性

        /// <summary>创建一个节点设置页 TreeView 行数据。</summary>
        /// <param name="id">TreeView 行唯一标识。</param>
        /// <param name="key">对应的节点 Asset。</param>
        public RedDotNodeSettingsViewData(int id, RedDotKey key)
        {
            Id = id;
            Key = key;
        }

        /// <summary>获取 TreeView 行唯一标识。</summary>
        public int Id { get; }

        /// <summary>获取对应节点 Asset。</summary>
        public RedDotKey Key { get; }

        /// <summary>获取节点显示名称。</summary>
        public string DisplayName => Key.SegmentName;

        /// <summary>获取节点派生路径。</summary>
        public string DerivedPath => Key.DerivedPath;

        /// <summary>获取直接子节点行数据。</summary>
        public List<RedDotNodeSettingsViewData> Children { get; } =
            new List<RedDotNodeSettingsViewData>();

        /// <summary>获取节点在当前父级同级列表中的零基索引。</summary>
        public int SiblingIndex { get; private set; }

        /// <summary>获取当前父级下的同级节点数量。</summary>
        public int SiblingCount { get; private set; }

        #endregion

        #region TreeView 投影

        /// <summary>
        /// 将 Config 节点清单转换为 TreeView 根条目和按 Asset 索引的行数据。
        /// </summary>
        /// <param name="config">当前编辑的 Config。</param>
        /// <param name="nodeByKeyMap">接收节点 Asset 到行数据的映射。</param>
        /// <returns>TreeView 根条目。</returns>
        internal static IList<TreeViewItemData<RedDotNodeSettingsViewData>> BuildTreeItems(
            RedDotConfig config,
            out IReadOnlyDictionary<RedDotKey, RedDotNodeSettingsViewData> nodeByKeyMap)
        {
            var mutableNodeByKeyMap =
                new Dictionary<RedDotKey, RedDotNodeSettingsViewData>();
            var allocatedTreeIdSet = new HashSet<int>();
            var configuredKeys = config.NodeKeys;
            for (int index = 0; index < configuredKeys.Count; index++)
            {
                RedDotKey key = configuredKeys[index];
                if (key == null)
                {
                    continue;
                }

                mutableNodeByKeyMap.Add(
                    key,
                    new RedDotNodeSettingsViewData(
                        AllocateTreeId(key, allocatedTreeIdSet),
                        key));
            }

            var roots = new List<RedDotNodeSettingsViewData>();
            foreach (RedDotNodeSettingsViewData node in mutableNodeByKeyMap.Values)
            {
                if (node.Key.Parent == null)
                {
                    roots.Add(node);
                    continue;
                }

                if (mutableNodeByKeyMap.TryGetValue(
                        node.Key.Parent,
                        out RedDotNodeSettingsViewData parent))
                {
                    parent.Children.Add(node);
                }
            }

            Comparison<RedDotNodeSettingsViewData> orderComparison =
                (left, right) =>
                {
                    int order = left.Key.SiblingOrder.CompareTo(right.Key.SiblingOrder);
                    return order != 0
                        ? order
                        : left.Id.CompareTo(right.Id);
                };
            roots.Sort(orderComparison);
            foreach (RedDotNodeSettingsViewData node in mutableNodeByKeyMap.Values)
            {
                node.Children.Sort(orderComparison);
            }

            SetSiblingMetadata(roots);
            foreach (RedDotNodeSettingsViewData node in mutableNodeByKeyMap.Values)
            {
                SetSiblingMetadata(node.Children);
            }

            var rootItems = new List<TreeViewItemData<RedDotNodeSettingsViewData>>();
            for (int index = 0; index < roots.Count; index++)
            {
                rootItems.Add(BuildTreeItem(roots[index]));
            }

            nodeByKeyMap = mutableNodeByKeyMap;
            return rootItems;
        }

        /// <summary>根据节点 Asset 的 GUID 分配当前树中稳定且不冲突的整数 ID。</summary>
        /// <param name="key">节点 Asset。</param>
        /// <param name="allocatedTreeIdSet">本次构建已经分配的 ID 集合。</param>
        /// <returns>TreeView 使用的稳定 ID。</returns>
        private static int AllocateTreeId(
            RedDotKey key,
            ISet<int> allocatedTreeIdSet)
        {
            string assetPath = AssetDatabase.GetAssetPath(key);
            string guid = string.IsNullOrEmpty(assetPath)
                ? string.Empty
                : AssetDatabase.AssetPathToGUID(assetPath);
            int id = string.IsNullOrEmpty(guid)
                ? key.GetInstanceID()
                : StringComparer.Ordinal.GetHashCode(guid);
            if (id == 0)
            {
                id = 1;
            }

            while (!allocatedTreeIdSet.Add(id))
            {
                id = unchecked(id + 1);
                if (id == 0)
                {
                    id = 1;
                }
            }

            return id;
        }

        /// <summary>记录一个同级列表的稳定索引和数量，供操作按钮判断边界。</summary>
        /// <param name="siblings">已按配置顺序排序的同级节点。</param>
        private static void SetSiblingMetadata(
            IReadOnlyList<RedDotNodeSettingsViewData> siblings)
        {
            for (int index = 0; index < siblings.Count; index++)
            {
                siblings[index].SiblingIndex = index;
                siblings[index].SiblingCount = siblings.Count;
            }
        }

        /// <summary>递归转换一个节点及其子节点。</summary>
        /// <param name="node">当前节点行数据。</param>
        /// <returns>TreeView 条目。</returns>
        private static TreeViewItemData<RedDotNodeSettingsViewData> BuildTreeItem(
            RedDotNodeSettingsViewData node)
        {
            var childItems = new List<TreeViewItemData<RedDotNodeSettingsViewData>>();
            for (int index = 0; index < node.Children.Count; index++)
            {
                childItems.Add(BuildTreeItem(node.Children[index]));
            }

            return new TreeViewItemData<RedDotNodeSettingsViewData>(
                node.Id,
                node,
                childItems);
        }

        #endregion
    }
}
#endif
