#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using RPG.DialogueSystem;

namespace RPG.DialogueSystem.Editor
{
    /// <summary>
    /// 为 Dialogue 的 SerializeReference 定义提供统一的派生类型选择和子字段绘制。
    /// </summary>
    internal abstract class DialogueDefinitionPropertyDrawer : PropertyDrawer
    {
        #region 常量

        private const float Spacing = 2f;

        #endregion

        #region 抽象配置

        /// <summary>获取当前 Drawer 对应的定义基类。</summary>
        protected abstract Type DefinitionBaseType { get; }

        #endregion

        #region 属性绘制

        /// <inheritdoc />
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            Rect line = new Rect(position.x, position.y, position.width,
                EditorGUIUtility.singleLineHeight);
            Rect foldoutRect = new Rect(line.x, line.y, EditorGUIUtility.labelWidth, line.height);
            Rect buttonRect = new Rect(
                line.x + EditorGUIUtility.labelWidth,
                line.y,
                line.width - EditorGUIUtility.labelWidth,
                line.height);

            property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label, true);
            Type managedType = property.managedReferenceValue?.GetType();
            string typeName = managedType == null ? "None" : ObjectNames.NicifyVariableName(managedType.Name);
            if (EditorGUI.DropdownButton(buttonRect, new GUIContent(typeName), FocusType.Keyboard))
                ShowTypeMenu(property);

            if (!property.isExpanded || property.managedReferenceValue == null) return;
            EditorGUI.indentLevel++;
            float y = line.yMax + Spacing;
            VisitDirectChildren(property, child =>
            {
                float height = EditorGUI.GetPropertyHeight(child, true);
                EditorGUI.PropertyField(new Rect(position.x, y, position.width, height), child, true);
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

        /// <summary>显示当前定义基类的具体可实例化类型菜单。</summary>
        /// <param name="property">当前 SerializeReference 属性。</param>
        private void ShowTypeMenu(SerializedProperty property)
        {
            UnityEngine.Object target = property.serializedObject.targetObject;
            string propertyPath = property.propertyPath;
            Type currentType = property.managedReferenceValue?.GetType();
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("None"), currentType == null,
                () => SetManagedReference(target, propertyPath, null));
            menu.AddSeparator(string.Empty);

            List<Type> definitionTypes = FindDefinitionTypes(DefinitionBaseType);
            for (int index = 0; index < definitionTypes.Count; index++)
            {
                Type definitionType = definitionTypes[index];
                menu.AddItem(
                    new GUIContent(ObjectNames.NicifyVariableName(definitionType.Name)),
                    currentType == definitionType,
                    () => SetManagedReference(target, propertyPath, definitionType));
            }

            menu.ShowAsContext();
        }

        /// <summary>查找公开、非抽象且拥有无参构造函数的派生定义。</summary>
        /// <param name="baseType">定义基类。</param>
        /// <returns>按完整类型名排序的可实例化类型。</returns>
        private static List<Type> FindDefinitionTypes(Type baseType)
        {
            List<Type> results = new List<Type>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom(baseType))
            {
                if ((type.IsPublic || type.IsNestedPublic) &&
                    !type.IsAbstract && !type.IsGenericType &&
                    type.GetConstructor(Type.EmptyTypes) != null)
                    results.Add(type);
            }

            results.Sort((left, right) =>
                string.Compare(left.FullName, right.FullName, StringComparison.Ordinal));
            return results;
        }

        /// <summary>通过重新取得 SerializedObject 替换一个 managed reference 并记录 Undo。</summary>
        /// <param name="target">包含属性的 Unity 对象。</param>
        /// <param name="propertyPath">属性路径。</param>
        /// <param name="definitionType">待创建的定义类型；为空表示清空。</param>
        private static void SetManagedReference(
            UnityEngine.Object target,
            string propertyPath,
            Type definitionType)
        {
            Undo.RecordObject(target, "Change Dialogue Definition");
            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyPath);
            if (property == null) return;

            property.managedReferenceValue = definitionType == null
                ? null
                : Activator.CreateInstance(definitionType);
            property.isExpanded = definitionType != null;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }

        #endregion

        #region 属性遍历

        /// <summary>只遍历当前 managed reference 的直接子字段。</summary>
        /// <param name="property">当前 managed reference 属性。</param>
        /// <param name="visitor">子字段访问回调。</param>
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

        #endregion
    }

    /// <summary>绘制 Dialogue Condition 派生定义。</summary>
    [CustomPropertyDrawer(typeof(DialogueCondition), true)]
    internal sealed class DialogueConditionDefinitionPropertyDrawer : DialogueDefinitionPropertyDrawer
    {
        #region 配置

        /// <summary>获取 Condition 定义基类。</summary>
        protected override Type DefinitionBaseType => typeof(DialogueCondition);

        #endregion
    }

    /// <summary>绘制 Dialogue Action 派生定义。</summary>
    [CustomPropertyDrawer(typeof(DialogueAction), true)]
    internal sealed class DialogueActionDefinitionPropertyDrawer : DialogueDefinitionPropertyDrawer
    {
        #region 配置

        /// <summary>获取 Action 定义基类。</summary>
        protected override Type DefinitionBaseType => typeof(DialogueAction);

        #endregion
    }
}
#endif
