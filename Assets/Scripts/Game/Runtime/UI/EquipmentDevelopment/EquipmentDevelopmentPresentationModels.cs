using System;
using System.Collections.Generic;
using UnityEngine;
using RPG.Game.UI.Bag;
using RPG.Game.UI.Common;

namespace RPG.Game.UI.WeaponDevelopment
{
    /// <summary>描述一条升级前后数值对比中涨跌标记的方向。</summary>
    public enum EquipmentAttributeUpgradeDirection
    {
        /// <summary>数值不变或当前／预计值不完整时不显示涨跌标记。</summary>
        None,
        /// <summary>预计数值高于当前数值。</summary>
        Increase,
        /// <summary>预计数值低于当前数值。</summary>
        Decrease
    }

    /// <summary>升级页面单个 Attribute 的结构化当前值与预计值。</summary>
    public sealed class EquipmentAttributeUpgradeLineViewData
    {
        /// <summary>创建一条升级属性对比行。</summary>
        /// <param name="attributeId">稳定的 Gameplay Attribute 标识。</param>
        /// <param name="attributeName">本地化属性名称。</param>
        /// <param name="currentValueText">当前等级格式化数值。</param>
        /// <param name="projectedValueText">预计等级格式化数值。</param>
        /// <param name="direction">当前值到预计值的变化方向。</param>
        /// <param name="showProjectedValue">是否显示预计值、比较箭头和涨跌标记。</param>
        public EquipmentAttributeUpgradeLineViewData(int attributeId, string attributeName,
            string currentValueText, string projectedValueText, EquipmentAttributeUpgradeDirection direction,
            bool showProjectedValue = true)
        {
            AttributeId = attributeId;
            AttributeName = attributeName ?? string.Empty;
            CurrentValueText = currentValueText ?? string.Empty;
            ProjectedValueText = projectedValueText ?? string.Empty;
            Direction = direction;
            ShowProjectedValue = showProjectedValue;
        }

        /// <summary>获取稳定的 Gameplay Attribute 标识。</summary>
        public int AttributeId { get; }
        /// <summary>获取属性名称。</summary>
        public string AttributeName { get; }
        /// <summary>获取当前等级格式化数值。</summary>
        public string CurrentValueText { get; }
        /// <summary>获取预计等级格式化数值。</summary>
        public string ProjectedValueText { get; }
        /// <summary>获取当前值到预计值的变化方向。</summary>
        public EquipmentAttributeUpgradeDirection Direction { get; }
        /// <summary>获取是否显示预计值、比较箭头和涨跌标记。</summary>
        public bool ShowProjectedValue { get; }
    }

    /// <summary>升级页面顶部等级和经验进度的只读数据。</summary>
    public sealed class EquipmentEnhancementViewData : DevelopmentEnhancementViewData
    {
        /// <summary>创建升级页面展示数据。</summary>
        /// <param name="title">页面标题。</param>
        /// <param name="subtitle">页面副标题。</param>
        /// <param name="currentLevel">当前等级。</param>
        /// <param name="projectedLevel">加入素材后的预计等级。</param>
        /// <param name="selectedExperience">已选择素材提供的经验。</param>
        /// <param name="projectedExperience">预计等级内的当前经验。</param>
        /// <param name="projectedNextExperience">预计等级的下一等级经验。</param>
        /// <param name="progress">经验条归一化进度。</param>
        /// <param name="attributeLines">按 Attribute ID 对齐的当前与预计属性行。</param>
        /// <param name="selectedMaterials">已选素材条目。</param>
        /// <param name="currencyOwned">当前拥有的培养货币数量。</param>
        /// <param name="currencyCost">本次预计升级消耗的培养货币数量。</param>
        /// <param name="actionInteractable">升级按钮是否可交互。</param>
        /// <param name="actionLabel">升级按钮文案。</param>
        /// <param name="selectMaterialInteractable">选择素材按钮是否可交互。</param>
        /// <param name="autoAddInteractable">自动添加按钮是否可交互。</param>
        public EquipmentEnhancementViewData(string title, string subtitle, int currentLevel, int projectedLevel,
            long selectedExperience, int projectedExperience, int projectedNextExperience, float progress,
            IReadOnlyList<EquipmentAttributeUpgradeLineViewData> attributeLines,
            IReadOnlyList<BagItemViewData> selectedMaterials,
            long currencyOwned, long currencyCost, bool actionInteractable, string actionLabel,
            bool selectMaterialInteractable,
            bool autoAddInteractable)
            : base(title, subtitle, currentLevel, projectedLevel, selectedExperience, projectedExperience,
                projectedNextExperience, progress, attributeLines, selectedMaterials, currencyOwned, currencyCost,
                actionInteractable, actionLabel, selectMaterialInteractable, autoAddInteractable)
        {
        }
    }

