using System;
using System.Collections.Generic;
using RPG.ItemSystem;

namespace RPG.Game.UI.Bag
{
    /// <summary>尚未接入具体背包数据的分类占位数据源。</summary>
    public sealed class UnavailableBagCategoryDataSource : IBagCategoryDataSource
    {
        /// <summary>创建指定分类的占位数据源。</summary>
        /// <param name="category">占位分类。</param>
        public UnavailableBagCategoryDataSource(ItemCategory category) => Category = category;

        /// <inheritdoc />
        public ItemCategory Category { get; }

        /// <inheritdoc />
        public IReadOnlyList<BagItemViewData> BuildEntries(BagSortMode sortMode, BagSortDirection sortDirection) =>
            Array.Empty<BagItemViewData>();

        /// <inheritdoc />
        public bool TryBuildDetails(BagEntryKey entryKey, out BagDetailViewData details)
        {
            details = null;
            return false;
        }
    }
}
