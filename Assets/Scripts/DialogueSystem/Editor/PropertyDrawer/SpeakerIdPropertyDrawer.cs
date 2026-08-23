#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RPG.DialogueSystem.Editor
{
    /// <summary>
    /// 使用 ProjectSettings 中的 SpeakerId 列表绘制严格下拉选择器。
    /// </summary>
    [CustomPropertyDrawer(typeof(SpeakerIdAttribute))]
    internal sealed class SpeakerIdPropertyDrawer : PropertyDrawer
    {
        #region 属性绘制

        /// <inheritdoc />
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            List<string> values = BuildValues(property.stringValue);
            GUIContent[] displayValues = BuildDisplayValues(values, property.stringValue);
            int selectedIndex = FindSelectedIndex(values, property.stringValue);

            EditorGUI.BeginProperty(position, label, property);
            bool previousMixed = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            int nextIndex = EditorGUI.Popup(position, label, selectedIndex, displayValues);
            EditorGUI.showMixedValue = previousMixed;

            if (nextIndex != selectedIndex && nextIndex >= 0 && nextIndex < values.Count)
            {
                Undo.RecordObjects(property.serializedObject.targetObjects, "Change Dialogue SpeakerId");
                property.stringValue = values[nextIndex];
                property.serializedObject.ApplyModifiedProperties();
            }

            EditorGUI.EndProperty();
        }

        #endregion

        #region 下拉数据

        /// <summary>构建包含空选项和当前历史值的严格选择集合。</summary>
        /// <param name="currentValue">当前字段值。</param>
        /// <returns>用于 Popup 的稳定值集合。</returns>
        private static List<string> BuildValues(string currentValue)
        {
            List<string> values = new List<string> { string.Empty };
            IReadOnlyList<string> configured = DialogueSpeakerIdSettings.instance.SpeakerIds;
            for (int index = 0; index < configured.Count; index++)
            {
                string value = configured[index] ?? string.Empty;
                if (!values.Contains(value)) values.Add(value);
            }

            if (!string.IsNullOrEmpty(currentValue) && !values.Contains(currentValue))
                values.Insert(1, currentValue);
            return values;
        }

        /// <summary>生成给用户看的选择文本，并保留历史未知值的 Missing 提示。</summary>
        /// <param name="values">内部选择值集合。</param>
        /// <param name="currentValue">当前字段值。</param>
        /// <returns>Popup 显示文本。</returns>
        private static GUIContent[] BuildDisplayValues(IReadOnlyList<string> values, string currentValue)
        {
            GUIContent[] display = new GUIContent[values.Count];
            for (int index = 0; index < values.Count; index++)
                display[index] = new GUIContent(string.IsNullOrEmpty(values[index])
                    ? "<empty>"
                    : values[index] == currentValue && !ContainsConfiguredValue(values[index])
                        ? $"Missing: {values[index]}"
                        : values[index]);
            return display;
        }

        /// <summary>按序比较配置列表，避免依赖编辑器集合扩展方法的重载差异。</summary>
        /// <param name="value">待查找的 SpeakerId。</param>
        /// <returns>配置列表中是否存在该值。</returns>
        private static bool ContainsConfiguredValue(string value)
        {
            IReadOnlyList<string> configured = DialogueSpeakerIdSettings.instance.SpeakerIds;
            for (int index = 0; index < configured.Count; index++)
                if (string.Equals(configured[index], value, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>查找当前值在 Popup 集合中的索引。</summary>
        /// <param name="values">Popup 值集合。</param>
        /// <param name="currentValue">当前字段值。</param>
        /// <returns>当前值索引。</returns>
        private static int FindSelectedIndex(IReadOnlyList<string> values, string currentValue)
        {
            for (int index = 0; index < values.Count; index++)
                if (string.Equals(values[index], currentValue, StringComparison.Ordinal)) return index;
            return 0;
        }

        #endregion
    }
}
#endif
