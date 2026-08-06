#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using WS_Modules.GAS.GameplayAbilitySystem;

namespace WS_Modules.GAS.Editor
{
    /// <summary>为 SerializeReference Task Definition 提供类型选择、清空与递归字段编辑。</summary>
    [CustomPropertyDrawer(typeof(GameplayAbilityTaskConfig), true)]
    public sealed class GameplayAbilityTaskDefinitionPropertyDrawer : PropertyDrawer
    {
        #region 常量
        private const float Spacing = 2f;
        #endregion

        #region 绘制
        /// <inheritdoc />
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            Rect line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            Rect foldoutRect = new Rect(line.x, line.y, EditorGUIUtility.labelWidth, line.height);
            Rect buttonRect = new Rect(
                line.x + EditorGUIUtility.labelWidth,
                line.y,
                line.width - EditorGUIUtility.labelWidth,
                line.height);

            property.isExpanded = EditorGUI.Foldout(
                foldoutRect,
                property.isExpanded,
                label,
                true);
            string typeName = GetManagedType(property)?.Name ?? "None";
            if (EditorGUI.DropdownButton(buttonRect, new GUIContent(typeName), FocusType.Keyboard))
                ShowTypeMenu(property);

            if (!property.isExpanded || property.managedReferenceValue == null) return;
            EditorGUI.indentLevel++;
            float y = line.yMax + Spacing;
            VisitDirectChildren(property, child =>
            {
                float height = EditorGUI.GetPropertyHeight(child, true);
                EditorGUI.PropertyField(
                    new Rect(position.x, y, position.width, height),
                    child,
                    true);
                y += height + Spacing;
            });
            EditorGUI.indentLevel--;
        }

        /// <inheritdoc />
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded || property.managedReferenceValue == null) return height;
            VisitDirectChildren(property, child =>
                height += Spacing + EditorGUI.GetPropertyHeight(child, true));
            return height;
        }
        #endregion

        #region 类型菜单
        // 菜单回调重新取得 SerializedProperty，避免菜单关闭前持有失效属性。
        private static void ShowTypeMenu(SerializedProperty property)
        {
            UnityEngine.Object target = property.serializedObject.targetObject;
            string propertyPath = property.propertyPath;
            Type currentType = GetManagedType(property);
            var menu = new GenericMenu();
            menu.AddItem(
                new GUIContent("None"),
                currentType == null,
                () => SetManagedReference(target, propertyPath, null));
            menu.AddSeparator(string.Empty);

            List<Type> types = FindDefinitionTypes();
            for (int i = 0; i < types.Count; i++)
            {
                Type type = types[i];
                menu.AddItem(
                    new GUIContent(ObjectNames.NicifyVariableName(type.Name)),
                    currentType == type,
                    () => SetManagedReference(target, propertyPath, type));
            }
            menu.ShowAsContext();
        }

        // TypeCache 只返回可由 Activator 创建的公开具体 Definition。
        private static List<Type> FindDefinitionTypes()
        {
            var results = new List<Type>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<GameplayAbilityTaskConfig>())
                if ((type.IsPublic || type.IsNestedPublic) &&
                    !type.IsAbstract && !type.IsGenericType &&
                    type.GetConstructor(Type.EmptyTypes) != null)
                    results.Add(type);
            results.Sort((left, right) =>
                string.Compare(left.FullName, right.FullName, StringComparison.Ordinal));
            return results;
        }

        // 通过目标对象与路径重建序列化上下文，并用一个 Undo 替换 managed reference。
        private static void SetManagedReference(
            UnityEngine.Object target,
            string propertyPath,
            Type type)
        {
            Undo.RecordObject(target, "Change Gameplay Ability Task");
            var serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyPath);
            property.managedReferenceValue = type == null ? null : Activator.CreateInstance(type);
            property.isExpanded = type != null;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }
        #endregion

        #region 属性遍历
        // 只访问 managed reference 的直接字段，嵌套 Definition 继续交给各自 Drawer。
        private static void VisitDirectChildren(
            SerializedProperty property,
            Action<SerializedProperty> visitor)
        {
            SerializedProperty iterator = property.Copy();
            SerializedProperty end = iterator.GetEndProperty();
            int childDepth = property.depth + 1;
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren) &&
                   !SerializedProperty.EqualContents(iterator, end))
            {
                enterChildren = false;
                if (iterator.depth == childDepth) visitor(iterator.Copy());
            }
        }

        // managedReferenceValue 是序列化边界，这里只读取其真实运行类型用于菜单勾选。
        private static Type GetManagedType(SerializedProperty property) =>
            property.managedReferenceValue?.GetType();
        #endregion
    }
}
#endif
