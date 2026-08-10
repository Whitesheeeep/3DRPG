#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using WS_Modules.GAS.GameplayCue;

namespace WS_Modules.GAS.Editor
{
    /// <summary>保存 Cue 编辑器的数据库、选择和搜索状态。</summary>
    public static class GameplayCueEditorSession
    {
        private const string DatabaseGuidKey = "WSFrame.GAS.GameplayCue.DatabaseGuid";
        private const string CueGuidKey = "WSFrame.GAS.GameplayCue.CueGuid";
        private const string SearchKey = "WSFrame.GAS.GameplayCue.Search";

        /// <summary>获取上次编辑的 Cue Database。</summary>
        public static GameplayCueDatabase GetDatabase()
        {
            string guid = SessionState.GetString(DatabaseGuidKey, string.Empty);
            string path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameplayCueDatabase>(path);
        }

        /// <summary>保存当前 Cue Database。</summary>
        /// <param name="database">要保存的数据库。</param>
        public static void SetDatabase(GameplayCueDatabase database)
        {
            SessionState.SetString(
                DatabaseGuidKey,
                database == null ? string.Empty : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(database)));
        }

        /// <summary>获取上次选中的 CueData。</summary>
        public static GameplayCueData GetCue()
        {
            string guid = SessionState.GetString(CueGuidKey, string.Empty);
            string path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameplayCueData>(path);
        }

        /// <summary>保存当前 CueData。</summary>
        /// <param name="cue">要保存的 CueData。</param>
        public static void SetCue(GameplayCueData cue)
        {
            SessionState.SetString(
                CueGuidKey,
                cue == null ? string.Empty : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(cue)));
        }

        /// <summary>获取上次搜索文本。</summary>
        public static string GetSearch() => SessionState.GetString(SearchKey, string.Empty);

        /// <summary>保存当前搜索文本。</summary>
        /// <param name="search">搜索文本。</param>
        public static void SetSearch(string search) => SessionState.SetString(SearchKey, search ?? string.Empty);
    }
}
#endif
