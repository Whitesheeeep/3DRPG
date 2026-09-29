using System;
using System.Collections.Generic;
using System.Globalization;
using RPG.Character;
using RPG.CurrencySystemNS;
using RPG.Game.Runtime.CharacterDevelopment;
using RPG.Game.UI.Bag;
using RPG.Game.UI.Common;
using RPG.Game.UI.WeaponDevelopment;
using RPG.ItemSystem;
using UnityEngine;
using WS_Modules.GAS.Generated;

namespace RPG.Game.UI.Character
{
    /// <summary>角色培养页面展示快照，集中描述升级或突破右侧面板所需的字段。</summary>
    public sealed class CharacterDevelopmentPanelViewData
    {
        /// <summary>创建角色培养页展示快照。</summary>
        /// <param name="mode">培养模式。</param>
        /// <param name="statusText">当前条件说明。</param>
        /// <param name="currentRank">当前突破阶数。</param>
        /// <param name="projectedRank">突破后阶数。</param>
        /// <param name="currentLevelCap">当前等级上限。</param>
        /// <param name="projectedLevelCap">突破后等级上限。</param>
        /// <param name="currencyOwned">当前摩拉余额。</param>
        /// <param name="currencyCost">本次预计摩拉成本。</param>
        /// <param name="actionInteractable">培养确认按钮是否可操作。</param>
        /// <param name="actionLabel">确认按钮文案。</param>
        /// <param name="enhancementData">升级时共用培养页面使用的展示快照。</param>
        /// <param name="requiredMaterials">突破时展示的固定配方材料卡片。</param>
        public CharacterDevelopmentPanelViewData(CharacterDevelopmentMode mode, string statusText,
            int currentRank, int projectedRank, int currentLevelCap, int projectedLevelCap,
            long currencyOwned, long currencyCost, bool actionInteractable, string actionLabel,
            DevelopmentEnhancementViewData enhancementData = null,
            IReadOnlyList<BagItemViewData> requiredMaterials = null)
        {
            Mode = mode;
            StatusText = statusText ?? string.Empty;
            CurrencyOwned = Math.Max(0L, currencyOwned);
            CurrencyCost = Math.Max(0L, currencyCost);
            CurrentRank = currentRank;
            ProjectedRank = projectedRank;
            CurrentLevelCap = currentLevelCap;
            ProjectedLevelCap = projectedLevelCap;
            ActionInteractable = actionInteractable;
            ActionLabel = actionLabel ?? string.Empty;
            EnhancementData = enhancementData;
            RequiredMaterials = requiredMaterials ?? Array.Empty<BagItemViewData>();
        }

        /// <summary>获取培养模式。</summary>
        public CharacterDevelopmentMode Mode { get; }
        /// <summary>获取当前条件说明。</summary>
        public string StatusText { get; }
        /// <summary>获取当前摩拉余额。</summary>
        public long CurrencyOwned { get; }
        /// <summary>获取预计摩拉成本。</summary>
        public long CurrencyCost { get; }
        /// <summary>获取当前突破阶数。</summary>
        public int CurrentRank { get; }
        /// <summary>获取突破后的阶数。</summary>
        public int ProjectedRank { get; }
        /// <summary>获取当前等级上限。</summary>
        public int CurrentLevelCap { get; }
        /// <summary>获取突破后的等级上限。</summary>
        public int ProjectedLevelCap { get; }
        /// <summary>获取确认按钮可操作状态。</summary>
        public bool ActionInteractable { get; }
        /// <summary>获取确认按钮文案。</summary>
        public string ActionLabel { get; }
        /// <summary>获取角色升级时绑定到共用等级培养页的数据。</summary>
        public DevelopmentEnhancementViewData EnhancementData { get; }
        /// <summary>获取角色突破的固定配方材料卡片。</summary>
        public IReadOnlyList<BagItemViewData> RequiredMaterials { get; }
    }

    /// <summary>把角色成长领域预览投影为角色窗口的等级、材料和属性行数据。</summary>
    internal sealed class CharacterDevelopmentPresentationBuilder
    {
        #region 常量与依赖字段

