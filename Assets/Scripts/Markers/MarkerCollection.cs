using System.Collections.Generic;
using UnityEngine;

namespace RPG.Markers
{
    /// <summary>
    /// 原子收集实例层级中的 TransformMarker，并提供按 MarkerKey 常数时间查询的运行时索引。
    /// </summary>
    public sealed class MarkerCollection : IMarkerProvider
    {
        #region 挂点索引

        private Dictionary<MarkerKey, Transform> markers = new();

        #endregion

        #region 公开操作

        /// <summary>
        /// 扫描实例根节点下包含未激活对象的全部挂点，并仅在整次扫描合法时替换现有索引。
        /// </summary>
        /// <param name="ownerRoot">限定挂点归属范围的实例根节点。</param>
        /// <param name="error">失败时返回空 Key、重复 Key 或无效根节点的明确原因。</param>
        /// <returns>完整索引构建成功时返回 true；失败时保留上一份有效索引。</returns>
        public bool TryRebuild(Transform ownerRoot, out string error)
        {
            if (ownerRoot == null)
            {
                error = "无法收集实例挂点：实例根节点为空。";
                return false;
            }

            TransformMarker[] components = ownerRoot.GetComponentsInChildren<TransformMarker>(true);
            Dictionary<MarkerKey, Transform> rebuilt = new(components.Length);
            foreach (TransformMarker component in components)
            {
                MarkerKey key = component.Key;
                if (key == null)
                {
                    error = $"挂点节点“{GetHierarchyPath(ownerRoot, component.transform)}”没有配置 MarkerKey。";
                    return false;
                }

                if (!rebuilt.TryAdd(key, component.transform))
                {
                    Transform existing = rebuilt[key];
                    error = $"实例层级中存在重复 MarkerKey“{key.name}”："
                            + $"“{GetHierarchyPath(ownerRoot, existing)}”与"
                            + $"“{GetHierarchyPath(ownerRoot, component.transform)}”。";
                    return false;
                }
            }

            markers = rebuilt;
            error = string.Empty;
            return true;
        }

        /// <summary>
        /// 查询指定语义挂点当前对应的 Transform。
        /// </summary>
        /// <param name="key">需要解析的挂点资产键。</param>
        /// <param name="marker">查询成功时返回当前实例层级中的实际挂点。</param>
        /// <returns>挂点键存在且目标 Transform 仍有效时返回 true。</returns>
        public bool TryGetMarker(MarkerKey key, out Transform marker)
        {
            if (key != null && markers.TryGetValue(key, out marker) && marker != null)
                return true;
            marker = null;
            return false;
        }

        /// <summary>
        /// 清空当前挂点索引；实例层级变化后应显式调用 TryRebuild 建立新索引。
        /// </summary>
        public void Clear() => markers.Clear();

        #endregion

        #region 层级诊断

        // 构建相对实例根节点的可读路径，
        // 仅用于把配置错误定位到具体节点。
        private static string GetHierarchyPath(Transform ownerRoot, Transform target)
        {
            if (target == ownerRoot) return ownerRoot.name;
            Stack<string> names = new();
            Transform current = target;
            while (current != null && current != ownerRoot)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return $"{ownerRoot.name}/{string.Join("/", names)}";
        }

        #endregion
    }
}
