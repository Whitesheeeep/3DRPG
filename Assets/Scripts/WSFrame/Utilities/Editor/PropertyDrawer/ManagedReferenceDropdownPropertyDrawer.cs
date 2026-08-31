#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace WS_Modules.Utilities.Editor
{
    /// <summary>
    /// 为 SerializeReference 基类提供派生类型下拉选择、折叠状态和直接子字段绘制。
    /// </summary>
    /// <typeparam name="TBase">managed reference 所使用的抽象基类或接口。</typeparam>
    public abstract class ManagedReferenceDropdownPropertyDrawer<TBase> : PropertyDrawer
        where TBase : class
    {
        #region 常量

        private const float Spacing = 2f;

        #endregion

        #region 可覆写配置

        /// <summary>
        /// 获取切换 managed reference 类型时使用的 Undo 操作名称。
        /// </summary>
        protected virtual string UndoActionName => "Change Managed Reference Type";

        /// <summary>
        /// 获取候选类型在下拉菜单中的显示名称。
        /// </summary>
        /// <param name="type">候选派生类型。</param>
        /// <returns>显示在菜单中的类型名称。</returns>
        protected virtual string GetTypeDisplayName(Type type)
        {
            return ObjectNames.NicifyVariableName(type.Name);
        }

        /// <summary>
        /// 在默认类型合法性规则通过后进一步筛选候选类型。
        /// </summary>
        /// <param name="type">待筛选的候选派生类型。</param>
        /// <returns>类型应该显示在菜单中时返回 true。</returns>
        protected virtual bool IsSelectableType(Type type)
        {
            return true;
        }

        #endregion

        #region 属性绘制

        /// <summary>
        /// 绘制类型选择首行及当前 managed reference 的直接子字段。
        /// </summary>
        /// <param name="position">属性在 Inspector 中的绘制区域。</param>
        /// <param name="property">当前 SerializeReference 属性。</param>
        /// <param name="label">属性标签。</param>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

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
            string typeName = managedType == null ? "None" : GetTypeDisplayName(managedType);
            if (EditorGUI.DropdownButton(buttonRect, new GUIContent(typeName), FocusType.Keyboard))
            {
                ShowTypeMenu(property, buttonRect);
            }

            if (property.isExpanded && property.managedReferenceValue != null)
            {
                // 只增加一层缩进，直接子字段内部的嵌套绘制由 Unity 自己处理。
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

            EditorGUI.EndProperty();
        }

        /// <summary>
        /// 计算类型选择首行及当前直接子字段所需的总高度。
        /// </summary>
        /// <param name="property">当前 SerializeReference 属性。</param>
        /// <param name="label">属性标签。</param>
        /// <returns>属性在 Inspector 中所需的高度。</returns>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded || property.managedReferenceValue == null)
            {
                return height;
            }

            VisitDirectChildren(property, child =>
                height += Spacing + EditorGUI.GetPropertyHeight(child, true));
            return height;
        }

        #endregion

        #region 类型菜单

        /// <summary>
        /// 显示当前泛型基类的可实例化派生类型菜单。
        /// </summary>
        /// <param name="property">当前 SerializeReference 属性。</param>
        private void ShowTypeMenu(SerializedProperty property, Rect buttonRect)
        {
            UnityEngine.Object target = property.serializedObject.targetObject;
            string propertyPath = property.propertyPath;
            Type currentType = property.managedReferenceValue?.GetType();
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("None"), currentType == null,
                () => SetManagedReference(target, propertyPath, null));
            menu.AddSeparator(string.Empty);

            List<Type> candidateTypes = FindSelectableTypes();
            for (int index = 0; index < candidateTypes.Count; index++)
            {
                Type candidateType = candidateTypes[index];
                menu.AddItem(
                    new GUIContent(GetTypeDisplayName(candidateType)),
                    currentType == candidateType,
                    () => SetManagedReference(target, propertyPath, candidateType));
            }

            menu.DropDown(buttonRect);
        }

        /// <summary>
        /// 查找当前泛型基类下公开且可由无参构造函数创建的派生类型。
        /// </summary>
        /// <returns>按完整类型名排序并通过扩展筛选的候选类型。</returns>
        private List<Type> FindSelectableTypes()
        {
            List<Type> results = new List<Type>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<TBase>())
            {
                if (!IsDefaultSelectableType(type) || !IsSelectableType(type))
                {
                    continue;
                }

                results.Add(type);
            }

            results.Sort((left, right) =>
                string.Compare(left.FullName, right.FullName, StringComparison.Ordinal));
            return results;
        }

        /// <summary>
        /// 判断类型是否满足通用的 Unity managed reference 创建约束。
        /// </summary>
        /// <param name="type">待判断的候选类型。</param>
        /// <returns>类型公开、具体、非泛型且具有公开无参构造函数时返回 true。</returns>
        private static bool IsDefaultSelectableType(Type type)
        {
            return (type.IsPublic || type.IsNestedPublic) &&
                !type.IsAbstract &&
                !type.IsGenericType &&
                type.GetConstructor(Type.EmptyTypes) != null;
        }

        /// <summary>
        /// 通过重新取得 SerializedObject 替换 managed reference，并记录一次可撤销的类型变更。
        /// </summary>
        /// <param name="target">包含属性的 Unity 对象。</param>
        /// <param name="propertyPath">属性在目标对象中的路径。</param>
        /// <param name="definitionType">待创建的派生类型；为空表示清空引用。</param>
        private void SetManagedReference(
            UnityEngine.Object target,
            string propertyPath,
            Type definitionType)
        {
            // 菜单回调执行时原 SerializedProperty 可能已失效，因此按目标和路径重新定位属性。
            Undo.RecordObject(target, UndoActionName);
            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyPath);
            if (property == null)
            {
                return;
            }

            property.managedReferenceValue = definitionType == null
                ? null
                : Activator.CreateInstance(definitionType);
            property.isExpanded = definitionType != null;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }

        #endregion

        #region 属性遍历

        /// <summary>
        /// 只遍历当前 managed reference 的直接子字段，避免重复绘制嵌套字段。
        /// </summary>
        /// <param name="property">当前 managed reference 属性。</param>
        /// <param name="visitor">每个直接子字段的访问回调。</param>
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
                if (iterator.depth == childDepth)
                {
                    visitor(iterator.Copy());
                }
            }
        }

        #endregion
    }
}
#endif
