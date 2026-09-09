using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.AttributeSystem;

namespace RPG.Character
{
    /// <summary>按经验、货币和 Attribute BaseValue 曲线生成角色逐级成长表的配置资产。</summary>
    [CreateAssetMenu(fileName = "CharacterGrowthProfile", menuName = "RPG/Character/Character Growth Profile", order = 20)]
    public sealed class CharacterGrowthProfile : ScriptableObject
    {
        #region 配置字段

        [SerializeField, MinValue(1), LabelText("最大等级"), ReadOnly] private int maxLevel = 90;
        [SerializeField, LabelText("累计经验曲线")] private AnimationCurve cumulativeExperienceCurve = AnimationCurve.Linear(1f, 0f, 90f, 1800000f);
        [SerializeField, LabelText("货币消耗曲线")] private AnimationCurve currencyCostCurve = AnimationCurve.Linear(1f, 1200f, 90f, 0f);
        [SerializeField, LabelText("Attribute BaseValue 曲线")] private List<CharacterAttributeGrowthCurve> attributeGrowthCurves = new();
        [SerializeField, LabelText("特殊等级覆盖")] private List<CharacterLevelProgressionOverride> levelOverrides = new();
        [SerializeField, LabelText("已烘焙等级结果")] private List<BakedCharacterLevelProgression> bakedLevelProgressions = new();
        [SerializeField, LabelText("已烘焙 Attribute 结果")] private List<BakedCharacterAttributeProgression> bakedAttributeProgressions = new();
        [SerializeField, HideInInspector] private int bakedInputHash;

        #endregion

        #region 属性

        /// <summary>获取最大等级。</summary>
        public int MaxLevel => maxLevel;

        /// <summary>获取累计经验曲线。</summary>
        public AnimationCurve CumulativeExperienceCurve => cumulativeExperienceCurve;

        /// <summary>获取升级货币消耗曲线。</summary>
        public AnimationCurve CurrencyCostCurve => currencyCostCurve;

        /// <summary>获取按需配置的 Attribute BaseValue 曲线。</summary>
        public IReadOnlyList<CharacterAttributeGrowthCurve> AttributeGrowthCurves => attributeGrowthCurves;

        /// <summary>获取特殊等级覆盖集合。</summary>
        public IReadOnlyList<CharacterLevelProgressionOverride> LevelOverrides => levelOverrides;

        /// <summary>获取等级经验和货币烘焙结果。</summary>
        public IReadOnlyList<BakedCharacterLevelProgression> BakedLevelProgressions => bakedLevelProgressions;

        /// <summary>获取 Attribute BaseValue 烘焙结果。</summary>
        public IReadOnlyList<BakedCharacterAttributeProgression> BakedAttributeProgressions => bakedAttributeProgressions;

        /// <summary>判断当前曲线输入是否已经重新烘焙。</summary>
        public bool NeedsRebake => bakedLevelProgressions == null || bakedAttributeProgressions == null ||
            bakedLevelProgressions.Count != maxLevel || bakedAttributeProgressions.Count != (attributeGrowthCurves?.Count ?? -1) ||
            bakedInputHash != CalculateInputHash();

        #endregion

        #region 公开操作

        /// <summary>按角色初始 AttributeSet 校验成长 Profile。</summary>
        /// <param name="initialAttributeSets">角色初始化时导入的 AttributeSet。</param>
        /// <exception cref="InvalidOperationException">Profile 或 Attribute 曲线不满足配置契约时抛出。</exception>
        public void Validate(IReadOnlyList<GameplayAttributeSet> initialAttributeSets)
        {
            Dictionary<int, GameplayAttributeDefinition> definitionByAttributeIdMap = BuildAttributeDefinitionIndex(initialAttributeSets);
            ValidateScalarCurves();
            ValidateLevelOverrides();
            ValidateAttributeCurves(definitionByAttributeIdMap);
        }

