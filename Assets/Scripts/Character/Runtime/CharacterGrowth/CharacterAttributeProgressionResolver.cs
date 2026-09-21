using System;
using System.Collections.Generic;
using WS_Modules.GAS.AttributeSystem;

namespace RPG.Character
{
    /// <summary>只读取 CharacterGrowthProfile 烘焙结果并解析指定等级 BaseValue 的运行时服务。</summary>
    internal sealed class CharacterAttributeProgressionResolver
    {
        #region 公开解析

        /// <summary>按角色配置顺序解析指定等级的全部初始 Attribute BaseValue。</summary>
        /// <param name="config">角色静态配置。</param>
        /// <param name="level">目标等级。</param>
        /// <returns>覆盖全部 InitialAttributeSets Attribute 的 BaseValue 列表。</returns>
        /// <exception cref="InvalidOperationException">配置过期、结果缺失或等级无效时抛出。</exception>
        public IReadOnlyList<GameplayAttributeValue> ResolveBaseValues(CharacterConfig config, int level)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (level < 1 || level > config.MaxLevel)
                throw new ArgumentOutOfRangeException(nameof(level), level, "角色等级超出配置范围。");
            if (config.GrowthProfile == null)
                throw new InvalidOperationException($"角色 {config.CharacterId} 缺少成长配置。");
            if (config.GrowthProfile.NeedsRebake(config.InitialAttributeSets))
                throw new InvalidOperationException($"角色 {config.CharacterId} 的成长烘焙结果已过期，请重新 Bake。");

            IReadOnlyList<BakedCharacterAttributeProgression> baked = config.GrowthProfile.BakedAttributeProgressions;
            var progressionByAttributeIdMap = new Dictionary<int, BakedCharacterAttributeProgression>();
            for (int index = 0; index < baked.Count; index++)
            {
                BakedCharacterAttributeProgression progression = baked[index];
                if (progression == null || !progressionByAttributeIdMap.TryAdd(progression.Attribute.Id, progression))
                    throw new InvalidOperationException($"角色 {config.CharacterId} 的烘焙 Attribute 结果重复或为空。");
            }

            var result = new List<GameplayAttributeValue>();
            var seenAttributeIdSet = new HashSet<int>();
            for (int setIndex = 0; setIndex < config.InitialAttributeSets.Count; setIndex++)
            {
                GameplayAttributeSet set = config.InitialAttributeSets[setIndex];
                if (set == null) throw new InvalidOperationException($"角色 {config.CharacterId} 的初始 AttributeSet 为空。");
                IReadOnlyList<GameplayAttributeDefinition> definitions = set.Definitions;
                for (int definitionIndex = 0; definitionIndex < definitions.Count; definitionIndex++)
                {
                    GameplayAttributeDefinition definition = definitions[definitionIndex];
                    if (definition == null || !seenAttributeIdSet.Add(definition.Attribute.Id) ||
                        !progressionByAttributeIdMap.TryGetValue(definition.Attribute.Id, out BakedCharacterAttributeProgression progression) ||
                        progression.BaseValues == null || progression.BaseValues.Count != config.MaxLevel)
                        throw new InvalidOperationException($"角色 {config.CharacterId} 的 Attribute 烘焙结果与配置不一致：id={definition?.Attribute.Id}。");

                    float value = progression.BaseValues[level - 1];
                    if (float.IsNaN(value) || float.IsInfinity(value))
                        throw new InvalidOperationException($"角色 {config.CharacterId} 的 Attribute {definition.Attribute} 在等级 {level} 不是有限值。");
                    result.Add(new GameplayAttributeValue(definition.Attribute, value));
                }
            }

            if (result.Count != baked.Count)
                throw new InvalidOperationException($"角色 {config.CharacterId} 的烘焙 Attribute 数量不完整：resolved={result.Count}, baked={baked.Count}。");
            return result;
        }

        #endregion
    }
}
