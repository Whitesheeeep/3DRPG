#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using WS_Modules.GAS.GameplayAbilitySystem;
using WS_Modules.GAS.GameplayCue;
using WS_Modules.GAS.GameplayEffect;
using WS_Modules.GAS.TAG;
using WS_Modules.Pooling;

namespace WS_Modules.GAS.Editor
{
    /// <summary>集中执行 GA 资产操作、具体类型发现与跨字段校验。</summary>
    public sealed class GameplayAbilityEditorService
    {

        /// <summary>设置 GA 跨字段校验使用的 Editor Tag Database 上下文。</summary>
        /// <param name="database">与 GameplayTagPropertyDrawer 一致的当前数据库。</param>

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

        /// <summary>恢复 Session Database，或在项目中仅有一个 Database 时自动解析。</summary>
        /// <returns>可明确确定的 GameplayAbilityDatabase，否则返回 null。</returns>
        public GameplayAbilityDatabase ResolveDatabase()
        {
            GameplayAbilityDatabase database = GameplayAbilityEditorSession.GetDatabase();
            if (database == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:GameplayAbilityDatabase");
                if (guids.Length == 1)
                    database = AssetDatabase.LoadAssetAtPath<GameplayAbilityDatabase>(
                        AssetDatabase.GUIDToAssetPath(guids[0]));
            }

            if (database != null && database.NormalizeBakedIdHistoryComparer())
            {
                EditorUtility.SetDirty(database);
                AssetDatabase.SaveAssetIfDirty(database);
            }

            return database;
        }
        #endregion

        #region Bake 操作

        /// <summary>校验当前 Data、稳定 ID 历史与 Database 运行时索引的完整 Bake 状态。</summary>
        /// <param name="database">当前 GA Editor 选择的 Database。</param>
        /// <returns>错误列表；空列表表示可直接运行。</returns>
        public List<string> ValidateBakeState(GameplayAbilityDatabase database) =>
            GameplayAbilityBaker.ValidateBakedState(database);

        /// <summary>使用 Controller 已扫描的 GA 列表校验 Bake 状态，避免重复访问 AssetDatabase。</summary>
        /// <param name="database">当前 GA Database。</param>
        /// <param name="abilities">本轮资产刷新得到的完整 GA 列表。</param>
        /// <returns>Bake 状态错误列表。</returns>
        public List<string> ValidateBakeState(
            GameplayAbilityDatabase database,
            IReadOnlyList<GameplayAbilityData> abilities) =>
            GameplayAbilityBaker.ValidateBakedState(database, abilities);

        /// <summary>校验 Database 是否具备 Bake 前置条件，允许当前 Data 尚未同步。</summary>
        /// <param name="database">当前 GA Editor 选择的 Database。</param>
        /// <returns>错误列表；空列表表示可开始 Bake。</returns>
        public List<string> ValidateForBake(GameplayAbilityDatabase database) =>
            GameplayAbilityBaker.ValidateForBake(database);

        /// <summary>先校验可 Bake 条件，再写入 AbilityId、运行时索引与 Database 历史。</summary>
        /// <param name="database">当前 GA Editor 选择的 Database。</param>
        /// <returns>Bake 成功摘要。</returns>
        public string Bake(GameplayAbilityDatabase database) => GameplayAbilityBaker.Bake(database);

        /// <summary>记录 Ability 资产集合发生结构变化，使 Build Guard 要求重新 Bake。</summary>
        /// <param name="database">当前 Database；未选择时不执行修改。</param>
        public void MarkBakeDirty(GameplayAbilityDatabase database)
        {
            if (database == null) return;
            Undo.RecordObject(database, "Mark Gameplay Ability Bake Dirty");
            database.MarkBakeDirty();
            EditorUtility.SetDirty(database);
        }

        /// <summary>仅在当前 Ability 资产 GUID 集合与烘焙历史不一致时标记 Database 过期。</summary>
        /// <param name="database">当前 Database；未选择时不执行修改。</param>
        public void MarkBakeDirtyIfAssetSetChanged(GameplayAbilityDatabase database)
        {
            if (database == null || database.BakeDirty) return;

            string[] guids = AssetDatabase.FindAssets("t:GameplayAbilityData");
            if (guids.Length == database.BakedIdHistory.Count)
            {
                bool same = true;
                for (int i = 0; i < guids.Length; i++)
                    if (!database.BakedIdHistory.ContainsKey(guids[i]))
                    {
                        same = false;
                        break;
                    }
                if (same) return;
            }

            MarkBakeDirty(database);
        }

