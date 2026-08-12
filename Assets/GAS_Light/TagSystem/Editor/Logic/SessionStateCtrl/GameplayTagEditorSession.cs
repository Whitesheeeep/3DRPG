#if UNITY_EDITOR
using System;
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

        #region 事件
        /// <summary>当前 Tag Database 改变后通知其他 Editor 页面刷新显示上下文。</summary>
        public static event Action<GameplayTagDatabase> DatabaseChanged;
        #endregion

        /// <summary>记录当前数据库资产 Guid。</summary>
        public static void SetDatabase(GameplayTagDatabase database)
        {
            string path = database == null ? string.Empty : AssetDatabase.GetAssetPath(database);
            SessionState.SetString(DatabaseGuidKey, string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path));
            DatabaseChanged?.Invoke(database);
        }

        /// <summary>恢复上次选择的数据库。</summary>
        public static GameplayTagDatabase GetDatabase()
        {
            string guid = SessionState.GetString(DatabaseGuidKey, string.Empty);
            string path = string.IsNullOrEmpty(guid) ? string.Empty : AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameplayTagDatabase>(path);
        }

        /// <summary>按 PropertyDrawer 的统一规则解析当前可明确使用的 Tag Database。</summary>
        /// <param name="error">无法唯一确定数据库时返回用于 Editor 显示的原因。</param>
        /// <returns>Session 中的数据库或项目内唯一数据库；无法确定时返回 null。</returns>
        public static GameplayTagDatabase ResolveSingleDatabase(out string error)
        {
            GameplayTagDatabase sessionDatabase = GetDatabase();
            if (sessionDatabase != null)
            {
                error = string.Empty;
                return sessionDatabase;
            }

            string[] guids = AssetDatabase.FindAssets("t:GameplayTagDatabase");
            if (guids.Length == 1)
            {
                error = string.Empty;
                return AssetDatabase.LoadAssetAtPath<GameplayTagDatabase>(
                    AssetDatabase.GUIDToAssetPath(guids[0]));
            }

            error = guids.Length == 0
                ? "未找到 GameplayTagDatabase（点击打开窗口）"
                : "存在多个数据库，请先在 Tag 窗口选择";
            return null;
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
