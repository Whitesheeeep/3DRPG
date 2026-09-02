using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace WS_Modules
{
    /// <summary>
    /// Draws Addressables address selectors for string fields and string arrays/lists.
    /// </summary>
    [CustomPropertyDrawer(typeof(WSAddressableKeyAttribute))]
    internal sealed class WSAddressableKeyDrawer : PropertyDrawer
    {
        #region 常量

        private const float VerticalSpacing = 2f;
        private const float ArrayElementIndent = 16f;
        private const string NoneLabel = "None";
        private const string UnsupportedTypeMessage = "[WSAddressableKey] only supports string, string[], or List<string> fields.";
        private const string MissingSettingsMessage = "Addressables Settings have not been created.";
        private const string EmptyOptionsMessage = "No Addressables entries match the current group and label filters.";

        #endregion

        #region 属性绘制生命周期

        /// <summary>
        /// Draws an Addressables selector for a supported string field or collection.
        /// </summary>
        /// <param name="position">The rectangle allocated by Unity for the property.</param>
        /// <param name="property">The serialized property being drawn.</param>
        /// <param name="label">The label displayed beside the property.</param>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            WSAddressableKeyAttribute keyAttribute = (WSAddressableKeyAttribute)attribute;

            if (property.propertyType == SerializedPropertyType.String)
            {
                DrawAddressableKeyField(position, property, label, keyAttribute);
                return;
            }

            if (IsStringCollection(property))
            {
                DrawAddressableKeyArray(position, property, label, keyAttribute);
                return;
            }

            DrawUnsupportedProperty(position, property, label);
        }

        /// <summary>
        /// Calculates the height required by the selector and any warning message.
        /// </summary>
        /// <param name="property">The serialized property being measured.</param>
        /// <param name="label">The label associated with the property.</param>
        /// <returns>The height in GUI points required to draw the property.</returns>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (property.propertyType == SerializedPropertyType.String)
            {
                return GetAddressableKeyFieldHeight((WSAddressableKeyAttribute)attribute);
            }

            if (IsStringCollection(property))
            {
                return GetAddressableKeyArrayHeight(property, (WSAddressableKeyAttribute)attribute);
            }

            return EditorGUI.GetPropertyHeight(property, label, true) +
                   VerticalSpacing +
                   EditorGUIUtility.singleLineHeight * 2f;
        }

        #endregion

        #region 类型与集合绘制

        /// <summary>
        /// Determines whether a serialized property is a string array or a List&lt;string&gt;.
        /// </summary>
        /// <param name="property">The serialized property to inspect.</param>
        /// <returns><see langword="true"/> when the property is a supported string collection.</returns>
        private bool IsStringCollection(SerializedProperty property)
        {
            if (!property.isArray || property.propertyType != SerializedPropertyType.Generic)
            {
                return false;
            }

            Type fieldType = fieldInfo?.FieldType;
            if (fieldType == typeof(string[]))
            {
                return true;
            }

            return fieldType != null &&
                   fieldType.IsGenericType &&
                   fieldType.GetGenericTypeDefinition() == typeof(List<>) &&
                   fieldType.GetGenericArguments()[0] == typeof(string);
        }

        /// <summary>
        /// Draws the foldout, size field, and Addressables selector for each string element.
        /// </summary>
        /// <param name="position">The rectangle allocated by Unity for the collection.</param>
        /// <param name="property">The serialized array or list property.</param>
        /// <param name="label">The collection label.</param>
        /// <param name="keyAttribute">The filter configuration attached to the field.</param>
        private static void DrawAddressableKeyArray(
            Rect position,
            SerializedProperty property,
            GUIContent label,
            WSAddressableKeyAttribute keyAttribute)
        {
            float currentY = position.y;
            Rect foldoutRect = new Rect(position.x, currentY, position.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label, true);
            currentY += EditorGUIUtility.singleLineHeight + VerticalSpacing;

            if (!property.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            SerializedProperty sizeProperty = property.FindPropertyRelative("Array.size");
            Rect sizeRect = new Rect(position.x, currentY, position.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.PropertyField(sizeRect, sizeProperty);
            currentY += EditorGUIUtility.singleLineHeight + VerticalSpacing;

            for (int i = 0; i < property.arraySize; i++)
            {
                SerializedProperty elementProperty = property.GetArrayElementAtIndex(i);
                Rect elementRect = new Rect(
                    position.x + ArrayElementIndent,
                    currentY,
                    position.width - ArrayElementIndent,
                    GetAddressableKeyFieldHeight(keyAttribute));

                DrawAddressableKeyField(elementRect, elementProperty, new GUIContent($"Element {i}"), keyAttribute);
                currentY += elementRect.height + VerticalSpacing;
            }

            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// Calculates the height of a collapsed or expanded string collection.
        /// </summary>
        /// <param name="property">The serialized array or list property.</param>
        /// <param name="keyAttribute">The filter configuration attached to the field.</param>
        /// <returns>The height in GUI points required to draw the collection.</returns>
        private static float GetAddressableKeyArrayHeight(
            SerializedProperty property,
            WSAddressableKeyAttribute keyAttribute)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded)
            {
                return height;
            }

            height += VerticalSpacing;
            height += EditorGUIUtility.singleLineHeight + VerticalSpacing;

            float fieldHeight = GetAddressableKeyFieldHeight(keyAttribute);
            height += property.arraySize * (fieldHeight + VerticalSpacing);
            return height;
        }

        #endregion

        #region 单值绘制

        /// <summary>
        /// Draws one Addressables address selector, including settings and empty-result warnings.
        /// </summary>
        /// <param name="position">The rectangle allocated by Unity for the field.</param>
        /// <param name="property">The serialized string property.</param>
        /// <param name="label">The field label.</param>
        /// <param name="keyAttribute">The filter configuration attached to the field.</param>
        private static void DrawAddressableKeyField(
            Rect position,
            SerializedProperty property,
            GUIContent label,
            WSAddressableKeyAttribute keyAttribute)
        {
            if (!AddressableAssetSettingsDefaultObject.SettingsExists)
            {
                DrawDisabledPopupWithHelp(position, label, property.stringValue, MissingSettingsMessage);
                return;
            }

            List<AddressableKeyOption> options = GetAddressableKeyOptions(keyAttribute);
            if (options.Count == 0)
            {
                DrawDisabledPopupWithHelp(position, label, property.stringValue, EmptyOptionsMessage);
                return;
            }

            DrawPopup(position, property, label, options);
        }

        /// <summary>
        /// Calculates the height of one Addressables selector and its warning message when needed.
        /// </summary>
        /// <param name="keyAttribute">The filter configuration attached to the field.</param>
        /// <returns>The height in GUI points required to draw the field.</returns>
        private static float GetAddressableKeyFieldHeight(WSAddressableKeyAttribute keyAttribute)
        {
            if (!AddressableAssetSettingsDefaultObject.SettingsExists ||
                GetAddressableKeyOptions(keyAttribute).Count == 0)
            {
                return EditorGUIUtility.singleLineHeight +
                       VerticalSpacing +
                       EditorGUIUtility.singleLineHeight * 2f;
            }

            return EditorGUIUtility.singleLineHeight;
        }

        /// <summary>
        /// Draws the popup and writes the selected Addressables address back to the serialized property.
        /// </summary>
        /// <param name="position">The rectangle allocated by Unity for the popup.</param>
        /// <param name="property">The serialized string property.</param>
        /// <param name="label">The field label.</param>
        /// <param name="options">The filtered and sorted Addressables options.</param>
        private static void DrawPopup(
            Rect position,
            SerializedProperty property,
            GUIContent label,
            List<AddressableKeyOption> options)
        {
            List<string> values = new List<string>(options.Count + 2) { string.Empty };
            List<GUIContent> labels = new List<GUIContent>(options.Count + 2) { new GUIContent(NoneLabel) };

            for (int i = 0; i < options.Count; i++)
            {
                AddressableKeyOption option = options[i];
                values.Add(option.Address);
                labels.Add(new GUIContent($"{option.Address} ({option.GroupName})", option.Tooltip));
            }

            string currentValue = property.stringValue ?? string.Empty;
            int currentIndex = values.IndexOf(currentValue);
            if (!string.IsNullOrWhiteSpace(currentValue) && currentIndex < 0)
            {
                values.Insert(1, currentValue);
                labels.Insert(1, new GUIContent($"Missing: {currentValue}"));
                currentIndex = 1;
            }
            else if (currentIndex < 0)
            {
                currentIndex = 0;
            }

            EditorGUI.BeginProperty(position, label, property);
            EditorGUI.BeginChangeCheck();
            int selectedIndex = EditorGUI.Popup(
                new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight),
                label,
                currentIndex,
                labels.ToArray());

            if (EditorGUI.EndChangeCheck() && selectedIndex >= 0 && selectedIndex < values.Count)
            {
                property.stringValue = values[selectedIndex];
            }

            EditorGUI.EndProperty();
        }

        /// <summary>
        /// Draws a disabled popup and explains why no selectable Addressables options are available.
        /// </summary>
        /// <param name="position">The rectangle allocated by Unity for the field.</param>
        /// <param name="label">The field label.</param>
        /// <param name="currentValue">The currently serialized address.</param>
        /// <param name="message">The warning text shown below the disabled popup.</param>
        private static void DrawDisabledPopupWithHelp(
            Rect position,
            GUIContent label,
            string currentValue,
            string message)
        {
            Rect popupRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            string popupLabel = string.IsNullOrWhiteSpace(currentValue) ? NoneLabel : $"Current: {currentValue}";
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUI.Popup(popupRect, label, 0, new[] { new GUIContent(popupLabel) });
            }

            Rect helpRect = new Rect(
                position.x,
                popupRect.yMax + VerticalSpacing,
                position.width,
                EditorGUIUtility.singleLineHeight * 2f);
            EditorGUI.HelpBox(helpRect, message, MessageType.Warning);
        }

        /// <summary>
        /// Draws unsupported properties using Unity's default field and an explanatory error.
        /// </summary>
        /// <param name="position">The rectangle allocated by Unity for the property.</param>
        /// <param name="property">The unsupported serialized property.</param>
        /// <param name="label">The field label.</param>
        private static void DrawUnsupportedProperty(Rect position, SerializedProperty property, GUIContent label)
        {
            Rect fieldRect = new Rect(
                position.x,
                position.y,
                position.width,
                EditorGUI.GetPropertyHeight(property, label, true));
            EditorGUI.PropertyField(fieldRect, property, label, true);

            Rect helpRect = new Rect(
                position.x,
                fieldRect.yMax + VerticalSpacing,
                position.width,
                EditorGUIUtility.singleLineHeight * 2f);
            EditorGUI.HelpBox(helpRect, UnsupportedTypeMessage, MessageType.Error);
        }

        #endregion

        #region Addressables 筛选

        /// <summary>
        /// Collects Addressables entries matching the configured group and label expressions.
        /// </summary>
        /// <param name="keyAttribute">The filter configuration attached to the field.</param>
        /// <returns>Sorted Addressables options that satisfy the filters.</returns>
        private static List<AddressableKeyOption> GetAddressableKeyOptions(WSAddressableKeyAttribute keyAttribute)
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            List<AddressableKeyOption> options = new List<AddressableKeyOption>();
            if (settings == null)
            {
                return options;
            }

            // 预先解析表达式，避免为每个 Addressables 条目重复拆分字符串。
            List<string> groupFilters = ParseGroupFilters(keyAttribute.GroupName);
            List<LabelFilterCondition> labelFilters = ParseLabelFilters(keyAttribute.Labels);

            foreach (AddressableAssetGroup group in settings.groups)
            {
                if (group == null || !MatchesGroup(group, groupFilters))
                {
                    continue;
                }

                foreach (AddressableAssetEntry entry in group.entries)
                {
                    if (entry == null ||
                        string.IsNullOrWhiteSpace(entry.address) ||
                        !MatchesLabels(entry, labelFilters))
                    {
                        continue;
                    }

                    options.Add(new AddressableKeyOption(entry.address, group.Name, CreateTooltip(entry)));
                }
            }

            return options
                .OrderBy(option => option.GroupName)
                .ThenBy(option => option.Address)
                .ToList();
        }

        /// <summary>
        /// Parses a group expression into trimmed OR alternatives.
        /// </summary>
        /// <param name="groupExpression">The raw group expression.</param>
        /// <returns>Valid group names, or an empty list when Group is unrestricted.</returns>
        private static List<string> ParseGroupFilters(string groupExpression)
        {
            return ParseExpressionParts(groupExpression, '|');
        }

        /// <summary>
        /// Parses label arguments into OR alternatives, each containing its required AND labels.
        /// </summary>
        /// <param name="labelExpressions">The raw label expression arguments.</param>
        /// <returns>Valid label conditions; an empty list means Label is unrestricted.</returns>
        private static List<LabelFilterCondition> ParseLabelFilters(IReadOnlyList<string> labelExpressions)
        {
            List<LabelFilterCondition> conditions = new List<LabelFilterCondition>();
            if (labelExpressions == null)
            {
                return conditions;
            }

            // 每个参数中的 | 产生 OR 分支，分支中的 & 产生必须同时存在的标签。
            for (int i = 0; i < labelExpressions.Count; i++)
            {
                List<string> alternatives = ParseExpressionParts(labelExpressions[i], '|');
                for (int j = 0; j < alternatives.Count; j++)
                {
                    List<string> requiredLabels = ParseExpressionParts(alternatives[j], '&');
                    if (requiredLabels.Count > 0)
                    {
                        conditions.Add(new LabelFilterCondition(requiredLabels));
                    }
                }
            }

            return conditions;
        }

        /// <summary>
        /// Splits one expression by an operator and removes whitespace-only parts.
        /// </summary>
        /// <param name="expression">The raw expression to split.</param>
        /// <param name="separator">The expression operator.</param>
        /// <returns>Trimmed, non-empty expression parts.</returns>
        private static List<string> ParseExpressionParts(string expression, char separator)
        {
            List<string> parts = new List<string>();
            if (string.IsNullOrWhiteSpace(expression))
            {
                return parts;
            }

            string[] values = expression.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < values.Length; i++)
            {
                string value = values[i].Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    parts.Add(value);
                }
            }

            return parts;
        }

        /// <summary>
        /// Determines whether a group satisfies at least one configured group alternative.
        /// </summary>
        /// <param name="group">The Addressables group to test.</param>
        /// <param name="groupFilters">The parsed group alternatives.</param>
        /// <returns><see langword="true"/> when Group is unrestricted or matches an alternative.</returns>
        private static bool MatchesGroup(AddressableAssetGroup group, IReadOnlyList<string> groupFilters)
        {
            if (groupFilters == null || groupFilters.Count == 0)
            {
                return true;
            }

            for (int i = 0; i < groupFilters.Count; i++)
            {
                if (group.Name == groupFilters[i])
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Determines whether an entry satisfies at least one complete label condition.
        /// </summary>
        /// <param name="entry">The Addressables entry to test.</param>
        /// <param name="labelFilters">The parsed OR conditions whose labels are ANDed within each condition.</param>
        /// <returns><see langword="true"/> when Label is unrestricted or one condition is fully satisfied.</returns>
        private static bool MatchesLabels(AddressableAssetEntry entry, IReadOnlyList<LabelFilterCondition> labelFilters)
        {
            if (labelFilters == null || labelFilters.Count == 0)
            {
                return true;
            }

            for (int i = 0; i < labelFilters.Count; i++)
            {
                LabelFilterCondition condition = labelFilters[i];
                bool matchesCondition = true;
                for (int j = 0; j < condition.RequiredLabels.Count; j++)
                {
                    if (entry.labels == null || !entry.labels.Contains(condition.RequiredLabels[j]))
                    {
                        matchesCondition = false;
                        break;
                    }
                }

                if (matchesCondition)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Creates the tooltip shown for one Addressables entry.
        /// </summary>
        /// <param name="entry">The Addressables entry represented by the option.</param>
        /// <returns>A tooltip containing path, GUID, and labels.</returns>
        private static string CreateTooltip(AddressableAssetEntry entry)
        {
            string labels = entry.labels == null || entry.labels.Count == 0
                ? "None"
                : string.Join(", ", entry.labels);

            return $"Path: {entry.AssetPath}\nGUID: {entry.guid}\nLabels: {labels}";
        }

        #endregion

        #region 筛选结果数据

        /// <summary>
        /// Stores the labels that must all exist for one Label OR branch.
        /// </summary>
        private readonly struct LabelFilterCondition
        {
            /// <summary>
            /// Initializes a label condition with its required labels.
            /// </summary>
            /// <param name="requiredLabels">The labels that must all be present.</param>
            public LabelFilterCondition(IReadOnlyList<string> requiredLabels)
            {
                RequiredLabels = requiredLabels;
            }

            /// <summary>
            /// Gets the labels required by this condition.
            /// </summary>
            public IReadOnlyList<string> RequiredLabels { get; }
        }

        /// <summary>
        /// Stores the display data for one selectable Addressables entry.
        /// </summary>
        private readonly struct AddressableKeyOption
        {
            /// <summary>
            /// Initializes an Addressables popup option.
            /// </summary>
            /// <param name="address">The entry address.</param>
            /// <param name="groupName">The owning group name.</param>
            /// <param name="tooltip">The tooltip shown for the option.</param>
            public AddressableKeyOption(string address, string groupName, string tooltip)
            {
                Address = address;
                GroupName = groupName;
                Tooltip = tooltip;
            }

            /// <summary>
            /// Gets the entry address written to the serialized field.
            /// </summary>
            public string Address { get; }

            /// <summary>
            /// Gets the owning Addressables group name.
            /// </summary>
            public string GroupName { get; }

            /// <summary>
            /// Gets the tooltip text for the option.
            /// </summary>
            public string Tooltip { get; }
        }

        #endregion
    }
}