        /// <summary>按曲线生成等级经验、货币与 Attribute BaseValue 烘焙结果。</summary>
        /// <param name="initialAttributeSets">角色初始化时导入的 AttributeSet。</param>
        /// <exception cref="InvalidOperationException">Profile 或 Attribute 曲线不满足配置契约时抛出。</exception>
        public void Bake(IReadOnlyList<GameplayAttributeSet> initialAttributeSets)
        {
            Validate(initialAttributeSets);
            Dictionary<int, CharacterLevelProgressionOverride> overrideByLevelMap = BuildOverrideIndex();
            var levelResults = new List<BakedCharacterLevelProgression>(maxLevel);
            int previousExperience = 0;
            for (int level = 1; level <= maxLevel; level++)
            {
                // 经验曲线按累计值采样，并保证手工曲线不会让累计经验倒退。
                int cumulative = Mathf.Max(previousExperience, Mathf.RoundToInt(cumulativeExperienceCurve.Evaluate(level)));
                int next = level == maxLevel
                    ? 0
                    : Mathf.Max(0, Mathf.RoundToInt(cumulativeExperienceCurve.Evaluate(level + 1)) - cumulative);
                int currency = level == maxLevel
                    ? 0
                    : Mathf.Max(0, Mathf.RoundToInt(currencyCostCurve.Evaluate(level)));
                if (overrideByLevelMap.TryGetValue(level, out CharacterLevelProgressionOverride levelOverride))
                {
                    next = level == maxLevel ? 0 : levelOverride.NextExperience;
                    currency = level == maxLevel ? 0 : levelOverride.CurrencyCost;
                }

                levelResults.Add(new BakedCharacterLevelProgression(level, cumulative, next, currency));
                previousExperience = cumulative;
            }

            var attributeResults = new List<BakedCharacterAttributeProgression>(attributeGrowthCurves.Count);
            for (int index = 0; index < attributeGrowthCurves.Count; index++)
            {
                CharacterAttributeGrowthCurve growthCurve = attributeGrowthCurves[index];
                var values = new List<float>(maxLevel);
                for (int level = 1; level <= maxLevel; level++)
                    values.Add(growthCurve.BaseValueCurve.Evaluate(level));
                attributeResults.Add(new BakedCharacterAttributeProgression(growthCurve.Attribute, values));
            }

            bakedLevelProgressions = levelResults;
            bakedAttributeProgressions = attributeResults;
            bakedInputHash = CalculateInputHash();
        }

        #endregion

        #region 校验

        /// <summary>校验经验曲线、货币曲线和最大等级。</summary>
        private void ValidateScalarCurves()
        {
            if (maxLevel < 1) throw new InvalidOperationException($"CharacterGrowthProfile '{name}' 的最大等级必须大于零。");
            if (cumulativeExperienceCurve == null) throw new InvalidOperationException($"CharacterGrowthProfile '{name}' 缺少累计经验曲线。");
            if (currencyCostCurve == null) throw new InvalidOperationException($"CharacterGrowthProfile '{name}' 缺少货币消耗曲线。");

            int previousExperience = 0;
            for (int level = 1; level <= maxLevel; level++)
            {
                int cumulative = EvaluateNonNegativeIntCurve(cumulativeExperienceCurve, level, "累计经验");
                if (cumulative < previousExperience)
                    throw new InvalidOperationException($"CharacterGrowthProfile '{name}' 的累计经验曲线在等级 {level} 处倒退。");
                previousExperience = cumulative;
                EvaluateNonNegativeIntCurve(currencyCostCurve, level, "货币消耗");
            }
        }

        /// <summary>校验特殊等级覆盖的唯一性和范围。</summary>
        private void ValidateLevelOverrides()
        {
            if (levelOverrides == null) throw new InvalidOperationException($"CharacterGrowthProfile '{name}' 的等级覆盖列表为空引用。");
            var levelSet = new HashSet<int>();
            for (int index = 0; index < levelOverrides.Count; index++)
            {
                CharacterLevelProgressionOverride item = levelOverrides[index];
                if (item == null) throw new InvalidOperationException($"CharacterGrowthProfile '{name}' 的等级覆盖第 {index} 项为空。");
                if (item.Level < 1 || item.Level >= maxLevel)
                    throw new InvalidOperationException($"CharacterGrowthProfile '{name}' 的覆盖等级 {item.Level} 超出范围。");
                if (!levelSet.Add(item.Level))
                    throw new InvalidOperationException($"CharacterGrowthProfile '{name}' 重复覆盖等级 {item.Level}。");
                if (item.NextExperience < 0 || item.CurrencyCost < 0)
                    throw new InvalidOperationException($"CharacterGrowthProfile '{name}' 的等级覆盖 {item.Level} 不能使用负数消耗。");
            }
        }

