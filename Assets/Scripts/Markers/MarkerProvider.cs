using System;
using System.Collections.Generic;
using UnityEngine;
// using Sirenix.OdinInspector;

namespace RPG.Markers
{
    /// <summary>
    /// 作为实例级 Socket 容器收集自身作用域内的 TransformMarker，并提供稳定语义查询。
    /// </summary>
    [DisallowMultipleComponent]
    //[InfoBox("扫描本节点及所有子节点的 TransformMarker，并通过其最近父级 IMarkerProvider 划分作用域；必需 Marker 必须在当前作用域内配置。")]
    public sealed class MarkerProvider : MarkerProviderBase
    {
        #region 挂点索引
#if UNITY_EDITOR
        // [InfoBox("$MarkerValidationMessage", InfoMessageType.Warning, "HasMarkerValidationWarning")]
#endif
        [SerializeField, Tooltip("该角色进入可切换队伍前必须存在的语义挂点。")]
        private List<MarkerKey> requiredMarkerKeys = new();
        private Dictionary<MarkerKey, Transform> markerByKeyMap = new();
        private bool isValid;

        /// <inheritdoc />
        public override IReadOnlyList<MarkerKey> RequiredMarkerKeys => requiredMarkerKeys;

        /// <inheritdoc />
        public override bool IsValid => isValid;
        #endregion

        #region Unity 生命周期
        /// <summary>在场景或 Prefab 实例完成反序列化后建立第一份挂点索引。</summary>
        private void Awake()
        {
            TryRebuild();
        }

        #endregion

        #region 公开操作
        /// <summary>
        /// 原子重建当前 Provider 作用域中的 Marker 索引，并排除嵌套 Provider 管理的节点。
        /// </summary>
        /// <returns>完整索引构建成功时返回 true；失败时保留上一份有效索引。</returns>
        public override bool TryRebuild()
        {
            isValid = false;
            List<TransformMarker> markerComponents = new();
            GetMarkers(markerComponents);
            Dictionary<MarkerKey, Transform> rebuiltMarkerByKeyMap = new(markerComponents.Count);
            foreach (TransformMarker component in markerComponents)
            {
                MarkerKey key = component.Key;
                if (key == null)
                {
                    Debug.LogError($"Marker 节点“{GetHierarchyPath(component.transform)}”没有配置 MarkerKey。");
                    return false;
                }

                if (!rebuiltMarkerByKeyMap.TryAdd(key, component.transform))
                {
                    Transform existing = rebuiltMarkerByKeyMap[key];
                    Debug.LogError(
                        $"MarkerProvider“{name}”中存在重复 MarkerKey“{key.name}”："
                        + $"“{GetHierarchyPath(existing)}”与“{GetHierarchyPath(component.transform)}”。");
                    return false;
                }
            }

            if (requiredMarkerKeys == null)
            {
                Debug.LogError($"MarkerProvider“{name}”的必需 MarkerKey 列表未初始化。", this);
                return false;
            }

            HashSet<MarkerKey> required = new();
            foreach (MarkerKey key in requiredMarkerKeys)
            {
                if (key == null)
                {
                    Debug.LogError($"MarkerProvider“{name}”的必需 MarkerKey 列表包含空项。", this);
                    return false;
                }

                if (!required.Add(key))
                {
                    Debug.LogError($"MarkerProvider“{name}”重复配置了必需 MarkerKey“{key.name}”。", this);
                    return false;
                }

                if (!rebuiltMarkerByKeyMap.ContainsKey(key))
                {
                    Debug.LogError($"MarkerProvider“{name}”缺少必需 Marker“{key.name}”。", this);
                    return false;
                }
            }

            markerByKeyMap = rebuiltMarkerByKeyMap;
            isValid = true;
            Debug.Log($"[MarkerProvider] Provider“{name}”重建 Marker 索引成功，markerCount={markerByKeyMap.Count}。", this);
            return true;
        }

        /// <summary>
        /// 收集当前 Provider 作用域内的全部 TransformMarker，并排除嵌套 Provider 接管的子树。
        /// </summary>
        /// <param name="results">
        /// 接收收集结果的列表；方法会先清空列表，并保留空 Key 或重复 Key 的 Marker 供诊断。
        /// </param>
        /// <exception cref="ArgumentNullException">当调用方传入空列表引用时抛出。</exception>
        public override void GetMarkers(List<TransformMarker> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));

