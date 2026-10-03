#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RPG.TaskSystemNS.Editor
{
    /// <summary>从显式候选数据库绘制任务 ID 引用，并保留尚未解析的历史值。</summary>
    [CustomPropertyDrawer(typeof(TaskIdDropdownAttribute))]
    internal sealed class TaskIdDropdownPropertyDrawer : PropertyDrawer
    {
        #region 绘制状态

        private static readonly HashSet<string> loggedInvalidPropertySet = new(StringComparer.Ordinal);

        #endregion

        #region UI Toolkit 绘制

        /// <summary>创建可随候选缓存变化而刷新的 UI Toolkit 下拉字段。</summary>
        /// <param name="property">标记为 TaskId 引用的序列化字符串。</param>
        /// <returns>任务引用下拉框或字段类型错误提示。</returns>
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            if (!IsStringProperty(property))
            {
                LogInvalidProperty(property);
                return new Label($"{property.displayName}：TaskIdDropdown 仅支持 string 字段");
            }

            UnityEngine.Object[] targets = property.serializedObject.targetObjects;
            string propertyPath = property.propertyPath;
            var dropdown = new DropdownField(property.displayName, new List<string> { "读取中" }, 0);
            Action refresh = () => RefreshDropdown(dropdown, targets, propertyPath);

            // 仅在字段挂载时监听缓存通知，避免静态事件长期持有已销毁 Inspector。
            dropdown.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                TaskIdEditorCatalog.Changed += refresh;
                refresh();
            });
            dropdown.RegisterCallback<DetachFromPanelEvent>(_ => TaskIdEditorCatalog.Changed -= refresh);
            dropdown.RegisterValueChangedCallback(change =>
            {
                if (!TryReadValue(targets, propertyPath, out string currentValue, out bool hasMultipleValues))
                {
                    refresh();
                    return;
                }

                TaskIdDropdownOptions options = BuildOptions(
                    TaskIdEditorCatalog.SourceDatabase,
                    currentValue,
                    hasMultipleValues);
                if (!options.TryGetValue(change.newValue, out string taskId)) return;
                if (!string.IsNullOrEmpty(taskId) && !TaskIdEditorCatalog.TryGetDefinition(taskId, out _))
                {
                    refresh();
                    return;
                }

                if (ApplyValue(targets, propertyPath, taskId))
                {
                    Debug.Log($"[TaskIdDropdownPropertyDrawer] 已设置任务引用，property={propertyPath}，taskId={(string.IsNullOrEmpty(taskId) ? "none" : taskId)}。");
                }

                refresh();
            });

            refresh();
            return dropdown;
        }

        #endregion

        #region IMGUI 绘制

        /// <summary>绘制兼容 Odin 和传统 Inspector 的任务 ID 下拉字段。</summary>
        /// <param name="position">字段绘制区域。</param>
        /// <param name="property">标记为 TaskId 引用的序列化字符串。</param>
        /// <param name="label">Inspector 字段标签。</param>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (!IsStringProperty(property))
            {
                LogInvalidProperty(property);
                EditorGUI.LabelField(position, label, new GUIContent("TaskIdDropdown 仅支持 string 字段"));
                return;
            }

            TaskDatabase database = TaskIdEditorCatalog.SourceDatabase;
            TaskIdDropdownOptions options = BuildOptions(database, property.stringValue, property.hasMultipleDifferentValues);
            GUIContent[] displayOptions = BuildDisplayOptions(options.Labels);
            GUIContent displayLabel = label ?? new GUIContent(property.displayName);
            EditorGUI.BeginProperty(position, displayLabel, property);
            bool previousMixedValue = EditorGUI.showMixedValue;
            try
            {
                EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
                using (new EditorGUI.DisabledScope(database == null))
                {
                    EditorGUI.BeginChangeCheck();
                    int selectedIndex = EditorGUI.Popup(position, displayLabel, options.SelectedIndex, displayOptions);
                    if (EditorGUI.EndChangeCheck() &&
                        selectedIndex >= 0 &&
                        selectedIndex < options.Values.Count &&
                        options.Values[selectedIndex] != null)
                    {
                        string taskId = options.Values[selectedIndex];
                        if (string.IsNullOrEmpty(taskId) || TaskIdEditorCatalog.TryGetDefinition(taskId, out _))
                        {
                            property.serializedObject.UpdateIfRequiredOrScript();
                            SerializedProperty currentProperty = property.serializedObject.FindProperty(property.propertyPath);
                            currentProperty.stringValue = taskId;
                            if (property.serializedObject.ApplyModifiedProperties())
                            {
                                Debug.Log($"[TaskIdDropdownPropertyDrawer] 已设置任务引用，property={property.propertyPath}，taskId={(string.IsNullOrEmpty(taskId) ? "none" : taskId)}。");
                            }
                        }
                    }
                }
            }
            finally
            {
                EditorGUI.showMixedValue = previousMixedValue;
                EditorGUI.EndProperty();
            }
        }

        #endregion

        #region 候选项生成与显示

        /// <summary>根据当前字符串值和候选数据库生成显示项与稳定 ID 映射。</summary>
        /// <param name="database">用户明确指定的候选数据库。</param>
        /// <param name="currentValue">字段当前保存的任务 ID。</param>
        /// <param name="hasMultipleValues">多对象编辑时字段值是否不同。</param>
        /// <returns>标签、写入值和当前选中项。</returns>
        private static TaskIdDropdownOptions BuildOptions(TaskDatabase database, string currentValue, bool hasMultipleValues)
        {
            var options = new TaskIdDropdownOptions(database != null);
            if (database == null)
            {
                string label = hasMultipleValues
                    ? "多个对象存在不同值"
                    : string.IsNullOrEmpty(currentValue)
                        ? "未设置候选数据库"
                        : $"无效引用（{currentValue}）";
                options.Add(label, null);
                options.Tooltip = "请在任务配置编辑器表头明确选择 TaskId 候选数据库；当前字段值不会被自动改写。";
                return options;
            }

            if (hasMultipleValues)
            {
                options.Add("多个对象存在不同值", null);
                options.SelectedIndex = 0;
            }

            options.Add("无", string.Empty);
            int emptyIndex = options.Labels.Count - 1;
            if (!hasMultipleValues && string.IsNullOrEmpty(currentValue)) options.SelectedIndex = emptyIndex;

            IReadOnlyList<TaskIdEditorCatalogEntry> entries = TaskIdEditorCatalog.Entries;
            bool currentFound = false;
            for (int index = 0; index < entries.Count; index++)
            {
                TaskIdEditorCatalogEntry entry = entries[index];
                options.Add(entry.DisplayLabel, entry.TaskId);
                if (!hasMultipleValues && string.Equals(entry.TaskId, currentValue, StringComparison.Ordinal))
                {
                    options.SelectedIndex = options.Labels.Count - 1;
                    currentFound = true;
                }
            }

            if (!hasMultipleValues && !string.IsNullOrEmpty(currentValue) && !currentFound)
            {
                options.Labels.Insert(emptyIndex, $"无效引用（{currentValue}）");
                options.Values.Insert(emptyIndex, null);
                options.SelectedIndex = emptyIndex;
            }

            options.Tooltip = hasMultipleValues
                ? $"候选来源：{database.name}。选择一项后会将该 TaskId 写入所有选中对象。"
                : $"候选来源：{database.name}。无效引用会保留原值，直到你主动选择新值。";
            return options;
        }

        /// <summary>为 UI Toolkit 字段重读当前对象值并刷新候选显示。</summary>
        /// <param name="dropdown">需要刷新的字段。</param>
        /// <param name="targets">创建 Drawer 时选中的配置对象。</param>
        /// <param name="propertyPath">字符串属性在对象中的序列化路径。</param>
        private static void RefreshDropdown(DropdownField dropdown, UnityEngine.Object[] targets, string propertyPath)
        {
            if (!TryReadValue(targets, propertyPath, out string currentValue, out bool hasMultipleValues))
            {
                dropdown.choices = new List<string> { "对象已不可用" };
                dropdown.SetValueWithoutNotify("对象已不可用");
                dropdown.SetEnabled(false);
                dropdown.tooltip = "任务引用目标资产已不可用；刷新 Inspector 后重新绑定。";
                return;
            }

            TaskDatabase database = TaskIdEditorCatalog.SourceDatabase;
            TaskIdDropdownOptions options = BuildOptions(database, currentValue, hasMultipleValues);
            dropdown.choices = options.Labels;
            dropdown.SetValueWithoutNotify(options.Labels[options.SelectedIndex]);
            dropdown.SetEnabled(options.IsEnabled);
            dropdown.tooltip = options.Tooltip;
        }

        /// <summary>将每个稳定 ID 显示标签转换为 IMGUI 所需内容。</summary>
        /// <param name="labels">候选显示标签。</param>
        /// <returns>下拉字段标签数组。</returns>
        private static GUIContent[] BuildDisplayOptions(IReadOnlyList<string> labels)
        {
            var displayOptions = new GUIContent[labels.Count];
            for (int index = 0; index < labels.Count; index++)
            {
                displayOptions[index] = new GUIContent(labels[index]);
            }

            return displayOptions;
        }

        /// <summary>从目标对象和属性路径读取当前值，不把 SerializedProperty 留在 UI 回调中。</summary>
        /// <param name="targets">属性所属的配置对象。</param>
        /// <param name="propertyPath">字符串属性序列化路径。</param>
        /// <param name="currentValue">单值编辑时读取到的字符串。</param>
        /// <param name="hasMultipleValues">多对象编辑是否存在不同值。</param>
        /// <returns>对象仍有效且字段仍为 string 时返回 true。</returns>
        private static bool TryReadValue(
            UnityEngine.Object[] targets,
            string propertyPath,
            out string currentValue,
            out bool hasMultipleValues)
        {
            currentValue = string.Empty;
            hasMultipleValues = false;
            if (targets == null || targets.Length == 0) return false;

            for (int index = 0; index < targets.Length; index++)
            {
                if (targets[index] == null) return false;
            }

            using var serializedTargets = new SerializedObject(targets);
            serializedTargets.UpdateIfRequiredOrScript();
            SerializedProperty property = serializedTargets.FindProperty(propertyPath);
            if (property == null || property.propertyType != SerializedPropertyType.String) return false;

            hasMultipleValues = property.hasMultipleDifferentValues;
            currentValue = hasMultipleValues ? string.Empty : property.stringValue;
            return true;
        }

        /// <summary>通过新的 SerializedObject 应用 UI Toolkit 的显式用户选择。</summary>
        /// <param name="targets">属性所属的配置对象。</param>
        /// <param name="propertyPath">字符串属性序列化路径。</param>
        /// <param name="taskId">要写入的稳定 ID；空字符串表示清除引用。</param>
        /// <returns>序列化对象确认有变更并完成应用时返回 true。</returns>
        private static bool ApplyValue(UnityEngine.Object[] targets, string propertyPath, string taskId)
        {
            if (!TryReadValue(targets, propertyPath, out _, out _)) return false;

            using var serializedTargets = new SerializedObject(targets);
            serializedTargets.UpdateIfRequiredOrScript();
            SerializedProperty property = serializedTargets.FindProperty(propertyPath);
            property.stringValue = taskId;
            return serializedTargets.ApplyModifiedProperties();
        }

        /// <summary>确认 Drawer 特性仅用于序列化字符串字段。</summary>
        /// <param name="property">待绘制字段。</param>
        /// <returns>字符串属性时返回 true。</returns>
        private static bool IsStringProperty(SerializedProperty property)
        {
            return property.propertyType == SerializedPropertyType.String;
        }

        /// <summary>对错误字段类型或失效对象路径只记录一次配置错误。</summary>
        /// <param name="property">问题字段。</param>
        private void LogInvalidProperty(SerializedProperty property)
        {
            string issueKey = $"{property.serializedObject.targetObject.GetInstanceID()}:{property.propertyPath}:{property.propertyType}";
            if (!loggedInvalidPropertySet.Add(issueKey)) return;
            Debug.LogError(
                $"[TaskIdDropdownPropertyDrawer] 字段 {property.propertyPath} 必须是 string，实际类型为 {property.propertyType}。",
                property.serializedObject.targetObject);
        }

        #endregion

        #region 嵌套类型

        /// <summary>维护一轮绘制所用的标签、序列化值及选择状态。</summary>
        private sealed class TaskIdDropdownOptions
        {
            #region 字段与属性

            internal readonly List<string> Labels = new();
            // 与 Labels 同索引；null 表示无效值或混合值提示，只能显示，不能写入。
            internal readonly List<string> Values = new();
            internal readonly bool IsEnabled;
            internal int SelectedIndex;
            internal string Tooltip;

            #endregion

            #region 构造与操作

            /// <summary>创建适用于当前候选数据库状态的临时下拉模型。</summary>
            /// <param name="isEnabled">存在候选数据库时为 true。</param>
            internal TaskIdDropdownOptions(bool isEnabled)
            {
                IsEnabled = isEnabled;
            }

            /// <summary>追加一个显示项及其可选序列化 ID。</summary>
            /// <param name="label">下拉显示文本。</param>
            /// <param name="taskId">可写入的 ID；null 表示展示态占位项。</param>
            internal void Add(string label, string taskId)
            {
                Labels.Add(label);
                Values.Add(taskId);
            }

            /// <summary>把 UI Toolkit 返回的显示文本解析成可写入 ID。</summary>
            /// <param name="label">下拉当前新标签。</param>
            /// <param name="taskId">解析到的稳定任务 ID。</param>
            /// <returns>该标签对应明确可选值时返回 true。</returns>
            internal bool TryGetValue(string label, out string taskId)
            {
                int index = Labels.IndexOf(label);
                if (index >= 0 && Values[index] != null)
                {
                    taskId = Values[index];
                    return true;
                }

                taskId = null;
                return false;
            }

            #endregion
        }

        #endregion
    }
}
#endif
