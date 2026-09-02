using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.GameplayEffect;

namespace RPG.ItemSystem
{
    /// <summary>仿原神五部位的简化圣遗物静态 Definition。</summary>
    [CreateAssetMenu(fileName = "ArtifactDefinition", menuName = "RPG/ItemSystem/Artifact", order = 3)]
    public sealed class ArtifactDefinition : ItemDefinition
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
