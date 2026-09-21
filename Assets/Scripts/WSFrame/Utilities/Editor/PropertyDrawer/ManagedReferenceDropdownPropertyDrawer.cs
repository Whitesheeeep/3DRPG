#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace WS_Modules.Utilities.Editor
{
    /// <summary>
    /// 为 SerializeReference 基类提供派生类型下拉选择、折叠状态、直接子字段和可选状态提示绘制。
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
        /// 获取候选类型在下拉菜单和标题中的显示名称。
        /// </summary>
        /// <param name="type">候选派生类型。</param>
        /// <returns>显示在菜单和标题中的类型名称。</returns>
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

        /// <summary>
        /// 获取 managed reference 类型丢失时标题按钮显示的文本。
        /// 默认值保持旧 Drawer 将该状态显示为 None 的兼容行为。
        /// </summary>
        protected virtual string MissingTypeDisplayName => "None";

        /// <summary>
        /// 获取 managed reference 类型丢失且元素展开时显示的提示；为空时不显示提示。
        /// </summary>
        protected virtual string MissingTypeMessage => null;

        /// <summary>
        /// 获取具体类型没有直接可见作者字段时显示的提示；为空时保持单行布局。
        /// </summary>
        protected virtual string EmptyContentMessage => null;

        /// <summary>
        /// 获取作者字段为空提示使用的消息级别。
        /// </summary>
        protected virtual MessageType EmptyContentMessageType => MessageType.Info;

        /// <summary>
        /// 获取 managed reference 类型丢失提示使用的消息级别。
        /// </summary>
        protected virtual MessageType MissingTypeMessageType => MessageType.Warning;

        /// <summary>
        /// 获取状态提示区域的高度；OnGUI 与 GetPropertyHeight 会使用同一值。
        /// </summary>
        protected virtual float SupplementalMessageHeight =>
            EditorGUIUtility.singleLineHeight * 2f;

        #endregion

        #region 属性绘制

        /// <summary>
        /// 绘制类型选择首行、状态提示及当前 managed reference 的直接子字段。
        /// </summary>
        /// <param name="position">属性在 Inspector 中的绘制区域。</param>
        /// <param name="property">当前 SerializeReference 属性。</param>
        /// <param name="label">属性标签。</param>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            Rect line = new Rect(
                position.x,
                position.y,
                position.width,
                EditorGUIUtility.singleLineHeight);
            Rect foldoutRect = new Rect(
                line.x,
                line.y,
                EditorGUIUtility.labelWidth,
                line.height);
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

            Type managedType = GetManagedType(property);
            bool isMissingType = IsMissingType(property, managedType);
            string typeName = managedType != null
                ? GetTypeDisplayName(managedType)
                : isMissingType
                    ? MissingTypeDisplayName
                    : "None";
            if (EditorGUI.DropdownButton(
                    buttonRect,
                    new GUIContent(typeName),
                    FocusType.Keyboard))
            {
                ShowTypeMenu(property, buttonRect);
            }

            if (!property.isExpanded)
            {
                EditorGUI.EndProperty();
                return;
            }

            float y = line.yMax + Spacing;
            if (isMissingType)
            {
                DrawSupplementalMessage(
                    position,
                    y,
                    MissingTypeMessage,
                    MissingTypeMessageType);
                EditorGUI.EndProperty();
                return;
            }

            if (managedType == null)
            {
                EditorGUI.EndProperty();
                return;
            }

            if (CountDirectChildren(property) == 0)
            {
                DrawSupplementalMessage(
                    position,
                    y,
                    EmptyContentMessage,
                    EmptyContentMessageType);
                EditorGUI.EndProperty();
                return;
            }

            // 直接字段交给 Unity 原生 PropertyField，嵌套对象和数组继续使用各自的 Drawer。
            EditorGUI.indentLevel++;
            VisitDirectChildren(property, childProperty =>
            {
                float childHeight = EditorGUI.GetPropertyHeight(childProperty, true);
                EditorGUI.PropertyField(
                    new Rect(position.x, y, position.width, childHeight),
                    childProperty,
                    true);
                y += childHeight + Spacing;
            });
            EditorGUI.indentLevel--;

            EditorGUI.EndProperty();
        }

        /// <summary>
        /// 计算类型选择首行、状态提示及当前直接子字段所需的总高度。
        /// </summary>
        /// <param name="property">当前 SerializeReference 属性。</param>
        /// <param name="label">属性标签。</param>
        /// <returns>属性在 Inspector 中所需的高度。</returns>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded)
            {
                return height;
            }

            Type managedType = GetManagedType(property);
            bool isMissingType = IsMissingType(property, managedType);
            if (isMissingType)
            {
                return AddSupplementalMessageHeight(height, MissingTypeMessage);
            }

            if (managedType == null)
            {
                return height;
            }

            if (CountDirectChildren(property) == 0)
            {
                return AddSupplementalMessageHeight(height, EmptyContentMessage);
            }

            VisitDirectChildren(property, childProperty =>
                height += Spacing + EditorGUI.GetPropertyHeight(childProperty, true));
            return height;
        }

        #endregion

        #region 类型菜单

        /// <summary>
        /// 显示可用派生类型，并保存目标对象和属性路径供菜单回调重新定位。
        /// </summary>
        /// <param name="property">当前 SerializeReference 属性。</param>
        /// <param name="buttonRect">类型按钮在 Inspector 中的区域。</param>
        private void ShowTypeMenu(SerializedProperty property, Rect buttonRect)
        {
            UnityEngine.Object target = property.serializedObject.targetObject;
            string propertyPath = property.propertyPath;
            Type currentType = GetManagedType(property);
            GenericMenu menu = new GenericMenu();
            bool isMissingType = IsMissingType(property, currentType);
            menu.AddItem(
                new GUIContent("None"),
                currentType == null && !isMissingType,
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

            results.Sort((leftType, rightType) =>
                string.Compare(leftType.FullName, rightType.FullName, StringComparison.Ordinal));
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
        /// 通过重新取得 SerializedObject 替换 managed reference，并记录可撤销的类型变更。
        /// </summary>
        /// <param name="target">包含属性的 Unity 对象。</param>
        /// <param name="propertyPath">属性在目标对象中的路径。</param>
        /// <param name="definitionType">待创建的派生类型；为空表示清空引用。</param>
        private void SetManagedReference(
            UnityEngine.Object target,
            string propertyPath,
            Type definitionType)
        {
            if (target == null)
            {
                return;
            }

            // 菜单回调执行时旧 SerializedProperty 可能已失效，因此按目标和路径重新定位属性。
            Undo.RecordObject(target, UndoActionName);
            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyPath);
            if (property == null)
            {
                Debug.LogWarning(
                    $"[ManagedReferenceDropdownPropertyDrawer] 无法重新定位属性，target={target.name}，path={propertyPath}。",
                    target);
                return;
            }

            property.managedReferenceValue = definitionType == null
                ? null
                : Activator.CreateInstance(definitionType);
            property.isExpanded = definitionType != null;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);

            string displayName = definitionType == null
                ? "None"
                : GetTypeDisplayName(definitionType);
            Debug.Log(
                $"[ManagedReferenceDropdownPropertyDrawer] 已将 {target.name} 的 {propertyPath} 设置为 {displayName}。",
                target);
        }

        #endregion

        #region 状态提示

        /// <summary>
        /// 判断当前属性是否保留了无法解析的 managed reference 类型名。
        /// </summary>
        /// <param name="property">当前 SerializeReference 属性。</param>
        /// <param name="managedType">当前解析出的运行时类型。</param>
        /// <returns>类型值为空但序列化类型名仍存在时返回 true。</returns>
        private static bool IsMissingType(
            SerializedProperty property,
            Type managedType)
        {
            return managedType == null &&
                !string.IsNullOrEmpty(property.managedReferenceFullTypename);
        }

        /// <summary>
        /// 绘制可选状态提示；空消息不会占用额外布局空间。
        /// </summary>
        /// <param name="position">当前属性绘制区域。</param>
        /// <param name="y">提示区域的起始 Y 坐标。</param>
        /// <param name="message">需要显示的提示文本。</param>
        /// <param name="messageType">Unity HelpBox 消息级别。</param>
        private void DrawSupplementalMessage(
            Rect position,
            float y,
            string message,
            MessageType messageType)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            EditorGUI.HelpBox(
                new Rect(position.x, y, position.width, SupplementalMessageHeight),
                message,
                messageType);
        }

        /// <summary>
        /// 将非空状态提示的高度加入属性总高度。
        /// </summary>
        /// <param name="height">当前已经计算出的高度。</param>
        /// <param name="message">需要显示的提示文本。</param>
        /// <returns>包含提示区域时的总高度。</returns>
        private float AddSupplementalMessageHeight(float height, string message)
        {
            return string.IsNullOrEmpty(message)
                ? height
                : height + Spacing + SupplementalMessageHeight;
        }

        #endregion

        #region 属性遍历

        /// <summary>
        /// 统计 managed reference 的直接可见子字段数量。
        /// </summary>
        /// <param name="property">当前 SerializeReference 属性。</param>
        /// <returns>直接可见子字段的数量。</returns>
        private static int CountDirectChildren(SerializedProperty property)
        {
            int childCount = 0;
            VisitDirectChildren(property, childProperty => childCount++);
            return childCount;
        }

        /// <summary>
        /// 只遍历当前 managed reference 的直接子字段，避免嵌套字段被重复绘制。
        /// </summary>
        /// <param name="property">当前 SerializeReference 属性。</param>
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

        /// <summary>
        /// 获取 SerializeReference 当前实际运行类型；类型丢失时返回空值。
        /// </summary>
        /// <param name="property">当前 SerializeReference 属性。</param>
        /// <returns>当前 managed reference 的具体类型。</returns>
        private static Type GetManagedType(SerializedProperty property)
        {
            return property.managedReferenceValue?.GetType();
        }

        #endregion
    }
}
#endif
