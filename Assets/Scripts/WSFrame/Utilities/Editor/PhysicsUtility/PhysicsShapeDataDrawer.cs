#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace WS_Modules.Utilities.Editor
{
    /// <summary>
    /// 为 PhysicsShapeData 提供按当前类型显示相关字段的 Inspector 绘制器。
    /// </summary>
    [CustomPropertyDrawer(typeof(PhysicsShapeData))]
    public sealed class PhysicsShapeDataDrawer : PropertyDrawer
    {
        #region Inspector 绘制

        /// <summary>
        /// 计算当前形状在 Inspector 中需要的高度。
        /// </summary>
        /// <param name="property">形状数据属性。</param>
        /// <param name="label">属性标签。</param>
        /// <returns>所需的 Inspector 行高。</returns>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            int fieldCount = 5;
            PhysicsShapeType type = ReadType(property);
            fieldCount += type switch
            {
                PhysicsShapeType.Box => 1,
                PhysicsShapeType.Sphere => 1,
                PhysicsShapeType.Capsule => 3,
                PhysicsShapeType.Sector => 4,
                PhysicsShapeType.Ray => 1,
                _ => 0
            };
            return fieldCount * EditorGUIUtility.singleLineHeight +
                (fieldCount - 1) * EditorGUIUtility.standardVerticalSpacing;
        }

        /// <summary>
        /// 绘制类型、通用姿态、Gizmos 开关、编辑按钮和当前形状专属参数。
        /// </summary>
        /// <param name="position">属性在 Inspector 中的绘制区域。</param>
        /// <param name="property">形状数据属性。</param>
        /// <param name="label">属性标签。</param>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            SerializedProperty typeProperty = property.FindPropertyRelative("type");
            SerializedProperty canDrawGizmosProperty = property.FindPropertyRelative("canDrawGizmos");
            SerializedProperty localPosition = property.FindPropertyRelative("localPosition");
            SerializedProperty localEulerAngles = property.FindPropertyRelative("localEulerAngles");
            float line = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            Rect fieldRect = new(position.x, position.y, position.width, line);

            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(fieldRect, typeProperty, new GUIContent("类型"));
            fieldRect.y += line + spacing;
            EditorGUI.PropertyField(fieldRect, canDrawGizmosProperty, new GUIContent("绘制 Gizmos"));
            fieldRect.y += line + spacing;
            EditorGUI.PropertyField(fieldRect, localPosition, new GUIContent("局部位置"));
            fieldRect.y += line + spacing;
            EditorGUI.PropertyField(fieldRect, localEulerAngles, new GUIContent("局部旋转"));
            fieldRect.y += line + spacing;

            PhysicsShapeType type = ReadType(property);
            switch (type)
            {
                case PhysicsShapeType.Box:
                    EditorGUI.PropertyField(fieldRect, property.FindPropertyRelative("size"),
                        new GUIContent("尺寸"));
                    break;
                case PhysicsShapeType.Sphere:
                    EditorGUI.PropertyField(fieldRect, property.FindPropertyRelative("radius"),
                        new GUIContent("半径"));
                    break;
                case PhysicsShapeType.Capsule:
                    EditorGUI.PropertyField(fieldRect, property.FindPropertyRelative("radius"),
                        new GUIContent("半径"));
                    fieldRect.y += line + spacing;
                    EditorGUI.PropertyField(fieldRect, property.FindPropertyRelative("height"),
                        new GUIContent("高度"));
                    fieldRect.y += line + spacing;
                    EditorGUI.PropertyField(fieldRect, property.FindPropertyRelative("capsuleAxis"),
                        new GUIContent("轴向"));
                    break;
                case PhysicsShapeType.Sector:
                    DrawSectorFields(ref fieldRect, property, line, spacing);
                    break;
                case PhysicsShapeType.Ray:
                    EditorGUI.PropertyField(fieldRect, property.FindPropertyRelative("length"),
                        new GUIContent("长度"));
                    break;
            }

            fieldRect.y += line + spacing;
            DrawEditButton(fieldRect, property);

            if (EditorGUI.EndChangeCheck()) ClampValues(property, type);

            EditorGUI.EndProperty();
        }

        #endregion

        #region 内部辅助

        /// <summary>绘制并处理当前宿主组件的临时 Handle 编辑按钮。</summary>
        private static void DrawEditButton(Rect position, SerializedProperty property)
        {
            Component target = property.serializedObject.targetObject as Component;
            bool canEdit = target != null && !property.serializedObject.isEditingMultipleObjects;
            bool editing = canEdit && PhysicsShapeHandleEditor.IsEditing(target, property.propertyPath);
            bool previousEnabled = GUI.enabled;
            GUI.enabled = canEdit;
            if (GUI.Button(position, editing ? "停止编辑" : "开始编辑"))
                PhysicsShapeHandleEditor.Toggle(target, property.propertyPath);
            GUI.enabled = previousEnabled;
        }

        /// <summary>读取当前序列化属性中的形状枚举。</summary>
        private static PhysicsShapeType ReadType(SerializedProperty property)
        {
            SerializedProperty typeProperty = property.FindPropertyRelative("type");
            return typeProperty == null ? PhysicsShapeType.None :
                (PhysicsShapeType)typeProperty.enumValueIndex;
        }

        /// <summary>绘制 Sector 的内外半径、角度和高度字段。</summary>
        private static void DrawSectorFields(ref Rect fieldRect, SerializedProperty property,
            float line, float spacing)
        {
            EditorGUI.PropertyField(fieldRect, property.FindPropertyRelative("innerRadius"),
                new GUIContent("内半径"));
            fieldRect.y += line + spacing;
            EditorGUI.PropertyField(fieldRect, property.FindPropertyRelative("outerRadius"),
                new GUIContent("外半径"));
            fieldRect.y += line + spacing;
            EditorGUI.PropertyField(fieldRect, property.FindPropertyRelative("angle"),
                new GUIContent("角度"));
            fieldRect.y += line + spacing;
            EditorGUI.PropertyField(fieldRect, property.FindPropertyRelative("height"),
                new GUIContent("高度"));
        }

        /// <summary>
        /// 在 Inspector 修改完成后收敛尺寸边界，保证运行时查询不会读取负数或非法关系。
        /// </summary>
        private static void ClampValues(SerializedProperty property, PhysicsShapeType type)
        {
            SerializedProperty typeProperty = property.FindPropertyRelative("type");
            if (typeProperty == null || typeProperty.hasMultipleDifferentValues) return;
            switch (type)
            {
                case PhysicsShapeType.Box:
                    Vector3 size = property.FindPropertyRelative("size").vector3Value;
                    property.FindPropertyRelative("size").vector3Value = new Vector3(
                        SanitizePositive(size.x), SanitizePositive(size.y), SanitizePositive(size.z));
                    break;
                case PhysicsShapeType.Sphere:
                    property.FindPropertyRelative("radius").floatValue = SanitizePositive(
                        property.FindPropertyRelative("radius").floatValue);
                    break;
                case PhysicsShapeType.Capsule:
                    SerializedProperty capsuleRadius = property.FindPropertyRelative("radius");
                    SerializedProperty capsuleHeight = property.FindPropertyRelative("height");
                    capsuleRadius.floatValue = SanitizePositive(capsuleRadius.floatValue);
                    capsuleHeight.floatValue = Mathf.Max(SanitizePositive(capsuleHeight.floatValue),
                        capsuleRadius.floatValue * 2f);
                    break;
                case PhysicsShapeType.Sector:
                    SerializedProperty inner = property.FindPropertyRelative("innerRadius");
                    SerializedProperty outer = property.FindPropertyRelative("outerRadius");
                    inner.floatValue = Mathf.Max(0f, SanitizePositive(inner.floatValue, 0f));
                    outer.floatValue = SanitizePositive(outer.floatValue);
                    inner.floatValue = Mathf.Min(inner.floatValue, outer.floatValue);
                    property.FindPropertyRelative("angle").floatValue = Mathf.Clamp(
                        SanitizePositive(property.FindPropertyRelative("angle").floatValue), 0.01f, 360f);
                    property.FindPropertyRelative("height").floatValue = SanitizePositive(
                        property.FindPropertyRelative("height").floatValue);
                    break;
                case PhysicsShapeType.Ray:
                    property.FindPropertyRelative("length").floatValue = SanitizePositive(
                        property.FindPropertyRelative("length").floatValue);
                    break;
            }
        }

        /// <summary>把浮点尺寸收敛为有限的最小正数。</summary>
        private static float SanitizePositive(float value, float minimum = 0.001f)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? minimum :
                Mathf.Max(minimum, Mathf.Abs(value));
        }

        #endregion
    }
}
#endif
