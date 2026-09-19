#if UNITY_EDITOR
using System.Collections.Generic;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace RPG.Markers.EditorNS
{
    /// <summary>
    /// 为 MarkerProviderBase 及其派生类保留 Odin Inspector，并追加通用 Marker 浏览面板。
    /// </summary>
    [CustomEditor(typeof(MarkerProviderBase), true)]
    [CanEditMultipleObjects]
    internal class MarkerProviderBaseEditor : OdinEditor
    {
        #region Inspector 绘制

        /// <summary>
        /// 先绘制 Odin 默认内容，再绘制基于接口快照的 Marker 浏览与诊断面板。
        /// </summary>
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            if (targets == null || targets.Length != 1)
            {
                EditorGUILayout.HelpBox(
                    "当前选择了多个 Marker Provider，已隐藏逐项 Marker 列表。请单独选择一个 Provider 查看详情。",
                    MessageType.Info);
                return;
            }

            MarkerProviderBase providerRoot = target as MarkerProviderBase;
            IMarkerProvider provider = providerRoot as IMarkerProvider;
            if (providerRoot == null || provider == null) return;

            List<TransformMarker> markerComponents = new();
            provider.GetMarkers(markerComponents);
            DrawMarkerProviderPanel(providerRoot, provider, markerComponents);
        }

        /// <summary>
        /// 绘制当前 Provider 的数量、校验操作、Marker 列表和必需项诊断。
        /// </summary>
        /// <param name="providerRoot">当前 Inspector 对应的 Provider 根组件。</param>
        /// <param name="provider">当前 Provider 的接口视图。</param>
        /// <param name="markerComponents">当前作用域的实时 Marker 快照。</param>
        private void DrawMarkerProviderPanel(
            MarkerProviderBase providerRoot,
            IMarkerProvider provider,
            IReadOnlyList<TransformMarker> markerComponents)
        {
            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Marker 浏览", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("当前作用域", $"{markerComponents.Count} 个 Marker");
                EditorGUILayout.LabelField("最近一次重建", provider.IsValid ? "有效" : "未验证或无效");

                if (GUILayout.Button("重新扫描并校验"))
                {
                    // 重建只刷新非序列化运行时索引；面板通过 Repaint 立即反映校验结果。
                    provider.TryRebuild();
                    Repaint();
                }

                DrawMarkerRows(providerRoot.transform, markerComponents);
                DrawRequiredMarkerDiagnostics(provider, markerComponents);
            }
        }

        /// <summary>
        /// 绘制每个 Marker 的 Key、相对路径、异常提示和层级选择按钮。
        /// </summary>
        /// <param name="providerRoot">当前 Provider 根节点。</param>
        /// <param name="markerComponents">当前 Provider 作用域内的 Marker 列表。</param>
        private static void DrawMarkerRows(Transform providerRoot, IReadOnlyList<TransformMarker> markerComponents)
        {
            if (markerComponents.Count == 0)
            {
                EditorGUILayout.HelpBox("当前作用域没有 TransformMarker。", MessageType.Info);
                return;
            }

            HashSet<MarkerKey> markerKeySet = new();
            EditorGUILayout.LabelField("Marker 列表", EditorStyles.miniBoldLabel);
            foreach (TransformMarker markerComponent in markerComponents)
            {
                MarkerKey key = markerComponent.Key;
                bool hasKey = key != null;
                bool isDuplicate = hasKey && !markerKeySet.Add(key);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        hasKey ? key.name : "<未配置 MarkerKey>",
                        GUILayout.Width(150f));
                    EditorGUILayout.LabelField(GetHierarchyPath(providerRoot, markerComponent.transform));
                    if (GUILayout.Button("选择", GUILayout.Width(48f)))
                    {
                        SelectMarkerGameObject(markerComponent);
                    }
                }

                if (!hasKey)
                {
                    EditorGUILayout.HelpBox("该 Marker 没有配置 MarkerKey。", MessageType.Error);
                }
                else if (isDuplicate)
                {
                    EditorGUILayout.HelpBox($"MarkerKey“{key.name}”在当前作用域内重复。", MessageType.Error);
                }
            }
        }

        /// <summary>
        /// 绘制必需 MarkerKey 的空项、重复项和缺失项诊断。
        /// </summary>
        /// <param name="provider">当前 Inspector 对应的 Marker Provider。</param>
        /// <param name="markerComponents">当前 Provider 作用域内的 Marker 列表。</param>
        private static void DrawRequiredMarkerDiagnostics(
            IMarkerProvider provider, IReadOnlyList<TransformMarker> markerComponents)
        {
            IReadOnlyList<MarkerKey> requiredMarkerKeys = provider.RequiredMarkerKeys;
            if (requiredMarkerKeys == null)
            {
                EditorGUILayout.HelpBox("必需 MarkerKey 列表未初始化。", MessageType.Error);
                return;
            }

            if (requiredMarkerKeys.Count == 0) return;

            HashSet<MarkerKey> existingMarkerKeySet = new();
            foreach (TransformMarker markerComponent in markerComponents)
            {
                if (markerComponent.Key != null) existingMarkerKeySet.Add(markerComponent.Key);
            }

            HashSet<MarkerKey> requiredMarkerKeySet = new();
            List<string> emptyRequiredEntries = new();
            List<string> duplicateRequiredKeys = new();
            List<string> missingRequiredKeys = new();
            foreach (MarkerKey requiredMarkerKey in requiredMarkerKeys)
            {
                if (requiredMarkerKey == null)
                {
                    emptyRequiredEntries.Add("<空项>");
                    continue;
                }

                if (!requiredMarkerKeySet.Add(requiredMarkerKey))
                {
                    duplicateRequiredKeys.Add(requiredMarkerKey.name);
                }

                if (!existingMarkerKeySet.Contains(requiredMarkerKey))
                {
                    missingRequiredKeys.Add(requiredMarkerKey.name);
                }
            }

            if (emptyRequiredEntries.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    $"必需 MarkerKey 存在空项（{emptyRequiredEntries.Count} 个）。", MessageType.Error);
            }

            if (duplicateRequiredKeys.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    $"必需 MarkerKey 重复：{string.Join("、", duplicateRequiredKeys)}。", MessageType.Error);
            }

            if (missingRequiredKeys.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    $"当前作用域缺少必需 Marker：{string.Join("、", missingRequiredKeys)}。", MessageType.Error);
            }
        }

        #endregion

        #region 层级定位

        /// <summary>
        /// 选择并 Ping Marker 所在的 GameObject，使 Inspector 与 Hierarchy 同步定位。
        /// </summary>
        /// <param name="markerComponent">需要定位的 Marker 组件。</param>
        private static void SelectMarkerGameObject(TransformMarker markerComponent)
        {
            GameObject markerGameObject = markerComponent.gameObject;
            Selection.activeGameObject = markerGameObject;
            EditorGUIUtility.PingObject(markerGameObject);
        }

        /// <summary>
        /// 构建相对 Provider 根节点的层级路径，便于在多个同名节点中识别 Marker。
        /// </summary>
        /// <param name="providerRoot">Provider 根节点。</param>
        /// <param name="target">Marker 所在 Transform。</param>
        /// <returns>以 Provider 根节点名称开头的层级路径。</returns>
        private static string GetHierarchyPath(Transform providerRoot, Transform target)
        {
            if (target == providerRoot) return providerRoot.name;

            Stack<string> names = new();
            Transform current = target;
            while (current != null && current != providerRoot)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return current == providerRoot
                ? $"{providerRoot.name}/{string.Join("/", names)}"
                : target.name;
        }

        #endregion
    }
}
#endif
