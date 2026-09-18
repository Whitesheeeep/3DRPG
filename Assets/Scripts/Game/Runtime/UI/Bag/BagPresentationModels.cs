using System;
using System.Collections.Generic;
using RPG.ItemSystem;
using UnityEngine;

namespace RPG.Game.UI.Bag
{
    /// <summary>可堆叠物品数量调整的方向。</summary>
    public enum BagItemQuantityDirection
    {
        /// <summary>增加选中数量。</summary>
        Increase,
        /// <summary>减少选中数量。</summary>
        Decrease
    }

    /// <summary>背包条目向上层报告的一次数量调整意图。</summary>
    public readonly struct BagItemQuantityIntent
    {
        /// <summary>创建数量调整意图。</summary>
        /// <param name="entryKey">条目标识。</param>
        /// <param name="direction">调整方向。</param>
        /// <param name="step">本次调整步长。</param>
        public BagItemQuantityIntent(BagEntryKey entryKey, BagItemQuantityDirection direction, int step)
        {
            EntryKey = entryKey;
            Direction = direction;
            Step = step;
        }

        /// <summary>条目标识。</summary>
        public BagEntryKey EntryKey { get; }
        /// <summary>调整方向。</summary>
        public BagItemQuantityDirection Direction { get; }
        /// <summary>调整步长。</summary>
        public int Step { get; }
    }

    /// <summary>定义背包列表的排序字段。</summary>
    public enum BagSortMode
    {
        /// <summary>按物品品质排序。</summary>
        Quality,
        /// <summary>按当前分类的主数值排序；装备使用等级，可堆叠物品使用数量。</summary>
        PrimaryValue,
        /// <summary>按获得顺序排序。</summary>
        AcquisitionSequence
    }

    /// <summary>定义背包列表的排序方向。</summary>
    public enum BagSortDirection
    {
        /// <summary>升序。</summary>
        Ascending,
        /// <summary>降序。</summary>
        Descending
    }

    /// <summary>背包列表和详情共用的稳定条目标识。</summary>
    public readonly struct BagEntryKey : IEquatable<BagEntryKey>
    {
        /// <summary>创建一个背包条目标识。</summary>
        /// <param name="category">条目所属分类。</param>
        /// <param name="value">条目的稳定字符串标识。</param>
        public BagEntryKey(ItemCategory category, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("背包条目标识不能为空。", nameof(value));
            Category = category;
            Value = value;
        }

        /// <summary>获取条目分类。</summary>
        public ItemCategory Category { get; }
        /// <summary>获取稳定字符串标识。</summary>
        public string Value { get; }

        /// <summary>判断两个条目标识是否相等。</summary>
        /// <param name="other">另一个条目标识。</param>
        /// <returns>相等时返回 true。</returns>
        public bool Equals(BagEntryKey other) => Category == other.Category && string.Equals(Value, other.Value, StringComparison.Ordinal);

        /// <summary>判断对象是否为相同条目标识。</summary>
        /// <param name="obj">待比较对象。</param>
        /// <returns>相等时返回 true。</returns>
        public override bool Equals(object obj) => obj is BagEntryKey other && Equals(other);

        /// <summary>获取条目标识哈希值。</summary>
        /// <returns>哈希值。</returns>
        public override int GetHashCode() => HashCode.Combine((int)Category, StringComparer.Ordinal.GetHashCode(Value ?? string.Empty));

        /// <summary>获取可诊断的条目标识文本。</summary>
        /// <returns>分类和稳定标识。</returns>
        public override string ToString() => $"{Category}:{Value}";

        /// <summary>比较两个条目标识。</summary>
        public static bool operator ==(BagEntryKey left, BagEntryKey right) => left.Equals(right);
        /// <summary>比较两个条目标识。</summary>
        public static bool operator !=(BagEntryKey left, BagEntryKey right) => !left.Equals(right);
    }

    /// <summary>供 BagItemView 渲染的一条只读列表数据。</summary>
    public sealed class BagItemViewData
    {
        /// <summary>创建列表显示数据。</summary>
        public BagItemViewData(
            BagEntryKey entryKey,
            string displayName,
            int rarity,
            string levelText,
            Sprite icon,
            Sprite ownerIcon,
            string ownerText,
            bool isNew,
            bool isLocked,
            bool isEquipped)
        {
            EntryKey = entryKey;
            DisplayName = displayName ?? string.Empty;
            Rarity = rarity;
            LevelText = levelText ?? string.Empty;
            Icon = icon;
            OwnerIcon = ownerIcon;
            OwnerText = ownerText ?? string.Empty;
            IsNew = isNew;
            IsLocked = isLocked;
            IsEquipped = isEquipped;
        }

