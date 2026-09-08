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
        [SerializeField] private string displayName;
        [SerializeField, TextArea] private string description;

        /// <summary>获取不会因重命名而变化的作者 Guid。</summary>
        public string Guid => guid;

        /// <summary>获取全局唯一 Attribute 名称。</summary>
        public string Name => name;

        /// <summary>获取面向玩家界面的展示名称。</summary>
        public string DisplayName => displayName;

        /// <summary>获取作者说明。</summary>
        public string Description => description;

        #endregion

        #region 构造与修改

        /// <summary>创建一个具有持久 Guid 的 Attribute Spec。</summary>
        /// <param name="guid">持久作者 Guid。</param>
        /// <param name="name">全局唯一名称。</param>
        /// <param name="displayName">面向玩家界面的展示名称。</param>
        /// <param name="description">作者说明。</param>
        public GameplayAttributeEditorNode(string guid, string name, string displayName, string description)
        {
            this.guid = guid;
            this.name = name;
            this.displayName = displayName;
            this.description = description;
        }

        /// <summary>设置代码与作者名称；调用方负责先记录 Undo。</summary>
        /// <param name="value">新的技术名称。</param>
        internal void SetName(string value) => name = value;

        /// <summary>设置面向玩家的展示名称；调用方负责先记录 Undo。</summary>
        /// <param name="value">新的展示名称。</param>
        internal void SetDisplayName(string value) => displayName = value;

        /// <summary>设置作者说明；调用方负责先记录 Undo。</summary>
        /// <param name="value">新的说明文本。</param>
        internal void SetDescription(string value) => description = value;

        #endregion
    }
}
#endif
