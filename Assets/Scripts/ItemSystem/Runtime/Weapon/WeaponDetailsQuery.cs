using System;
using System.Collections.Generic;
using WS_Modules.GAS.GameplayEffect;

namespace RPG.ItemSystem
{
    /// <summary>根据武器定义和实例生成无副作用的详情快照。</summary>
    public static class WeaponDetailsQuery
    {
        /// <summary>查询武器等级和精炼效果的静态属性贡献。</summary>
        /// <param name="definition">武器静态定义。</param>
        /// <param name="instance">武器运行时实例。</param>
        /// <returns>武器详情快照。</returns>
        /// <exception cref="ArgumentNullException">定义或实例为空。</exception>
        /// <exception cref="InvalidOperationException">定义与实例的标识不匹配。</exception>
        public static WeaponDetails Create(WeaponDefinition definition, WeaponInstance instance)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            if (definition.ItemId != instance.DefinitionId)
                throw new InvalidOperationException("武器详情的 Definition 与实例不匹配。");

            return new WeaponDetails(
                definition,
                instance,
                Evaluate(definition.LevelEffects, instance.Level),
                Evaluate(definition.RefinementEffects, instance.RefinementRank));
        }

        /// <summary>按列表顺序查询一组 GE 的静态属性贡献。</summary>
        /// <param name="effects">GE 列表。</param>
        /// <param name="level">每个 GE 使用的等级。</param>
        /// <returns>带效果索引和 Modifier 索引的贡献列表。</returns>
        private static IReadOnlyList<WeaponEffectEvaluation> Evaluate(
            IReadOnlyList<GameplayEffectData> effects, int level)
        {
            var evaluations = new List<WeaponEffectEvaluation>(effects.Count);
            // 每个 GE 单独保留状态与原始 Modifier 索引，避免动态项导致后续索引压缩。
            for (int effectIndex = 0; effectIndex < effects.Count; effectIndex++)
            {
                GameplayEffectData effect = effects[effectIndex];
                GameplayEffectStaticEvaluationStatus status =
                    GameplayEffectStaticEvaluation.Evaluate(effect, level, out IReadOnlyList<GameplayEffectStaticModifierResult> results);
                var contributions = new List<WeaponEffectContribution>(results.Count);
                for (int resultIndex = 0; resultIndex < results.Count; resultIndex++)
                {
                    GameplayEffectStaticModifierResult result = results[resultIndex];
                    contributions.Add(new WeaponEffectContribution(effectIndex, result.ModifierIndex, result));
                }
                evaluations.Add(new WeaponEffectEvaluation(effectIndex, status, contributions));
            }

            return evaluations;
        }
    }
}
