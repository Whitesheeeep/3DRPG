using System;
using System.Collections.Generic;
using RPG.Game.UI.Bag;
using RPG.Game.UI.WeaponDevelopment;
using UnityEngine;

namespace RPG.Game.UI.Common
{
    /// <summary>描述共用等级培养页的等级、经验、属性、素材、费用与操作状态。</summary>
    public class DevelopmentEnhancementViewData
    {
        /// <summary>创建培养页面的只读展示快照。</summary>
        /// <param name="title">页面标题。</param>
        /// <param name="subtitle">页面副标题或状态说明。</param>
        /// <param name="currentLevel">当前等级。</param>
        /// <param name="projectedLevel">使用素材后的预计等级。</param>
        /// <param name="selectedExperience">已选素材提供的经验总量。</param>
        /// <param name="projectedExperience">预计等级内的当前经验。</param>
        /// <param name="projectedNextExperience">预计等级的下一等级经验目标。</param>
        /// <param name="progress">归一化经验进度。</param>
        /// <param name="attributeLines">升级前后的属性对比行。</param>
        /// <param name="selectedMaterials">当前已选择的素材卡片。</param>
        /// <param name="currencyOwned">当前货币余额。</param>
        /// <param name="currencyCost">预计消耗的货币数量。</param>
        /// <param name="actionInteractable">主要操作是否可用。</param>
        /// <param name="actionLabel">主要操作按钮文案。</param>
        /// <param name="selectMaterialInteractable">素材选择入口是否可用。</param>
        /// <param name="autoAddInteractable">自动添加按钮是否可用。</param>
        public DevelopmentEnhancementViewData(string title, string subtitle, int currentLevel, int projectedLevel,
            long selectedExperience, int projectedExperience, int projectedNextExperience, float progress,
            IReadOnlyList<EquipmentAttributeUpgradeLineViewData> attributeLines,
            IReadOnlyList<BagItemViewData> selectedMaterials, long currencyOwned, long currencyCost,
            bool actionInteractable, string actionLabel, bool selectMaterialInteractable,
            bool autoAddInteractable)
        {
            Title = title ?? string.Empty;
            Subtitle = subtitle ?? string.Empty;
            CurrentLevel = currentLevel;
            ProjectedLevel = projectedLevel;
            SelectedExperience = selectedExperience;
            ProjectedExperience = projectedExperience;
            ProjectedNextExperience = projectedNextExperience;
            Progress = Mathf.Clamp01(progress);
            AttributeLines = attributeLines ?? Array.Empty<EquipmentAttributeUpgradeLineViewData>();
            SelectedMaterials = selectedMaterials ?? Array.Empty<BagItemViewData>();
            CurrencyOwned = Math.Max(0L, currencyOwned);
            CurrencyCost = Math.Max(0L, currencyCost);
            ActionInteractable = actionInteractable;
            ActionLabel = actionLabel ?? string.Empty;
            SelectMaterialInteractable = selectMaterialInteractable;
            AutoAddInteractable = autoAddInteractable;
        }

        /// <summary>页面标题。</summary>
        public string Title { get; }
        /// <summary>页面副标题或状态说明。</summary>
        public string Subtitle { get; }
        /// <summary>当前等级。</summary>
        public int CurrentLevel { get; }
        /// <summary>使用素材后的预计等级。</summary>
        public int ProjectedLevel { get; }
        /// <summary>已选素材提供的经验总量。</summary>
        public long SelectedExperience { get; }
        /// <summary>预计等级内的当前经验。</summary>
        public int ProjectedExperience { get; }
        /// <summary>预计等级的下一等级经验目标。</summary>
        public int ProjectedNextExperience { get; }
        /// <summary>归一化经验进度。</summary>
        public float Progress { get; }
        /// <summary>升级前后的属性对比行。</summary>
        public IReadOnlyList<EquipmentAttributeUpgradeLineViewData> AttributeLines { get; }
        /// <summary>当前已选择的素材卡片。</summary>
        public IReadOnlyList<BagItemViewData> SelectedMaterials { get; }
        /// <summary>当前货币余额。</summary>
        public long CurrencyOwned { get; }
        /// <summary>预计消耗的货币数量。</summary>
        public long CurrencyCost { get; }
        /// <summary>主要操作是否可用。</summary>
        public bool ActionInteractable { get; }
        /// <summary>主要操作按钮文案。</summary>
        public string ActionLabel { get; }
        /// <summary>素材选择入口是否可用。</summary>
        public bool SelectMaterialInteractable { get; }
        /// <summary>自动添加按钮是否可用。</summary>
        public bool AutoAddInteractable { get; }
    }
}
