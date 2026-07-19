using System.IO;
using UnityEditor;
using UnityEngine;

namespace WS_Modules
{
    /// <summary>
    /// WSFolderPathAttribute 的编辑器绘制器，提供文件夹选择按钮并回填项目相对路径。
    /// </summary>
    [CustomPropertyDrawer(typeof(WSFolderPathAttribute))]
    internal sealed class WSFolderPathDrawer : PropertyDrawer
    {
        private const float FolderButtonWidth = 28f;
        private const float ButtonSpacing = 2f;
        private const float VerticalSpacing = 2f;
        private const float ArrayElementIndent = 16f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (IsStringArray(property))
            {
                DrawFolderPathArray(position, property, label);
                return;
            }

            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label, true);
                return;
            }

            DrawFolderPathField(position, property, label);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (IsStringArray(property))
            {
                return GetFolderPathArrayHeight(property);
            }

            return EditorGUI.GetPropertyHeight(property, label, true);
        }

        private static bool IsStringArray(SerializedProperty property)
        {
            return property.isArray &&
                   property.propertyType == SerializedPropertyType.Generic &&
                   (property.arraySize == 0 ||
                    property.GetArrayElementAtIndex(0).propertyType == SerializedPropertyType.String);
        }

        private static void DrawFolderPathArray(Rect position, SerializedProperty property, GUIContent label)
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
                    EditorGUIUtility.singleLineHeight);
                DrawFolderPathField(elementRect, elementProperty, new GUIContent($"Element {i}"));
                currentY += EditorGUIUtility.singleLineHeight + VerticalSpacing;
            }

            EditorGUI.indentLevel--;
        }

        private static float GetFolderPathArrayHeight(SerializedProperty property)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded)
            {
                return height;
            }

            height += VerticalSpacing;
            height += EditorGUIUtility.singleLineHeight + VerticalSpacing;
            height += property.arraySize * (EditorGUIUtility.singleLineHeight + VerticalSpacing);
            return height;
        }

        private static void DrawFolderPathField(Rect position, SerializedProperty property, GUIContent label)
        {
            Rect fieldRect = new Rect(
                position.x,
                position.y,
                position.width - FolderButtonWidth - ButtonSpacing,
                position.height);
            Rect buttonRect = new Rect(
                fieldRect.xMax + ButtonSpacing,
                position.y,
                FolderButtonWidth,
                EditorGUIUtility.singleLineHeight);

            EditorGUI.PropertyField(fieldRect, property, label);
            if (!GUI.Button(buttonRect, "..."))
            {
                return;
            }

            string selectedPath = EditorUtility.OpenFolderPanel(
                "选择文件夹",
                GetFolderPanelStartPath(property.stringValue),
                string.Empty);
            if (!string.IsNullOrEmpty(selectedPath))
            {
                property.stringValue = ConvertToProjectRelativePath(selectedPath);
            }
        }

        private static string GetFolderPanelStartPath(string currentPath)
        {
            if (string.IsNullOrEmpty(currentPath))
            {
                return Application.dataPath;
            }

            if (Path.IsPathRooted(currentPath))
            {
                return currentPath;
            }

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            return string.IsNullOrEmpty(projectRoot)
                ? Application.dataPath
                : Path.Combine(projectRoot, currentPath);
        }

        private static string ConvertToProjectRelativePath(string absolutePath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
            {
                return absolutePath;
            }

            string normalizedAbsolutePath = absolutePath.Replace("\\", "/");
            string normalizedProjectRoot = projectRoot.Replace("\\", "/");
            if (!normalizedProjectRoot.EndsWith("/"))
            {
                normalizedProjectRoot += "/";
            }

            return normalizedAbsolutePath.StartsWith(normalizedProjectRoot)
                ? normalizedAbsolutePath.Substring(normalizedProjectRoot.Length)
                : absolutePath;
        }
    }
}
