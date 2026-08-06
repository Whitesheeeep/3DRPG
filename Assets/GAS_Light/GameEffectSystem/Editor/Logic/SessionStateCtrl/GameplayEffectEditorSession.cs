#if UNITY_EDITOR
using UnityEditor;
using WS_Modules.GAS.GameplayEffect;

namespace WS_Modules.GAS.Editor
{
    /// <summary>通过 Asset GUID 与 SessionState 恢复 GE Editor 选择状态。</summary>
    public static class GameplayEffectEditorSession
    {
        #region 常量

        private const string EffectKey = "WSFrame.GAS.GameplayEffect.Effect";
        private const string SearchKey = "WSFrame.GAS.GameplayEffect.Search";
        private const string ModifierIndexKey = "WSFrame.GAS.GameplayEffect.ModifierIndex";

        #endregion

        #region 资产与 UI 状态

        /// <summary>保存当前 GE 的 Asset GUID。</summary>
        /// <param name="effect">当前 GE；null 表示清空。</param>
        public static void SetEffect(GameplayEffectData effect)
        {
            string path = effect == null ? string.Empty : AssetDatabase.GetAssetPath(effect);
            SessionState.SetString(
                EffectKey,
                string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path));
        }

        /// <summary>恢复当前 GE 资产。</summary>
        /// <returns>资产仍存在时返回对象，否则返回 null。</returns>
        public static GameplayEffectData GetEffect()
        {
            string guid = SessionState.GetString(EffectKey, string.Empty);
            if (string.IsNullOrEmpty(guid)) return null;
            string path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameplayEffectData>(path);
        }

        /// <summary>获取或保存当前搜索文本。</summary>
        public static string Search
        {
            get => SessionState.GetString(SearchKey, string.Empty);
            set => SessionState.SetString(SearchKey, value ?? string.Empty);
        }

        /// <summary>获取或保存当前 Modifier 索引；-1 表示无选择。</summary>
        public static int SelectedModifierIndex
        {
            get => SessionState.GetInt(ModifierIndexKey, -1);
            set => SessionState.SetInt(ModifierIndexKey, value);
        }

        #endregion
    }
}
#endif