    /// <summary>突破页面星级、等级上限和材料的只读数据。</summary>
    public sealed class WeaponAscensionViewData
    {
        /// <summary>创建突破页面展示数据。</summary>
        /// <param name="mode">当前成长状态。</param>
        /// <param name="subtitle">页面副标题。</param>
        /// <param name="currentRank">当前突破阶数。</param>
        /// <param name="nextRank">下一突破阶数。</param>
        /// <param name="currentLevel">当前等级。</param>
        /// <param name="currentCap">当前等级上限。</param>
        /// <param name="nextCap">突破后等级上限。</param>
        /// <param name="showNextStage">是否显示下一阶段比较。</param>
        /// <param name="requiredMaterials">突破所需素材。</param>
        /// <param name="currencyOwned">当前拥有的摩拉。</param>
        /// <param name="currencyCost">本次突破需要的摩拉。</param>
        /// <param name="actionInteractable">突破按钮是否可交互。</param>
        /// <param name="actionLabel">突破按钮文案。</param>
        public WeaponAscensionViewData(EquipmentGrowthMode mode, string subtitle, int currentRank,
            int nextRank, int currentLevel, int currentCap, int nextCap, bool showNextStage,
            IReadOnlyList<BagItemViewData> requiredMaterials,
            long currencyOwned, long currencyCost,
            bool actionInteractable, string actionLabel)
        {
            Mode = mode;
            Subtitle = subtitle ?? string.Empty;
            CurrentRank = Math.Max(0, currentRank);
            NextRank = Math.Max(0, nextRank);
            CurrentLevel = currentLevel;
            CurrentCap = currentCap;
            NextCap = nextCap;
            ShowNextStage = showNextStage;
            RequiredMaterials = requiredMaterials ?? Array.Empty<BagItemViewData>();
            CurrencyOwned = currencyOwned;
            CurrencyCost = currencyCost;
            ActionInteractable = actionInteractable;
            ActionLabel = actionLabel ?? string.Empty;
        }

        /// <summary>当前成长状态。</summary>
        public EquipmentGrowthMode Mode { get; }
        /// <summary>页面副标题。</summary>
        public string Subtitle { get; }
        /// <summary>当前突破阶数。</summary>
        public int CurrentRank { get; }
        /// <summary>下一突破阶数。</summary>
        public int NextRank { get; }
        /// <summary>当前等级。</summary>
        public int CurrentLevel { get; }
        /// <summary>当前阶段等级上限。</summary>
        public int CurrentCap { get; }
        /// <summary>突破后等级上限。</summary>
        public int NextCap { get; }
        /// <summary>是否显示下一阶段的比较信息。</summary>
        public bool ShowNextStage { get; }
        /// <summary>突破所需素材。</summary>
        public IReadOnlyList<BagItemViewData> RequiredMaterials { get; }
        /// <summary>当前拥有的摩拉。</summary>
        public long CurrencyOwned { get; }
        /// <summary>本次突破需要的摩拉。</summary>
        public long CurrencyCost { get; }
        /// <summary>突破按钮是否可交互。</summary>
        public bool ActionInteractable { get; }
        /// <summary>突破按钮文案。</summary>
        public string ActionLabel { get; }
    }

    /// <summary>精炼页面的只读展示数据。</summary>
    public sealed class WeaponRefinementViewData
    {
        /// <summary>创建精炼页面展示数据。</summary>
        /// <param name="title">页面标题。</param>
        /// <param name="subtitle">页面副标题。</param>
        /// <param name="currentRank">当前精炼阶数。</param>
        /// <param name="nextRank">下一精炼阶数。</param>
        /// <param name="showNextRank">是否显示下一精炼阶数。</param>
        /// <param name="attributeLines">精炼效果的结构化属性对比行。</param>
        /// <param name="emptyEffectText">没有静态属性时显示的空状态说明。</param>
        /// <param name="selectedMaterials">已选择的同名武器材料。</param>
        /// <param name="selectedCount">已选择材料数量。</param>
        /// <param name="requiredCount">需要的材料数量。</param>
        /// <param name="currencyOwned">当前拥有的培养货币数量。</param>
        /// <param name="currencyCost">本次精炼消耗的培养货币数量。</param>
        /// <param name="actionInteractable">精炼按钮是否可交互。</param>
        /// <param name="actionLabel">精炼按钮文案。</param>
        /// <param name="addMaterialInteractable">是否允许打开精炼材料选择面板。</param>
        public WeaponRefinementViewData(string title, string subtitle, int currentRank, int nextRank,
            bool showNextRank, IReadOnlyList<EquipmentAttributeUpgradeLineViewData> attributeLines,
            string emptyEffectText,
            IReadOnlyList<BagItemViewData> selectedMaterials, int selectedCount, int requiredCount,
            long currencyOwned, long currencyCost, bool actionInteractable, string actionLabel,
            bool addMaterialInteractable)
        {
            Title = title ?? string.Empty;
            Subtitle = subtitle ?? string.Empty;
            CurrentRank = Math.Max(0, currentRank);
            NextRank = Math.Max(0, nextRank);
            ShowNextRank = showNextRank;
            AttributeLines = attributeLines ?? Array.Empty<EquipmentAttributeUpgradeLineViewData>();
            EmptyEffectText = emptyEffectText ?? string.Empty;
            SelectedMaterials = selectedMaterials ?? Array.Empty<BagItemViewData>();
            SelectedCount = Math.Max(0, selectedCount);
            RequiredCount = Math.Max(0, requiredCount);
            CurrencyOwned = Math.Max(0L, currencyOwned);
            CurrencyCost = Math.Max(0L, currencyCost);
            ActionInteractable = actionInteractable;
            ActionLabel = actionLabel ?? string.Empty;
            AddMaterialInteractable = addMaterialInteractable;
        }

