#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using WS_Modules.GAS.GameplayAbilitySystem;
using WS_Modules.GAS.GameplayEffect;

namespace WS_Modules.GAS.Editor
{
    /// <summary>集中执行 GA 资产扫描、创建、复制、删除、重命名、Ping 与跨字段校验。</summary>
    public sealed class GameplayAbilityEditorService
    {
        #region 资产查询
        /// <summary>扫描项目中的全部 GameplayAbilityData 并按名称和路径排序。</summary>
        public List<GameplayAbilityData> FindAllAbilities()
        {
            string[] guids = AssetDatabase.FindAssets("t:GameplayAbilityData");
            var results = new List<GameplayAbilityData>(guids.Length);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                GameplayAbilityData ability = AssetDatabase.LoadAssetAtPath<GameplayAbilityData>(path);
                if (ability != null) results.Add(ability);
            }

            results.Sort(CompareAbilities);
            return results;
        }
        #endregion

        #region 资产操作
        /// <summary>通过保存面板创建一个新的 GA 资产。</summary>
        public GameplayAbilityData CreateAbility()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Gameplay Ability",
                "GameplayAbilityData",
                "asset",
                "Choose where to create the Gameplay Ability asset.");
            if (string.IsNullOrEmpty(path)) return null;

            var ability = ScriptableObject.CreateInstance<GameplayAbilityData>();
            AssetDatabase.CreateAsset(ability, path);
            AssetDatabase.SaveAssets();
            return ability;
        }

        /// <summary>复制指定 GA 资产并保持内部 GE 引用。</summary>
        public GameplayAbilityData DuplicateAbility(GameplayAbilityData source)
        {
            if (source == null) return null;
            string sourcePath = AssetDatabase.GetAssetPath(source);
            string directory = Path.GetDirectoryName(sourcePath)?.Replace('\\', '/');
            string targetPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{directory}/{source.name} Copy.asset");
            if (!AssetDatabase.CopyAsset(sourcePath, targetPath)) return null;
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<GameplayAbilityData>(targetPath);
        }

        /// <summary>将指定 GA 资产移动到系统回收站。</summary>
        public bool DeleteAbility(GameplayAbilityData ability)
        {
            if (ability == null) return false;
            return AssetDatabase.MoveAssetToTrash(AssetDatabase.GetAssetPath(ability));
        }

        /// <summary>使用 Unity AssetDatabase 规则重命名真实 GA 资产。</summary>
        public bool TryRenameAbility(
            GameplayAbilityData ability,
            string newName,
            out string error)
        {
            string normalized = newName?.Trim() ?? string.Empty;
            if (ability == null)
            {
                error = "Gameplay Ability asset no longer exists.";
                return false;
            }
            if (string.IsNullOrEmpty(normalized))
            {
                error = "Gameplay Ability name cannot be empty.";
                return false;
            }
            if (string.Equals(ability.name, normalized, StringComparison.Ordinal))
            {
                error = string.Empty;
                return true;
            }

            error = AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(ability), normalized);
            if (!string.IsNullOrEmpty(error)) return false;
            AssetDatabase.SaveAssets();
            return true;
        }

        /// <summary>在 Project 窗口定位指定 GA，不改变当前 Selection。</summary>
        public void PingAbility(GameplayAbilityData ability)
        {
            if (ability != null) EditorGUIUtility.PingObject(ability);
        }
        #endregion

        #region 校验
        /// <summary>按第一阶段运行时契约校验 GA 直接持有的配置。</summary>
        public List<GameplayAbilityValidationIssue> Validate(GameplayAbilityData ability)
        {
            var issues = new List<GameplayAbilityValidationIssue>();
            if (ability == null) return issues;

            if (ability.CostEffect != null &&
                ability.CostEffect.DurationType != E_GameEffectDurationType.Instant)
                issues.Add(Error(
                    $"Cost GE '{GetAssetLabel(ability.CostEffect)}' 必须是 Instant。"));

            if (ability.CooldownEffect != null &&
                ability.CooldownEffect.DurationType != E_GameEffectDurationType.Duration &&
                ability.CooldownEffect.DurationType != E_GameEffectDurationType.Infinite)
                issues.Add(Error(
                    $"Cooldown GE '{GetAssetLabel(ability.CooldownEffect)}' 必须是 Duration 或 Infinite。"));

            ValidateEffectList("SelfEffects", ability.SelfEffects, issues);
            ValidateEffectList("TargetEffects", ability.TargetEffects, issues);
            if (ability.SelfEffects.Count == 0 && ability.TargetEffects.Count == 0)
                issues.Add(new GameplayAbilityValidationIssue(
                    GameplayAbilityValidationSeverity.Info,
                    "该 Ability 没有 Self/Target Effect；仅提交 Cost/Cooldown 的配置仍然合法。"));
            return issues;
        }
        #endregion

        #region 内部辅助
        // 以名称优先、资产路径次优先建立稳定列表顺序。
        private static int CompareAbilities(GameplayAbilityData left, GameplayAbilityData right)
        {
            int nameResult = string.Compare(left.name, right.name, StringComparison.OrdinalIgnoreCase);
            return nameResult != 0
                ? nameResult
                : string.Compare(
                    AssetDatabase.GetAssetPath(left),
                    AssetDatabase.GetAssetPath(right),
                    StringComparison.OrdinalIgnoreCase);
        }

        // 为每个空 GE 引用输出包含列表名和索引的明确问题。
        private static void ValidateEffectList(
            string listName,
            IReadOnlyList<GameplayEffectData> effects,
            ICollection<GameplayAbilityValidationIssue> issues)
        {
            for (int i = 0; i < effects.Count; i++)
                if (effects[i] == null)
                    issues.Add(Error($"{listName}[{i}] 的 GameplayEffectData 引用为空。"));
        }

        // 创建 Error 级别结果，避免每条规则重复构造代码。
        private static GameplayAbilityValidationIssue Error(string message) =>
            new(GameplayAbilityValidationSeverity.Error, message);

        // 将资产名称和路径组合为可直接定位的校验文本。
        private static string GetAssetLabel(UnityEngine.Object asset) =>
            $"{asset.name} ({AssetDatabase.GetAssetPath(asset)})";
        #endregion
    }
}
#endif
