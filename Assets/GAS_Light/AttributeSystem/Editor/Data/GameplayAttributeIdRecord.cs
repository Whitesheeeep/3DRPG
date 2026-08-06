#if UNITY_EDITOR
using System;
using UnityEngine;

namespace WS_Modules.GAS.Editor
{
    /// <summary>持久记录 Attribute 作者 Guid 与全局稳定整数 ID 的映射。</summary>
    [Serializable]
    public sealed class GameplayAttributeIdRecord
    {
        #region 字段与属性

        [SerializeField] private string guid;
        [SerializeField] private int id;

        /// <summary>获取作者 Guid。</summary>
        public string Guid => guid;

        /// <summary>获取已分配且永不复用的 AttributeId。</summary>
        public int Id => id;

        #endregion

        #region 构造

        /// <summary>创建一个持久 ID 记录。</summary>
        /// <param name="guid">作者 Guid。</param>
        /// <param name="id">全局稳定 AttributeId。</param>
        public GameplayAttributeIdRecord(string guid, int id)
        {
            this.guid = guid;
            this.id = id;
        }

        #endregion
    }
}
#endif
