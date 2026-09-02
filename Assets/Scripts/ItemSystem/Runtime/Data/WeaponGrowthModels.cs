using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.ItemSystem
{
    /// <summary>单个等级覆盖的烘焙经验和货币消耗。</summary>
    [Serializable]
    public sealed class WeaponLevelOverride
    {
        #region 字段

        [SerializeField, MinValue(1), LabelText("等级")] private int level = 1;
        [SerializeField, MinValue(0), LabelText("下一级所需经验")] private int nextExperience;
        [SerializeField, MinValue(0), LabelText("货币消耗")] private int currencyCost;

        #endregion

        #region 属性

        /// <summary>获取覆盖等级。</summary>
        public int Level => level;

        /// <summary>获取下一级经验覆盖值。</summary>
        public int NextExperience => nextExperience;

        /// <summary>获取升级货币覆盖值。</summary>
        public int CurrencyCost => currencyCost;

        #endregion
    }

    /// <summary>由曲线生成的单级成长结果，供运行时只读使用。</summary>
    [Serializable]
    public sealed class BakedWeaponLevelProgression
    {
        #region 字段

        [SerializeField, MinValue(1), LabelText("等级")] private int level;
        [SerializeField, MinValue(0), LabelText("累计经验")] private int cumulativeExperience;
        [SerializeField, MinValue(0), LabelText("下一级所需经验")] private int nextExperience;
        [SerializeField, MinValue(0), LabelText("货币消耗")] private int currencyCost;

        #endregion

        #region 属性

        /// <summary>获取等级。</summary>
        public int Level => level;

        /// <summary>获取累计经验。</summary>
        public int CumulativeExperience => cumulativeExperience;

        /// <summary>获取升到下一级所需经验。</summary>
        public int NextExperience => nextExperience;

        /// <summary>获取升级货币消耗。</summary>
        public int CurrencyCost => currencyCost;

        #endregion

        #region 生命周期

        /// <summary>创建烘焙结果。</summary>
        /// <param name="level">等级。</param>
        /// <param name="cumulativeExperience">累计经验。</param>
        /// <param name="nextExperience">下一级经验。</param>
        /// <param name="currencyCost">货币消耗。</param>
        public BakedWeaponLevelProgression(int level, int cumulativeExperience, int nextExperience, int currencyCost)
        {
            this.level = level;
            this.cumulativeExperience = cumulativeExperience;
            this.nextExperience = nextExperience;
            this.currencyCost = currencyCost;
        }

        #endregion
    }
}
