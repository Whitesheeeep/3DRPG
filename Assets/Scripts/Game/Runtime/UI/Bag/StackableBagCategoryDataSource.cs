using System;
using System.Collections.Generic;
using RPG.ItemSystem;
using UnityEngine;

namespace RPG.Game.UI.Bag
{
    /// <summary>将一个可堆叠物品分类转换为背包网格和通用详情快照。</summary>
    public sealed class StackableBagCategoryDataSource : IBagCategoryDataSource
    {
        #region 依赖字段

        private readonly ItemCategory category;
        private readonly StackableInventoryManager manager;
        private readonly Func<string, string, Sprite> spriteResolver;

        #endregion

        #region 初始化

        /// <summary>创建指定分类的可堆叠数据源。</summary>
        /// <param name="category">可堆叠顶层分类。</param>
        /// <param name="manager">由 GameArchitecture 持有的可堆叠 Manager。</param>
        /// <param name="spriteResolver">按图集地址和 Sprite 名称解析图标。</param>
        public StackableBagCategoryDataSource(
            ItemCategory category,
            StackableInventoryManager manager,
            Func<string, string, Sprite> spriteResolver)
        {
            if (category != ItemCategory.DevelopmentExperienceItem && category != ItemCategory.Food && category != ItemCategory.DevelopmentItem)
                throw new ArgumentException("可堆叠背包数据源只能使用经验道具、食物或养成道具分类。", nameof(category));
            this.category = category;
            this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
            this.spriteResolver = spriteResolver ?? throw new ArgumentNullException(nameof(spriteResolver));
        }

        #endregion

        #region 属性

        /// <inheritdoc />
        public ItemCategory Category => category;

        /// <inheritdoc />
        public string PrimarySortLabel => "数量";

        #endregion

        #region 构建投影

        /// <inheritdoc />
        public IReadOnlyList<BagItemViewData> BuildEntries(BagSortMode sortMode, BagSortDirection sortDirection)
        {
            if (!ItemManager.Instance.IsConfigured)
                return Array.Empty<BagItemViewData>();

            IReadOnlyList<StackableInventoryEntry> entries = manager.GetEntries(category);
            var values = new List<StackableEntry>(entries.Count);
            for (int index = 0; index < entries.Count; index++)
            {
                StackableInventoryEntry entry = entries[index];
                if (ItemManager.Instance.TryGetDefinition(entry.ItemId, out ItemDefinition definition) &&
                    definition.Category == category && definition is StackableItemDefinition stackable)
                    values.Add(new StackableEntry(entry, stackable));
            }

            values.Sort((left, right) => Compare(left, right, sortMode, sortDirection));
            var result = new List<BagItemViewData>(values.Count);
            for (int index = 0; index < values.Count; index++)
            {
                StackableEntry value = values[index];
                result.Add(new BagItemViewData(
                    new BagEntryKey(category, value.Entry.ItemId.ToString()),
                    value.Definition.DisplayName,
                    (int)value.Definition.Rarity,
                    $"×{value.Entry.Quantity}",
                    ResolveSprite(value.Definition.IconAddress, value.Definition.IconSpriteName),
                    null,
                    string.Empty,
                    value.Entry.IsNew,
                    false,
                    false));
            }

            return result;
        }

        /// <inheritdoc />
        public bool TryBuildDetails(BagEntryKey entryKey, out BagDetailViewData details)
        {
            details = null;
            if (!ItemManager.Instance.IsConfigured ||
                entryKey.Category != category || !ItemId.TryCreate(entryKey.Value, out ItemId itemId) ||
                !manager.TryGetEntry(itemId, out StackableInventoryEntry entry) ||
                !ItemManager.Instance.TryGetDefinition(itemId, out ItemDefinition definition) ||
                definition.Category != category)
                return false;

            IReadOnlyList<string> detailLines;
            string secondaryText = string.Empty;
            string categoryText = GetCategoryText(definition);
            switch (definition)
            {
                case DevelopmentExperienceItemDefinition experience:
                    secondaryText = $"提供经验 {experience.ExperienceValue}";
                    detailLines = new[] { $"适用对象：{FormatExperienceTypes(experience.ExperienceTypes)}" };
                    break;
                case FoodItemDefinition food:
                    detailLines = BagGameplayEffectPresentationBuilder.BuildEffectDescriptionLines(
                        food.UseEffects, $"食物 {food.DisplayName}");
                    break;
                case DevelopmentItemDefinition development:
                    detailLines = new[] { $"用途：{FormatDevelopmentTypes(development.DevelopmentTypes)}" };
                    break;
                default:
                    return false;
            }

            details = new BagDetailViewData(
                entryKey,
                definition.DisplayName,
                (int)definition.Rarity,
                ResolveSprite(definition.IconAddress, definition.IconSpriteName),
                categoryText,
                $"拥有 {entry.Quantity}",
                secondaryText,
                detailLines,
                definition.Description,
                string.Empty,
                null,
                false,
                false,
                false);
            return true;
        }