        /// <summary>获取稳定条目标识。</summary>
        public BagEntryKey EntryKey { get; }
        /// <summary>获取显示名称。</summary>
        public string DisplayName { get; }
        /// <summary>获取一至五星品质。</summary>
        public int Rarity { get; }
        /// <summary>获取等级或数量文本。</summary>
        public string LevelText { get; }
        /// <summary>获取物品图标。</summary>
        public Sprite Icon { get; }
        /// <summary>获取装备者头像。</summary>
        public Sprite OwnerIcon { get; }
        /// <summary>获取装备者名称文本。</summary>
        public string OwnerText { get; }
        /// <summary>获取新获得状态。</summary>
        public bool IsNew { get; }
        /// <summary>获取锁定状态。</summary>
        public bool IsLocked { get; }
        /// <summary>获取装备状态。</summary>
        public bool IsEquipped { get; }
    }

    /// <summary>供所有背包分类共用详情 View 使用的只读详情数据。</summary>
    public sealed class BagDetailViewData
    {
        /// <summary>创建背包详情显示数据。</summary>
        /// <param name="entryKey">稳定条目标识。</param>
        /// <param name="displayName">物品名称。</param>
        /// <param name="rarity">物品品质。</param>
        /// <param name="icon">物品主图。</param>
        /// <param name="categoryText">分类文本。</param>
        /// <param name="primaryText">主信息文本。</param>
        /// <param name="secondaryText">次信息文本。</param>
        /// <param name="detailLines">分类详情行。</param>
        /// <param name="description">物品描述。</param>
        /// <param name="ownerText">装备者文本。</param>
        /// <param name="ownerIcon">装备者图标。</param>
        /// <param name="showOwner">是否显示装备者区域。</param>
        /// <param name="showDeleteAction">是否显示删除操作。</param>
        /// <param name="showDetailsAction">是否显示详情/培养操作。</param>
        public BagDetailViewData(
            BagEntryKey entryKey,
            string displayName,
            int rarity,
            Sprite icon,
            string categoryText,
            string primaryText,
            string secondaryText,
            IReadOnlyList<string> detailLines,
            string description,
            string ownerText,
            Sprite ownerIcon,
            bool showOwner,
            bool showDeleteAction,
            bool showDetailsAction)
        {
            EntryKey = entryKey;
            DisplayName = displayName ?? string.Empty;
            Rarity = rarity;
            Icon = icon;
            CategoryText = categoryText ?? string.Empty;
            PrimaryText = primaryText ?? string.Empty;
            SecondaryText = secondaryText ?? string.Empty;
            DetailLines = detailLines ?? Array.Empty<string>();
            Description = description ?? string.Empty;
            OwnerText = ownerText ?? string.Empty;
            OwnerIcon = ownerIcon;
            ShowOwner = showOwner;
            ShowDeleteAction = showDeleteAction;
            ShowDetailsAction = showDetailsAction;
        }

        /// <summary>获取稳定条目标识。</summary>
        public BagEntryKey EntryKey { get; }
        /// <summary>获取名称。</summary>
        public string DisplayName { get; }
        /// <summary>获取品质。</summary>
        public int Rarity { get; }
        /// <summary>获取主图。</summary>
        public Sprite Icon { get; }
        /// <summary>获取分类名称。</summary>
        public string CategoryText { get; }
        /// <summary>获取主信息。</summary>
        public string PrimaryText { get; }
        /// <summary>获取次信息。</summary>
        public string SecondaryText { get; }
        /// <summary>获取分类详情行。</summary>
        public IReadOnlyList<string> DetailLines { get; }
        /// <summary>获取描述。</summary>
        public string Description { get; }
        /// <summary>获取装备者名称。</summary>
        public string OwnerText { get; }
        /// <summary>获取装备者头像。</summary>
        public Sprite OwnerIcon { get; }
        /// <summary>获取是否显示装备者区域。</summary>
        public bool ShowOwner { get; }
        /// <summary>获取是否允许删除操作。</summary>
        public bool ShowDeleteAction { get; }
        /// <summary>获取是否允许详情/培养操作。</summary>
        public bool ShowDetailsAction { get; }
    }
}
