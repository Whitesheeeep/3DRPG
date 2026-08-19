using System;
using System.Collections.Generic;
using UnityEngine;

namespace RPG.DialogueSystem
{
    /// <summary>
    /// 保存对话内容、节点子资产和直接 ScriptableObject 跳转引用的图资产。
    /// </summary>
    [CreateAssetMenu(fileName = "DialogueAsset", menuName = "RPG/Dialogue/Dialogue Asset", order = 0)]
    public sealed class DialogueAsset : ScriptableObject
    {
        #region 序列化字段

        [SerializeField] private string dialogueId = string.Empty;
        [SerializeField] private DialogueEntryNode entryNode;
        [SerializeField] private List<DialogueNode> nodes = new List<DialogueNode>();

        #endregion

        #region 属性

        /// <summary>获取稳定对话资产标识。</summary>
        public string DialogueId => dialogueId;

        /// <summary>获取唯一入口节点。</summary>
        public DialogueEntryNode EntryNode => entryNode;

        /// <summary>获取资产内所有节点的编辑器顺序集合。</summary>
        public IReadOnlyList<DialogueNode> Nodes =>
            nodes ?? (IReadOnlyList<DialogueNode>)Array.Empty<DialogueNode>();

        #endregion

        #region 资产编辑操作

        /// <summary>
        /// 设置稳定 DialogueId；空值由调用方在保存前通过校验发现。
        /// </summary>
        /// <param name="id">对话稳定标识。</param>
        public void SetDialogueId(string id) => dialogueId = id ?? string.Empty;

        /// <summary>
        /// 设置对话图唯一入口节点并确保它属于当前资产。
        /// </summary>
        /// <param name="node">待设置的入口节点。</param>
        public void SetEntryNode(DialogueEntryNode node)
        {
            entryNode = node;
            if (node != null) AddNode(node);
        }

        /// <summary>
        /// 将节点加入资产节点列表并保证节点稳定标识存在。
        /// </summary>
        /// <param name="node">待加入的节点。</param>
        public void AddNode(DialogueNode node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            node.EnsureNodeId();
            if (!nodes.Contains(node)) nodes.Add(node);
        }

        /// <summary>
        /// 从资产节点列表移除节点，同时清理入口引用和所有直接跳转引用。
        /// </summary>
        /// <param name="node">待移除的节点。</param>
        /// <returns>节点存在并被移除时返回 true。</returns>
        public bool RemoveNode(DialogueNode node)
        {
            if (node == null) return false;
            if (ReferenceEquals(entryNode, node)) entryNode = null;
            ClearReferencesTo(node);
            return nodes.Remove(node);
        }

        /// <summary>
        /// 为序列化边界补齐资产和节点缺失的稳定标识。
        /// </summary>
        public void EnsureStableIds()
        {
            if (nodes == null) nodes = new List<DialogueNode>();
            if (string.IsNullOrWhiteSpace(dialogueId)) dialogueId = Guid.NewGuid().ToString("D");
            for (int index = 0; index < nodes.Count; index++) nodes[index]?.EnsureNodeId();
        }

        #endregion

        #region 引用清理

        /// <summary>
        /// 清理所有节点指向被删除节点的直接引用，保持图资产可校验。
        /// </summary>
        /// <param name="removedNode">已经从节点列表移除的节点。</param>
        private void ClearReferencesTo(DialogueNode removedNode)
        {
            if (nodes == null) return;
            for (int index = 0; index < nodes.Count; index++)
            {
                DialogueNode node = nodes[index];
                if (node is DialogueEntryNode entry && ReferenceEquals(entry.FirstSpeechNode, removedNode))
                    entry.SetFirstSpeechNode(null);
                if (node is DialogueSpeechNode speech)
                {
                    if (ReferenceEquals(speech.NextNode, removedNode)) speech.SetNextNode(null);
                    if (removedNode is DialogueChoiceNode choice) speech.RemoveChoice(choice);
                }
                if (node is DialogueChoiceNode choiceNode && ReferenceEquals(choiceNode.TargetNode, removedNode))
                    choiceNode.SetTargetNode(null);
            }
        }

        #endregion

        #region Unity 生命周期

        /// <summary>
        /// 在 Unity 序列化数据变化后补齐稳定标识，不执行图结构自动修复。
        /// </summary>
        private void OnValidate() => EnsureStableIds();

        #endregion
    }
}