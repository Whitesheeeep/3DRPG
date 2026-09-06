#if UNITY_EDITOR
using UnityEditor;

namespace RPG.Character.Editor
{
    /// <summary>保存角色配置编辑器的非业务会话状态，供窗口和引用 Drawer 共同解析数据库。</summary>
    internal static class CharacterConfigEditorSession
    {
        #region 会话键

        private const string DatabasePathKey = "RPG.CharacterConfig.Database";
        private const string ConfigPathKey = "RPG.CharacterConfig.Config";
        private const string SearchKey = "RPG.CharacterConfig.Search";

        #endregion

        #region 属性与写入

        /// <summary>获取当前会话记录的数据库路径。</summary>
        internal static string DatabasePath => SessionState.GetString(DatabasePathKey, string.Empty);

        /// <summary>获取当前会话记录的配置路径。</summary>
        internal static string ConfigPath => SessionState.GetString(ConfigPathKey, string.Empty);

        /// <summary>获取当前搜索文本。</summary>
        internal static string Search => SessionState.GetString(SearchKey, string.Empty);

        /// <summary>记录当前数据库路径。</summary>
        internal static void SetDatabase(CharacterDatabase database) =>
            SessionState.SetString(DatabasePathKey, database == null ? string.Empty : AssetDatabase.GetAssetPath(database));

        /// <summary>记录当前配置路径。</summary>
        internal static void SetConfig(CharacterConfig config) =>
            SessionState.SetString(ConfigPathKey, config == null ? string.Empty : AssetDatabase.GetAssetPath(config));

        /// <summary>记录当前搜索文本。</summary>
        internal static void SetSearch(string search) => SessionState.SetString(SearchKey, search ?? string.Empty);

        #endregion

        #region 数据库解析

        /// <summary>优先读取会话数据库，否则仅在项目存在唯一数据库时返回该数据库。</summary>
        /// <returns>可唯一确定时返回数据库，否则返回空。</returns>
        internal static CharacterDatabase ResolveDatabase()
        {
            if (!string.IsNullOrEmpty(DatabasePath))
            {
                CharacterDatabase sessionDatabase = AssetDatabase.LoadAssetAtPath<CharacterDatabase>(DatabasePath);
                if (sessionDatabase != null) return sessionDatabase;
            }

            string[] guids = AssetDatabase.FindAssets("t:CharacterDatabase");
            CharacterDatabase resolvedDatabase = null;
            for (int index = 0; index < guids.Length; index++)
            {
                CharacterDatabase candidate = AssetDatabase.LoadAssetAtPath<CharacterDatabase>(AssetDatabase.GUIDToAssetPath(guids[index]));
                if (candidate == null) continue;
                if (resolvedDatabase != null) return null;
                resolvedDatabase = candidate;
            }

            return resolvedDatabase;
        }

        /// <summary>读取窗口重建前保存的角色配置，配置失效时返回空。</summary>
        /// <returns>当前会话记录的角色配置。</returns>
        internal static CharacterConfig ResolveConfig()
        {
            if (string.IsNullOrEmpty(ConfigPath)) return null;
            return AssetDatabase.LoadAssetAtPath<CharacterConfig>(ConfigPath);
        }

        #endregion
    }
}
#endif