        // 属性顺序与 CharacterAttributePageView 的五个固定 Stat 行一致。
        private static readonly int[] DisplayedAttributeIdsInOrder =
        {
            GameplayAttributes.Attribute_MaxHealth.Id,
            GameplayAttributes.Attribute_AttackPower.Id,
            GameplayAttributes.Attribute_Armor.Id,
            GameplayAttributes.Attribute_CriticalChance.Id,
            GameplayAttributes.Attribute_CriticalDamage.Id
        };

        // 依赖字段：服务提供权威预览，库存读取突破材料图标与候选列表，图集解析器提供运行时 Sprite。
        private readonly CharacterDevelopmentService developmentService;
        private readonly StackableInventoryManager stackableInventoryManager;
        private readonly CurrencyManager currencyManager;
        private readonly Func<string, string, Sprite> spriteResolver;

        #endregion

        #region 生命周期

        /// <summary>创建角色培养页投影器。</summary>
        /// <param name="developmentServiceValue">角色培养事务服务。</param>
        /// <param name="stackableInventoryManagerValue">角色材料库存管理器。</param>
        /// <param name="currencyManagerValue">当前摩拉余额查询器。</param>
        /// <param name="spriteResolverValue">动态图集 Sprite 查询函数。</param>
        public CharacterDevelopmentPresentationBuilder(CharacterDevelopmentService developmentServiceValue,
            StackableInventoryManager stackableInventoryManagerValue, CurrencyManager currencyManagerValue,
            Func<string, string, Sprite> spriteResolverValue)
        {
            developmentService = developmentServiceValue ?? throw new ArgumentNullException(nameof(developmentServiceValue));
            stackableInventoryManager = stackableInventoryManagerValue ??
                                        throw new ArgumentNullException(nameof(stackableInventoryManagerValue));
            currencyManager = currencyManagerValue ?? throw new ArgumentNullException(nameof(currencyManagerValue));
            spriteResolver = spriteResolverValue ?? throw new ArgumentNullException(nameof(spriteResolverValue));
        }

        #endregion

        #region 页面数据构建

        /// <summary>构建角色升级页数据与当前库存中的经验素材候选。</summary>
        /// <param name="instance">当前角色实例。</param>
        /// <param name="selectedQuantityByItemIdMap">已选择的经验素材数量。</param>
        /// <param name="sortMode">候选面板的排序字段。</param>
        /// <param name="sortDirection">候选面板的排序方向。</param>
        /// <param name="entries">共用选择网格的经验素材条目。</param>
        /// <returns>升级右侧面板数据。</returns>
        public CharacterDevelopmentPanelViewData BuildLevelUp(CharacterInstance instance,
            IReadOnlyDictionary<ItemId, int> selectedQuantityByItemIdMap, BagSortMode sortMode,
            BagSortDirection sortDirection, out IReadOnlyList<BagItemViewData> entries)
        {
            CharacterLevelUpPreview preview = developmentService.BuildLevelUpPreview(
                instance.CharacterId, selectedQuantityByItemIdMap);
            entries = BuildExperienceEntries(selectedQuantityByItemIdMap, sortMode, sortDirection);
            string status = preview.Message;
            if (preview.Status == CharacterDevelopmentStatus.Succeeded && !preview.CanSubmit)
                status = "选择经验素材以预览升级。";

            int levelCap = ResolveCurrentLevelCap(instance);
            float progress = preview.NextExperience <= 0 || preview.ProjectedLevel >= levelCap
                ? 1f
                : Mathf.Clamp01(preview.ProjectedExperience / (float)preview.NextExperience);
            long currencyOwned = currencyManager.GetBalance(CurrencyId.Mola);
            IReadOnlyList<BagItemViewData> selectedMaterials = BuildSelectedExperienceEntries(
                entries, selectedQuantityByItemIdMap);
            IReadOnlyList<EquipmentAttributeUpgradeLineViewData> attributeLines =
                BuildAttributeLines(preview.Attributes);
            bool canAutoFill = entries.Count > 0 && preview.Status != CharacterDevelopmentStatus.ProgressAtCap;
            var enhancementData = new DevelopmentEnhancementViewData(
                "角色升级", status, preview.CurrentLevel, preview.ProjectedLevel,
                preview.SelectedExperience, preview.ProjectedExperience, preview.NextExperience, progress,
                attributeLines, selectedMaterials, currencyOwned, preview.CurrencyCost,
                preview.CanSubmit, "升级", entries.Count > 0, canAutoFill);

            return new CharacterDevelopmentPanelViewData(CharacterDevelopmentMode.LevelUp, status,
                0, 0, 0, 0, currencyOwned, preview.CurrencyCost, preview.CanSubmit, "升级", enhancementData);
        }

