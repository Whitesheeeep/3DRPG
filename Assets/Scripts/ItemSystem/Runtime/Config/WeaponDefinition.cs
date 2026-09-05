using System;
using System.Collections.Generic;
using System.Globalization;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.Baking;
using WS_Modules.GAS.GameplayEffect;

namespace RPG.ItemSystem
{
    /// <summary>保存武器静态资源、成长规则和未来装备 GE 引用的定义资产。</summary>
    [CreateAssetMenu(fileName = "WeaponDefinition", menuName = "RPG/ItemSystem/Weapon", order = 1)]
#if UNITY_EDITOR
    public sealed class WeaponDefinition : ItemDefinition, IBakedResultDataSource
#else
    public sealed class WeaponDefinition : ItemDefinition
#endif
    {
        [SerializeField, LabelText("武器类型")] private WeaponType weaponType;
        [SerializeField, MinValue(1), LabelText("最大等级")] private int maxLevel = 90;
        [SerializeField, MinValue(0), LabelText("最大突破阶数")] private int maxAscensionRank = 6;
        [SerializeField, MinValue(1), LabelText("最大精炼阶数")] private int maxRefinementRank = 5;
        [SerializeField, LabelText("成长配置")] private WeaponGrowthProfile growthProfile;
        [SerializeField, LabelText("武器等级属性效果")] private List<GameplayEffectData> levelEffects = new();
        [SerializeField, LabelText("武器精炼属性效果")] private List<GameplayEffectData> refinementEffects = new();
        [SerializeField, LabelText("突破阶段与消耗")] private List<WeaponAscensionStage> ascensionStages = new();
        [SerializeField, LabelText("精炼阶段与消耗")] private List<WeaponRefinementStage> refinementStages = new();

        /// <summary>获取武器类型。</summary>
        public WeaponType WeaponType => weaponType;

        /// <summary>获取最大等级。</summary>
        public int MaxLevel => maxLevel;

        /// <summary>获取最大突破阶数。</summary>
        public int MaxAscensionRank => maxAscensionRank;

        /// <summary>获取最大精炼阶数。</summary>
        public int MaxRefinementRank => maxRefinementRank;

        /// <summary>获取等级成长曲线配置。</summary>
        public WeaponGrowthProfile GrowthProfile => growthProfile;

        /// <summary>获取武器等级属性 Gameplay Effect 列表。</summary>
        public IReadOnlyList<GameplayEffectData> LevelEffects => levelEffects;

        /// <summary>获取武器精炼属性 Gameplay Effect 列表。</summary>
        public IReadOnlyList<GameplayEffectData> RefinementEffects => refinementEffects;

        /// <summary>获取突破阶段配置。</summary>
        public IReadOnlyList<WeaponAscensionStage> AscensionStages => ascensionStages;

        /// <summary>获取精炼阶段配置。</summary>
        public IReadOnlyList<WeaponRefinementStage> RefinementStages => refinementStages;

#if UNITY_EDITOR
        #region 烘焙结果数据源

        /// <summary>获取武器成长结果窗口标题。</summary>
        public string BakedResultTitle => $"{(string.IsNullOrWhiteSpace(DisplayName) ? name : DisplayName)} - 武器成长烘焙结果";

        /// <summary>获取 Bake 会修改的 GrowthProfile。</summary>
        public IReadOnlyList<UnityEngine.Object> BakeTargets => growthProfile == null
            ? Array.Empty<UnityEngine.Object>()
            : new UnityEngine.Object[] { growthProfile };

        /// <summary>委托 GrowthProfile 生成武器等级结果。</summary>
        /// <exception cref="InvalidOperationException">GrowthProfile 缺失时抛出。</exception>
        public void Bake()
        {
            if (growthProfile == null) throw new InvalidOperationException($"武器定义 '{name}' 缺少 WeaponGrowthProfile。");
            growthProfile.Bake();
        }

        /// <summary>将最近一次武器成长烘焙结果转换为扁平表。</summary>
        /// <returns>武器等级、经验、货币和突破状态表。</returns>
        public BakedResultTableData CreateBakedResultTableData()
        {
            if (growthProfile == null) throw new InvalidOperationException($"武器定义 '{name}' 缺少 WeaponGrowthProfile。");
            var headers = new[] { "等级", "累计经验", "下一级经验", "货币消耗", "突破状态" };
            var rows = new List<BakedResultRowData>(growthProfile.BakedProgressions.Count);
            for (int index = 0; index < growthProfile.BakedProgressions.Count; index++)
            {
                BakedWeaponLevelProgression item = growthProfile.BakedProgressions[index];
                rows.Add(new BakedResultRowData(new[]
                {
                    item.Level.ToString("N0", CultureInfo.InvariantCulture),
                    item.CumulativeExperience.ToString("N0", CultureInfo.InvariantCulture),
                    item.NextExperience.ToString("N0", CultureInfo.InvariantCulture),
                    item.CurrencyCost.ToString("N0", CultureInfo.InvariantCulture),
                    IsBreakthroughLevel(item.Level) ? "突破点" : "—"
                }));
            }

            return new BakedResultTableData(BakedResultTitle, headers, rows);
        }

        /// <summary>判断等级是否命中武器突破阶段。</summary>
        /// <param name="level">待检查等级。</param>
        /// <returns>命中突破点时返回 true。</returns>
        private bool IsBreakthroughLevel(int level)
        {
            for (int index = 0; index < ascensionStages.Count; index++)
                if (ascensionStages[index] != null && ascensionStages[index].RequiredLevel == level)
                    return true;
            return false;
        }

        #endregion
#endif

        /// <summary>验证武器成长字段和阶段序列。</summary>
        /// <exception cref="InvalidOperationException">武器配置不合法时抛出。</exception>
        protected override void ValidateSpecific()
        {
            if (Category != ItemCategory.Weapon) throw new InvalidOperationException($"武器定义 '{name}' 必须使用 Weapon 分类。");
            if (MaxLevel < 1) throw new InvalidOperationException($"武器定义 '{name}' 的最大等级必须大于零。");
            if (MaxAscensionRank < 0 || MaxRefinementRank < 1) throw new InvalidOperationException($"武器定义 '{name}' 的突破或精炼上限无效。");
            if (growthProfile == null) throw new InvalidOperationException($"武器定义 '{name}' 缺少 WeaponGrowthProfile。");
            if (growthProfile.MaxLevel != MaxLevel) throw new InvalidOperationException($"武器定义 '{name}' 与成长配置的最大等级不一致。");
            growthProfile.Validate();
            ValidateStages();
            ValidateEffectList(levelEffects, "levelEffects");
            ValidateEffectList(refinementEffects, "refinementEffects");
        }

        /// <summary>验证突破和精炼阶段的边界与顺序。</summary>
        /// <exception cref="InvalidOperationException">阶段列表不合法时抛出。</exception>
        private void ValidateStages()
        {
            if (ascensionStages == null || refinementStages == null) throw new InvalidOperationException($"武器定义 '{name}' 的阶段列表为空引用。");
            int previousLevel = 0;
            for (int index = 0; index < ascensionStages.Count; index++)
            {
                WeaponAscensionStage stage = ascensionStages[index];
                if (stage == null) throw new InvalidOperationException($"武器定义 '{name}' 的突破阶段第 {index} 项为空。");
                if (stage.RequiredLevel <= previousLevel || stage.RequiredLevel > MaxLevel) throw new InvalidOperationException($"武器定义 '{name}' 的突破等级顺序无效。");
                previousLevel = stage.RequiredLevel;
            }

            for (int index = 0; index < refinementStages.Count; index++)
            {
                WeaponRefinementStage stage = refinementStages[index];
                if (stage == null) throw new InvalidOperationException($"武器定义 '{name}' 的精炼阶段第 {index} 项为空。");
                if (stage.Rank < 1 || stage.Rank > MaxRefinementRank) throw new InvalidOperationException($"武器定义 '{name}' 的精炼阶数超出范围。");
            }
        }

        /// <summary>校验武器 GE 列表中的引用。</summary>
        /// <param name="effects">待校验的效果列表。</param>
        /// <param name="fieldName">用于错误信息的字段名称。</param>
        private void ValidateEffectList(IReadOnlyList<GameplayEffectData> effects, string fieldName)
        {
            if (effects == null) throw new InvalidOperationException($"武器定义 '{name}' 的效果字段 '{fieldName}' 不能为 null。");
            for (int index = 0; index < effects.Count; index++)
                if (effects[index] == null) throw new InvalidOperationException($"武器定义 '{name}' 的效果字段 '{fieldName}' 第 {index} 项为空。");
        }
    }

