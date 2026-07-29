#if UNITY_EDITOR
using UnityEditor;
using WS_Modules.GAS.AttributeSystem;

namespace WS_Modules.GAS.Editor
{
    /// <summary>使用 Asset GUID 与 SessionState 恢复 Attribute 编辑器选择状态。</summary>
    public static class GameplayAttributeEditorSession
    {
        #region 常量

        private const string RegistryKey = "WSFrame.GAS.Attribute.Registry";
        private const string SetKey = "WSFrame.GAS.Attribute.Set";
        private const string PageKey = "WSFrame.GAS.Attribute.Page";
        private const string SearchKey = "WSFrame.GAS.Attribute.Search";
        private const string SpecSelectionKey = "WSFrame.GAS.Attribute.SpecSelection";
        private const string DefinitionSelectionKey = "WSFrame.GAS.Attribute.DefinitionSelection";

        #endregion

        #region 资产状态

        /// <summary>保存当前 Registry 的 Asset GUID。</summary>
        /// <param name="registry">当前 Registry；null 表示清空。</param>
        public static void SetRegistry(GameplayAttributeRegistry registry) =>
            SetAssetGuid(RegistryKey, registry);

        /// <summary>恢复当前 Registry。</summary>
        /// <returns>资产仍存在时返回 Registry，否则返回 null。</returns>
        public static GameplayAttributeRegistry GetRegistry() =>
            GetAsset<GameplayAttributeRegistry>(RegistryKey);

        /// <summary>保存当前 AttributeSet 的 Asset GUID。</summary>
        /// <param name="set">当前 Set；null 表示清空。</param>
        public static void SetAttributeSet(GameplayAttributeSet set) => SetAssetGuid(SetKey, set);

        /// <summary>恢复当前 AttributeSet。</summary>
        /// <returns>资产仍存在时返回 Set，否则返回 null。</returns>
        public static GameplayAttributeSet GetAttributeSet() => GetAsset<GameplayAttributeSet>(SetKey);

        /// <summary>在项目中解析唯一 Registry；存在多个时优先使用 Session 选择。</summary>
        /// <param name="error">无法得到明确 Registry 时返回原因。</param>
        /// <returns>明确 Registry 或 null。</returns>
        public static GameplayAttributeRegistry ResolveSingleRegistry(out string error)
        {
            GameplayAttributeRegistry session = GetRegistry();
            if (session != null)
            {
                error = string.Empty;
                return session;
            }

            string[] guids = AssetDatabase.FindAssets("t:GameplayAttributeRegistry");
            if (guids.Length == 1)
            {
                GameplayAttributeRegistry registry =
                    AssetDatabase.LoadAssetAtPath<GameplayAttributeRegistry>(
                        AssetDatabase.GUIDToAssetPath(guids[0]));
                SetRegistry(registry);
                error = string.Empty;
                return registry;
            }

            error = guids.Length == 0
                ? "项目中没有 GameplayAttributeRegistry。"
                : "项目中存在多个 GameplayAttributeRegistry，请先在 Attribute 窗口选择。";
            return null;
        }

        #endregion

        #region UI 状态

        /// <summary>保存当前 Attribute 子页面。</summary>
        public static GameplayAttributeEditorPage Page
        {
            get => (GameplayAttributeEditorPage)SessionState.GetInt(
                PageKey, (int)GameplayAttributeEditorPage.Specs);
            set => SessionState.SetInt(PageKey, (int)value);
        }

        /// <summary>保存当前搜索文本。</summary>
        public static string Search
        {
            get => SessionState.GetString(SearchKey, string.Empty);
            set => SessionState.SetString(SearchKey, value ?? string.Empty);
        }

        /// <summary>保存当前 Spec Guid。</summary>
        public static string SelectedSpecGuid
        {
            get => SessionState.GetString(SpecSelectionKey, string.Empty);
            set => SessionState.SetString(SpecSelectionKey, value ?? string.Empty);
        }

        /// <summary>保存当前 Set Definition 的 AttributeId；-1 表示无选择。</summary>
        public static int SelectedDefinitionId
        {
            get => SessionState.GetInt(DefinitionSelectionKey, -1);
            set => SessionState.SetInt(DefinitionSelectionKey, value);
        }

        #endregion

        #region 内部辅助

        // 将 Unity 资产引用转换为可跨域重载恢复的 Asset GUID。
        private static void SetAssetGuid(string key, UnityEngine.Object asset)
        {
            string path = asset == null ? string.Empty : AssetDatabase.GetAssetPath(asset);
            SessionState.SetString(
                key,
                string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path));
        }

        // 按 Session 中的 Asset GUID 恢复指定类型资产。
        private static T GetAsset<T>(string key) where T : UnityEngine.Object
        {
            string guid = SessionState.GetString(key, string.Empty);
            if (string.IsNullOrEmpty(guid)) return null;
            string path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<T>(path);
        }

        #endregion
    }
}
#endif
