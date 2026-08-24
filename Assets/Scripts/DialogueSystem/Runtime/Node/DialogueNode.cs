using System;
using UnityEngine;

namespace RPG.DialogueSystemModule
{
    /// <summary>
    /// 表示对话图中的抽象节点；节点作为 DialogueAsset 的 ScriptableObject 子资产保存。
    /// </summary>
    public abstract class DialogueNode : ScriptableObject
    {
        #region 序列化字段

        [SerializeField] private string nodeId = string.Empty;
        [SerializeField] private Vector2 editorPosition;

        #endregion

        #region 属性

        /// <summary>
        /// 获取节点稳定字符串 GUID。
        /// </summary>
        public string NodeId => nodeId;

        /// <summary>
        /// 获取或设置节点在 GraphView 内容坐标空间中的布局位置。
        /// </summary>
        public Vector2 EditorPosition
        {
            get => editorPosition;
            set => editorPosition = value;
        }

        #endregion

        #region 稳定标识

        /// <summary>
        /// 在节点首次创建或旧资产缺少标识时生成稳定 GUID。
        /// </summary>
        public void EnsureNodeId()
        {
            if (!string.IsNullOrWhiteSpace(nodeId)) return;
            nodeId = Guid.NewGuid().ToString("D");
        }

        #endregion
    }
}