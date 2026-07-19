using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using WS_Modules.UIModule;

namespace WS_Modules
{
    /// <summary>
    /// Shared UIManagerSetting drawer used by the FrameSetting inspector and FrameSettingWindow.
    /// </summary>
    [CustomPropertyDrawer(typeof(UIManagerSetting))]
    internal sealed class UIManagerSettingDrawer : PropertyDrawer
    {
        private const float VerticalSpacing = 2f;
        private readonly HashSet<string> initializedExpandedProperties = new HashSet<string>();

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EnsureDefaultExpanded(property);
            EditorGUI.BeginProperty(position, label, property);

            Rect foldoutRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label, true);

            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                float currentY = foldoutRect.yMax + VerticalSpacing;

                DrawProperty(ref currentY, position, property, "uiRootPath", "UI Root Path");
                DrawProperty(ref currentY, position, property, "uiCameraPrefabPath", "UI Camera Path");
                DrawProperty(ref currentY, position, property, "uiEventSystemPrefabPath", "UI EventSystem Path");
                DrawProperty(ref currentY, position, property, "windowConfig", "Window Config");
                DrawProperty(ref currentY, position, property, "isSingleMask", "Single Mask");
                DrawProperty(ref currentY, position, property, "BindComponentGeneratorPath", "Bind Component Output");
                DrawProperty(ref currentY, position, property, "BindComponentNameSpace", "Bind Component Namespace");
                DrawProperty(ref currentY, position, property, "WindowGeneratorPath", "Window Code Output");
                DrawProperty(ref currentY, position, property, "ItemScriptsGeneratorPath", "Item Script Output");
                DrawProperty(ref currentY, position, property, "WindowPrefabFolderPathArr", "Window Prefab Folders");
                DrawProperty(ref currentY, position, property, "UsingNameSpaceArr", "Using Namespaces");

                EditorGUI.indentLevel--;
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            EnsureDefaultExpanded(property);
            float height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded)
            {
                return height;
            }

            height += VerticalSpacing;
            height += GetPropertyHeight(property, "uiRootPath", "UI Root Path");
            height += GetPropertyHeight(property, "uiCameraPrefabPath", "UI Camera Path");
            height += GetPropertyHeight(property, "uiEventSystemPrefabPath", "UI EventSystem Path");
            height += GetPropertyHeight(property, "windowConfig", "Window Config");
            height += GetPropertyHeight(property, "isSingleMask", "Single Mask");
            height += GetPropertyHeight(property, "BindComponentGeneratorPath", "Bind Component Output");
            height += GetPropertyHeight(property, "BindComponentNameSpace", "Bind Component Namespace");
            height += GetPropertyHeight(property, "WindowGeneratorPath", "Window Code Output");
            height += GetPropertyHeight(property, "ItemScriptsGeneratorPath", "Item Script Output");
            height += GetPropertyHeight(property, "WindowPrefabFolderPathArr", "Window Prefab Folders");
            height += GetPropertyHeight(property, "UsingNameSpaceArr", "Using Namespaces");
            return height;
        }

        private void EnsureDefaultExpanded(SerializedProperty property)
        {
            string key = GetPropertyKey(property);
            if (!initializedExpandedProperties.Add(key))
            {
                return;
            }

            property.isExpanded = true;
        }

        private static string GetPropertyKey(SerializedProperty property)
        {
            UnityEngine.Object targetObject = property.serializedObject.targetObject;
            int targetId = targetObject != null ? targetObject.GetInstanceID() : 0;
            return $"{targetId}:{property.propertyPath}";
        }

        private static void DrawProperty(
            ref float currentY,
            Rect position,
            SerializedProperty rootProperty,
            string relativePropertyName,
            string labelText)
        {
            SerializedProperty childProperty = rootProperty.FindPropertyRelative(relativePropertyName);
            if (childProperty == null)
            {
                return;
            }

            GUIContent childLabel = CreateLabel(relativePropertyName, labelText);
            float height = EditorGUI.GetPropertyHeight(childProperty, childLabel, true);
            Rect propertyRect = new Rect(position.x, currentY, position.width, height);
            EditorGUI.PropertyField(propertyRect, childProperty, childLabel, true);
            currentY += height + VerticalSpacing;
        }

        private static float GetPropertyHeight(SerializedProperty rootProperty, string relativePropertyName, string labelText)
        {
            SerializedProperty childProperty = rootProperty.FindPropertyRelative(relativePropertyName);
            if (childProperty == null)
            {
                return 0f;
            }

            return EditorGUI.GetPropertyHeight(childProperty, CreateLabel(relativePropertyName, labelText), true) + VerticalSpacing;
        }

        private static GUIContent CreateLabel(string relativePropertyName, string labelText)
        {
            FieldInfo field = typeof(UIManagerSetting).GetField(
                relativePropertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            TooltipAttribute tooltipAttribute = field?.GetCustomAttribute<TooltipAttribute>();
            return new GUIContent(labelText, tooltipAttribute?.tooltip);
        }
    }
}