        /// <summary>构建角色突破页数据与本阶段固定材料展示卡片。</summary>
        /// <param name="instance">当前角色实例。</param>
        /// <returns>突破右侧面板数据。</returns>
        public CharacterDevelopmentPanelViewData BuildAscension(CharacterInstance instance)
        {
            CharacterAscensionPreview preview = developmentService.BuildAscensionPreview(instance.CharacterId);
            IReadOnlyList<BagItemViewData> entries = BuildAscensionEntries(preview.Materials);

            return new CharacterDevelopmentPanelViewData(CharacterDevelopmentMode.Ascension,
                string.Empty, preview.CurrentRank, preview.NextRank, preview.CurrentLevelCap,
                preview.NextLevelCap, preview.CurrencyOwned, preview.CurrencyCost, preview.CanSubmit,
                "突破", null, entries);
        }

        /// <summary>按库存排序返回本次已选择的经验素材卡片。</summary>
        /// <param name="entries">完整经验素材候选列表。</param>
        /// <param name="selectedQuantityByItemIdMap">已选素材数量，键为 ItemId，值为数量。</param>
        /// <returns>仅包含选择数量大于零的素材卡片。</returns>
        private static IReadOnlyList<BagItemViewData> BuildSelectedExperienceEntries(
            IReadOnlyList<BagItemViewData> entries,
            IReadOnlyDictionary<ItemId, int> selectedQuantityByItemIdMap)
        {
            var selectedEntries = new List<BagItemViewData>();
            for (int index = 0; index < entries.Count; index++)
            {
                BagItemViewData entry = entries[index];
                if (!ItemId.TryCreate(entry.EntryKey.Value, out ItemId itemId) ||
                    !selectedQuantityByItemIdMap.TryGetValue(itemId, out int selectedQuantity) || selectedQuantity <= 0)
                    continue;
                selectedEntries.Add(new BagItemViewData(entry.EntryKey, entry.DisplayName, entry.Rarity,
                    selectedQuantity.ToString("N0", CultureInfo.InvariantCulture), entry.Icon, entry.OwnerIcon,
                    entry.OwnerText, entry.IsNew, entry.IsLocked, entry.IsEquipped));
            }

            return selectedEntries;
        }

        /// <summary>根据角色突破阶段取得当前经验进度的等级上限。</summary>
        /// <param name="instance">当前角色实例。</param>
        /// <returns>当前突破阶段的最高等级。</returns>
        private static int ResolveCurrentLevelCap(CharacterInstance instance)
        {
            IReadOnlyList<CharacterAscensionStage> stages = instance.Config.AscensionStages;
            if (stages.Count == 0) return instance.Config.MaxLevel;
            int stageIndex = instance.AscensionRank == 0 ? 0 : instance.AscensionRank - 1;
            CharacterAscensionStage stage = stages[stageIndex];
            return instance.AscensionRank == 0 ? stage.RequiredLevel : stage.MaxLevelAfter;
        }