        #endregion

        #region 内部辅助

        /// <summary>按分类主排序字段比较两个可堆叠条目。</summary>
        /// <param name="left">左侧条目。</param>
        /// <param name="right">右侧条目。</param>
        /// <param name="mode">排序字段。</param>
        /// <param name="direction">排序方向。</param>
        /// <returns>比较结果。</returns>
        private static int Compare(StackableEntry left, StackableEntry right, BagSortMode mode, BagSortDirection direction)
        {
            int primary = mode switch
            {
                BagSortMode.PrimaryValue => left.Entry.Quantity.CompareTo(right.Entry.Quantity),
                BagSortMode.AcquisitionSequence => left.Entry.AcquisitionSequence.CompareTo(right.Entry.AcquisitionSequence),
                _ => ((int)left.Definition.Rarity).CompareTo((int)right.Definition.Rarity)
            };
            if (direction == BagSortDirection.Descending) primary = -primary;
            if (primary != 0) return primary;
            int rarity = ((int)right.Definition.Rarity).CompareTo((int)left.Definition.Rarity);
            if (rarity != 0) return rarity;
            int quantity = right.Entry.Quantity.CompareTo(left.Entry.Quantity);
            if (quantity != 0) return quantity;
            int sequence = left.Entry.AcquisitionSequence.CompareTo(right.Entry.AcquisitionSequence);
            return sequence != 0 ? sequence : left.Entry.ItemId.CompareTo(right.Entry.ItemId);
        }

        /// <summary>返回可堆叠定义的用户可见分类。</summary>
        /// <param name="definition">物品定义。</param>
        /// <returns>分类文本。</returns>
        private static string GetCategoryText(ItemDefinition definition) => definition switch
        {
            DevelopmentExperienceItemDefinition => "养成经验道具",
            FoodItemDefinition => "食物",
            DevelopmentItemDefinition => "养成道具",
            _ => "物品"
        };

        /// <summary>按固定 Flags 位顺序格式化经验适用对象。</summary>
        /// <param name="types">经验适用对象。</param>
        /// <returns>中文对象名称。</returns>
        private static string FormatExperienceTypes(DevelopmentExperienceItemType types)
        {
            var labels = new List<string>(3);
            if ((types & DevelopmentExperienceItemType.Character) != 0) labels.Add("角色");
            if ((types & DevelopmentExperienceItemType.Weapon) != 0) labels.Add("武器");
            if ((types & DevelopmentExperienceItemType.Artifact) != 0) labels.Add("圣遗物");
            return labels.Count == 0 ? "未配置" : string.Join("、", labels);
        }

        /// <summary>按固定 Flags 位顺序格式化普通养成用途。</summary>
        /// <param name="types">养成用途。</param>
        /// <returns>中文用途名称。</returns>
        private static string FormatDevelopmentTypes(DevelopmentItemType types)
        {
            var labels = new List<string>(4);
            if ((types & DevelopmentItemType.CharacterAscension) != 0) labels.Add("角色突破");
            if ((types & DevelopmentItemType.CharacterTalent) != 0) labels.Add("角色天赋");
            if ((types & DevelopmentItemType.WeaponAscension) != 0) labels.Add("武器突破");
            if ((types & DevelopmentItemType.WeaponRefinement) != 0) labels.Add("武器精炼");
            return labels.Count == 0 ? "未配置" : string.Join("、", labels);
        }

        /// <summary>按定义配置解析物品图标。</summary>
        /// <param name="address">图集地址。</param>
        /// <param name="spriteName">Sprite 名称。</param>
        /// <returns>解析到的 Sprite。</returns>
        private Sprite ResolveSprite(string address, string spriteName) =>
            string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(spriteName)
                ? null
                : spriteResolver(address, spriteName);

        /// <summary>缓存一个可堆叠库存条目和定义。</summary>
        private readonly struct StackableEntry
        {
            /// <summary>创建排序输入。</summary>
            /// <param name="entry">库存条目。</param>
            /// <param name="definition">物品定义。</param>
            public StackableEntry(StackableInventoryEntry entry, StackableItemDefinition definition)
            {
                Entry = entry;
                Definition = definition;
            }

            /// <summary>获取库存条目。</summary>
            public StackableInventoryEntry Entry { get; }
            /// <summary>获取物品定义。</summary>
            public StackableItemDefinition Definition { get; }
        }

        #endregion
    }
}
