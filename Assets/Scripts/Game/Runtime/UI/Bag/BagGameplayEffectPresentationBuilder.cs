using System;
using System.Collections.Generic;
using System.Globalization;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.GameplayEffect;

namespace RPG.Game.UI.Bag
{
    /// <summary>把 Gameplay Effect 的静态结果转换为背包和装备页面可复用的数据。</summary>
    public static class BagGameplayEffectPresentationBuilder
    {
        #region 属性行构建

        /// <summary>按效果配置顺序聚合可静态计算的属性行。</summary>
        /// <param name="effects">待展示的 GE 列表。</param>
        /// <param name="level">静态计算使用的等级，至少为一。</param>
        /// <param name="context">用于诊断日志的业务上下文。</param>
        /// <returns>按 Attribute 首次出现顺序排列的文本行。</returns>
        public static IReadOnlyList<string> BuildStaticAttributeLines(
            IReadOnlyList<GameplayEffectData> effects, int level, string context)
        {
            IReadOnlyList<StaticGameplayAttributePresentationValue> values =
                BuildStaticAttributeValues(effects, level, context);
            var lines = new List<string>(values.Count);
            for (int index = 0; index < values.Count; index++)
            {
                StaticGameplayAttributePresentationValue value = values[index];
                string attributeName = string.IsNullOrWhiteSpace(value.Attribute.DisplayName)
                    ? value.Attribute.Name
                    : value.Attribute.DisplayName;
                lines.Add($"{attributeName}: {FormatStaticAttributeValue(value.Type, value.Value)}");
            }
            return lines;
        }

        /// <summary>按效果配置顺序聚合静态属性，供页面跨多件装备继续汇总。</summary>
        /// <param name="effects">待展示的 GE 列表。</param>
        /// <param name="level">静态计算使用的等级，至少为一。</param>
        /// <param name="context">用于诊断日志的业务上下文。</param>
        /// <returns>按 Attribute 首次出现顺序排列的结构化结果。</returns>
        public static IReadOnlyList<StaticGameplayAttributePresentationValue> BuildStaticAttributeValues(
            IReadOnlyList<GameplayEffectData> effects, int level, string context)
        {
            if (effects == null || effects.Count == 0)
                return Array.Empty<StaticGameplayAttributePresentationValue>();

            // 先完成每个装备效果列表内部的静态计算与 Add/Multiply 冲突处理，调用方再做跨装备汇总。
            var values = new List<StaticGameplayAttributePresentationValue>();
            int evaluationLevel = Math.Max(1, level);
            for (int effectIndex = 0; effectIndex < effects.Count; effectIndex++)
            {
                GameplayEffectStaticEvaluationStatus status = GameplayEffectStaticEvaluation.Evaluate(
                    effects[effectIndex], evaluationLevel,
                    out IReadOnlyList<GameplayEffectStaticModifierResult> results);
                if (status == GameplayEffectStaticEvaluationStatus.InvalidConfiguration)
                    UnityEngine.Debug.LogError($"[BagGameplayEffect] {context} 的第 {effectIndex + 1} 个 GE 静态计算结果无效。", effects[effectIndex]);

                for (int resultIndex = 0; resultIndex < results.Count; resultIndex++)
                    AppendResult(results[resultIndex], values, context);
            }
            return values;
        }

        #endregion

        #region 描述构建

        /// <summary>将食物 GE 转换为描述优先的效果文本。</summary>
        /// <param name="effects">食物使用效果。</param>
        /// <param name="context">用于诊断日志的业务上下文。</param>
        /// <returns>按 GE 配置顺序排列的效果行。</returns>
        public static IReadOnlyList<string> BuildEffectDescriptionLines(
            IReadOnlyList<GameplayEffectData> effects, string context)
        {
            if (effects == null || effects.Count == 0) return Array.Empty<string>();
            var lines = new List<string>(effects.Count);
            for (int index = 0; index < effects.Count; index++)
            {
                GameplayEffectData effect = effects[index];
                if (!string.IsNullOrWhiteSpace(effect.Description))
                {
                    lines.Add(effect.Description.Trim());
                    continue;
                }

                GameplayEffectStaticEvaluationStatus status = GameplayEffectStaticEvaluation.Evaluate(
                    effect, 1, out IReadOnlyList<GameplayEffectStaticModifierResult> results);
                if (results.Count > 0)
                {
                    for (int resultIndex = 0; resultIndex < results.Count; resultIndex++)
                    {
                        GameplayEffectStaticModifierResult result = results[resultIndex];
                        if (!result.Attribute.IsValid) continue;
                        string attributeName = string.IsNullOrWhiteSpace(result.Attribute.DisplayName)
                            ? result.Attribute.Name
                            : result.Attribute.DisplayName;
                        lines.Add($"{attributeName}: {FormatStaticAttributeValue(result.Type, result.Magnitude)}");
                    }
                    continue;
                }

                if (status == GameplayEffectStaticEvaluationStatus.RequiresRuntimeContext)
                    lines.Add("效果将在使用时计算");
                else
                    UnityEngine.Debug.LogWarning($"[BagGameplayEffect] {context} 的 GE 没有可展示的描述或静态 Modifier。", effect);
            }
            return lines;
        }

