#if UNITY_EDITOR
using UnityEditor;
using WS_Modules.GAS.GameplayAbilitySystem;

namespace WS_Modules.GAS.Editor
{
    /// <summary>通过 Asset GUID 与 SessionState 恢复 GA Editor 资产和搜索状态。</summary>
    public static class GameplayAbilityEditorSession
    {
        #region 常量
        private const string AbilityKey = "WSFrame.GAS.GameplayAbility.Ability";
        private const string DatabaseKey = "WSFrame.GAS.GameplayAbility.Database";
        private const string SearchKey = "WSFrame.GAS.GameplayAbility.Search";
        #endregion

        #region 资产与 UI 状态
        /// <summary>保存当前 GA 的 Asset GUID。</summary>
        public static void SetAbility(GameplayAbilityData ability)
        {
            string path = ability == null ? string.Empty : AssetDatabase.GetAssetPath(ability);
            SessionState.SetString(
                AbilityKey,
                string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path));
        }

        /// <summary>恢复当前 GA 资产。</summary>
        public static GameplayAbilityData GetAbility()
        {
            string guid = SessionState.GetString(AbilityKey, string.Empty);
            if (string.IsNullOrEmpty(guid)) return null;
            string path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameplayAbilityData>(path);
        }

        /// <summary>保存当前 GameplayAbilityDatabase 的 Asset GUID。</summary>
        /// <param name="database">当前 GA Editor 使用的 Database。</param>
        public static void SetDatabase(GameplayAbilityDatabase database)
        {
            string path = database == null ? string.Empty : AssetDatabase.GetAssetPath(database);
            SessionState.SetString(
                DatabaseKey,
                string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path));
        }

        /// <summary>从 SessionState 恢复上次选择的 GameplayAbilityDatabase。</summary>
        /// <returns>资产仍存在时返回 Database，否则返回 null。</returns>
        public static GameplayAbilityDatabase GetDatabase()
        {
            string guid = SessionState.GetString(DatabaseKey, string.Empty);
            if (string.IsNullOrEmpty(guid)) return null;
            string path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameplayAbilityDatabase>(path);
        }

        /// <summary>获取或保存当前搜索文本。</summary>
        public static string Search
        {
            get => SessionState.GetString(SearchKey, string.Empty);
            set => SessionState.SetString(SearchKey, value ?? string.Empty);
        }
        #endregion
    }
}
#endif