        /// <summary>把角色经验素材库存投影成带真实图标和库存数量的背包条目。</summary>
        /// <param name="selectedQuantityByItemIdMap">各经验素材当前选中数量。</param>
        /// <param name="sortMode">面板当前排序字段。</param>
        /// <param name="sortDirection">面板当前排序方向。</param>
        /// <returns>按排序状态排列的候选快照。</returns>
        private IReadOnlyList<BagItemViewData> BuildExperienceEntries(
            IReadOnlyDictionary<ItemId, int> selectedQuantityByItemIdMap, BagSortMode sortMode,
            BagSortDirection sortDirection)
        {
            IReadOnlyList<StackableInventoryEntry> inventoryEntries = stackableInventoryManager
                .GetDevelopmentExperienceItems(DevelopmentExperienceItemType.Character);
            var sortedInventoryEntries = new List<StackableInventoryEntry>(inventoryEntries.Count);
            for (int index = 0; index < inventoryEntries.Count; index++)
                sortedInventoryEntries.Add(inventoryEntries[index]);
            sortedInventoryEntries.Sort((left, right) => CompareExperienceEntries(left, right, sortMode, sortDirection));

            var entries = new List<BagItemViewData>(sortedInventoryEntries.Count);
            for (int index = 0; index < sortedInventoryEntries.Count; index++)
            {
                StackableInventoryEntry inventoryEntry = sortedInventoryEntries[index];
                if (!ItemManager.Instance.TryGetDefinition(inventoryEntry.ItemId, out ItemDefinition definition))
                    continue;
                int selectedQuantity = selectedQuantityByItemIdMap != null &&
                                       selectedQuantityByItemIdMap.TryGetValue(inventoryEntry.ItemId, out int selected)
                    ? selected
                    : 0;
                entries.Add(new BagItemViewData(
                    new BagEntryKey(ItemCategory.DevelopmentExperienceItem, inventoryEntry.ItemId.ToString()),
                    definition.DisplayName, (int)definition.Rarity,
                    $"{selectedQuantity:N0}/{inventoryEntry.Quantity:N0}",
                    ResolveIcon(definition), null, string.Empty, inventoryEntry.IsNew, false, false));
            }

            return entries;
        }

        /// <summary>按 Dropdown 字段及稳定次级字段比较角色经验素材库存项。</summary>
        /// <param name="left">左侧候选。</param>
        /// <param name="right">右侧候选。</param>
        /// <param name="sortMode">排序字段。</param>
        /// <param name="sortDirection">主排序方向。</param>
        /// <returns>标准比较结果。</returns>
        private static int CompareExperienceEntries(StackableInventoryEntry left, StackableInventoryEntry right,
            BagSortMode sortMode, BagSortDirection sortDirection)
        {
            ItemManager.Instance.TryGetDefinition(left.ItemId, out ItemDefinition leftDefinition);
            ItemManager.Instance.TryGetDefinition(right.ItemId, out ItemDefinition rightDefinition);
            int leftRarity = leftDefinition == null ? 0 : (int)leftDefinition.Rarity;
            int rightRarity = rightDefinition == null ? 0 : (int)rightDefinition.Rarity;
            int primary = sortMode switch
            {
                BagSortMode.PrimaryValue => left.Quantity.CompareTo(right.Quantity),
                BagSortMode.AcquisitionSequence => left.AcquisitionSequence.CompareTo(right.AcquisitionSequence),
                _ => leftRarity.CompareTo(rightRarity)
            };
            if (sortDirection == BagSortDirection.Descending) primary = -primary;
            if (primary != 0) return primary;

            int rarity = rightRarity.CompareTo(leftRarity);
            if (rarity != 0) return rarity;
            int quantity = right.Quantity.CompareTo(left.Quantity);
            if (quantity != 0) return quantity;
            int sequence = left.AcquisitionSequence.CompareTo(right.AcquisitionSequence);
            return sequence != 0 ? sequence : string.CompareOrdinal(left.ItemId.ToString(), right.ItemId.ToString());
        }

