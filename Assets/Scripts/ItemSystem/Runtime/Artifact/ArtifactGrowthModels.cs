using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.ItemSystem
{
    /// <summary>圣遗物某一级的手工成长覆盖。</summary>
    [Serializable]
    public sealed class ArtifactLevelProgressionOverride
    {
        [SerializeField, MinValue(1), LabelText("等级")] private int level = 1;
        [SerializeField, MinValue(0), LabelText("下一级所需经验")] private int nextExperience;
        [SerializeField, MinValue(0), LabelText("货币消耗")] private int currencyCost;

        /// <summary>获取覆盖等级。</summary>
        public int Level => level;

        /// <summary>获取下一级经验。</summary>
        public int NextExperience => nextExperience;

        /// <summary>获取货币消耗。</summary>
        public int CurrencyCost => currencyCost;
    }

    /// <summary>圣遗物成长曲线烘焙出的单级结果。</summary>
    [Serializable]
    public sealed class BakedArtifactLevelProgression
    {
        [SerializeField, MinValue(0), LabelText("等级")] private int level;
        [SerializeField, MinValue(0), LabelText("累计经验")] private int cumulativeExperience;
        [SerializeField, MinValue(0), LabelText("下一级所需经验")] private int nextExperience;
        [SerializeField, MinValue(0), LabelText("货币消耗")] private int currencyCost;

        /// <summary>创建圣遗物烘焙结果。</summary>
        /// <param name="level">等级。</param>
        /// <param name="cumulativeExperience">累计经验。</param>
        /// <param name="nextExperience">下一级经验。</param>
        /// <param name="currencyCost">货币消耗。</param>
        public BakedArtifactLevelProgression(int level, int cumulativeExperience, int nextExperience, int currencyCost)
        {
            this.level = level;
            this.cumulativeExperience = cumulativeExperience;
            this.nextExperience = nextExperience;
            this.currencyCost = currencyCost;
        }

        /// <summary>获取等级。</summary>
        public int Level => level;

        /// <summary>获取累计经验。</summary>
        public int CumulativeExperience => cumulativeExperience;

        /// <summary>获取下一级经验。</summary>
        public int NextExperience => nextExperience;

        /// <summary>获取货币消耗。</summary>
        public int CurrencyCost => currencyCost;
    }
}