    /// <summary>武器突破阶段配置。</summary>
    [Serializable]
    public sealed class WeaponAscensionStage
    {
        [SerializeField, MinValue(1), LabelText("所需等级")] private int requiredLevel = 20;
        [SerializeField, MinValue(1), LabelText("突破后等级上限")] private int maxLevelAfter = 40;
        [SerializeField, LabelText("突破消耗")] private WeaponGrowthCost cost = new();

        /// <summary>获取触发突破所需等级。</summary>
        public int RequiredLevel => requiredLevel;

        /// <summary>获取突破后的等级上限。</summary>
        public int MaxLevelAfter => maxLevelAfter;

        /// <summary>获取突破消耗。</summary>
        public WeaponGrowthCost Cost => cost;
    }

    /// <summary>武器精炼阶段配置。</summary>
    [Serializable]
    public sealed class WeaponRefinementStage
    {
        [SerializeField, MinValue(1), LabelText("精炼阶数")] private int rank = 1;
        [SerializeField, MinValue(1), LabelText("所需同名武器数量")] private int requiredDuplicateCount = 1;
        [SerializeField, LabelText("精炼消耗")] private WeaponGrowthCost cost = new();

        /// <summary>获取精炼阶数。</summary>
        public int Rank => rank;

        /// <summary>获取所需同名武器数量。</summary>
        public int RequiredDuplicateCount => requiredDuplicateCount;

        /// <summary>获取精炼消耗。</summary>
        public WeaponGrowthCost Cost => cost;
    }
}
