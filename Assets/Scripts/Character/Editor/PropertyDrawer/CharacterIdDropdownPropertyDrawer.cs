#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.EditorExtensions;

namespace RPG.Character.Editor
{
    /// <summary>从当前 CharacterDatabase 选择 CharacterId 引用的原生字段绘制器。</summary>
    [CustomPropertyDrawer(typeof(CharacterIdDropdownAttribute))]
    internal sealed class CharacterIdDropdownPropertyDrawer : PropertyDrawer
    {
        /// <summary>使用 UI Toolkit 创建角色引用下拉框。</summary>
        /// <param name="property">待绘制的 CharacterId 属性。</param>
        /// <returns>下拉字段。</returns>
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            CharacterDatabase database = CharacterConfigEditorSession.ResolveDatabase();
            List<string> values = BuildValues(database, property);
            List<string> labels = BuildLabels(database, values);
            int selectedIndex = FindIndex(values, GetValue(property));
            var dropdown = new DropdownField(property.displayName, labels, Math.Max(0, selectedIndex));
            dropdown.tooltip = database == null ? "请先选择唯一的 CharacterDatabase。" : "选择角色引用（ID（Name））；序列化只保存 ID。";
            dropdown.SetEnabled(database != null);
            dropdown.RegisterValueChangedCallback(change =>
            {
                int index = labels.IndexOf(change.newValue);
                if (index < 0 || index >= values.Count) return;
                SetValue(property, values[index]);
            });
            return dropdown;
        }

        /// <summary>使用 IMGUI 创建角色引用下拉框，兼容 Odin 和传统 Inspector。</summary>
        /// <param name="position">绘制区域。</param>
        /// <param name="property">待绘制的 CharacterId 属性。</param>
        /// <param name="label">字段标签。</param>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            CharacterDatabase database = CharacterConfigEditorSession.ResolveDatabase();
            List<string> values = BuildValues(database, property);
            List<string> labels = BuildLabels(database, values);
            int selectedIndex = FindIndex(values, GetValue(property));
            EditorGUI.BeginProperty(position, label, property);
            int nextIndex;
            // Unity 2022.3 的 EditorGUI.Popup 不提供带 GUIContent 标签的四参数重载，先绘制前缀再使用稳定的三参数重载。
            Rect popupPosition = EditorGUI.PrefixLabel(position, label);
            using (new EditorGUI.DisabledScope(database == null))
                nextIndex = EditorGUI.Popup(popupPosition, Math.Max(0, selectedIndex), labels.ToArray());
            if (nextIndex != selectedIndex && nextIndex >= 0 && nextIndex < values.Count) SetValue(property, values[nextIndex]);
            EditorGUI.EndProperty();
        }

        /// <summary>构建候选 CharacterId 并保留当前失效值。</summary>
        /// <param name="database">当前数据库。</param>
        /// <param name="property">引用属性。</param>
        /// <returns>稳定 ID 字符串集合。</returns>
        private static List<string> BuildValues(CharacterDatabase database, SerializedProperty property)
        {
            var values = new List<string> { string.Empty };
            string currentValue = GetValue(property);
            if (database != null)
            {
                for (int index = 0; index < database.Characters.Count; index++)
                {
                    CharacterConfig config = database.Characters[index];
                    if (config == null) continue;
                    string value = config.CharacterId.ToString();
                    if (!string.IsNullOrEmpty(value) && !values.Contains(value)) values.Add(value);
                }
            }
            if (!string.IsNullOrEmpty(currentValue) && !values.Contains(currentValue)) values.Insert(1, currentValue);
            return values;
        }

        /// <summary>构建下拉显示名称，失效引用不会被静默清空。</summary>
        private static List<string> BuildLabels(CharacterDatabase database, IReadOnlyList<string> values)
        {
            var labels = new List<string>(values.Count);
            for (int index = 0; index < values.Count; index++)
            {
                string value = values[index];
                if (string.IsNullOrEmpty(value)) { labels.Add("无"); continue; }
                CharacterConfig config = FindConfig(database, value);
                labels.Add(config == null
                    ? ConfigEditorStableIdUtility.FormatInvalidReferenceLabel(value)
                    : ConfigEditorStableIdUtility.FormatReferenceLabel(value, config.Name));
            }
            return labels;
        }

        /// <summary>直接从数据库列表读取配置，不触发全库严格校验。</summary>
        private static CharacterConfig FindConfig(CharacterDatabase database, string value)
        {
            if (database == null) return null;
            for (int index = 0; index < database.Characters.Count; index++)
            {
                CharacterConfig config = database.Characters[index];
                if (config != null && string.Equals(config.CharacterId.ToString(), value, StringComparison.Ordinal)) return config;
            }
            return null;
        }

        /// <summary>读取 CharacterId 内部字符串。</summary>
        private static string GetValue(SerializedProperty property) => property.FindPropertyRelative("value")?.stringValue ?? string.Empty;

        /// <summary>写入 CharacterId 内部字符串。</summary>
        private static void SetValue(SerializedProperty property, string value)
        {
            SerializedProperty valueProperty = property.FindPropertyRelative("value");
            if (valueProperty == null) return;
            property.serializedObject.Update();
            valueProperty.stringValue = value ?? string.Empty;
            property.serializedObject.ApplyModifiedProperties();
        }

        /// <summary>查找当前值在候选项中的索引。</summary>
        private static int FindIndex(IReadOnlyList<string> values, string current)
        {
            for (int index = 0; index < values.Count; index++)
                if (string.Equals(values[index], current, StringComparison.Ordinal)) return index;
            return 0;
        }
    }
}
#endif