        /// <summary>使用已扫描 GA 列表检查资产 GUID 集合是否改变。</summary>
        /// <param name="database">当前 GA Database。</param>
        /// <param name="abilities">本轮资产刷新得到的完整 GA 列表。</param>
        public void MarkBakeDirtyIfAssetSetChanged(
            GameplayAbilityDatabase database,
            IReadOnlyList<GameplayAbilityData> abilities)
        {
            if (database == null || database.BakeDirty) return;
            if (abilities.Count == database.BakedIdHistory.Count)
            {
                bool same = true;
                for (int i = 0; i < abilities.Count; i++)
                {
                    string path = AssetDatabase.GetAssetPath(abilities[i]);
                    string guid = AssetDatabase.AssetPathToGUID(path);
                    if (database.BakedIdHistory.ContainsKey(guid)) continue;
                    same = false;
                    break;
                }

                if (same) return;
            }

            MarkBakeDirty(database);
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
            ValidateAbilityTags(ability.AbilityTags, "AbilityTags", issues);
            ValidateAbilityTags(ability.CancelTags, "CancelTags", issues);
            ValidateCueTags(ability.CueTags, issues);

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

        /// <summary>校验具体同步或异步 Ability 的专属配置，不重复 GE 内部规则。</summary>
        /// <param name="ability">待校验的 Ability 资产。</param>
        /// <param name="issues">接收校验结果的集合。</param>
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
                if (passive.RootTask is not PersistentSelfEffectsGameplayAbilityTaskConfig)
                    issues.Add(Error("Passive Ability 必须使用 PersistentSelfEffects Task。"));
                ValidatePersistentEffects(passive.Effects, "Passive", issues);
                return;
            }

            if (ability is ToggleGameplayAbilityData toggle)
            {
                if (toggle.RootTask is not PersistentSelfEffectsGameplayAbilityTaskConfig)
                    issues.Add(Error("Toggle Ability 必须使用 PersistentSelfEffects Task。"));
                ValidatePersistentEffects(toggle.Effects, "Toggle", issues);
                return;
            }

            if (ability is SelfCastGameplayAbilityData cast)
            {
                if (!HasSelfCastTaskShape(cast.RootTask))
                    issues.Add(Error("SelfCast Root Task 必须是 WaitDuration → ApplySelfEffects。"));
                return;
            }

            if (ability is SelfChannelGameplayAbilityData channel)
            {
                if (channel.RootTask is not PeriodicSelfEffectsGameplayAbilityTaskConfig)
                    issues.Add(Error("SelfChannel 必须使用 PeriodicSelfEffects Root Task。"));
                ValidateInstantEffects(channel.Effects, issues, "SelfChannel");
                return;
            }

            if (ability is LinearProjectileGameplayAbilityData projectile)
            {
                ValidateLinearProjectile(projectile, issues);
                return;
            }

        }

        /// <summary>校验所有 Ability 共用结果列表中的空引用。</summary>
        /// <param name="effects">待校验的统一 Effects 列表。</param>
        /// <param name="issues">接收校验结果的集合。</param>
        private static void ValidateEffectReferences(
            IReadOnlyList<GameplayEffectData> effects,
            ICollection<GameplayAbilityValidationIssue> issues)
        {
            if (effects == null) return;
            for (int i = 0; i < effects.Count; i++)
                if (effects[i] == null)
                    issues.Add(Error($"Effects[{i}] 不能为空。"));
        }

        // CueTag 只检查作者数据完整性；映射是否存在仅在 CueDatabase 已初始化时检查。
        private static void ValidateCueTags(
            IReadOnlyList<WS_Modules.GAS.TAG.GameplayTag> cueTags,
            ICollection<GameplayAbilityValidationIssue> issues)
        {
            if (cueTags == null) return;
            var unique = new HashSet<WS_Modules.GAS.TAG.GameplayTag>();
            for (int i = 0; i < cueTags.Count; i++)
            {
                var tag = cueTags[i];
                if (!tag.IsValid)
                {
                    issues.Add(Error($"CueTags[{i}] 不是有效的 GameplayTag。"));
                    continue;
                }
                if (!unique.Add(tag))
                    issues.Add(Error($"CueTags[{i}] 与前面的 CueTag 重复。"));
                if (GameplayCueManager.Instance.IsInitialized &&
                    !GameplayCueManager.Instance.TryGetCue(tag, out _))
                    issues.Add(Error($"CueTags[{i}] 在当前 CueDatabase 中没有对应 CueData。"));
            }
        }

