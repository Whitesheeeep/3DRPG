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
        private const float VerticalSpacing = 2f;
        private const float ArrayElementIndent = 16f;
        private const string NoneLabel = "None";
        private const string UnsupportedTypeMessage = "[WSAddressableKey] only supports string, string[], or List<string> fields.";
        private const string MissingSettingsMessage = "Addressables Settings have not been created.";
        private const string EmptyOptionsMessage = "No Addressables entries match the current group and label filters.";

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

        private static List<AddressableKeyOption> GetAddressableKeyOptions(WSAddressableKeyAttribute keyAttribute)
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            List<AddressableKeyOption> options = new List<AddressableKeyOption>();
            if (settings == null)
            {
                return options;
            }

            string groupFilter = keyAttribute.GroupName ?? string.Empty;
            string[] labelFilters = keyAttribute.Labels ?? new string[0];

            foreach (AddressableAssetGroup group in settings.groups)
            {
                if (group == null || !MatchesGroup(group, groupFilter))
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

        private static bool MatchesGroup(AddressableAssetGroup group, string groupFilter)
        {
            return string.IsNullOrWhiteSpace(groupFilter) || group.Name == groupFilter;
        }

        private static bool MatchesLabels(AddressableAssetEntry entry, IReadOnlyList<string> labelFilters)
        {
            for (int i = 0; i < labelFilters.Count; i++)
            {
                string label = labelFilters[i];
                if (string.IsNullOrWhiteSpace(label))
                {
                    continue;
                }

                if (!entry.labels.Contains(label))
                {
                    return false;
                }
            }

            return true;
        }

        private static string CreateTooltip(AddressableAssetEntry entry)
        {
            string labels = entry.labels == null || entry.labels.Count == 0
                ? "None"
                : string.Join(", ", entry.labels);

            return $"Path: {entry.AssetPath}\nGUID: {entry.guid}\nLabels: {labels}";
        }

        private readonly struct AddressableKeyOption
        {
            public AddressableKeyOption(string address, string groupName, string tooltip)
            {
                Address = address;
                GroupName = groupName;
                Tooltip = tooltip;
            }

            public string Address { get; }

            public string GroupName { get; }

            public string Tooltip { get; }
        }
    }
}
