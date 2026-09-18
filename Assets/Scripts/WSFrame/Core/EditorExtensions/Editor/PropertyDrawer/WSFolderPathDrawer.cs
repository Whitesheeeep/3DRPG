using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WS_Modules
{
    /// <summary>
    /// WSFolderPathAttribute 的编辑器绘制器，提供文件夹选择按钮并回填 Assets 相对路径。
    /// </summary>
    [CustomPropertyDrawer(typeof(WSFolderPathAttribute))]
    internal sealed class WSFolderPathDrawer : PropertyDrawer
    {
        private const float FolderButtonWidth = 28f;
        private const float ButtonSpacing = 2f;
        private const float VerticalSpacing = 2f;
        private const float ArrayElementIndent = 16f;

        /// <summary>
        /// 绘制单个路径或路径数组，并在用户提交时统一规范化路径。
        /// </summary>
        /// <param name="position">属性绘制区域。</param>
        /// <param name="property">待绘制的序列化属性。</param>
        /// <param name="label">属性标签。</param>
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

        /// <summary>计算路径属性或路径数组所需的绘制高度。</summary>
        /// <param name="property">待计算的序列化属性。</param>
        /// <param name="label">属性标签。</param>
        /// <returns>属性绘制高度。</returns>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (IsStringArray(property))
            {
                return GetFolderPathArrayHeight(property);
            }

            return EditorGUI.GetPropertyHeight(property, label, true);
        }

        /// <summary>判断序列化属性是否为字符串数组。</summary>
        /// <param name="property">待判断的序列化属性。</param>
        /// <returns>属性是字符串数组时返回 true。</returns>
        private static bool IsStringArray(SerializedProperty property)
        {
            return property.isArray &&
                   property.propertyType == SerializedPropertyType.Generic &&
                   string.Equals(property.arrayElementType, "string", StringComparison.Ordinal);
        }

        /// <summary>绘制可展开的字符串路径数组。</summary>
        /// <param name="position">数组绘制区域。</param>
        /// <param name="property">字符串数组序列化属性。</param>
        /// <param name="label">数组属性标签。</param>
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

        /// <summary>计算字符串路径数组的绘制高度。</summary>
        /// <param name="property">字符串数组序列化属性。</param>
        /// <returns>数组绘制高度。</returns>
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

        /// <summary>
        /// 绘制单个路径输入框，并把手动输入和选择器结果转换为 Assets 相对路径。
        /// </summary>
        /// <param name="position">字段绘制区域。</param>
        /// <param name="property">字符串路径序列化属性。</param>
        /// <param name="label">字段标签。</param>
        private static void DrawFolderPathField(Rect position, SerializedProperty property, GUIContent label)
        {
            NormalizeStoredValue(property);
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

            string previousValue = property.stringValue;
            EditorGUI.BeginChangeCheck();
            string editedValue = EditorGUI.DelayedTextField(fieldRect, label, previousValue);
            if (EditorGUI.EndChangeCheck())
            {
                if (TryNormalizeAssetsRelativePath(editedValue, out string normalizedEditedValue))
                {
                    property.stringValue = normalizedEditedValue;
                }
                else
                {
                    property.stringValue = previousValue;
                    Debug.LogWarning(
                        $"[WSFolderPath] 已拒绝非法目录路径：{editedValue}。路径必须位于当前项目 Assets 目录内。");
                }
            }

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
                if (TryNormalizeAssetsRelativePath(selectedPath, out string normalizedSelectedPath))
                {
                    property.stringValue = normalizedSelectedPath;
                }
                else
                {
                    Debug.LogWarning(
                        $"[WSFolderPath] 选择的目录不在当前项目 Assets 内，已忽略：{selectedPath}");
                }
            }
        }

        /// <summary>把已保存的旧路径值迁移为 Assets 相对路径。</summary>
        /// <param name="property">待迁移的字符串路径属性。</param>
        private static void NormalizeStoredValue(SerializedProperty property)
        {
            if (string.IsNullOrWhiteSpace(property.stringValue))
            {
                return;
            }

            if (TryNormalizeAssetsRelativePath(property.stringValue, out string normalizedPath) &&
                !string.Equals(property.stringValue, normalizedPath, StringComparison.Ordinal))
            {
                property.stringValue = normalizedPath;
            }
        }

        /// <summary>取得文件夹选择器应打开的初始绝对路径。</summary>
        /// <param name="currentPath">当前保存的路径值。</param>
        /// <returns>合法时返回对应绝对路径，否则返回 Assets 根目录。</returns>
        private static string GetFolderPanelStartPath(string currentPath)
        {
            if (!TryNormalizeAssetsRelativePath(currentPath, out string normalizedPath))
            {
                return Application.dataPath;
            }

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            return string.IsNullOrEmpty(projectRoot)
                ? Application.dataPath
                : Path.Combine(projectRoot, normalizedPath);
        }

        /// <summary>
        /// 将用户输入或选择的路径规范化为 Assets 相对路径。
        /// </summary>
        /// <param name="path">绝对路径或 Assets 相对路径。</param>
        /// <param name="normalizedPath">规范化后的 Assets 路径。</param>
        /// <returns>路径位于当前项目 Assets 内且格式有效时返回 true。</returns>
        private static bool TryNormalizeAssetsRelativePath(string path, out string normalizedPath)
        {
            normalizedPath = string.Empty;
            string trimmedPath = (path ?? string.Empty).Trim().Replace('\\', '/');
            if (string.IsNullOrEmpty(trimmedPath))
            {
                return false;
            }

            string assetsPath = GetNormalizedFullPath(Application.dataPath);
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(assetsPath) || string.IsNullOrEmpty(projectRoot))
            {
                return false;
            }

            try
            {
                string absolutePath = Path.IsPathRooted(trimmedPath)
                    ? Path.GetFullPath(trimmedPath)
                    : Path.GetFullPath(Path.Combine(projectRoot, trimmedPath));
                if (!IsPathInsideOrEqual(absolutePath, assetsPath))
                {
                    return false;
                }

                string relativePath = absolutePath.Substring(assetsPath.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Replace('\\', '/');
                normalizedPath = string.IsNullOrEmpty(relativePath)
                    ? "Assets"
                    : $"Assets/{relativePath}";
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (NotSupportedException)
            {
                return false;
            }
        }

        /// <summary>获取去除尾部分隔符的规范化绝对路径。</summary>
        /// <param name="path">待规范化路径。</param>
        /// <returns>规范化绝对路径；输入无效时返回空字符串。</returns>
        private static string GetNormalizedFullPath(string path)
        {
            try
            {
                return Path.GetFullPath(path)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
            catch (NotSupportedException)
            {
                return string.Empty;
            }
        }

        /// <summary>判断候选路径是否等于基准目录或位于其子目录中。</summary>
        /// <param name="candidatePath">待判断的绝对路径。</param>
        /// <param name="basePath">基准目录绝对路径。</param>
        /// <returns>路径边界合法时返回 true。</returns>
        private static bool IsPathInsideOrEqual(string candidatePath, string basePath)
        {
            string normalizedCandidate = candidatePath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            string normalizedBase = basePath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            return string.Equals(normalizedCandidate, normalizedBase, StringComparison.OrdinalIgnoreCase) ||
                   normalizedCandidate.StartsWith(
                       normalizedBase + Path.DirectorySeparatorChar,
                       StringComparison.OrdinalIgnoreCase) ||
                   normalizedCandidate.StartsWith(
                       normalizedBase + Path.AltDirectorySeparatorChar,
                       StringComparison.OrdinalIgnoreCase);
        }
    }
}
