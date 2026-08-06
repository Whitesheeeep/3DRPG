#if UNITY_EDITOR
using UnityEditor;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.Editor
{
    /// <summary>集中保存 Gameplay Tag Editor 跨域重载的会话状态。</summary>
    internal static class GameplayTagEditorSession
    {
        private const string DatabaseGuidKey = "WSFrame.GAS.GameplayTag.DatabaseGuid";
        private const string SelectedNodeGuidKey = "WSFrame.GAS.GameplayTag.SelectedNodeGuid";
        private const string SearchKey = "WSFrame.GAS.GameplayTag.Search";
        private const string ExpandedGuidsKey = "WSFrame.GAS.GameplayTag.ExpandedGuids";

        /// <summary>记录当前数据库资产 Guid。</summary>
        public static void SetDatabase(GameplayTagDatabase database)
        {
            string path = database == null ? string.Empty : AssetDatabase.GetAssetPath(database);
            SessionState.SetString(DatabaseGuidKey, string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path));
        }

        /// <summary>恢复上次选择的数据库。</summary>
        public static GameplayTagDatabase GetDatabase()
        {
            string guid = SessionState.GetString(DatabaseGuidKey, string.Empty);
            string path = string.IsNullOrEmpty(guid) ? string.Empty : AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameplayTagDatabase>(path);
        }

        /// <summary>记录当前作者节点 Guid。</summary>
        public static void SetSelectedNodeGuid(string guid) => SessionState.SetString(SelectedNodeGuidKey, guid ?? string.Empty);
        /// <summary>恢复作者节点 Guid。</summary>
        public static string GetSelectedNodeGuid() => SessionState.GetString(SelectedNodeGuidKey, string.Empty);
        /// <summary>记录搜索文本。</summary>
        public static void SetSearch(string value) => SessionState.SetString(SearchKey, value ?? string.Empty);
        /// <summary>恢复搜索文本。</summary>
        public static string GetSearch() => SessionState.GetString(SearchKey, string.Empty);
        /// <summary>记录逗号分隔的展开节点 Guid。</summary>
        public static void SetExpandedGuids(string value) => SessionState.SetString(ExpandedGuidsKey, value ?? string.Empty);
        /// <summary>恢复逗号分隔的展开节点 Guid。</summary>
        public static string GetExpandedGuids() => SessionState.GetString(ExpandedGuidsKey, string.Empty);
    }
}
#endif
