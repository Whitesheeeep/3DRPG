using System.Collections.Generic;
using RPG.ItemSystem;

namespace RPG.Game.UI.Bag
{
    /// <summary>把一个背包顶层分类转换为列表和详情快照的运行时数据源。</summary>
    public interface IBagCategoryDataSource
    {
        /// <summary>获取该数据源对应的顶层分类。</summary>
        ItemCategory Category { get; }

        /// <summary>按当前排序设置生成列表快照。</summary>
        /// <param name="sortMode">排序字段。</param>
        /// <param name="sortDirection">排序方向。</param>
        /// <returns>列表显示数据。</returns>
        IReadOnlyList<BagItemViewData> BuildEntries(BagSortMode sortMode, BagSortDirection sortDirection);

        /// <summary>查询一个条目的详情快照。</summary>
        /// <param name="entryKey">条目标识。</param>
        /// <param name="details">找到时返回详情。</param>
        /// <returns>找到时返回 true。</returns>
        bool TryBuildDetails(BagEntryKey entryKey, out BagDetailViewData details);
    }
}
