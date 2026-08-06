#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using WS_Modules.GAS.AttributeSystem;

namespace WS_Modules.GAS.Editor
{
    /// <summary>以内联五字段布局绘制 AttributeSet Definition，并隐藏只属于运行时副本的状态。</summary>
    [CustomPropertyDrawer(typeof(GameplayAttributeDefinition))]
    public sealed class GameplayAttributeDefinitionDrawer : PropertyDrawer
    {
        #region Inspector 绘制

        /// <summary>绘制 Attribute、分类、默认值及固定边界。</summary>
        /// <param name="position">Unity 分配给 Definition 的完整绘制区域。</param>
        /// <param name="property">GameplayAttributeDefinition 序列化属性。</param>
        /// <param name="label">当前数组元素标签；具体字段使用固定业务标签。</param>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            float step = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            DrawPropertyLine(position, 0, step, property.FindPropertyRelative("attribute"), "Attribute");
            DrawPropertyLine(position, 1, step, property.FindPropertyRelative("type"), "Type");
            DrawPropertyLine(position, 2, step, property.FindPropertyRelative("defaultValue"), "Default Value");
            DrawPropertyLine(position, 3, step, property.FindPropertyRelative("minValue"), "Min Value");
            DrawPropertyLine(position, 4, step, property.FindPropertyRelative("maxValue"), "Max Value");
            EditorGUI.EndProperty();
        }

        /// <summary>返回五行配置字段所需的固定高度。</summary>
        /// <param name="property">GameplayAttributeDefinition 序列化属性。</param>
        /// <param name="label">当前数组元素标签。</param>
        /// <returns>五个单行字段与四个标准间距之和。</returns>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) =>
            EditorGUIUtility.singleLineHeight * 5f + EditorGUIUtility.standardVerticalSpacing * 4f;

        // 在指定行绘制字段；字段缺失时显示明确错误以暴露序列化契约变化。
        private static void DrawPropertyLine(
            Rect position,
            int row,
            float step,
            SerializedProperty property,
            string label)
        {
            var line = new Rect(
                position.x,
                position.y + row * step,
                position.width,
                EditorGUIUtility.singleLineHeight);
            if (property == null)
            {
                EditorGUI.HelpBox(line, $"Missing serialized field: {label}", MessageType.Error);
                return;
            }

            EditorGUI.PropertyField(line, property, new GUIContent(label));
        }

        #endregion
    }
}
#endif
