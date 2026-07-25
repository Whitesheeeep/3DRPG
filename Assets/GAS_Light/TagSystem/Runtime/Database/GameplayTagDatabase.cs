using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using Sirenix.Serialization;
using UnityEngine;

namespace WS_Modules.GAS.TAG
{
    /// <summary>存储烘焙后的 Gameplay Tag 与层级关系；Editor 作者数据由条件编译区隔离。</summary>
    [CreateAssetMenu(fileName = "GameplayTagDatabase", menuName = "WSFrame/GAS/Gameplay Tag Database")]
    public sealed class GameplayTagDatabase : SerializedScriptableObject
    {
        #region 运行时数据
        [OdinSerialize, ReadOnly]
        private Dictionary<GameplayTag, GameplayTagNode> tagNodes = new();
        #endregion

        #region 运行时属性
        /// <summary>获取烘焙后的 Tag 节点只读索引。</summary>
        public IReadOnlyDictionary<GameplayTag, GameplayTagNode> TagNodes => tagNodes;
        /// <summary>获取烘焙后的 Tag 数量。</summary>
        public int Count => tagNodes?.Count ?? 0;
        #endregion

        #region 运行时查询
        /// <summary>尝试按 Gameplay Tag 获取关系节点。</summary>
        /// <param name="tag">待查询标签。</param>
        /// <param name="node">查询成功时返回关系节点。</param>
        /// <returns>标签存在于数据库时返回 true。</returns>
        public bool TryGetNode(GameplayTag tag, out GameplayTagNode node)
        {
            if (tagNodes == null || !tag.IsValid)
            {
                node = default;
                return false;
            }

            return tagNodes.TryGetValue(tag, out node);
        }

        /// <summary>尝试按稳定 ID 获取关系节点。</summary>
        /// <param name="tagId">稳定 TagId。</param>
        /// <param name="node">查询成功时返回关系节点。</param>
        /// <returns>对应标签存在时返回 true。</returns>
        public bool TryGetNode(int tagId, out GameplayTagNode node) => TryGetNode(new GameplayTag(tagId), out node);
        #endregion

#if UNITY_EDITOR

        #region Editor 作者数据
        [SerializeField, ListDrawerSettings(ShowIndexLabels = false)]
        private List<GameplayTagEditorNode> editorNodes = new();
        /// <summary>
        /// Key 是编辑器的单个标签的 GUID， Value 是 id
        /// </summary>
        [OdinSerialize, ReadOnly]
        private Dictionary<string, int> bakedIdHistory = new();
        [SerializeField, ReadOnly]
        private List<int> retiredTagIds = new();
        [SerializeField, MinValue(0)]
        private int nextTagId;
        [SerializeField, ReadOnly]
        private bool bakeDirty = true;
        #endregion

        #region Editor 属性
        /// <summary>获取作者节点只读列表。</summary>
        public IReadOnlyList<GameplayTagEditorNode> EditorNodes => editorNodes;
        /// <summary>获取数据库是否存在尚未烘焙的编辑。</summary>
        public bool BakeDirty => bakeDirty;
        /// <summary>获取下一个可分配且永不回退的 TagId。</summary>
        public int NextTagId => nextTagId;
        /// <summary>获取 Guid 到已分配 TagId 的只读历史。</summary>
        public IReadOnlyDictionary<string, int> BakedIdHistory => bakedIdHistory;
        /// <summary>获取永久废弃的 TagId。</summary>
        public IReadOnlyList<int> RetiredTagIds => retiredTagIds;
        #endregion

        #region Editor 作者操作
        /// <summary>创建作者节点并立即分配持久 Guid。</summary>
        /// <param name="name">节点局部名称。</param>
        /// <param name="parentGuid">父节点 Guid；空值表示根节点。</param>
        /// <returns>新创建的作者节点。</returns>
        public GameplayTagEditorNode CreateEditorNode(string name, string parentGuid)
        {
            var node = new GameplayTagEditorNode(Guid.NewGuid().ToString("N"), name, string.Empty, parentGuid);
            editorNodes.Add(node);
            bakeDirty = true;
            return node;
        }