        /// <summary>校验 Ability 分类或取消标签列表中是否存在重复项。</summary>
        /// <param name="tags">待校验的 GameplayTag 列表。</param>
        /// <param name="fieldName">用于错误定位的字段名称。</param>
        /// <param name="issues">接收校验结果的集合。</param>
        private static void ValidateAbilityTags(
            IReadOnlyList<WS_Modules.GAS.TAG.GameplayTag> tags,
            string fieldName,
            ICollection<GameplayAbilityValidationIssue> issues)
        {
            if (tags == null) return;
            var unique = new HashSet<WS_Modules.GAS.TAG.GameplayTag>();
            for (int i = 0; i < tags.Count; i++)
                if (!unique.Add(tags[i]))
                    issues.Add(Error($"{fieldName}[{i}] 与前面的标签重复。"));
        }

        /// <summary>校验指定 Ability 的 Effects 是否全部为 Instant GE。</summary>
        /// <param name="effects">待校验的 Effects 列表。</param>
        /// <param name="issues">接收校验结果的集合。</param>
        /// <param name="abilityLabel">错误信息中显示的 Ability 名称。</param>
        private static void ValidateInstantEffects(
            IReadOnlyList<GameplayEffectData> effects,
            ICollection<GameplayAbilityValidationIssue> issues,
            string abilityLabel = "Instant Skill")
        {
            if (effects == null) return;
            for (int i = 0; i < effects.Count; i++)
            {
                GameplayEffectData effect = effects[i];
                if (effect == null) continue;
                if (effect.DurationType != E_GameEffectDurationType.Instant)
                    issues.Add(Error($"{abilityLabel} Effects[{i}] 必须引用 Instant GE：{GetAssetLabel(effect)}。"));
            }
        }

        /// <summary>校验 Persistent Task 是否只持有可按精确句柄清理的非叠层 Infinite GE。</summary>
        /// <param name="effects">待校验的 Effects 列表。</param>
        /// <param name="abilityLabel">错误信息中显示的 Ability 名称。</param>
        /// <param name="issues">接收校验结果的集合。</param>
        private static void ValidatePersistentEffects(
            IReadOnlyList<GameplayEffectData> effects,
            string abilityLabel,
            ICollection<GameplayAbilityValidationIssue> issues)
        {
            if (effects == null) return;
            for (int i = 0; i < effects.Count; i++)
            {
                GameplayEffectData effect = effects[i];
                if (effect == null) continue;

                if (effect.DurationType != E_GameEffectDurationType.Infinite)
                    issues.Add(Error($"{abilityLabel} Effects[{i}] 必须是 Infinite GE：{GetAssetLabel(effect)}。"));
                if (effect.StackingType != E_GameEffectStackingType.None)
                    issues.Add(Error($"{abilityLabel} Effects[{i}] 必须使用 None 叠层：{GetAssetLabel(effect)}。"));
            }
        }

        /// <summary>校验线性投射物的对象池资源入口与 Fallback Prefab 组件契约。</summary>
        /// <param name="projectile">待校验的投射物 Ability。</param>
        /// <param name="issues">接收校验结果的集合。</param>
        private static void ValidateLinearProjectile(
            LinearProjectileGameplayAbilityData projectile,
            ICollection<GameplayAbilityValidationIssue> issues)
        {
            if (projectile.Speed < 0f || float.IsNaN(projectile.Speed) || float.IsInfinity(projectile.Speed))
                issues.Add(Error("LinearProjectile Speed 必须是大于等于 0 的有限值。"));
            if (projectile.Lifetime <= 0f || float.IsNaN(projectile.Lifetime) || float.IsInfinity(projectile.Lifetime))
                issues.Add(Error("LinearProjectile Lifetime 必须是大于 0 的有限值。"));

            if (string.IsNullOrWhiteSpace(projectile.AddressableKey) && projectile.FallbackPrefab == null)
            {
                issues.Add(Error("LinearProjectile 必须配置 Addressable Key 或 Fallback Prefab。"));
                return;
            }

            GameObject prefab = projectile.FallbackPrefab;
            if (prefab == null)
            {
                issues.Add(Info("LinearProjectile 只配置了 Addressable Key，资源组件将在运行时验证。"));
                return;
            }

            if (!prefab.TryGetComponent<GameplayAbilityProjectileBehaviour>(out _))
                issues.Add(Error($"投射物 Prefab '{GetAssetLabel(prefab)}' 缺少 GameplayAbilityProjectileBehaviour。"));
            if (!prefab.TryGetComponent<Rigidbody>(out _))
                issues.Add(Error($"投射物 Prefab '{GetAssetLabel(prefab)}' 缺少 Rigidbody。"));
            if (!prefab.TryGetComponent<Collider>(out Collider collider) || !collider.isTrigger)
                issues.Add(Error($"投射物 Prefab '{GetAssetLabel(prefab)}' 必须包含 Trigger Collider。"));
            IGameObjectPoolable poolable = prefab.GetComponent<IGameObjectPoolable>();
            if (poolable == null)
            {
                issues.Add(Error($"投射物 Prefab '{GetAssetLabel(prefab)}' 必须实现 IGameObjectPoolable。"));
                return;
            }

            if (string.IsNullOrWhiteSpace(poolable.Key))
                issues.Add(Error($"投射物 Prefab '{GetAssetLabel(prefab)}' 的 IGameObjectPoolable.Key 不能为空。"));
            else if (!string.IsNullOrWhiteSpace(projectile.AddressableKey) &&
                     poolable.Key != projectile.AddressableKey)
                issues.Add(Error(
                    $"投射物 Prefab '{GetAssetLabel(prefab)}' 的 Key '{poolable.Key}' 与 Addressable Key '{projectile.AddressableKey}' 不一致。"));
        }