            // 统一收集入口同时服务运行时索引与编辑器列表，确保两者不会出现不同的作用域判断。
            results.Clear();
            TransformMarker[] markerComponents = GetComponentsInChildren<TransformMarker>(true);
            foreach (TransformMarker markerComponent in markerComponents)
            {
                if (IsMarkerInCurrentScope(markerComponent.transform))
                {
                    results.Add(markerComponent);
                }
            }
        }

        /// <summary>
        /// 查询当前实例作用域中指定语义 Socket 对应的 Transform。
        /// </summary>
        /// <param name="key">需要解析的 MarkerKey。</param>
        /// <param name="marker">查询成功时返回该实例中的实际 Transform。</param>
        /// <returns>索引中存在有效节点时返回 true。</returns>
        public override bool TryGetMarker(MarkerKey key, out Transform marker)
        {
            if (key != null && markerByKeyMap.TryGetValue(key, out marker) && marker != null) return true;
            marker = null;
            return false;
        }
        #endregion

        #region 层级诊断
        /// <summary>构建相对 Provider 根节点的可读路径，仅用于定位配置错误。</summary>
        /// <param name="target">需要定位的挂点 Transform。</param>
        /// <returns>以 Provider 名称开头的层级路径。</returns>
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

        /// <summary>
        /// 判断目标 Marker 的最近 Provider 是否为当前实例，从而划分嵌套 Provider 的作用域。
        /// </summary>
        /// <param name="markerTransform">待判断的 Marker Transform。</param>
        /// <returns>最近 Provider 为当前实例时返回 true。</returns>
        private bool IsMarkerInCurrentScope(Transform markerTransform)
        {
            Transform current = markerTransform;
            while (current != null)
            {
                // 同一层级上不允许多个 Provider 共同声明所有权；按组件顺序找到最近实现者。
                MonoBehaviour[] behaviours = current.GetComponents<MonoBehaviour>();
                foreach (MonoBehaviour behaviour in behaviours)
                {
                    if (behaviour is not IMarkerProvider) continue;
                    return behaviour == this;
                }

                if (current == transform) return false;
                current = current.parent;
            }

            return false;
        }

#if UNITY_EDITOR
        /// <summary>
        /// 获取当前层级必需 Marker 的只读配置诊断文本，供 Odin InfoBox 显示。
        /// </summary>
        /// <returns>存在配置问题时返回完整警告文本，否则返回空字符串。</returns>
        private string MarkerValidationMessage
        {
            get
            {
                List<string> emptyEntries = new();
                List<string> duplicateKeys = new();
                List<string> missingKeys = new();
                HashSet<MarkerKey> configuredKeys = new();

                if (requiredMarkerKeys == null || requiredMarkerKeys.Count == 0)
                {
                    return string.Empty;
                }

                foreach (MarkerKey key in requiredMarkerKeys)
                {
                    if (key == null)
                    {
                        emptyEntries.Add("<空项>");
                        continue;
                    }

                    if (!configuredKeys.Add(key))
                    {
                        duplicateKeys.Add(key.name);
                    }
                }

                // 包含未激活子物体，并通过最近父级 IMarkerProvider 保证嵌套 Provider 的挂点不越界。
                List<TransformMarker> components = new();
                GetMarkers(components);
                HashSet<MarkerKey> existingKeys = new();
                foreach (TransformMarker component in components)
                {
                    if (component.Key != null) existingKeys.Add(component.Key);
                }

                foreach (MarkerKey key in configuredKeys)
                {
                    if (!existingKeys.Contains(key))
                    {
                        missingKeys.Add(key.name);
                    }
                }

                if (emptyEntries.Count == 0 && duplicateKeys.Count == 0 && missingKeys.Count == 0)
                {
                    return string.Empty;
                }

                List<string> problems = new();
                if (emptyEntries.Count > 0)
                {
                    problems.Add($"必需 MarkerKey 存在空项（{emptyEntries.Count} 个）");
                }

                if (duplicateKeys.Count > 0)
                {
                    problems.Add($"重复配置必需 Marker：{string.Join("、", duplicateKeys)}");
                }

                if (missingKeys.Count > 0)
                {
                    problems.Add($"缺少必需 Marker：{string.Join("、", missingKeys)}");
                }

                return "当前 MarkerProvider 的挂点配置不完整：\n"
                    + string.Join("；", problems)
                    + "。\n请在当前 Provider 所属层级内配置对应的 TransformMarker。";
            }
        }

        /// <summary>
        /// 判断当前层级是否需要在 Inspector 中显示必需 Marker 警告。
        /// </summary>
        /// <returns>存在空项、重复项或缺失挂点时返回 true。</returns>
        private bool HasMarkerValidationWarning
        {
            get { return !string.IsNullOrEmpty(MarkerValidationMessage); }
        }

#endif
        #endregion
    }
}
