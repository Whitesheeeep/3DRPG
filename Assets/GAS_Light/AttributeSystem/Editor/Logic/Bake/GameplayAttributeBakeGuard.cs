#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace WS_Modules.GAS.Editor
{
    /// <summary>在进入 Play Mode 和正式 Build 前阻止使用过期或非法 Attribute 数据。</summary>
    [InitializeOnLoad]
    public sealed class GameplayAttributeBakeGuard : IPreprocessBuildWithReport
    {
        /// <summary>获取构建前处理顺序。</summary>
        public int callbackOrder => 0;

        // 注册一次 Play Mode 切换守卫。
        static GameplayAttributeBakeGuard() =>
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

        /// <summary>构建开始前验证唯一 Registry 和全部 AttributeSet。</summary>
        /// <param name="report">Unity Build 报告上下文。</param>
        /// <exception cref="BuildFailedException">Attribute 数据过期或非法。</exception>
        public void OnPreprocessBuild(BuildReport report)
        {
            if (!TryValidateAll(out string message)) throw new BuildFailedException(message);
        }

        // 仅在离开 Edit Mode 时检查，失败后取消进入播放。
        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode || TryValidateAll(out string message)) return;
            EditorApplication.isPlaying = false;
            EditorUtility.DisplayDialog("Gameplay Attribute Validation Failed", message, "OK");
        }

        // 验证项目中 Registry 唯一、已 Bake 且所有 Set 引用有效；完全未接入时不阻止运行。
        private static bool TryValidateAll(out string message)
        {
            string[] registryGuids = AssetDatabase.FindAssets("t:GameplayAttributeRegistry");
            string[] setGuids = AssetDatabase.FindAssets("t:GameplayAttributeSet");
            if (registryGuids.Length == 0 && setGuids.Length == 0)
            {
                message = string.Empty;
                return true;
            }

            var errors = new List<string>();
            if (registryGuids.Length != 1)
            {
                errors.Add(registryGuids.Length == 0
                    ? "项目已使用 AttributeSet，但不存在 GameplayAttributeRegistry。"
                    : $"项目中存在 {registryGuids.Length} 个 GameplayAttributeRegistry；运行时身份必须唯一。");
            }
            else
            {
                string path = AssetDatabase.GUIDToAssetPath(registryGuids[0]);
                GameplayAttributeRegistry registry =
                    AssetDatabase.LoadAssetAtPath<GameplayAttributeRegistry>(path);
                if (registry == null)
                    errors.Add($"无法加载 GameplayAttributeRegistry：{path}");
                else
                {
                    if (registry.BakeDirty)
                        errors.Add($"{path}: Attribute Specs 已修改但尚未 Bake。");
                    errors.AddRange(GameplayAttributeBaker.ValidateRegistry(registry));
                    errors.AddRange(GameplayAttributeBaker.ValidateAllSets(registry));
                }
            }

            message = errors.Count == 0
                ? string.Empty
                : "Gameplay Attribute 数据不可用于运行：\n" + string.Join("\n", errors);
            return errors.Count == 0;
        }
    }
}
#endif
