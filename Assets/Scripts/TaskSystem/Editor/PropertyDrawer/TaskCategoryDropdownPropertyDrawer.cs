#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RPG.TaskSystem.Editor
{
    /// <summary>
    /// 从静态任务分类表绘制选择框，并保留资产中已有的未知 ID 供显式修复。
    /// </summary>
    [CustomPropertyDrawer(typeof(TaskCategoryDropdownAttribute))]
    internal sealed class TaskCategoryDropdownPropertyDrawer : PropertyDrawer
    {
        #region 字段绘制

        /// <summary>
        /// 使用 UI Toolkit 绘制任务分类下拉框。
        /// </summary>
        /// <param name="property">待绘制的分类字符串属性。</param>
        /// <returns>任务分类下拉字段。</returns>
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            List<string> categoryIds = BuildCategoryIds(property.stringValue);
            List<string> displayNames = BuildDisplayNames(categoryIds);
            int selectedIndex = FindIndex(categoryIds, property.stringValue);
            var dropdown = new DropdownField(property.displayName, displayNames, selectedIndex)
            {
                tooltip = "选择任务分类；资产中保存稳定分类 ID。"
            };

            dropdown.RegisterValueChangedCallback(change =>
            {
                int changedIndex = displayNames.IndexOf(change.newValue);
                if (changedIndex < 0 || string.IsNullOrEmpty(categoryIds[changedIndex]))
                {
                    return;
                }

                SetValue(property, categoryIds[changedIndex]);
            });

            return dropdown;
        }

        /// <summary>
        /// 使用 IMGUI 绘制任务分类下拉框，兼容 Odin 和传统 Inspector。
        /// </summary>
        /// <param name="position">字段绘制区域。</param>
        /// <param name="property">待绘制的分类字符串属性。</param>
        /// <param name="label">Inspector 字段标签。</param>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            List<string> categoryIds = BuildCategoryIds(property.stringValue);
            List<string> displayNames = BuildDisplayNames(categoryIds);
            GUIContent[] displayOptions = BuildDisplayOptions(displayNames);
            int selectedIndex = FindIndex(categoryIds, property.stringValue);

            EditorGUI.BeginProperty(position, label, property);
            EditorGUI.BeginChangeCheck();
            int changedIndex = EditorGUI.Popup(position, label, selectedIndex, displayOptions);
            if (EditorGUI.EndChangeCheck() &&
                changedIndex >= 0 &&
                changedIndex < categoryIds.Count &&
                !string.IsNullOrEmpty(categoryIds[changedIndex]))
            {
                SetValue(property, categoryIds[changedIndex]);
            }

            EditorGUI.EndProperty();
        }

        #endregion

        #region 分类选项与序列化

        /// <summary>
        /// 构建稳定 ID 候选项，并把当前未知 ID 保留为单独选项。
        /// </summary>
        /// <param name="currentValue">资产当前保存的 ID。</param>
        /// <returns>与显示标签索引对应的分类 ID 列表。</returns>
        private static List<string> BuildCategoryIds(string currentValue)
        {
            var categoryIds = new List<string> { string.Empty };
            if (!string.IsNullOrEmpty(currentValue) && !TaskCategoryCatalog.IsDefined(currentValue))
            {
                categoryIds.Add(currentValue);
            }

            IReadOnlyList<TaskCategoryOption> options = TaskCategoryCatalog.Options;
            for (int index = 0; index < options.Count; index++)
            {
                categoryIds.Add(options[index].Id.Value);
            }

            return categoryIds;
        }

        /// <summary>
        /// 为占位项、未知历史值和静态分类生成 Inspector 标签。
        /// </summary>
        /// <param name="categoryIds">稳定 ID 候选项。</param>
        /// <returns>与 ID 索引对应的显示标签。</returns>
        private static List<string> BuildDisplayNames(IReadOnlyList<string> categoryIds)
        {
            var displayNames = new List<string>(categoryIds.Count);
            for (int index = 0; index < categoryIds.Count; index++)
            {
                string categoryId = categoryIds[index];
                if (string.IsNullOrEmpty(categoryId))
                {
                    displayNames.Add("请选择分类");
                }
                else if (TaskCategoryCatalog.TryGetDisplayName(categoryId, out string displayName))
                {
                    displayNames.Add(displayName);
                }
                else
                {
                    displayNames.Add($"无效分类（{categoryId}）");
                }
            }

            return displayNames;
        }

        /// <summary>
        /// 将显示文本转为 IMGUI 下拉框所需的标签对象。
        /// </summary>
        /// <param name="displayNames">候选显示名称。</param>
        /// <returns>IMGUI 显示选项。</returns>
        private static GUIContent[] BuildDisplayOptions(IReadOnlyList<string> displayNames)
        {
            var displayOptions = new GUIContent[displayNames.Count];
            for (int index = 0; index < displayNames.Count; index++)
            {
                displayOptions[index] = new GUIContent(displayNames[index]);
            }

            return displayOptions;
        }

        /// <summary>
        /// 查找当前稳定 ID 对应的下拉选项索引。
        /// </summary>
        /// <param name="categoryIds">稳定 ID 候选项。</param>
        /// <param name="currentValue">资产当前保存的 ID。</param>
        /// <returns>对应选项索引；未找到时返回占位项索引。</returns>
        private static int FindIndex(IReadOnlyList<string> categoryIds, string currentValue)
        {
            for (int index = 0; index < categoryIds.Count; index++)
            {
                if (string.Equals(categoryIds[index], currentValue, StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return 0;
        }

        /// <summary>
        /// 通过 Unity 序列化对象写入用户选中的稳定分类 ID。
        /// </summary>
        /// <param name="property">分类字符串属性。</param>
        /// <param name="categoryId">用户选择的新分类 ID。</param>
        private static void SetValue(SerializedProperty property, string categoryId)
        {
            property.serializedObject.Update();
            property.stringValue = categoryId;
            property.serializedObject.ApplyModifiedProperties();
        }

        #endregion
    }
}
#endif
