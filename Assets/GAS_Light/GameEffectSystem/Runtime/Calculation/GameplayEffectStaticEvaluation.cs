using System;
using System.Collections.Generic;
using WS_Modules.GAS.AttributeSystem;

namespace WS_Modules.GAS.GameplayEffect
{
    /// <summary>GE 静态查询的结果状态。</summary>
    public enum GameplayEffectStaticEvaluationStatus
    {
        /// <summary>全部 Modifier 都已得到有限数值。</summary>
        Succeeded = 0,
        /// <summary>至少一个 Modifier 需要运行时上下文。</summary>
        RequiresRuntimeContext,
        /// <summary>配置产生了非有限数值。</summary>
        InvalidConfiguration
    }

    /// <summary>单个 GE Modifier 的静态查询结果。</summary>
    public readonly struct GameplayEffectStaticModifierResult
    {
        /// <summary>创建静态 Modifier 查询结果。</summary>
        /// <param name="attribute">目标 Attribute。</param>
        /// <param name="type">运算类型。</param>
        /// <param name="priority">Modifier 优先级。</param>
        /// <param name="magnitude">静态 Magnitude。</param>
        public GameplayEffectStaticModifierResult(GameplayAttribute attribute, AttributeModifierType type, int priority, float magnitude)
            : this(-1, attribute, type, priority, magnitude)
        {
        }

        /// <summary>创建带原始 Modifier 索引的静态查询结果。</summary>
        /// <param name="modifierIndex">GE 内的原始 Modifier 索引。</param>
        /// <param name="attribute">目标 Attribute。</param>
        /// <param name="type">运算类型。</param>
        /// <param name="priority">Modifier 优先级。</param>
        /// <param name="magnitude">静态 Magnitude。</param>
        internal GameplayEffectStaticModifierResult(int modifierIndex, GameplayAttribute attribute,
            AttributeModifierType type, int priority, float magnitude)
        {
            ModifierIndex = modifierIndex;
            Attribute = attribute;
            Type = type;
            Priority = priority;
            Magnitude = magnitude;
        }

        /// <summary>GE 内的原始 Modifier 索引；直接构造的结果为 -1。</summary>
        public int ModifierIndex { get; }
        /// <summary>目标 Attribute。</summary>
        public GameplayAttribute Attribute { get; }
        /// <summary>运算类型。</summary>
        public AttributeModifierType Type { get; }
        /// <summary>Modifier 优先级。</summary>
        public int Priority { get; }
        /// <summary>静态 Magnitude。</summary>
        public float Magnitude { get; }
    }

    /// <summary>对单个 GE 执行不改变运行时状态的静态 Magnitude 查询。</summary>
    public static class GameplayEffectStaticEvaluation
    {
        /// <summary>按指定等级查询 GE 的全部 Modifier。</summary>
        /// <param name="data">待查询的 GE。</param>
        /// <param name="level">GE Level，精炼效果使用精炼阶数。</param>
        /// <param name="results">成功或部分可查询时返回的有序结果。</param>
        /// <returns>查询状态。</returns>
        /// <exception cref="ArgumentNullException">GE 配置为空。</exception>
        /// <exception cref="ArgumentOutOfRangeException">等级小于 1。</exception>
        /// <exception cref="InvalidOperationException">Modifier 列表包含空项。</exception>
        public static GameplayEffectStaticEvaluationStatus Evaluate(
            GameplayEffectData data,
            int level,
            out IReadOnlyList<GameplayEffectStaticModifierResult> results)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (level < 1) throw new ArgumentOutOfRangeException(nameof(level));

            var values = new List<GameplayEffectStaticModifierResult>(data.Modifiers.Count);
            bool requiresRuntimeContext = false;
            // 保持 GE 作者配置的 Modifier 顺序，不合并运算类型，也不创建 ASC 或 Active Runtime。
            for (int index = 0; index < data.Modifiers.Count; index++)
            {
                GameplayEffectModifier modifier = data.Modifiers[index];
                if (modifier == null) throw new InvalidOperationException($"GE '{data.name}' 的 Modifier 第 {index} 项为空。");
                if (!modifier.TryCalculateStaticMagnitude(level, out float magnitude))
                {
                    requiresRuntimeContext = true;
                    continue;
                }

                if (float.IsNaN(magnitude) || float.IsInfinity(magnitude))
                {
                    results = values;
                    return GameplayEffectStaticEvaluationStatus.InvalidConfiguration;
                }

                values.Add(new GameplayEffectStaticModifierResult(
                    index, modifier.Attribute, modifier.Type, modifier.Priority, magnitude));
            }

            results = values;
            return requiresRuntimeContext
                ? GameplayEffectStaticEvaluationStatus.RequiresRuntimeContext
                : GameplayEffectStaticEvaluationStatus.Succeeded;
        }
    }
}