        /// <summary>校验 Attribute 曲线是否对应合法的初始 Attribute Definition。</summary>
        /// <param name="definitionByAttributeIdMap">按稳定 AttributeId 建立的 Definition 索引。</param>
        private void ValidateAttributeCurves(IReadOnlyDictionary<int, GameplayAttributeDefinition> definitionByAttributeIdMap)
        {
            if (attributeGrowthCurves == null)
                throw new InvalidOperationException($"CharacterGrowthProfile '{name}' 的 Attribute 曲线列表为空引用。");
            var configuredAttributeIds = new HashSet<int>();
            for (int index = 0; index < attributeGrowthCurves.Count; index++)
            {
                CharacterAttributeGrowthCurve growthCurve = attributeGrowthCurves[index];
                if (growthCurve == null) throw new InvalidOperationException($"CharacterGrowthProfile '{name}' 的 Attribute 曲线第 {index} 项为空。");
                if (!growthCurve.Attribute.IsValid)
                    throw new InvalidOperationException($"CharacterGrowthProfile '{name}' 的 Attribute 曲线第 {index} 项使用了无效 Attribute。");
                if (!configuredAttributeIds.Add(growthCurve.Attribute.Id))
                    throw new InvalidOperationException($"CharacterGrowthProfile '{name}' 重复配置 AttributeId {growthCurve.Attribute.Id}。");
                if (!definitionByAttributeIdMap.TryGetValue(growthCurve.Attribute.Id, out GameplayAttributeDefinition definition))
                    throw new InvalidOperationException($"CharacterGrowthProfile '{name}' 的 Attribute '{growthCurve.Attribute}' 不存在于角色初始 AttributeSet。");
                if (growthCurve.BaseValueCurve == null)
                    throw new InvalidOperationException($"CharacterGrowthProfile '{name}' 的 Attribute '{growthCurve.Attribute}' 缺少 BaseValue 曲线。");

                for (int level = 1; level <= maxLevel; level++)
                {
                    float value = growthCurve.BaseValueCurve.Evaluate(level);
                    if (float.IsNaN(value) || float.IsInfinity(value))
                        throw new InvalidOperationException($"CharacterGrowthProfile '{name}' 的 Attribute '{growthCurve.Attribute}' 在等级 {level} 产生非有限 BaseValue。");
                    if (value < definition.MinValue || value > definition.MaxValue)
                        throw new InvalidOperationException($"CharacterGrowthProfile '{name}' 的 Attribute '{growthCurve.Attribute}' 在等级 {level} 超出 AttributeSet 边界。");
                    if (level == 1 && !Mathf.Approximately(value, definition.DefaultValue))
                        throw new InvalidOperationException($"CharacterGrowthProfile '{name}' 的 Attribute '{growthCurve.Attribute}' 一级 BaseValue 必须等于 AttributeSet 默认值。");
                }
            }
        }

        /// <summary>按角色初始 AttributeSet 建立 Attribute Definition 索引并校验重复项。</summary>
        /// <param name="initialAttributeSets">角色初始 AttributeSet。</param>
        /// <returns>按稳定 AttributeId 索引的 Definition。</returns>
        private Dictionary<int, GameplayAttributeDefinition> BuildAttributeDefinitionIndex(IReadOnlyList<GameplayAttributeSet> initialAttributeSets)
        {
            if (initialAttributeSets == null)
                throw new InvalidOperationException($"CharacterGrowthProfile '{name}' 的初始 AttributeSet 列表不能为 null。");
            var definitionByAttributeIdMap = new Dictionary<int, GameplayAttributeDefinition>();
            var setReferences = new HashSet<GameplayAttributeSet>();
            for (int setIndex = 0; setIndex < initialAttributeSets.Count; setIndex++)
            {
                GameplayAttributeSet attributeSet = initialAttributeSets[setIndex];
                if (attributeSet == null) throw new InvalidOperationException($"角色初始 AttributeSet 第 {setIndex} 项为空。");
                if (!setReferences.Add(attributeSet)) throw new InvalidOperationException($"角色初始 AttributeSet '{attributeSet.name}' 被重复引用。");
                IReadOnlyList<GameplayAttributeDefinition> definitions = attributeSet.Definitions;
                if (definitions == null) throw new InvalidOperationException($"AttributeSet '{attributeSet.name}' 的 Definition 列表为空引用。");
                for (int definitionIndex = 0; definitionIndex < definitions.Count; definitionIndex++)
                {
                    GameplayAttributeDefinition definition = definitions[definitionIndex];
                    if (definition == null) throw new InvalidOperationException($"AttributeSet '{attributeSet.name}' 的 Definition 第 {definitionIndex} 项为空。");
                    if (!definition.TryValidateTemplate(out string error))
                        throw new InvalidOperationException($"AttributeSet '{attributeSet.name}'：{error}");
                    if (!definitionByAttributeIdMap.TryAdd(definition.Attribute.Id, definition))
                        throw new InvalidOperationException($"角色初始 AttributeSet 重复配置 AttributeId {definition.Attribute.Id}。");
                }
            }

            return definitionByAttributeIdMap;
        }

