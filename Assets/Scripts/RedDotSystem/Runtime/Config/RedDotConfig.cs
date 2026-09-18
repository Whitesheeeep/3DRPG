using System;
using System.Collections.Generic;
using UnityEngine;

namespace RPG.RedDotSystemNS
{
    /// <summary>
    /// 保存一套红点树所拥有的节点 Asset 清单。
    /// Parent 关系位于各自的 <see cref="RedDotKey"/>，本配置不重复序列化 Children。
    /// </summary>
    [CreateAssetMenu(fileName = "RedDotConfig", menuName = "RPG/Red Dot/Config", order = 1)]
    public sealed class RedDotConfig : ScriptableObject
    {
        #region 配置字段

        [SerializeField]
        [Tooltip("当前红点树拥有的全部独立 RedDotKey Asset。")]
        private List<RedDotKey> nodeKeys = new List<RedDotKey>();

        #endregion

        #region 公开查询

        /// <summary>获取当前配置拥有的全部节点 Asset。</summary>
        public IReadOnlyList<RedDotKey> NodeKeys =>
            nodeKeys == null
                ? (IReadOnlyList<RedDotKey>)Array.Empty<RedDotKey>()
                : nodeKeys;

        /// <summary>判断指定节点是否属于当前配置。</summary>
        /// <param name="key">待检查节点。</param>
        /// <returns>节点在配置清单中时返回 true。</returns>
        public bool Contains(RedDotKey key)
        {
            return key != null && nodeKeys != null && nodeKeys.Contains(key);
        }

        #endregion
    }
}
