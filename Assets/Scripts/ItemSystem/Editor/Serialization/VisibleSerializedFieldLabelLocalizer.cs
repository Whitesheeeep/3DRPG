#if UNITY_EDITOR
using System;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace RPG.ItemSystem.Editor
{
    /// <summary>
    /// 只修改当前已经生成的 SerializedProperty 字段标签，不参与 Unity 的序列化绑定生命周期。
    /// </summary>
    internal static class VisibleSerializedFieldLabelLocalizer
    {
        /// <summary>
        /// 将指定视觉子树中当前可见的字段标签更新为中文。
        /// </summary>
        /// <param name="root">需要扫描的视觉子树。</param>
        internal static void Apply(VisualElement root)
        {
            if (root == null) return;

            root.Query<PropertyField>().ForEach(ApplyFieldLabel);
        }

        /// <summary>
        /// 根据绑定路径修正单个 PropertyField 内部已经生成的 Label 节点。
        /// </summary>
        /// <param name="field">目标序列化字段。</param>
        private static void ApplyFieldLabel(PropertyField field)
        {
            string labelText = GetLabelText(field.bindingPath);
            if (string.IsNullOrEmpty(labelText)) return;

            // 不调用 PropertyField.label setter，避免重建 IMGUI PropertyDrawer 或嵌套 ListView。
            Label visibleLabel = field.Q<Label>(className: "unity-label") ?? field.Q<Label>();
            if (visibleLabel != null && !string.Equals(visibleLabel.text, labelText, StringComparison.Ordinal))
                visibleLabel.text = labelText;
        }

        /// <summary>
        /// 根据序列化字段路径获取固定中文名称。
        /// </summary>
        /// <param name="bindingPath">Unity 序列化绑定路径。</param>
        /// <returns>匹配到的中文名称；不匹配时返回空字符串。</returns>
        private static string GetLabelText(string bindingPath)
        {
            if (string.IsNullOrEmpty(bindingPath)) return string.Empty;
            // 只比较最后一个序列化字段名，避免把 maxAscensionRank 等字段误识别成 rank。
            int separatorIndex = bindingPath.LastIndexOf('.');
            string fieldName = separatorIndex >= 0
                ? bindingPath.Substring(separatorIndex + 1)
                : bindingPath;
            if (fieldName == "requiredLevel") return "所需等级";
            if (fieldName == "maxLevelAfter") return "突破后等级上限";
            if (fieldName == "requiredDuplicateCount") return "所需同名武器数量";
            if (fieldName == "rank") return "精炼阶数";
            if (fieldName == "currencyCost") return "货币消耗";
            if (fieldName == "cost") return "消耗";
            if (fieldName == "itemCosts") return "物品消耗";
            if (fieldName == "currencyCosts") return "货币消耗";
            if (fieldName == "itemId") return "物品标识";
            if (fieldName == "quantity") return "数量";
            if (fieldName == "currencyId") return "货币标识";
            if (fieldName == "amount") return "金额";
            if (fieldName == "nextExperience") return "下一级所需经验";
            if (fieldName == "level" && bindingPath.Contains("levelOverrides", StringComparison.Ordinal))
                return "等级";
            if (fieldName == "growthProfile") return "成长配置";
            return string.Empty;
        }
    }
}
#endif
