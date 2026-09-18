#if UNITY_EDITOR
using System;
using UnityEditor;

namespace RPG.RedDotSystemNS.Editor
{
    /// <summary>
    /// 校验 RedDotKey Asset 的 Config 所有权，保证一个节点只归属于一套配置。
    /// </summary>
    internal static class RedDotConfigOwnershipFinder
    {
        #region Config 所有权查询

        /// <summary>
        /// 查找已拥有指定节点 Asset 的其他 RedDotConfig。
        /// </summary>
        /// <param name="key">待查询节点 Asset。</param>
        /// <param name="ignoredConfig">需要忽略的当前编辑 Config。</param>
        /// <returns>第一个拥有该节点的其他 Config；不存在时返回 null。</returns>
        internal static RedDotConfig FindOwningConfig(
            RedDotKey key,
            RedDotConfig ignoredConfig)
        {
            if (key == null)
            {
                return null;
            }

            string[] configGuids = AssetDatabase.FindAssets("t:RedDotConfig");
            Array.Sort(configGuids, StringComparer.Ordinal);
            for (int index = 0; index < configGuids.Length; index++)
            {
                RedDotConfig config = AssetDatabase.LoadAssetAtPath<RedDotConfig>(
                    AssetDatabase.GUIDToAssetPath(configGuids[index]));
                if (config != null && config != ignoredConfig && config.Contains(key))
                {
                    return config;
                }
            }

            return null;
        }

        #endregion
    }
}
#endif