        /// <summary>判断 SelfCast 是否保持固定的等待后自身结算结构。</summary>
        /// <param name="root">待检查的 Root Task。</param>
        /// <returns>结构完整时返回 true。</returns>
        private static bool HasSelfCastTaskShape(GameplayAbilityTaskConfig root) =>
            root is SequenceGameplayAbilityTaskConfig sequence &&
            sequence.Children.Count == 2 &&
            sequence.Children[0] is WaitDurationGameplayAbilityTaskConfig &&
            sequence.Children[1] is ApplySelfEffectsGameplayAbilityTaskConfig;

        /// <summary>递归校验内置 Task Config 的时间参数与 Sequence 子项。</summary>
        /// <param name="config">待校验的 Task Config。</param>
        /// <param name="path">用于定位错误的序列化路径。</param>
        /// <param name="issues">接收校验结果的集合。</param>
        private static void ValidateTask(
            GameplayAbilityTaskConfig config,
            string path,
            ICollection<GameplayAbilityValidationIssue> issues)
        {
            if (config is RPG.SkillSystem.PlaySkillConfigGameplayAbilityTaskConfig playSkill)
            {
                if (playSkill.SkillConfig == null)
                    issues.Add(Error($"{path} 的 SkillConfig 不能为空。"));
                else
                {
                    if (playSkill.SkillConfig.FrameRate <= 0)
                        issues.Add(Error($"{path} 的 SkillConfig FPS 必须大于 0。"));
                    if (playSkill.SkillConfig.DurationFrames <= 0)
                        issues.Add(Error($"{path} 的 SkillConfig 总帧必须大于 0。"));
                }
                return;
            }

            if (config is WaitDurationGameplayAbilityTaskConfig wait)
            {
                if (wait.Duration < 0f || float.IsNaN(wait.Duration) || float.IsInfinity(wait.Duration))
                    issues.Add(Error($"{path} 的 Wait Duration 必须是大于等于 0 的有限值。"));
                return;
            }

            if (config is PeriodicSelfEffectsGameplayAbilityTaskConfig periodic)
            {
                if (periodic.Period <= 0f || float.IsNaN(periodic.Period) || float.IsInfinity(periodic.Period))
                    issues.Add(Error($"{path} 的 Period 必须是大于 0 的有限值。"));
                if (!periodic.Infinite &&
                    (periodic.Duration < 0f || float.IsNaN(periodic.Duration) || float.IsInfinity(periodic.Duration)))
                    issues.Add(Error($"{path} 的有限 Duration 必须是大于等于 0 的有限值。"));
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

        /// <summary>构造只解释合法特殊配置的 Info 结果。</summary>
        /// <param name="message">显示给作者的说明。</param>
        /// <returns>Info 级校验结果。</returns>
        private static GameplayAbilityValidationIssue Info(string message) =>
            new(GameplayAbilityValidationSeverity.Info, message);

        // 组合资产名与路径，便于定位错误 GE。
        private static string GetAssetLabel(UnityEngine.Object asset) =>
            $"{asset.name} ({AssetDatabase.GetAssetPath(asset)})";
        #endregion
    }
}
#endif