        #endregion

        #region 聚合与格式化

        /// <summary>追加一个 Modifier，并处理同一 Attribute 的 Add/Multiply 冲突。</summary>
        /// <param name="result">静态 Modifier 结果。</param>
        /// <param name="values">聚合目标列表。</param>
        /// <param name="context">用于诊断日志的业务上下文。</param>
        private static void AppendResult(GameplayEffectStaticModifierResult result,
            List<StaticGameplayAttributePresentationValue> values, string context)
        {
            if (!result.Attribute.IsValid ||
                (result.Type != AttributeModifierType.Add && result.Type != AttributeModifierType.Multiply)) return;

            int existingIndex = values.FindIndex(value => value.Attribute.Id == result.Attribute.Id);
            if (existingIndex < 0)
            {
                values.Add(new StaticGameplayAttributePresentationValue(result.Attribute, result.Type, result.Magnitude, false));
                return;
            }

            StaticGameplayAttributePresentationValue existing = values[existingIndex];
            if (existing.Type != result.Type)
            {
                UnityEngine.Debug.LogError($"[BagGameplayEffect] {context} 的 Attribute '{result.Attribute.DisplayName}' 同时配置 Add 与 Multiply，详情优先显示 Multiply。", null);
                values[existingIndex] = result.Type == AttributeModifierType.Multiply
                    ? new StaticGameplayAttributePresentationValue(existing.Attribute, AttributeModifierType.Multiply, result.Magnitude, true)
                    : new StaticGameplayAttributePresentationValue(existing.Attribute, existing.Type, existing.Value, true);
                return;
            }

            float aggregate = existing.Type == AttributeModifierType.Add
                ? existing.Value + result.Magnitude
                : existing.Value * result.Magnitude;
            values[existingIndex] = new StaticGameplayAttributePresentationValue(
                existing.Attribute, existing.Type, aggregate, existing.HasTypeConflict);
        }

        /// <summary>按背包详情规则格式化静态 Modifier 聚合值。</summary>
        /// <param name="type">Modifier 运算类型。</param>
        /// <param name="value">Modifier 聚合值。</param>
        /// <returns>整数或整数百分比文本。</returns>
        public static string FormatStaticAttributeValue(AttributeModifierType type, float value)
        {
            if (type == AttributeModifierType.Add && value >= 0f && value <= 1f)
                return $"{Math.Round(value * 100f, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture)}%";
            if (type == AttributeModifierType.Multiply)
            {
                int percentage = (int)Math.Round((value - 1f) * 100f, MidpointRounding.AwayFromZero);
                return $"{(percentage > 0 ? "+" : string.Empty)}{percentage.ToString(CultureInfo.InvariantCulture)}%";
            }
            return Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
        }

        #endregion
    }

    /// <summary>武器或圣遗物静态 Gameplay Effect 的单个 Attribute 聚合结果。</summary>
    public readonly struct StaticGameplayAttributePresentationValue
    {
        /// <summary>创建静态 Attribute 展示结果。</summary>
        /// <param name="attribute">Attribute 标识。</param>
        /// <param name="type">最终采用的 Modifier 类型。</param>
        /// <param name="value">聚合后的 Modifier 值。</param>
        /// <param name="hasTypeConflict">是否出现 Add/Multiply 配置冲突。</param>
        public StaticGameplayAttributePresentationValue(GameplayAttribute attribute,
            AttributeModifierType type, float value, bool hasTypeConflict)
        {
            Attribute = attribute;
            Type = type;
            Value = value;
            HasTypeConflict = hasTypeConflict;
        }

        /// <summary>获取 Attribute 标识。</summary>
        public GameplayAttribute Attribute { get; }
        /// <summary>获取最终采用的运算类型。</summary>
        public AttributeModifierType Type { get; }
        /// <summary>获取 Modifier 聚合值。</summary>
        public float Value { get; }
        /// <summary>获取是否存在运算类型冲突。</summary>
        public bool HasTypeConflict { get; }
    }
}
