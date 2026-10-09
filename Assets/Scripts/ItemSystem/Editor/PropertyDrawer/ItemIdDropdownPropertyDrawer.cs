#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.EditorExtensions;

namespace RPG.ItemSystem.Editor
{
    /// <summary>从当前 ItemDatabase 选择 ItemId 引用的原生字段绘制器。</summary>
    [CustomPropertyDrawer(typeof(ItemIdDropdownAttribute))]
    internal sealed class ItemIdDropdownPropertyDrawer : PropertyDrawer
    {
        #region 绘制数据

        private static readonly GUIContent defaultDisplayLabel = new("物品标识");

        #endregion

        #region UI Toolkit 绘制

        /// <summary>使用 UI Toolkit 创建 ItemId 下拉字段。</summary>
        /// <param name="property">待绘制的 ItemId 属性。</param>
        /// <returns>下拉字段视觉元素。</returns>
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            string currentValue = GetValue(property);
            ItemIdDropdownOptionSet displayedOptions = ItemIdEditorCatalog.GetOptions(currentValue);
            DropdownField dropdown = new DropdownField(
                "物品标识",
                new List<string>(displayedOptions.Labels),
                displayedOptions.FindIndex(currentValue));
            Action refresh = () => displayedOptions = RefreshDropdown(dropdown, property);
            dropdown.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                ItemIdEditorCatalog.Changed += refresh;
                refresh();
            });
            dropdown.RegisterCallback<DetachFromPanelEvent>(_ => ItemIdEditorCatalog.Changed -= refresh);
            dropdown.RegisterValueChangedCallback(change =>
            {
                if (!displayedOptions.TryGetItemId(change.newValue, out string itemId)) return;
                SetValue(property, itemId);
            });
            return dropdown;
        }

        #endregion

        #region IMGUI 绘制

        /// <summary>使用 IMGUI 创建 ItemId 下拉字段。</summary>
        /// <param name="position">绘制区域。</param>
        /// <param name="property">待绘制的 ItemId 属性。</param>
        /// <param name="label">字段标签。</param>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            string currentValue = GetValue(property);
            ItemIdDropdownOptionSet displayedOptions = ItemIdEditorCatalog.GetOptions(currentValue);
            GUIContent displayLabel = label == null || string.IsNullOrEmpty(label.text)
                ? defaultDisplayLabel
                : label;
            EditorGUI.BeginProperty(position, displayLabel, property);
            using (new EditorGUI.DisabledScope(!displayedOptions.HasDatabase))
            {
                int currentIndex = displayedOptions.FindIndex(currentValue);
                int nextIndex = EditorGUI.Popup(position, displayLabel, currentIndex, displayedOptions.DisplayOptions);
                if (nextIndex >= 0 && nextIndex < displayedOptions.Values.Count && nextIndex != currentIndex)
                    SetValue(property, displayedOptions.Values[nextIndex]);
            }
            EditorGUI.EndProperty();
        }

        #endregion

        #region 候选刷新与序列化值读写

        /// <summary>读取属性当前值并更新 UI Toolkit 字段使用的缓存选项。</summary>
        /// <param name="dropdown">待更新的下拉字段。</param>
        /// <param name="property">字段对应的序列化属性。</param>
        /// <returns>字段当前使用的候选集。</returns>
        private static ItemIdDropdownOptionSet RefreshDropdown(DropdownField dropdown, SerializedProperty property)
        {
            string current = GetValue(property);
            ItemIdDropdownOptionSet options = ItemIdEditorCatalog.GetOptions(current);
            dropdown.choices = new List<string>(options.Labels);
            dropdown.SetValueWithoutNotify(options.Labels[options.FindIndex(current)]);
            dropdown.tooltip = options.HasDatabase
                ? "选择物品引用（ID（Name））；序列化只保存 ID。"
                : "请先在物品配置窗口选择唯一的 ItemDatabase。";
            dropdown.SetEnabled(options.HasDatabase);
            return options;
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

        #endregion
    }
}
#endif
