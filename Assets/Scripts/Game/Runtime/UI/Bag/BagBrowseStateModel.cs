using System;
using RPG.ItemSystem;

namespace RPG.Game.UI.Bag
{
    /// <summary>保存背包窗口内跨 View 共用的浏览状态，不持久化到玩家存档。</summary>
    public sealed class BagBrowseStateModel
    {
        private ItemCategory currentCategory = ItemCategory.Weapon;
        private BagSortMode sortMode = BagSortMode.Quality;
        private BagSortDirection sortDirection = BagSortDirection.Descending;
        private BagEntryKey? selectedEntryKey;

        /// <summary>获取当前分类。</summary>
        public ItemCategory CurrentCategory => currentCategory;
        /// <summary>获取当前排序字段。</summary>
        public BagSortMode SortMode => sortMode;
        /// <summary>获取当前排序方向。</summary>
        public BagSortDirection SortDirection => sortDirection;
        /// <summary>获取当前选中的条目标识。</summary>
        public BagEntryKey? SelectedEntryKey => selectedEntryKey;

        /// <summary>当前分类变化时触发。</summary>
        public event Action<ItemCategory> CategoryChanged;
        /// <summary>排序设置变化时触发。</summary>
        public event Action SortChanged;
        /// <summary>选择变化时触发。</summary>
        public event Action<BagEntryKey?> SelectionChanged;

        /// <summary>切换分类并清空旧分类选择。</summary>
        /// <param name="category">目标分类。</param>
        public void SetCategory(ItemCategory category)
        {
            if (currentCategory == category) return;
            currentCategory = category;
            selectedEntryKey = null;
            // 先通知选择清空，再让 Controller 构建新分类；否则新分类刷新出的首项会被
            // 后续的旧分类清空事件再次覆盖，详情面板会停留在空状态。
            SelectionChanged?.Invoke(null);
            CategoryChanged?.Invoke(category);
        }

        /// <summary>设置排序字段。</summary>
        /// <param name="mode">排序字段。</param>
        public void SetSortMode(BagSortMode mode)
        {
            if (sortMode == mode) return;
            sortMode = mode;
            SortChanged?.Invoke();
        }

        /// <summary>切换排序方向。</summary>
        public void ToggleSortDirection()
        {
            sortDirection = sortDirection == BagSortDirection.Ascending
                ? BagSortDirection.Descending
                : BagSortDirection.Ascending;
            SortChanged?.Invoke();
        }

        /// <summary>设置当前选中的条目。</summary>
        /// <param name="entryKey">目标条目；为空表示清空选择。</param>
        public void SetSelection(BagEntryKey? entryKey)
        {
            if (Nullable.Equals(selectedEntryKey, entryKey)) return;
            selectedEntryKey = entryKey;
            SelectionChanged?.Invoke(entryKey);
        }
    }
}
