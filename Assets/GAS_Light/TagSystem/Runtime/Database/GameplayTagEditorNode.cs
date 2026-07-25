#if UNITY_EDITOR
using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace WS_Modules.GAS.TAG
{
    /// <summary>保存仅用于 Editor 作者阶段的 Gameplay Tag 节点。</summary>
    [Serializable]
    public sealed class GameplayTagEditorNode
    {
        #region 字段与属性
        [SerializeField, ReadOnly]
        private string guid;
        [SerializeField]
        private string name;
        [SerializeField, TextArea(2, 6)]
        private string description;
        [SerializeField, ReadOnly]
        private string parentGuid;
        /// <summary>获取不会随重命名或移动变化的持久 Guid。</summary>
        public string Guid => guid;
        /// <summary>获取节点局部名称。</summary>
        public string Name => name;
        /// <summary>获取节点描述。</summary>
        public string Description => description;
        /// <summary>获取父节点 Guid；空字符串表示根节点。</summary>
        public string ParentGuid => parentGuid;
        #endregion

        /// <summary>创建作者节点。</summary>
        public GameplayTagEditorNode(string guid, string name, string description, string parentGuid)
        {
            this.guid = guid;
            this.name = name;
            this.description = description;
            this.parentGuid = parentGuid ?? string.Empty;
        }

        // 作者数据只允许数据库和 Editor Service 通过受控入口修改。
        internal void SetName(string value) => name = value;

        // 描述不影响关系，但仍由受控入口保持 Undo 一致。
        internal void SetDescription(string value) => description = value ?? string.Empty;

        // 移动只改变父 Guid，Path 始终按父链动态推导。
        internal void SetParentGuid(string value) => parentGuid = value ?? string.Empty;
    }
}
#endif