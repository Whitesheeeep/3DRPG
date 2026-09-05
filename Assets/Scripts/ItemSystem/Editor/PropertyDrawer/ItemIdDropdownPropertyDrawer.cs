#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RPG.ItemSystem.Editor
{
    /// <summary>从当前 ItemDatabase 选择 ItemId 引用的原生字段绘制器。</summary>
    [CustomPropertyDrawer(typeof(ItemIdDropdownAttribute))]
    internal sealed class ItemIdDropdownPropertyDrawer : PropertyDrawer
    {
        /// <summary>使用 UI Toolkit 创建 ItemId 下拉字段。</summary>
        /// <param name="property">待绘制的 ItemId 属性。</param>
        /// <returns>下拉字段视觉元素。</returns>
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            ItemDatabase database = ItemConfigEditorSession.ResolveDatabase();
            List<string> values = BuildValues(database, property);
            List<string> labels = BuildLabels(database, property, values);
            int selected = FindIndex(values, GetValue(property));
            DropdownField dropdown = new DropdownField(
                "物品标识",
                labels,
                Math.Max(0, selected));
            dropdown.tooltip = database == null ? "请先在物品配置窗口选择唯一的 ItemDatabase。" : "选择物品引用（名称 + ItemId）。";
            dropdown.SetEnabled(database != null);
            dropdown.RegisterValueChangedCallback(change =>
            {
                int index = labels.IndexOf(change.newValue);
                if (index < 0 || index >= values.Count) return;
                SetValue(property, values[index]);
            });
            return dropdown;
        }

        /// <summary>使用 IMGUI 创建 ItemId 下拉字段。</summary>
        /// <param name="position">绘制区域。</param>
        /// <param name="property">待绘制的 ItemId 属性。</param>
        /// <param name="label">字段标签。</param>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            ItemDatabase database = ItemConfigEditorSession.ResolveDatabase();
            List<string> values = BuildValues(database, property);
            List<string> labels = BuildLabels(database, property, values);
            int selected = FindIndex(values, GetValue(property));
            GUIContent displayLabel = label == null || string.IsNullOrEmpty(label.text)
                ? new GUIContent("物品标识")
                : label;
            GUIContent[] displayOptions = new GUIContent[labels.Count];
            for (int index = 0; index < labels.Count; index++)
                displayOptions[index] = new GUIContent(labels[index]);
            EditorGUI.BeginProperty(position, displayLabel, property);
            int next;
            using (new EditorGUI.DisabledScope(database == null))
            {
                next = EditorGUI.Popup(position, displayLabel, Math.Max(0, selected), displayOptions);
            }
            if (next != selected && next >= 0 && next < values.Count) SetValue(property, values[next]);
            EditorGUI.EndProperty();
        }

        /// <summary>构建内部 ItemId 值集合，并保留历史失效值。</summary>
        /// <param name="database">当前数据库。</param>
        /// <param name="property">ItemId 属性。</param>
        /// <returns>下拉值集合。</returns>
        private static List<string> BuildValues(ItemDatabase database, SerializedProperty property)
        {
            List<string> values = new List<string> { string.Empty };
            string current = GetValue(property);
            if (database != null)
            {
                for (int index = 0; index < database.Definitions.Count; index++)
                {
                    ItemDefinition definition = database.Definitions[index];
                    if (definition == null || values.Contains(definition.ItemId.Value)) continue;
                    values.Add(definition.ItemId.Value);
                }
            }

            if (!string.IsNullOrEmpty(current) && !values.Contains(current)) values.Insert(1, current);
            return values;
        }

        /// <summary>构建中文显示值集合。</summary>
        /// <param name="database">当前数据库。</param>
        /// <param name="property">ItemId 属性。</param>
        /// <param name="values">内部值集合。</param>
        /// <returns>下拉显示文本。</returns>
        private static List<string> BuildLabels(ItemDatabase database, SerializedProperty property, IReadOnlyList<string> values)
        {
            string current = GetValue(property);
            List<string> labels = new List<string>(values.Count);
            for (int index = 0; index < values.Count; index++)
            {
                string value = values[index];
                if (string.IsNullOrEmpty(value))
                {
                    labels.Add("无");
                    continue;
                }

                ItemDefinition definition = FindDefinition(database, value);
                labels.Add(definition == null
                    ? $"无效引用（{value}）"
                    : $"{definition.DisplayName}（{value}）");
            }

            if (database == null && labels.Count == 1) labels[0] = string.IsNullOrEmpty(current) ? "未找到唯一 ItemDatabase" : $"无效引用（{current}）";
            return labels;
        }

        /// <summary>读取 ItemId 结构内部的字符串值。</summary>
        /// <param name="property">ItemId 属性。</param>
        /// <returns>稳定字符串。</returns>
        private static string GetValue(SerializedProperty property) => property.FindPropertyRelative("value")?.stringValue ?? string.Empty;

        /// <summary>写入 ItemId 结构内部的字符串值。</summary>
        /// <param name="property">ItemId 属性。</param>
        /// <param name="value">新的稳定字符串。</param>
        private static void SetValue(SerializedProperty property, string value)
        {
            SerializedProperty valueProperty = property.FindPropertyRelative("value");
            if (valueProperty == null) return;
            property.serializedObject.Update();
            valueProperty.stringValue = value ?? string.Empty;
            property.serializedObject.ApplyModifiedProperties();
        }

        /// <summary>查找数据库中的物品定义。</summary>
        /// <param name="database">当前数据库。</param>
        /// <param name="value">ItemId 字符串。</param>
        /// <returns>对应定义；不存在时返回空。</returns>
        private static ItemDefinition FindDefinition(ItemDatabase database, string value)
        {
            if (database == null || !ItemId.TryCreate(value, out ItemId itemId)) return null;
            // 这里仅用于编辑器下拉框的显示名称查询，不触发数据库全量校验；其他 Definition 的临时配置错误不能阻断当前字段绘制。
            for (int index = 0; index < database.Definitions.Count; index++)
            {
                ItemDefinition definition = database.Definitions[index];
                if (definition != null && definition.ItemId == itemId) return definition;
            }

            return null;
        }

        /// <summary>查找当前值在下拉集合中的索引。</summary>
        /// <param name="values">内部值集合。</param>
        /// <param name="current">当前值。</param>
        /// <returns>索引。</returns>
        private static int FindIndex(IReadOnlyList<string> values, string current)
        {
            for (int index = 0; index < values.Count; index++)
                if (string.Equals(values[index], current, StringComparison.Ordinal)) return index;
            return 0;
        }
    }
}
#endif
