using System;
using System.Collections.Generic;
using System.Globalization;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.Baking;
using WS_Modules.GAS.GameplayEffect;

namespace RPG.ItemSystem
{
    /// <summary>仿原神五部位的简化圣遗物静态 Definition。</summary>
    [CreateAssetMenu(fileName = "ArtifactDefinition", menuName = "RPG/ItemSystem/Artifact", order = 3)]
#if UNITY_EDITOR
    public sealed class ArtifactDefinition : ItemDefinition, IBakedResultDataSource
#else
    public sealed class ArtifactDefinition : ItemDefinition
#endif
    {
        [SerializeField, LabelText("圣遗物部位")] private ArtifactSlot slot;
        [SerializeField, MinValue(1), LabelText("最大等级")] private int maxLevel = 20;
        [SerializeField, LabelText("成长配置")] private ArtifactGrowthProfile growthProfile;
        [SerializeField, LabelText("圣遗物等级属性效果")] private List<GameplayEffectData> levelEffects = new();

        /// <summary>获取圣遗物部位。</summary>
        public ArtifactSlot Slot => slot;

        /// <summary>获取最大等级。</summary>
        public int MaxLevel => maxLevel;

        /// <summary>获取成长配置。</summary>
        public ArtifactGrowthProfile GrowthProfile => growthProfile;

        /// <summary>获取等级属性效果。</summary>
        public IReadOnlyList<GameplayEffectData> LevelEffects => levelEffects;

#if UNITY_EDITOR
        #region 烘焙结果数据源

        /// <summary>获取圣遗物成长结果窗口标题。</summary>
        public string BakedResultTitle => $"{(string.IsNullOrWhiteSpace(DisplayName) ? name : DisplayName)} - 圣遗物成长烘焙结果";

        /// <summary>获取 Bake 会修改的 GrowthProfile。</summary>
        public IReadOnlyList<UnityEngine.Object> BakeTargets => growthProfile == null
            ? Array.Empty<UnityEngine.Object>()
            : new UnityEngine.Object[] { growthProfile };

        /// <summary>委托 GrowthProfile 生成圣遗物等级结果。</summary>
        /// <exception cref="InvalidOperationException">GrowthProfile 缺失时抛出。</exception>
        public void Bake()
        {
            if (growthProfile == null) throw new InvalidOperationException($"圣遗物定义 '{name}' 缺少 ArtifactGrowthProfile。");
            growthProfile.Bake();
        }

        /// <summary>将最近一次圣遗物成长烘焙结果转换为扁平表。</summary>
        /// <returns>圣遗物等级、经验和货币表。</returns>
        public BakedResultTableData CreateBakedResultTableData()
        {
            if (growthProfile == null) throw new InvalidOperationException($"圣遗物定义 '{name}' 缺少 ArtifactGrowthProfile。");
            var headers = new[] { "等级", "累计经验", "下一级经验", "货币消耗" };
            var rows = new List<BakedResultRowData>(growthProfile.BakedProgressions.Count);
            for (int index = 0; index < growthProfile.BakedProgressions.Count; index++)
            {
                BakedArtifactLevelProgression item = growthProfile.BakedProgressions[index];
                rows.Add(new BakedResultRowData(new[]
                {
                    item.Level.ToString("N0", CultureInfo.InvariantCulture),
                    item.CumulativeExperience.ToString("N0", CultureInfo.InvariantCulture),
                    item.NextExperience.ToString("N0", CultureInfo.InvariantCulture),
                    item.CurrencyCost.ToString("N0", CultureInfo.InvariantCulture)
                }));
            }

            return new BakedResultTableData(BakedResultTitle, headers, rows);
        }

        #endregion
#endif

        /// <summary>验证圣遗物分类和成长配置。</summary>
        /// <exception cref="InvalidOperationException">配置无效时抛出。</exception>
        protected override void ValidateSpecific()
        {
            if (Category != ItemCategory.Artifact) throw new InvalidOperationException($"圣遗物定义 '{name}' 必须使用 Artifact 分类。");
            if (!Enum.IsDefined(typeof(ArtifactSlot), slot)) throw new InvalidOperationException($"圣遗物定义 '{name}' 的部位无效。");
            if (maxLevel < 1) throw new InvalidOperationException($"圣遗物定义 '{name}' 的最大等级必须大于零。");
            if (growthProfile == null) throw new InvalidOperationException($"圣遗物定义 '{name}' 缺少 ArtifactGrowthProfile。");
            if (growthProfile.MaxLevel != maxLevel) throw new InvalidOperationException($"圣遗物定义 '{name}' 与成长配置最大等级不一致。");
            growthProfile.Validate();
            ValidateEffectList(levelEffects, "levelEffects");
        }

        /// <summary>校验圣遗物 GE 列表中的引用。</summary>
        /// <param name="effects">待校验的效果列表。</param>
        /// <param name="fieldName">字段名称。</param>
        private void ValidateEffectList(IReadOnlyList<GameplayEffectData> effects, string fieldName)
        {
            if (effects == null) throw new InvalidOperationException($"圣遗物定义 '{name}' 的效果字段 '{fieldName}' 不能为 null。");
            for (int index = 0; index < effects.Count; index++)
                if (effects[index] == null) throw new InvalidOperationException($"圣遗物定义 '{name}' 的效果字段 '{fieldName}' 第 {index} 项为空。");
        }
    }
}