        /// <summary>按突破配方顺序投影材料，即使库存为零仍保留需求卡片。</summary>
        /// <param name="requirements">当前阶段突破材料需求。</param>
        /// <returns>标明持有量与需求量的材料卡片。</returns>
        private IReadOnlyList<BagItemViewData> BuildAscensionEntries(
            IReadOnlyList<CharacterAscensionMaterialRequirement> requirements)
        {
            var entries = new List<BagItemViewData>(requirements.Count);
            for (int index = 0; index < requirements.Count; index++)
            {
                CharacterAscensionMaterialRequirement requirement = requirements[index];
                if (!ItemManager.Instance.TryGetDefinition(requirement.ItemId, out ItemDefinition definition))
                    continue;
                entries.Add(new BagItemViewData(
                    new BagEntryKey(ItemCategory.DevelopmentItem, requirement.ItemId.ToString()),
                    definition.DisplayName, (int)definition.Rarity,
                    $"{requirement.OwnedQuantity}/{requirement.RequiredQuantity}",
                    ResolveIcon(definition), null, string.Empty, false, false, false));
            }

            return entries;
        }

        /// <summary>将成长服务的完整 Stat 列表裁剪成角色页固定五项并生成对比显示值。</summary>
        /// <param name="values">当前与预计等级的静态属性投影。</param>
        /// <returns>按角色属性页固定顺序排列的对比行。</returns>
        private static IReadOnlyList<EquipmentAttributeUpgradeLineViewData> BuildAttributeLines(
            IReadOnlyList<CharacterDevelopmentAttributeValue> values)
        {
            var valueByAttributeIdMap = new Dictionary<int, CharacterDevelopmentAttributeValue>(values.Count);
            for (int index = 0; index < values.Count; index++)
                valueByAttributeIdMap.Add(values[index].Attribute.Id, values[index]);

            var lines = new List<EquipmentAttributeUpgradeLineViewData>(DisplayedAttributeIdsInOrder.Length);
            for (int index = 0; index < DisplayedAttributeIdsInOrder.Length; index++)
            {
                if (!valueByAttributeIdMap.TryGetValue(DisplayedAttributeIdsInOrder[index],
                        out CharacterDevelopmentAttributeValue value)) continue;
                string attributeName = string.IsNullOrWhiteSpace(value.Attribute.DisplayName)
                    ? value.Attribute.Name
                    : value.Attribute.DisplayName;
                bool percentage = IsPercentageAttribute(value.Attribute.Id);
                string currentText = FormatAttributeValue(value.CurrentValue, percentage);
                string projectedText = FormatAttributeValue(value.ProjectedValue, percentage);
                EquipmentAttributeUpgradeDirection direction = Math.Abs(value.ProjectedValue - value.CurrentValue) < 0.0001f
                    ? EquipmentAttributeUpgradeDirection.None
                    : value.ProjectedValue > value.CurrentValue
                        ? EquipmentAttributeUpgradeDirection.Increase
                        : EquipmentAttributeUpgradeDirection.Decrease;
                lines.Add(new EquipmentAttributeUpgradeLineViewData(value.Attribute.Id,
                    attributeName, currentText, projectedText, direction));
            }

            return lines;
        }

        /// <summary>按角色属性页的暴击字段规则格式化整数或百分比。</summary>
        /// <param name="value">待显示值。</param>
        /// <param name="percentage">是否需要转换为百分比。</param>
        /// <returns>适合 UI 的格式化文本。</returns>
        private static string FormatAttributeValue(float value, bool percentage)
        {
            if (percentage)
                return $"{(value * 100f).ToString("0.0", CultureInfo.InvariantCulture)}%";
            return Math.Round(value, MidpointRounding.AwayFromZero).ToString("N0", CultureInfo.InvariantCulture);
        }

        /// <summary>判断角色 UI 将暴击率和暴击伤害显示为百分比。</summary>
        /// <param name="attributeId">Attribute ID。</param>
        /// <returns>该 Attribute 采用百分比格式时为 true。</returns>
        private static bool IsPercentageAttribute(int attributeId) =>
            attributeId == GameplayAttributes.Attribute_CriticalChance.Id ||
            attributeId == GameplayAttributes.Attribute_CriticalDamage.Id;

        /// <summary>查询素材配置在已加载动态图集中的 Sprite。</summary>
        /// <param name="definition">素材配置。</param>
        /// <returns>当前可用的 Sprite；动态图集尚未完成时返回 null。</returns>
        private Sprite ResolveIcon(ItemDefinition definition) =>
            spriteResolver(definition.IconAddress, definition.IconSpriteName);

        #endregion
    }
}
