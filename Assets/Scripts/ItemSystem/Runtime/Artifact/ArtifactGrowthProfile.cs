using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.ItemSystem
{
    /// <summary>按累计经验和货币曲线生成圣遗物逐级成长表的配置资产。</summary>
    [CreateAssetMenu(fileName = "ArtifactGrowthProfile", menuName = "RPG/ItemSystem/Artifact Growth Profile", order = 21)]
    public sealed class ArtifactGrowthProfile : ScriptableObject
    {
        [SerializeField, MinValue(1), LabelText("最大等级"), ReadOnly] private int maxLevel = 20;
        [SerializeField, LabelText("累计经验曲线")] private AnimationCurve cumulativeExperienceCurve = AnimationCurve.Linear(0f, 0f, 20f, 270475f);
        [SerializeField, LabelText("货币消耗曲线")] private AnimationCurve currencyCostCurve = AnimationCurve.Linear(0f, 0f, 20f, 0f);
        [SerializeField, LabelText("特殊等级覆盖")] private List<ArtifactLevelProgressionOverride> levelOverrides = new List<ArtifactLevelProgressionOverride>();
        [SerializeField, LabelText("已烘焙等级结果")] private List<BakedArtifactLevelProgression> bakedProgressions = new List<BakedArtifactLevelProgression>();

        /// <summary>获取最大等级。</summary>
        public int MaxLevel => maxLevel;

        /// <summary>获取累计经验曲线。</summary>
        public AnimationCurve CumulativeExperienceCurve => cumulativeExperienceCurve;

        /// <summary>获取货币消耗曲线。</summary>
        public AnimationCurve CurrencyCostCurve => currencyCostCurve;

        /// <summary>获取特殊等级覆盖。</summary>
        public IReadOnlyList<ArtifactLevelProgressionOverride> LevelOverrides => levelOverrides;

        /// <summary>获取烘焙结果。</summary>
        public IReadOnlyList<BakedArtifactLevelProgression> BakedProgressions => bakedProgressions;

        /// <summary>按曲线生成圣遗物等级结果。</summary>
        /// <exception cref="InvalidOperationException">曲线或覆盖配置无效时抛出。</exception>
        public void Bake()
        {
            Validate();
            var overrides = new Dictionary<int, ArtifactLevelProgressionOverride>();
            for (int index = 0; index < levelOverrides.Count; index++)
            {
                overrides.Add(levelOverrides[index].Level, levelOverrides[index]);
            }

            var results = new List<BakedArtifactLevelProgression>(maxLevel + 1);
            int previousExperience = 0;
            for (int level = 0; level <= maxLevel; level++)
            {
                int cumulative = Mathf.Max(previousExperience, Mathf.RoundToInt(cumulativeExperienceCurve.Evaluate(level)));
                int next = level == maxLevel ? 0 : Mathf.Max(0, Mathf.RoundToInt(cumulativeExperienceCurve.Evaluate(level + 1)) - cumulative);
                int currency = level == maxLevel ? 0 : Mathf.Max(0, Mathf.RoundToInt(currencyCostCurve.Evaluate(level)));
                if (overrides.TryGetValue(level, out ArtifactLevelProgressionOverride item))
                {
                    next = level == maxLevel ? 0 : item.NextExperience;
                    currency = level == maxLevel ? 0 : item.CurrencyCost;
                }

                results.Add(new BakedArtifactLevelProgression(level, cumulative, next, currency));
                previousExperience = cumulative;
            }

            bakedProgressions = results;
        }

        /// <summary>验证成长曲线和等级覆盖。</summary>
        /// <exception cref="InvalidOperationException">配置无效时抛出。</exception>
        public void Validate()
        {
            if (maxLevel < 1) throw new InvalidOperationException($"ArtifactGrowthProfile '{name}' 的最大等级必须大于零。");
            if (cumulativeExperienceCurve == null) throw new InvalidOperationException($"ArtifactGrowthProfile '{name}' 缺少累计经验曲线。");
            if (currencyCostCurve == null) throw new InvalidOperationException($"ArtifactGrowthProfile '{name}' 缺少货币消耗曲线。");
            if (levelOverrides == null) throw new InvalidOperationException($"ArtifactGrowthProfile '{name}' 的等级覆盖列表为空引用。");
            var levels = new HashSet<int>();
            for (int index = 0; index < levelOverrides.Count; index++)
            {
                ArtifactLevelProgressionOverride item = levelOverrides[index];
                if (item == null) throw new InvalidOperationException($"ArtifactGrowthProfile '{name}' 的等级覆盖第 {index} 项为空。");
                if (item.Level < 0 || item.Level >= maxLevel) throw new InvalidOperationException($"ArtifactGrowthProfile '{name}' 的覆盖等级 {item.Level} 超出范围。");
                if (!levels.Add(item.Level)) throw new InvalidOperationException($"ArtifactGrowthProfile '{name}' 重复覆盖等级 {item.Level}。");
            }
        }
    }
}