        /// <summary>页面标题。</summary>
        public string Title { get; }
        /// <summary>页面副标题。</summary>
        public string Subtitle { get; }
        /// <summary>当前精炼阶数。</summary>
        public int CurrentRank { get; }
        /// <summary>下一精炼阶数。</summary>
        public int NextRank { get; }
        /// <summary>是否显示下一精炼阶数。</summary>
        public bool ShowNextRank { get; }
        /// <summary>精炼效果的结构化属性对比行。</summary>
        public IReadOnlyList<EquipmentAttributeUpgradeLineViewData> AttributeLines { get; }
        /// <summary>没有可展示静态属性时显示的空状态说明。</summary>
        public string EmptyEffectText { get; }
        /// <summary>已选择的同名武器材料。</summary>
        public IReadOnlyList<BagItemViewData> SelectedMaterials { get; }
        /// <summary>已选择材料数量。</summary>
        public int SelectedCount { get; }
        /// <summary>需要的材料数量。</summary>
        public int RequiredCount { get; }
        /// <summary>当前拥有的培养货币数量。</summary>
        public long CurrencyOwned { get; }
        /// <summary>本次精炼消耗的培养货币数量。</summary>
        public long CurrencyCost { get; }
        /// <summary>精炼按钮是否可交互。</summary>
        public bool ActionInteractable { get; }
        /// <summary>精炼按钮文案。</summary>
        public string ActionLabel { get; }
        /// <summary>是否允许打开精炼材料选择面板。</summary>
        public bool AddMaterialInteractable { get; }
    }

    /// <summary>统一装备培养窗口顶层路由数据。</summary>
    public sealed class EquipmentDevelopmentViewData
    {
        /// <summary>创建顶层页面路由数据。</summary>
        /// <param name="page">当前 Tab 页面。</param>
        /// <param name="growthTabLabel">成长 Tab 文案。</param>
        /// <param name="growthMode">成长入口内部模式。</param>
        /// <param name="enhancement">升级页面数据。</param>
        /// <param name="ascension">突破页面数据。</param>
        /// <param name="refinement">精炼页面数据。</param>
        public EquipmentDevelopmentViewData(EquipmentDevelopmentPage page, string growthTabLabel, EquipmentGrowthMode growthMode,
            EquipmentEnhancementViewData enhancement, WeaponAscensionViewData ascension,
            WeaponRefinementViewData refinement)
        {
            Page = page;
            GrowthTabLabel = growthTabLabel ?? string.Empty;
            GrowthMode = growthMode;
            Enhancement = enhancement;
            Ascension = ascension;
            Refinement = refinement;
        }

        /// <summary>当前 Tab 页面。</summary>
        public EquipmentDevelopmentPage Page { get; }
        /// <summary>成长 Tab 文案。</summary>
        public string GrowthTabLabel { get; }
        /// <summary>成长入口内部模式。</summary>
        public EquipmentGrowthMode GrowthMode { get; }
        /// <summary>升级页面数据。</summary>
        public EquipmentEnhancementViewData Enhancement { get; }
        /// <summary>突破页面数据。</summary>
        public WeaponAscensionViewData Ascension { get; }
        /// <summary>精炼页面数据。</summary>
        public WeaponRefinementViewData Refinement { get; }
    }

    /// <summary>精炼材料选择面板的一次绑定数据。</summary>
    public sealed class EquipmentRefinementSelectionData
    {
        /// <summary>创建精炼材料选择数据。</summary>
        /// <param name="entries">候选武器列表。</param>
        /// <param name="selectedCount">当前已选择数量。</param>
        /// <param name="maxCount">最大选择数量。</param>
        public EquipmentRefinementSelectionData(IReadOnlyList<BagItemViewData> entries, int selectedCount, int maxCount)
        {
            Entries = entries ?? Array.Empty<BagItemViewData>();
            SelectedCount = selectedCount;
            MaxCount = maxCount;
        }

        /// <summary>候选武器列表。</summary>
        public IReadOnlyList<BagItemViewData> Entries { get; }
        /// <summary>当前已选择数量。</summary>
        public int SelectedCount { get; }
        /// <summary>最大选择数量。</summary>
        public int MaxCount { get; }
    }
}