        /// <summary>删除指定 Guid 集合中的作者节点。</summary>
        public int RemoveEditorNodes(ISet<string> guids)
        {
            if (guids == null || guids.Count == 0) return 0;
            int removed = editorNodes.RemoveAll(node => node != null && guids.Contains(node.Guid));
            if (removed > 0) bakeDirty = true;
            return removed;
        }

        /// <summary>更新节点名称并标记烘焙过期。</summary>
        public void RenameEditorNode(GameplayTagEditorNode node, string name)
        {
            if (node == null || node.Name == name) return;
            node.SetName(name);
            bakeDirty = true;
        }

        /// <summary>更新节点描述。</summary>
        public void SetEditorNodeDescription(GameplayTagEditorNode node, string description)
        {
            if (node == null || node.Description == description) return;
            node.SetDescription(description);
        }

        /// <summary>移动节点到新的父节点并标记烘焙过期。</summary>
        public void ReparentEditorNode(GameplayTagEditorNode node, string parentGuid)
        {
            if (node == null || node.ParentGuid == parentGuid) return;
            node.SetParentGuid(parentGuid);
            bakeDirty = true;
        }

        /// <summary>显式标记运行时数据需要重新烘焙。</summary>
        public void MarkBakeDirty() => bakeDirty = true;

        /// <summary>迁移旧资产中被 Odin 持久化的 StringComparer.Ordinal 实例。</summary>
        /// <returns>字典被迁移或重新初始化时返回 true。</returns>
        public bool NormalizeBakedIdHistoryComparer()
        {
            if (bakedIdHistory == null)
            {
                bakedIdHistory = new Dictionary<string, int>();
                return true;
            }

            if (bakedIdHistory.Comparer?.GetType() == EqualityComparer<string>.Default.GetType()) return false;
            bakedIdHistory = new Dictionary<string, int>(bakedIdHistory);
            return true;
        }
        #endregion

        #region Editor 查询与烘焙
        /// <summary>尝试取得已烘焙 Guid 对应的 Gameplay Tag。</summary>
        public bool TryGetBakedTag(string guid, out GameplayTag tag)
        {
            if (!string.IsNullOrEmpty(guid) && bakedIdHistory.TryGetValue(guid, out int id) && TryGetNode(id, out _))
            {
                tag = new GameplayTag(id);
                return true;
            }

            tag = GameplayTag.Empty;
            return false;
        }

        /// <summary>尝试从作者数据解析已烘焙标签当前路径。</summary>
        public bool TryGetBakedPath(GameplayTag tag, out string path)
        {
            string guid = null;
            foreach (KeyValuePair<string, int> pair in bakedIdHistory)
                if (pair.Value == tag.Id)
                {
                    guid = pair.Key;
                    break;
                }

            GameplayTagEditorNode node = FindEditorNode(guid);
            if (node == null || !TryGetNode(tag, out _))
            {
                path = string.Empty;
                return false;
            }

            var segments = new Stack<string>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (node != null && visited.Add(node.Guid))
            {
                segments.Push(node.Name);
                node = string.IsNullOrEmpty(node.ParentGuid) ? null : FindEditorNode(node.ParentGuid);
            }

            path = string.Join(".", segments);
            return true;
        }

        /// <summary>原子提交已完成校验的运行时节点和 ID 历史。</summary>
        public void ApplyBake(Dictionary<GameplayTag, GameplayTagNode> nodes, Dictionary<string, int> idHistory,
            List<int> retiredIds, int followingTagId)
        {
            tagNodes = nodes ?? new Dictionary<GameplayTag, GameplayTagNode>();
            // 复制时使用默认 comparer，避免把运行时内部 comparer 类型写入 Odin 数据。
            bakedIdHistory = idHistory == null
                ? new Dictionary<string, int>()
                : new Dictionary<string, int>(idHistory);
            retiredTagIds = retiredIds;
            nextTagId = followingTagId;
            bakeDirty = false;
        }

        // 按 Guid 查找作者节点，供 Editor-only 路径解析使用。
        private GameplayTagEditorNode FindEditorNode(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return null;
            for (int i = 0; i < editorNodes.Count; i++)
                if (editorNodes[i] != null && editorNodes[i].Guid == guid)
                    return editorNodes[i];
            return null;
        }
        #endregion

#endif
    }
}
