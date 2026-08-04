using System.Collections.Generic;
using UnityEngine;

namespace RPG.Markers
{
    /// <summary>
    /// 作为实例级 Socket 容器收集自身作用域内的 TransformMarker，并提供稳定语义查询。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MarkerProvider : MonoBehaviour, IMarkerProvider
    {
        #region 挂点索引

        private Dictionary<MarkerKey, Transform> markers = new();

        /// <summary>
        /// 获取最近一次重建失败原因；空字符串表示当前索引有效。
        /// </summary>
        public string LastError { get; private set; } = string.Empty;

        #endregion

        #region 生命周期

        // 在运行时实例初始化时建立当前作用域索引；换装或层级变化后由所有者再次显式重建。
        private void Awake()
        {
            if (!TryRebuild(out string error)) Debug.LogError(error, this);
        }

        #endregion

        #region 公开操作

        /// <summary>
        /// 原子重建当前 Provider 作用域中的 Marker 索引，并排除嵌套 Provider 管理的节点。
        /// </summary>
        /// <param name="error">失败时返回空 Key 或同作用域重复 Key 的明确原因。</param>
        /// <returns>完整索引构建成功时返回 true；失败时保留上一份有效索引。</returns>
        public bool TryRebuild(out string error)
        {
            TransformMarker[] components = GetComponentsInChildren<TransformMarker>(true);
            Dictionary<MarkerKey, Transform> rebuilt = new(components.Length);
            foreach (TransformMarker component in components)
            {
                if (component.GetComponentInParent<MarkerProvider>(true) != this) continue;
                MarkerKey key = component.Key;
                if (key == null)
                {
                    error = $"Marker 节点“{GetHierarchyPath(component.transform)}”没有配置 MarkerKey。";
                    LastError = error;
                    return false;
                }

                if (!rebuilt.TryAdd(key, component.transform))
                {
                    Transform existing = rebuilt[key];
                    error = $"MarkerProvider“{name}”中存在重复 MarkerKey“{key.name}”："
                            + $"“{GetHierarchyPath(existing)}”与“{GetHierarchyPath(component.transform)}”。";
                    LastError = error;
                    return false;
                }
            }

            markers = rebuilt;
            error = string.Empty;
            LastError = string.Empty;
            return true;
        }

        /// <summary>
        /// 查询当前实例作用域中指定语义 Socket 对应的 Transform。
        /// </summary>
        /// <param name="key">需要解析的 MarkerKey。</param>
        /// <param name="marker">查询成功时返回该实例中的实际 Transform。</param>
        /// <returns>索引中存在有效节点时返回 true。</returns>
        public bool TryGetMarker(MarkerKey key, out Transform marker)
        {
            if (key != null && markers.TryGetValue(key, out marker) && marker != null) return true;
            marker = null;
            return false;
        }

        #endregion

        #region 层级诊断

        // 构建相对 Provider 根节点的可读路径，仅用于定位配置错误。
        private string GetHierarchyPath(Transform target)
        {
            if (target == transform) return name;
            Stack<string> names = new();
            Transform current = target;
            while (current != null && current != transform)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return $"{name}/{string.Join("/", names)}";
        }

        #endregion
    }
}