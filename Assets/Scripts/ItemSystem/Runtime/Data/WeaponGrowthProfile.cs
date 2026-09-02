using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.ItemSystem
{
    /// <summary>按累计经验曲线生成武器逐级成长表的配置资产。</summary>
    [CreateAssetMenu(fileName = "WeaponGrowthProfile", menuName = "RPG/ItemSystem/Weapon Growth Profile", order = 20)]
    public sealed class WeaponGrowthProfile : ScriptableObject
    {
        #region 字段

        [SerializeField, MinValue(1), LabelText("最大等级"), ReadOnly] private int maxLevel = 90;
        [SerializeField, LabelText("累计经验曲线")] private AnimationCurve cumulativeExperienceCurve = AnimationCurve.Linear(1f, 0f, 90f, 1800000f);
        [SerializeField, LabelText("货币消耗曲线")] private AnimationCurve currencyCostCurve = AnimationCurve.Linear(1f, 1200f, 90f, 0f);
        [SerializeField, LabelText("特殊等级覆盖")] private List<WeaponLevelOverride> levelOverrides = new();
        [SerializeField, LabelText("已烘焙等级结果")] private List<BakedWeaponLevelProgression> bakedProgressions = new();

        #endregion

        #region 属性

        /// <summary>获取最大等级。</summary>
        public int MaxLevel => maxLevel;

        /// <summary>获取累计经验曲线。</summary>
        public AnimationCurve CumulativeExperienceCurve => cumulativeExperienceCurve;

        /// <summary>获取升级货币消耗曲线。</summary>
        public AnimationCurve CurrencyCostCurve => currencyCostCurve;

        /// <summary>获取特殊等级覆盖集合。</summary>
        public IReadOnlyList<WeaponLevelOverride> LevelOverrides => levelOverrides;

        /// <summary>获取已经烘焙的等级结果。</summary>
        public IReadOnlyList<BakedWeaponLevelProgression> BakedProgressions => bakedProgressions;

        #endregion

        #region 公开操作

        /// <summary>按曲线和等级覆盖生成稳定整数成长表。</summary>
        /// <exception cref="InvalidOperationException">曲线或等级配置无效时抛出。</exception>
        public void Bake()
        {
            Validate();
            Dictionary<int, WeaponLevelOverride> overrides = BuildOverrideIndex();
            var results = new List<BakedWeaponLevelProgression>(maxLevel);
            int previousExperience = 0;
            for (int level = 1; level <= maxLevel; level++)
            {
                // 经验曲线按累计值采样，并保证手工调整不会让累计经验倒退。
                int cumulative = Mathf.Max(previousExperience, Mathf.RoundToInt(cumulativeExperienceCurve.Evaluate(level)));
                int next = level == maxLevel ? 0 : Mathf.Max(0, Mathf.RoundToInt(cumulativeExperienceCurve.Evaluate(level + 1)) - cumulative);
                int currency = level == maxLevel ? 0 : Mathf.Max(0, Mathf.RoundToInt(currencyCostCurve.Evaluate(level)));
                if (overrides.TryGetValue(level, out WeaponLevelOverride levelOverride))
                {
                    next = level == maxLevel ? 0 : levelOverride.NextExperience;
                    currency = level == maxLevel ? 0 : levelOverride.CurrencyCost;
                }

                results.Add(new BakedWeaponLevelProgression(level, cumulative, next, currency));
                previousExperience = cumulative;
            }

            bakedProgressions = results;
        }

        /// <summary>验证曲线、等级范围和烘焙覆盖配置。</summary>
        /// <exception cref="InvalidOperationException">配置不满足成长表契约时抛出。</exception>
        public void Validate()
        {
            if (maxLevel < 1) throw new InvalidOperationException($"WeaponGrowthProfile '{name}' 的最大等级必须大于零。");
            if (cumulativeExperienceCurve == null) throw new InvalidOperationException($"WeaponGrowthProfile '{name}' 缺少累计经验曲线。");
            if (currencyCostCurve == null) throw new InvalidOperationException($"WeaponGrowthProfile '{name}' 缺少货币消耗曲线。");
            if (levelOverrides == null) throw new InvalidOperationException($"WeaponGrowthProfile '{name}' 的等级覆盖列表为空引用。");
            var levels = new HashSet<int>();
            for (int index = 0; index < levelOverrides.Count; index++)
            {
                WeaponLevelOverride item = levelOverrides[index];
                if (item == null) throw new InvalidOperationException($"WeaponGrowthProfile '{name}' 的等级覆盖第 {index} 项为空。");
                if (item.Level < 1 || item.Level >= maxLevel) throw new InvalidOperationException($"WeaponGrowthProfile '{name}' 的覆盖等级 {item.Level} 超出范围。");
                if (!levels.Add(item.Level)) throw new InvalidOperationException($"WeaponGrowthProfile '{name}' 重复覆盖等级 {item.Level}。");
            }
        }

        #endregion

        #region 内部辅助

        /// <summary>根据覆盖列表创建等级索引。</summary>
        /// <returns>覆盖等级索引。</returns>
        private Dictionary<int, WeaponLevelOverride> BuildOverrideIndex()
        {
            var index = new Dictionary<int, WeaponLevelOverride>();
            for (int i = 0; i < levelOverrides.Count; i++) index.Add(levelOverrides[i].Level, levelOverrides[i]);
            return index;
        }

        #endregion
    }
}
