using System;
using System.Collections.Generic;
using RPG.ItemSystem;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.AttributeSystem;

namespace RPG.Character
{
    /// <summary>描述一个 Attribute 的等级 BaseValue 成长曲线。</summary>
    [Serializable]
    public sealed class CharacterAttributeGrowthCurve
    {
        #region 配置字段

        [SerializeField, LabelText("Attribute")] private GameplayAttribute attribute = GameplayAttribute.Empty;
        [SerializeField, LabelText("BaseValue 曲线")] private AnimationCurve baseValueCurve = AnimationCurve.Linear(1f, 0f, 90f, 0f);

        #endregion

        #region 属性

        /// <summary>获取曲线对应的稳定 Attribute。</summary>
        public GameplayAttribute Attribute => attribute;

        /// <summary>获取按等级采样 BaseValue 的曲线。</summary>
        public AnimationCurve BaseValueCurve => baseValueCurve;

        #endregion
    }

    /// <summary>覆盖单个等级的升级经验和货币消耗。</summary>
    [Serializable]
    public sealed class CharacterLevelProgressionOverride
    {
        #region 配置字段

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

    /// <summary>由成长曲线生成的单级经验和货币结果。</summary>
    [Serializable]
    public sealed class BakedCharacterLevelProgression
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

        /// <summary>创建烘焙后的等级结果。</summary>
        /// <param name="level">等级。</param>
        /// <param name="cumulativeExperience">累计经验。</param>
        /// <param name="nextExperience">下一级经验。</param>
        /// <param name="currencyCost">升级货币消耗。</param>
        public BakedCharacterLevelProgression(int level, int cumulativeExperience, int nextExperience, int currencyCost)
        {
            this.level = level;
            this.cumulativeExperience = cumulativeExperience;
            this.nextExperience = nextExperience;
            this.currencyCost = currencyCost;
        }

        #endregion
    }

    /// <summary>保存一个 Attribute 从等级 1 到最大等级的烘焙 BaseValue。</summary>
    [Serializable]
    public sealed class BakedCharacterAttributeProgression
    {
        #region 字段

        [SerializeField, LabelText("Attribute")] private GameplayAttribute attribute = GameplayAttribute.Empty;
        [SerializeField, LabelText("等级 BaseValue")] private List<float> baseValues = new();

        #endregion

        #region 属性

        /// <summary>获取烘焙结果对应的稳定 Attribute。</summary>
        public GameplayAttribute Attribute => attribute;

        /// <summary>获取等级 1 起按顺序排列的 BaseValue；下标 0 对应等级 1。</summary>
        public IReadOnlyList<float> BaseValues => baseValues;

        #endregion

        #region 生命周期

        /// <summary>创建一个 Attribute 烘焙结果并复制数值列表。</summary>
        /// <param name="attribute">稳定 Attribute。</param>
        /// <param name="values">等级 1 起的 BaseValue 列表。</param>
        public BakedCharacterAttributeProgression(GameplayAttribute attribute, IReadOnlyList<float> values)
        {
            this.attribute = attribute;
            baseValues = new List<float>(values ?? throw new ArgumentNullException(nameof(values)));
        }

        #endregion
    }

    /// <summary>描述一次角色突破的等级门槛、突破后上限与材料消耗。</summary>
    [Serializable]
    public sealed class CharacterAscensionStage
    {
        #region 配置字段

        [SerializeField, MinValue(1), LabelText("所需等级")] private int requiredLevel = 20;
        [SerializeField, MinValue(1), LabelText("突破后等级上限")] private int maxLevelAfter = 40;
        [SerializeField, LabelText("突破消耗")] private GrowthCost cost = new();

        #endregion

        #region 属性

        /// <summary>获取触发突破所需等级。</summary>
        public int RequiredLevel => requiredLevel;

        /// <summary>获取突破后的等级上限。</summary>
        public int MaxLevelAfter => maxLevelAfter;

        /// <summary>获取突破消耗。</summary>
        public GrowthCost Cost => cost;

        #endregion
    }
}
