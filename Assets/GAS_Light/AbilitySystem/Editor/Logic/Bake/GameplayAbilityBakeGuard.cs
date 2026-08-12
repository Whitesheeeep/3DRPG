#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using WS_Modules.GAS.GameplayAbilitySystem;

namespace WS_Modules.GAS.Editor
{
    /// <summary>在进入 Play Mode 和正式 Build 前阻止使用未 Bake 或不一致的 Ability 数据。</summary>
    [InitializeOnLoad]
    public sealed class GameplayAbilityBakeGuard : IPreprocessBuildWithReport
    {
        /// <summary>获取 Build 前处理顺序。</summary>
        public int callbackOrder => 0;

        /// <summary>注册唯一的 Play Mode 切换校验回调。</summary>
        static GameplayAbilityBakeGuard() =>
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

        /// <summary>在 Build 开始前校验 Data、稳定 ID 历史与 Database 运行时索引的一致性。</summary>
        /// <param name="report">Unity Build 报告上下文。</param>
        /// <exception cref="BuildFailedException">Ability 数据不可用。</exception>
        public void OnPreprocessBuild(BuildReport report)
        {
            List<string> errors = ValidateProject();
            if (errors.Count > 0)
                throw new BuildFailedException(
                    "Gameplay Ability 数据不可用：\n" + string.Join("\n", errors));
        }

        /// <summary>离开 Edit Mode 时校验 Ability Bake 状态，失败则取消进入播放。</summary>
        /// <param name="state">当前 Play Mode 状态变化。</param>
        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode) return;
            List<string> errors = ValidateProject();
            if (errors.Count == 0) return;

            EditorApplication.isPlaying = false;
            EditorUtility.DisplayDialog(
                "Gameplay Ability Validation Failed",
                string.Join("\n", errors),
                "OK");
        }

        /// <summary>查找唯一 Database 并校验项目当前的完整 Bake 结果。</summary>
        /// <returns>错误列表；项目完全未使用 GA 时返回空列表。</returns>
        private static List<string> ValidateProject()
        {
            string[] abilityGuids = AssetDatabase.FindAssets("t:GameplayAbilityData");
            string[] databaseGuids = AssetDatabase.FindAssets("t:GameplayAbilityDatabase");
            if (abilityGuids.Length == 0 && databaseGuids.Length == 0)
                return new List<string>();

            if (databaseGuids.Length != 1)
                return new List<string>
                {
                    databaseGuids.Length == 0
                        ? "项目已包含 GameplayAbilityData，但不存在 GameplayAbilityDatabase。"
                        : $"项目中存在 {databaseGuids.Length} 个 GameplayAbilityDatabase；全局 AbilityId 历史必须唯一。"
                };

            string path = AssetDatabase.GUIDToAssetPath(databaseGuids[0]);
            GameplayAbilityDatabase database =
                AssetDatabase.LoadAssetAtPath<GameplayAbilityDatabase>(path);
            return GameplayAbilityBaker.ValidateBakedState(database);
        }
    }
}
#endif
