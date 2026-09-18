using System;
using System.Collections.Generic;
using System.Globalization;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.GameplayEffect;

namespace RPG.Game.UI.Bag
{
    /// <summary>把 Gameplay Effect 的静态结果转换为背包详情可显示的文本。</summary>
    public static class BagGameplayEffectPresentationBuilder
    {
        /// <summary>按效果配置顺序聚合可静态计算的属性行。</summary>
        /// <param name="effects">待展示的 GE 列表。</param>
        /// <param name="level">静态计算使用的等级，至少为一。</param>
        /// <param name="context">用于诊断日志的业务上下文。</param>
        /// <returns>按 Attribute 首次出现顺序排列的文本行。</returns>
        public static IReadOnlyList<string> BuildStaticAttributeLines(
            IReadOnlyList<GameplayEffectData> effects,
            int level,
            string context)
        {
            if (effects == null || effects.Count == 0) return Array.Empty<string>();
            var values = new List<AttributeValue>();
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

            var lines = new List<string>(values.Count);
            for (int index = 0; index < values.Count; index++)
                lines.Add($"{values[index].Attribute.DisplayName}: {FormatValue(values[index])}");
            return lines;
        }

        /// <summary>将食物 GE 转换为描述优先的效果文本。</summary>
        /// <param name="effects">食物使用效果。</param>
        /// <param name="context">用于诊断日志的业务上下文。</param>
        /// <returns>按 GE 配置顺序排列的效果行。</returns>
        public static IReadOnlyList<string> BuildEffectDescriptionLines(
            IReadOnlyList<GameplayEffectData> effects,
            string context)
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
                        lines.Add($"{result.Attribute.DisplayName}: {FormatRawValue(result.Type, result.Magnitude)}");
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

        /// <summary>追加一个 Modifier，并处理同一 Attribute 的 Add/Multiply 冲突。</summary>
        /// <param name="result">静态 Modifier 结果。</param>
        /// <param name="values">聚合目标列表。</param>
        /// <param name="context">用于诊断日志的业务上下文。</param>
        private static void AppendResult(GameplayEffectStaticModifierResult result,
            List<AttributeValue> values, string context)
        {
            if (!result.Attribute.IsValid ||
                (result.Type != AttributeModifierType.Add && result.Type != AttributeModifierType.Multiply)) return;

            int existingIndex = values.FindIndex(value => value.Attribute.Id == result.Attribute.Id);
            if (existingIndex < 0)
            {
                values.Add(new AttributeValue(result.Attribute, result.Type, result.Magnitude, false));
                return;
            }

            AttributeValue existing = values[existingIndex];
            if (existing.Type != result.Type)
            {
                UnityEngine.Debug.LogError($"[BagGameplayEffect] {context} 的 Attribute '{result.Attribute.DisplayName}' 同时配置 Add 与 Multiply，详情优先显示 Multiply。", null);
                values[existingIndex] = result.Type == AttributeModifierType.Multiply
                    ? new AttributeValue(existing.Attribute, AttributeModifierType.Multiply, result.Magnitude, true)
                    : new AttributeValue(existing.Attribute, existing.Type, existing.Value, true);
                return;
            }

            float aggregate = existing.Type == AttributeModifierType.Add
                ? existing.Value + result.Magnitude
                : existing.Value * result.Magnitude;
            values[existingIndex] = new AttributeValue(existing.Attribute, existing.Type, aggregate, existing.HasTypeConflict);
        }

        /// <summary>格式化已聚合的 Attribute 数值。</summary>
        /// <param name="value">聚合值。</param>
        /// <returns>整数或整数百分比文本。</returns>
        private static string FormatValue(AttributeValue value) => FormatRawValue(value.Type, value.Value);

        /// <summary>格式化单个 Modifier 数值。</summary>
        /// <param name="type">Modifier 类型。</param>
        /// <param name="value">Modifier 数值。</param>
        /// <returns>显示文本。</returns>
        private static string FormatRawValue(AttributeModifierType type, float value)
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

        /// <summary>缓存一个 Attribute 的聚合结果。</summary>
        private readonly struct AttributeValue
        {
            /// <summary>创建聚合值。</summary>
            /// <param name="attribute">Attribute 定义。</param>
            /// <param name="type">采用的运算类型。</param>
            /// <param name="value">聚合数值。</param>
            /// <param name="hasTypeConflict">是否发生运算类型冲突。</param>
            public AttributeValue(GameplayAttribute attribute, AttributeModifierType type, float value, bool hasTypeConflict)
            {
                Attribute = attribute;
                Type = type;
                Value = value;
                HasTypeConflict = hasTypeConflict;
            }

            /// <summary>获取 Attribute。</summary>
            public GameplayAttribute Attribute { get; }
            /// <summary>获取运算类型。</summary>
            public AttributeModifierType Type { get; }
            /// <summary>获取聚合数值。</summary>
            public float Value { get; }
            /// <summary>获取冲突标记。</summary>
            public bool HasTypeConflict { get; }
        }
    }
}
