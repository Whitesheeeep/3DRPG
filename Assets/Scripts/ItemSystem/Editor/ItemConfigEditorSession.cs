#if UNITY_EDITOR
using UnityEditor;

namespace RPG.ItemSystem.Editor
{
    /// <summary>保存 Item 配置窗口的非业务会话状态。</summary>
    internal static class ItemConfigEditorSession
    {
        private const string DatabaseKey = "RPG.ItemConfig.Database";
        private const string DefinitionKey = "RPG.ItemConfig.Definition";
        private const string SearchKey = "RPG.ItemConfig.Search";
        private const string CategoryKey = "RPG.ItemConfig.Category";
        private const string SortFieldKey = "RPG.ItemConfig.SortField";
        private const string SortDirectionKey = "RPG.ItemConfig.SortDirection";

        /// <summary>获取上次选择的数据库资产路径。</summary>
        internal static string DatabasePath => SessionState.GetString(DatabaseKey, string.Empty);

        /// <summary>保存数据库资产路径。</summary>
        /// <param name="path">资产路径。</param>
        internal static void SetDatabasePath(string path) => SessionState.SetString(DatabaseKey, path ?? string.Empty);

        /// <summary>获取上次选择的物品资产路径。</summary>
        internal static string DefinitionPath => SessionState.GetString(DefinitionKey, string.Empty);

        /// <summary>保存物品资产路径。</summary>
        /// <param name="path">资产路径。</param>
        internal static void SetDefinitionPath(string path) => SessionState.SetString(DefinitionKey, path ?? string.Empty);

        /// <summary>获取搜索文本。</summary>
        internal static string Search => SessionState.GetString(SearchKey, string.Empty);

        /// <summary>保存搜索文本。</summary>
        /// <param name="value">搜索文本。</param>
        internal static void SetSearch(string value) => SessionState.SetString(SearchKey, value ?? string.Empty);

        /// <summary>获取分类筛选文本。</summary>
        internal static string Category => SessionState.GetString(CategoryKey, "全部类型");

        /// <summary>保存分类筛选文本。</summary>
        /// <param name="value">分类筛选文本。</param>
        internal static void SetCategory(string value) => SessionState.SetString(CategoryKey, value ?? "全部类型");

        /// <summary>获取列表排序字段。</summary>
        internal static string SortField => SessionState.GetString(SortFieldKey, "默认排序优先级");

        /// <summary>保存列表排序字段。</summary>
        /// <param name="value">排序字段中文名称。</param>
        internal static void SetSortField(string value) => SessionState.SetString(SortFieldKey, value ?? "默认排序优先级");

        /// <summary>获取列表排序方向。</summary>
        internal static string SortDirection => SessionState.GetString(SortDirectionKey, "降序");

        /// <summary>保存列表排序方向。</summary>
        /// <param name="value">排序方向中文名称。</param>
        internal static void SetSortDirection(string value) => SessionState.SetString(SortDirectionKey, value ?? "降序");

        /// <summary>解析当前会话对应的 ItemDatabase，供编辑器字段选择器复用。</summary>
        /// <returns>当前窗口选择的数据库；无法唯一确定时返回空。</returns>
        internal static ItemDatabase ResolveDatabase()
        {
            string path = DatabasePath;
            if (!string.IsNullOrEmpty(path))
            {
                ItemDatabase sessionDatabase = AssetDatabase.LoadAssetAtPath<ItemDatabase>(path);
                if (sessionDatabase != null) return sessionDatabase;
            }

            string[] guids = AssetDatabase.FindAssets("t:ItemDatabase");
            ItemDatabase resolved = null;
            for (int index = 0; index < guids.Length; index++)
            {
                ItemDatabase candidate = AssetDatabase.LoadAssetAtPath<ItemDatabase>(AssetDatabase.GUIDToAssetPath(guids[index]));
                if (candidate == null) continue;
                if (resolved != null) return null;
                resolved = candidate;
            }

            return resolved;
        }
    }
}
#endif
