#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using WS_Modules.GAS.AttributeSystem;

namespace WS_Modules.GAS.Editor
{
    /// <summary>仅在 Editor 中保存 Attribute Specs、稳定 ID 历史与 Bake 状态。</summary>
    [CreateAssetMenu(fileName = "GameplayAttributeRegistry", menuName = "WSFrame/GAS/Gameplay Attribute Registry")]
    public sealed class GameplayAttributeRegistry : ScriptableObject
    {
        #region 字段

        [SerializeField] private List<GameplayAttributeEditorNode> nodes = new();
        [SerializeField, HideInInspector] private List<GameplayAttributeIdRecord> idRecords = new();
        [SerializeField, HideInInspector] private List<int> retiredIds = new();
        [SerializeField, HideInInspector] private int nextId;
        [SerializeField, HideInInspector] private bool bakeDirty = true;

        #endregion

        #region 属性

        /// <summary>获取 Attribute Spec 作者列表。</summary>
        public IReadOnlyList<GameplayAttributeEditorNode> Nodes => nodes;

        /// <summary>获取 Guid 到稳定 ID 的记录。</summary>
        public IReadOnlyList<GameplayAttributeIdRecord> IdRecords => idRecords;

        /// <summary>获取永久废弃 ID。</summary>
        public IReadOnlyList<int> RetiredIds => retiredIds;

        /// <summary>获取下一个可分配的全局 ID。</summary>
        public int NextId => nextId;

        /// <summary>获取作者数据是否存在尚未 Bake 的修改。</summary>
        public bool BakeDirty => bakeDirty;

        #endregion

        #region 作者操作

        /// <summary>创建 Attribute Spec 并立即生成持久 Guid。</summary>
        /// <param name="name">全局唯一名称。</param>
        /// <returns>新建作者节点。</returns>
        public GameplayAttributeEditorNode CreateNode(string name)
        {
            var node = new GameplayAttributeEditorNode(
                Guid.NewGuid().ToString("N"),
                name,
                string.Empty);
            nodes.Add(node);
            bakeDirty = true;
            return node;
        }

        /// <summary>删除指定 Guid 的 Attribute Spec。</summary>
        /// <param name="guid">待删除作者 Guid。</param>
        /// <returns>实际删除节点时返回 true。</returns>
        public bool RemoveNode(string guid)
        {
            int removed = nodes.RemoveAll(node => node != null && node.Guid == guid);
            if (removed > 0) bakeDirty = true;
            return removed > 0;
        }

        /// <summary>修改 Attribute 名称并标记 Bake Dirty。</summary>
        /// <param name="node">待修改节点。</param>
        /// <param name="name">新名称。</param>
        public void RenameNode(GameplayAttributeEditorNode node, string name)
        {
            if (node == null || node.Name == name) return;
            node.SetName(name);
            bakeDirty = true;
        }

        /// <summary>修改作者说明；说明不影响运行时标识与 Bake 状态。</summary>
        /// <param name="node">待修改节点。</param>
        /// <param name="description">新说明。</param>
        public void SetDescription(GameplayAttributeEditorNode node, string description)
        {
            if (node == null || node.Description == description) return;
            node.SetDescription(description);
        }

        #endregion

        #region Bake 查询与提交

        /// <summary>尝试按作者 Guid 取得已烘焙 Attribute。</summary>
        /// <param name="guid">作者 Guid。</param>
        /// <param name="attribute">成功时返回稳定 Attribute。</param>
        /// <returns>Guid 存在已烘焙记录时返回 true。</returns>
        public bool TryGetBakedAttribute(string guid, out GameplayAttribute attribute)
        {
            for (int i = 0; i < idRecords.Count; i++)
            {
                GameplayAttributeIdRecord record = idRecords[i];
                if (record != null && record.Guid == guid)
                {
                    string attributeName = string.Empty;
                    for (int nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
                    {
                        if (nodes[nodeIndex] != null && nodes[nodeIndex].Guid == record.Guid)
                        {
                            attributeName = nodes[nodeIndex].Name;
                            break;
                        }
                    }

                    attribute = new GameplayAttribute(record.Id, attributeName);
                    return true;
                }
            }

            attribute = GameplayAttribute.Empty;
            return false;
        }

        /// <summary>尝试按稳定 ID 取得当前作者节点。</summary>
        /// <param name="id">AttributeId。</param>
        /// <param name="node">成功时返回当前作者节点。</param>
        /// <returns>ID 已烘焙且节点仍存在时返回 true。</returns>
        public bool TryGetNodeById(int id, out GameplayAttributeEditorNode node)
        {
            string guid = null;
            for (int i = 0; i < idRecords.Count; i++)
            {
                GameplayAttributeIdRecord record = idRecords[i];
                if (record != null && record.Id == id)
                {
                    guid = record.Guid;
                    break;
                }
            }

            if (!string.IsNullOrEmpty(guid))
                for (int i = 0; i < nodes.Count; i++)
                    if (nodes[i] != null && nodes[i].Guid == guid)
                    {
                        node = nodes[i];
                        return true;
                    }

            node = null;
            return false;
        }

        /// <summary>原子提交已验证的 ID 历史、废弃列表和 nextId。</summary>
        /// <param name="records">当前有效 Guid→Id 记录。</param>
        /// <param name="retired">永久废弃 ID。</param>
        /// <param name="followingId">下一次分配起点。</param>
        public void ApplyBake(
            List<GameplayAttributeIdRecord> records,
            List<int> retired,
            int followingId)
        {
            idRecords = records ?? new List<GameplayAttributeIdRecord>();
            retiredIds = retired ?? new List<int>();
            nextId = followingId;
            bakeDirty = false;
        }

        /// <summary>显式标记 Attribute Spec 需要重新 Bake。</summary>
        public void MarkBakeDirty() => bakeDirty = true;

        #endregion
    }
}
#endif
