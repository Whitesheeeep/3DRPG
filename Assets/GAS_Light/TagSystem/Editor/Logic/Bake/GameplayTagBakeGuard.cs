#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using WS_Modules.GAS.TAG;

namespace WSFrame.GAS.Editor
{
    /// <summary>在进入 Play Mode 和正式构建前阻止使用过期或非法 Tag 数据。</summary>
    [InitializeOnLoad]
    public sealed class GameplayTagBakeGuard : IPreprocessBuildWithReport
    {
        /// <summary>获取构建前处理顺序。</summary>
        public int callbackOrder => 0;

        static GameplayTagBakeGuard() => EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

        /// <summary>在构建开始前验证全部 Gameplay Tag 数据库。</summary>
        public void OnPreprocessBuild(BuildReport report)
        {
            if (!TryValidateAll(out string message)) throw new BuildFailedException(message);
        }

        // 仅在离开 Edit Mode 时检查，避免播放期间反复扫描资产。
        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode || TryValidateAll(out string message)) return;
            EditorApplication.isPlaying = false;
            EditorUtility.DisplayDialog("Gameplay Tag Bake Required", message, "OK");
        }

        // 扫描项目内数据库；不存在数据库时不阻止尚未接入 GAS_Light 的场景。
        private static bool TryValidateAll(out string message)
        {
            string[] guids = AssetDatabase.FindAssets("t:GameplayTagDatabase");
            var failures = new List<string>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameplayTagDatabase database = AssetDatabase.LoadAssetAtPath<GameplayTagDatabase>(path);
                if (database == null) continue;
                if (database.BakeDirty)
                {
                    failures.Add($"{path}: 数据已修改但尚未 Bake。");
                    continue;
                }

                List<GameplayTagValidationIssue> issues = new GameplayTagEditorService(database).Validate();
                int errors = issues.Count(issue => issue.Severity == GameplayTagValidationSeverity.Error);
                if (errors > 0) failures.Add($"{path}: 存在 {errors} 个校验错误。");
            }

            message = failures.Count == 0 ? string.Empty : "Gameplay Tag 数据不可用于运行：\n" + string.Join("\n", failures);
            return failures.Count == 0;
        }
    }
}
#endif