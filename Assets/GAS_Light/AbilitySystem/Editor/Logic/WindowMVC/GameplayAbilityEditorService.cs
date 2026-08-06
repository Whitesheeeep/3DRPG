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
    /// <summary>集中执行 GA 资产操作、具体类型发现与跨字段校验。</summary>
    public sealed class GameplayAbilityEditorService
    {
        #region 资产与类型查询
        /// <summary>扫描项目中的全部 GameplayAbilityData 并建立稳定顺序。</summary>
        public List<GameplayAbilityData> FindAllAbilities()
        {
            string[] guids = AssetDatabase.FindAssets("t:GameplayAbilityData");
            var results = new List<GameplayAbilityData>(guids.Length);
            for (int i = 0; i < guids.Length; i++)
            {
                GameplayAbilityData ability = AssetDatabase.LoadAssetAtPath<GameplayAbilityData>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                if (ability != null) results.Add(ability);
            }

            results.Sort(CompareAbilities);
            return results;
        }

        /// <summary>发现可实例化且公开的具体 GameplayAbilityData 子类。</summary>
        public List<Type> FindCreatableAbilityTypes()
        {
            var results = new List<Type>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<GameplayAbilityData>())
                if ((type.IsPublic || type.IsNestedPublic) &&
                    !type.IsAbstract && !type.IsGenericType &&
                    typeof(GameplayAbilityData).IsAssignableFrom(type))
                    results.Add(type);
            results.Sort((left, right) =>
                string.Compare(left.FullName, right.FullName, StringComparison.Ordinal));
            return results;
        }
        #endregion

        #region 资产操作
        /// <summary>通过保存面板创建指定具体类型的 GA 资产。</summary>
        public GameplayAbilityData CreateAbility(Type abilityType)
        {
            if (abilityType == null ||
                (!abilityType.IsPublic && !abilityType.IsNestedPublic) ||
                abilityType.IsAbstract || abilityType.IsGenericType ||
                !typeof(GameplayAbilityData).IsAssignableFrom(abilityType))
                throw new ArgumentException("必须提供可实例化的 GameplayAbilityData 子类。", nameof(abilityType));

            string path = EditorUtility.SaveFilePanelInProject(
                "Create Gameplay Ability",
                abilityType.Name,
                "asset",
                "Choose where to create the Gameplay Ability asset.");
            if (string.IsNullOrEmpty(path)) return null;

            var ability = (GameplayAbilityData)ScriptableObject.CreateInstance(abilityType);
            AssetDatabase.CreateAsset(ability, path);
            AssetDatabase.SaveAssets();
            return ability;
        }

        /// <summary>复制指定 GA 资产并保持其具体类型与内部引用。</summary>
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
        public bool DeleteAbility(GameplayAbilityData ability) =>
            ability != null && AssetDatabase.MoveAssetToTrash(AssetDatabase.GetAssetPath(ability));

        /// <summary>使用 AssetDatabase 规则重命名真实 GA 资产。</summary>
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

        /// <summary>在 Project 窗口定位指定 GA，但不改变 Selection。</summary>
        public void PingAbility(GameplayAbilityData ability)
        {
            if (ability != null) EditorGUIUtility.PingObject(ability);
        }
        #endregion

        #region 校验
        /// <summary>校验 GA 公共提交契约和异步 Task 树。</summary>
        public List<GameplayAbilityValidationIssue> Validate(GameplayAbilityData ability)
        {
            var issues = new List<GameplayAbilityValidationIssue>();
            if (ability == null) return issues;

            if (ability.CostEffect != null &&
                ability.CostEffect.DurationType != E_GameEffectDurationType.Instant)
                issues.Add(Error($"Cost GE '{GetAssetLabel(ability.CostEffect)}' 必须是 Instant。"));

            if (ability.CooldownEffect != null &&
                ability.CooldownEffect.DurationType != E_GameEffectDurationType.Duration &&
                ability.CooldownEffect.DurationType != E_GameEffectDurationType.Infinite)
                issues.Add(Error(
                    $"Cooldown GE '{GetAssetLabel(ability.CooldownEffect)}' 必须是 Duration 或 Infinite。"));

            ValidateEffectReferences(ability.Effects, issues);

            if (ability is AsynchronousGameplayAbilityData asynchronous)
            {
                if (asynchronous.RootTask == null)
                    issues.Add(Error("异步 Ability 必须配置 Root Task。"));
                else
                    ValidateTask(asynchronous.RootTask, "RootTask", issues);
            }

            ValidateSpecializedAbility(ability, issues);
            return issues;
        }

        // 校验具体同步/异步 Ability 的专属配置，不把 GE 内部规则重复到 GA Validator。
        private static void ValidateSpecializedAbility(
            GameplayAbilityData ability,
            ICollection<GameplayAbilityValidationIssue> issues)
        {
            if (ability is InstantGameplayAbilityData instant)
            {
                ValidateInstantEffects(instant.Effects, issues);
                return;
            }

            if (ability is PassiveGameplayAbilityData passive)
            {
                if (passive.RootTask is not PassiveGameplayAbilityTaskConfig)
                    issues.Add(Error("Passive Ability 必须使用 PassiveGameplayAbilityTaskConfig。"));
                ValidatePassiveEffects(passive.Effects, issues);
                return;
            }

        }

        // Every Ability shares the same result list; concrete types add only timing-specific rules.
        private static void ValidateEffectReferences(
            IReadOnlyList<GameplayEffectData> effects,
            ICollection<GameplayAbilityValidationIssue> issues)
        {
            if (effects == null) return;
            for (int i = 0; i < effects.Count; i++)
                if (effects[i] == null)
                    issues.Add(Error($"Effects[{i}] cannot be null."));
        }

        private static void ValidateInstantEffects(
            IReadOnlyList<GameplayEffectData> effects,
            ICollection<GameplayAbilityValidationIssue> issues)
        {
            if (effects == null) return;
            for (int i = 0; i < effects.Count; i++)
            {
                GameplayEffectData effect = effects[i];
                if (effect == null) continue;
                if (effect.DurationType != E_GameEffectDurationType.Instant)
                    issues.Add(Error($"Instant Skill Effects[{i}] 必须引用 Instant GE：{GetAssetLabel(effect)}。"));
            }
        }

        // Passive Skill 只持有非叠层 Infinite GE，便于按句柄精确清理。
        private static void ValidatePassiveEffects(
            IReadOnlyList<GameplayEffectData> effects,
            ICollection<GameplayAbilityValidationIssue> issues)
        {
            if (effects == null) return;
            for (int i = 0; i < effects.Count; i++)
            {
                GameplayEffectData effect = effects[i];
                if (effect == null) continue;

                if (effect.DurationType != E_GameEffectDurationType.Infinite)
                    issues.Add(Error($"Passive Skill Effects[{i}] 必须是 Infinite GE：{GetAssetLabel(effect)}。"));
                if (effect.StackingType != E_GameEffectStackingType.None)
                    issues.Add(Error($"Passive Skill Effects[{i}] 必须使用 None 叠层：{GetAssetLabel(effect)}。"));
            }
        }

        private static void ValidateTask(
            GameplayAbilityTaskConfig config,
            string path,
            ICollection<GameplayAbilityValidationIssue> issues)
        {
            if (config is WaitDurationGameplayAbilityTaskConfig wait)
            {
                if (wait.Duration < 0f || float.IsNaN(wait.Duration) || float.IsInfinity(wait.Duration))
                    issues.Add(Error($"{path} 的 Wait Duration 必须是大于等于 0 的有限值。"));
                return;
            }

            if (config is not SequenceGameplayAbilityTaskConfig sequence) return;
            for (int i = 0; i < sequence.Children.Count; i++)
            {
                GameplayAbilityTaskConfig child = sequence.Children[i];
                string childPath = $"{path}.Children[{i}]";
                if (child == null)
                    issues.Add(Error($"{childPath} 不能为空。"));
                else
                    ValidateTask(child, childPath, issues);
            }
        }
        #endregion

        #region 内部辅助
        // 名称优先、路径次优先建立稳定列表顺序。
        private static int CompareAbilities(GameplayAbilityData left, GameplayAbilityData right)
        {
            int result = string.Compare(left.name, right.name, StringComparison.OrdinalIgnoreCase);
            return result != 0
                ? result
                : string.Compare(
                    AssetDatabase.GetAssetPath(left),
                    AssetDatabase.GetAssetPath(right),
                    StringComparison.OrdinalIgnoreCase);
        }

        // 构造 Error 级别结果。
        private static GameplayAbilityValidationIssue Error(string message) =>
            new(GameplayAbilityValidationSeverity.Error, message);

        // 组合资产名与路径，便于定位错误 GE。
        private static string GetAssetLabel(UnityEngine.Object asset) =>
            $"{asset.name} ({AssetDatabase.GetAssetPath(asset)})";
        #endregion
    }
}
#endif
