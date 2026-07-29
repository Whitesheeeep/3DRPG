#if UNITY_EDITOR
using System;
using UnityEngine;

namespace WS_Modules.GAS.Editor
{
    /// <summary>保存全局 Gameplay Attribute Spec 的 Editor 作者信息。</summary>
    [Serializable]
    public sealed class GameplayAttributeEditorNode
    {
        #region 字段与属性

        [SerializeField] private string guid;
        [SerializeField] private string name;
        [SerializeField, TextArea] private string description;

        /// <summary>获取不会因重命名而变化的作者 Guid。</summary>
        public string Guid => guid;

        /// <summary>获取全局唯一 Attribute 名称。</summary>
        public string Name => name;

        /// <summary>获取作者说明。</summary>
        public string Description => description;

        #endregion

        #region 构造与修改

        /// <summary>创建一个具有持久 Guid 的 Attribute Spec。</summary>
        /// <param name="guid">持久作者 Guid。</param>
        /// <param name="name">全局唯一名称。</param>
        /// <param name="description">作者说明。</param>
        public GameplayAttributeEditorNode(string guid, string name, string description)
        {
            this.guid = guid;
            this.name = name;
            this.description = description;
        }

        // 仅由 Editor Service 在 Undo 记录后修改名称。
        internal void SetName(string value) => name = value;

        // 仅由 Editor Service 在 Undo 记录后修改说明。
        internal void SetDescription(string value) => description = value;

        #endregion
    }
}
#endif