        /// <summary>采样一个非负有限的整数曲线值。</summary>
        /// <param name="curve">待采样曲线。</param>
        /// <param name="level">采样等级。</param>
        /// <param name="fieldName">字段显示名称。</param>
        /// <returns>四舍五入后的非负整数。</returns>
        private int EvaluateNonNegativeIntCurve(AnimationCurve curve, int level, string fieldName)
        {
            float value = curve.Evaluate(level);
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f || value > int.MaxValue)
                throw new InvalidOperationException($"CharacterGrowthProfile '{name}' 的 {fieldName}曲线在等级 {level} 处不是合法非负整数范围。");
            return Mathf.RoundToInt(value);
        }

        #endregion

        #region 内部辅助

        /// <summary>计算影响烘焙结果的配置输入指纹。</summary>
        /// <returns>当前成长输入的稳定整数指纹。</returns>
        private int CalculateInputHash()
        {
            unchecked
            {
                int hash = 17;
                hash = CombineHash(hash, maxLevel);
                hash = CombineCurveHash(hash, cumulativeExperienceCurve);
                hash = CombineCurveHash(hash, currencyCostCurve);
                hash = CombineHash(hash, attributeGrowthCurves?.Count ?? -1);
                for (int index = 0; index < (attributeGrowthCurves?.Count ?? 0); index++)
                {
                    CharacterAttributeGrowthCurve growthCurve = attributeGrowthCurves[index];
                    hash = CombineHash(hash, growthCurve?.Attribute.Id ?? 0);
                    hash = CombineCurveHash(hash, growthCurve?.BaseValueCurve);
                }

                hash = CombineHash(hash, levelOverrides?.Count ?? -1);
                for (int index = 0; index < (levelOverrides?.Count ?? 0); index++)
                {
                    CharacterLevelProgressionOverride levelOverride = levelOverrides[index];
                    hash = CombineHash(hash, levelOverride?.Level ?? 0);
                    hash = CombineHash(hash, levelOverride?.NextExperience ?? 0);
                    hash = CombineHash(hash, levelOverride?.CurrencyCost ?? 0);
                }

                return hash;
            }
        }

        /// <summary>把一个整数值并入输入指纹。</summary>
        /// <param name="hash">当前指纹。</param>
        /// <param name="value">待并入整数。</param>
        /// <returns>并入后的指纹。</returns>
        private static int CombineHash(int hash, int value) => (hash * 31) + value;

        /// <summary>把动画曲线关键帧并入输入指纹。</summary>
        /// <param name="hash">当前指纹。</param>
        /// <param name="curve">待计算曲线，可为空。</param>
        /// <returns>并入曲线后的指纹。</returns>
        private static int CombineCurveHash(int hash, AnimationCurve curve)
        {
            unchecked
            {
                Keyframe[] keys = curve?.keys;
                hash = CombineHash(hash, keys?.Length ?? -1);
                if (keys == null) return hash;
                for (int index = 0; index < keys.Length; index++)
                {
                    Keyframe key = keys[index];
                    hash = CombineHash(hash, key.time.GetHashCode());
                    hash = CombineHash(hash, key.value.GetHashCode());
                    hash = CombineHash(hash, key.inTangent.GetHashCode());
                    hash = CombineHash(hash, key.outTangent.GetHashCode());
                    hash = CombineHash(hash, key.weightedMode.GetHashCode());
                    hash = CombineHash(hash, key.inWeight.GetHashCode());
                    hash = CombineHash(hash, key.outWeight.GetHashCode());
                }

                return hash;
            }
        }

        /// <summary>根据等级覆盖列表创建等级索引。</summary>
        /// <returns>按等级索引的覆盖数据。</returns>
        private Dictionary<int, CharacterLevelProgressionOverride> BuildOverrideIndex()
        {
            var overrideByLevelMap = new Dictionary<int, CharacterLevelProgressionOverride>();
            for (int index = 0; index < levelOverrides.Count; index++)
                overrideByLevelMap.Add(levelOverrides[index].Level, levelOverrides[index]);
            return overrideByLevelMap;
        }

        #endregion
    }
}
