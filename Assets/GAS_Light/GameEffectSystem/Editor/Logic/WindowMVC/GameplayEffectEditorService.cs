#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.GameplayCue;
using WS_Modules.GAS.GameplayEffect;

namespace WS_Modules.GAS.Editor
{
    /// <summary>集中执行 GE 资产、SerializeReference Modifier、Undo 和 AttributeSet 校验。</summary>
    public sealed class GameplayEffectEditorService
    {
        #region 字段

        private List<GameplayAttributeSet> cachedValidationSets;

        #endregion

        #region 资产操作

        /// <summary>扫描并按名称与路径排序项目中的 GE 资产。</summary>
        /// <returns>直接指向项目 Model 的资产列表。</returns>
        public List<GameplayEffectData> FindAllEffects()
        {
            var effects = new List<GameplayEffectData>();
            foreach (string guid in AssetDatabase.FindAssets("t:GameplayEffectData"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameplayEffectData effect = AssetDatabase.LoadAssetAtPath<GameplayEffectData>(path);
                if (effect != null) effects.Add(effect);
            }

            effects.Sort(CompareEffects);
            return effects;
        }

        /// <summary>使全项目 AttributeSet 扫描缓存失效；项目资产变化后由 Controller 调用。</summary>
        public void InvalidateValidationSetCache() => cachedValidationSets = null;

        /// <summary>在 Project 窗口定位并闪烁指定 GE 资产，不改变当前 Selection。</summary>
        /// <param name="effect">需要定位的 GE 资产；null 时不执行操作。</param>
        public void PingEffect(GameplayEffectData effect)
        {
            if (effect != null) EditorGUIUtility.PingObject(effect);
        }

        /// <summary>通过 AssetDatabase 重命名 GE 资产并保留原 Asset GUID。</summary>
        /// <param name="effect">需要重命名的 GE 资产。</param>
        /// <param name="newName">不包含扩展名的新名称。</param>
        /// <param name="error">失败时返回输入或 AssetDatabase 的具体错误。</param>
        /// <returns>名称无需改变或资产重命名成功时返回 true。</returns>
        public bool TryRenameEffect(
            GameplayEffectData effect,
            string newName,
            out string error)
        {
            if (effect == null)
            {
                error = "未指定需要重命名的 GameplayEffectData。";
                return false;
            }

            string trimmedName = newName?.Trim() ?? string.Empty;
            if (trimmedName.Length == 0)
            {
                error = "GameplayEffectData 名称不能为空。";
                return false;
            }

            if (string.Equals(effect.name, trimmedName, StringComparison.Ordinal))
            {
                error = string.Empty;
                return true;
            }

            string path = AssetDatabase.GetAssetPath(effect);
            if (string.IsNullOrEmpty(path))
            {
                error = "GameplayEffectData 不是可重命名的项目资产。";
                return false;
            }

            error = AssetDatabase.RenameAsset(path, trimmedName);
            return string.IsNullOrEmpty(error);
        }

        /// <summary>在指定 Assets 路径创建 GE 资产。</summary>
        /// <param name="assetPath">已由 Editor 对话框选择的项目路径。</param>
        /// <param name="error">失败时的具体原因。</param>
        /// <returns>创建成功时返回新资产，否则返回 null。</returns>
        public GameplayEffectData CreateEffect(string assetPath, out string error)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || !assetPath.StartsWith("Assets/"))
            {
                error = "GameplayEffectData 必须创建在 Assets 目录下。";
                return null;
            }

            string uniquePath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
            var effect = ScriptableObject.CreateInstance<GameplayEffectData>();
            AssetDatabase.CreateAsset(effect, uniquePath);
            Undo.RegisterCreatedObjectUndo(effect, "Create Gameplay Effect");
            AssetDatabase.SaveAssets();
            Selection.activeObject = effect;
            error = string.Empty;
            return effect;
        }

        /// <summary>在当前 GE 同目录创建唯一命名的副本。</summary>
        /// <param name="source">待复制的 GE。</param>
        /// <param name="error">失败时的具体原因。</param>
        /// <returns>复制成功时返回副本，否则返回 null。</returns>
        public GameplayEffectData DuplicateEffect(GameplayEffectData source, out string error)
        {
            if (source == null)
            {
                error = "未选择需要复制的 GameplayEffectData。";
                return null;
            }

            string sourcePath = AssetDatabase.GetAssetPath(source);
            string directory = System.IO.Path.GetDirectoryName(sourcePath)?.Replace('\\', '/');
            string copyPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{directory}/{source.name} Copy.asset");
            if (!AssetDatabase.CopyAsset(sourcePath, copyPath))
            {
                error = $"无法复制资产：{sourcePath}";
                return null;
            }

            AssetDatabase.SaveAssets();
            GameplayEffectData copy = AssetDatabase.LoadAssetAtPath<GameplayEffectData>(copyPath);
            Selection.activeObject = copy;
            error = string.Empty;
            return copy;
        }

        /// <summary>将指定 GE 移入系统回收站。</summary>
        /// <param name="effect">待删除资产。</param>
        /// <param name="error">失败时的具体原因。</param>
        /// <returns>资产已移入回收站时返回 true。</returns>
        public bool MoveEffectToTrash(GameplayEffectData effect, out string error)
        {
            if (effect == null)
            {
                error = "未选择需要删除的 GameplayEffectData。";
                return false;
            }

            string path = AssetDatabase.GetAssetPath(effect);
            if (!AssetDatabase.MoveAssetToTrash(path))
            {
                error = $"无法将资产移入回收站：{path}";
                return false;
            }

            error = string.Empty;
            return true;
        }

        #endregion

        #region Modifier 操作

        /// <summary>发现所有可作为 SerializeReference 创建的 Modifier 派生类型。</summary>
        /// <returns>按类型显示名排序的列表。</returns>
        public List<Type> FindModifierTypes()
        {
            return TypeCache.GetTypesDerivedFrom<GameplayEffectModifier>()
                .Where(IsCreatableModifierType)
                .OrderBy(type => ObjectNames.NicifyVariableName(type.Name), StringComparer.Ordinal)
                .ThenBy(type => type.FullName, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>向 GE 末尾添加指定派生类型的 Modifier。</summary>
        /// <param name="effect">目标 GE。</param>
        /// <param name="modifierType">已从 TypeCache 发现的类型。</param>
        /// <param name="newIndex">成功时返回新元素索引。</param>
        /// <param name="error">失败时包含具体类型的原因。</param>
        /// <returns>已以一次 Undo 写入资产时返回 true。</returns>
        public bool TryAddModifier(
            GameplayEffectData effect,
            Type modifierType,
            out int newIndex,
            out string error)
        {
            newIndex = -1;
            if (effect == null || !IsCreatableModifierType(modifierType))
            {
                error = $"无法创建 Modifier 类型：{modifierType?.FullName ?? "null"}";
                return false;
            }

            GameplayEffectModifier instance;
            try
            {
                instance = (GameplayEffectModifier)Activator.CreateInstance(modifierType);
            }
            catch (Exception exception)
            {
                error = $"创建 Modifier '{modifierType.FullName}' 失败：{exception.GetBaseException().Message}";
                return false;
            }

            Undo.RecordObject(effect, "Add Gameplay Effect Modifier");
            var serializedObject = new SerializedObject(effect);
            SerializedProperty modifiers = serializedObject.FindProperty("modifiers");
            newIndex = modifiers.arraySize;
            modifiers.arraySize++;
            modifiers.GetArrayElementAtIndex(newIndex).managedReferenceValue = instance;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(effect);
            error = string.Empty;
            return true;
        }

        /// <summary>删除指定索引的 Modifier。</summary>
        /// <param name="effect">目标 GE。</param>
        /// <param name="index">当前 Modifier 索引。</param>
        /// <returns>实际删除时返回 true。</returns>
        public bool RemoveModifier(GameplayEffectData effect, int index)
        {
            if (effect == null || index < 0 || index >= effect.Modifiers.Count) return false;
            Undo.RecordObject(effect, "Remove Gameplay Effect Modifier");
            var serializedObject = new SerializedObject(effect);
            SerializedProperty modifiers = serializedObject.FindProperty("modifiers");
            int oldSize = modifiers.arraySize;
            modifiers.DeleteArrayElementAtIndex(index);
            if (modifiers.arraySize == oldSize) modifiers.DeleteArrayElementAtIndex(index);
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(effect);
            return true;
        }

        /// <summary>使用 SerializedProperty 移动 Modifier，保持 managed reference 完整性。</summary>
        /// <param name="effect">目标 GE。</param>
        /// <param name="request">原索引和目标索引。</param>
        /// <returns>索引合法且已移动时返回 true。</returns>
        public bool MoveModifier(
            GameplayEffectData effect,
            GameplayEffectModifierMoveRequest request)
        {
            if (effect == null || request.FromIndex == request.ToIndex ||
                request.FromIndex < 0 || request.FromIndex >= effect.Modifiers.Count ||
                request.ToIndex < 0 || request.ToIndex >= effect.Modifiers.Count)
                return false;

            Undo.RecordObject(effect, "Move Gameplay Effect Modifier");
            var serializedObject = new SerializedObject(effect);
            SerializedProperty modifiers = serializedObject.FindProperty("modifiers");
            modifiers.MoveArrayElement(request.FromIndex, request.ToIndex);
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(effect);
            return true;
        }

        #endregion

        #region 校验

        /// <summary>校验 GE 策略、Modifier 和 AttributeSet 兼容性，不重复校验 Tag。</summary>
        /// <param name="effect">待校验 GE。</param>
        /// <returns>按发现顺序排列的错误、警告和提示。</returns>
        public List<GameplayEffectValidationIssue> Validate(GameplayEffectData effect)
        {
            var issues = new List<GameplayEffectValidationIssue>();
            if (effect == null) return issues;

            ValidatePolicies(effect, issues);
            ValidateCueTags(effect.CueTags, issues);
            GameplayAttributeRegistry registry = ResolveRegistry(issues);
            List<GameplayAttributeSet> sets = ResolveValidationSets(effect, issues);
            ValidateModifiers(effect, registry, sets, issues);
            return issues;
        }

        // 校验策略本身的有限数值和必要组合，不处理被 UI 隐藏的无关字段。
        private static void ValidatePolicies(
            GameplayEffectData effect,
            ICollection<GameplayEffectValidationIssue> issues)
        {
            if (!Enum.IsDefined(typeof(E_GameEffectDurationType), effect.DurationType))
                AddError(issues, "DurationType 包含未定义枚举值。");
            if (effect.DurationType == E_GameEffectDurationType.Duration &&
                (!IsFinite(effect.Duration) || effect.Duration <= 0f))
                AddError(issues, "Duration GE 的 Duration 必须大于 0 且为有限数值。");
            if (!IsFinite(effect.Period) || effect.Period < 0f)
                AddError(issues, "Period 必须是大于等于 0 的有限数值。");
            if (effect.DurationType == E_GameEffectDurationType.Instant && effect.Period > 0f)
                AddError(issues, "Instant GE 不支持 Period；周期结算必须使用 Duration 或 Infinite。");
            if (!Enum.IsDefined(typeof(E_GameEffectStackingType), effect.StackingType))
                AddError(issues, "StackingType 包含未定义枚举值。");
            if (effect.StackingType != E_GameEffectStackingType.None && effect.MaxStackCount < 1)
                AddError(issues, "启用叠层时 MaxStackCount 必须至少为 1。");
        }

        // CueTag 只检查序列化值和当前已初始化数据库中的映射，不复制运行时 Cue 规则。
        private static void ValidateCueTags(
            IReadOnlyList<WS_Modules.GAS.TAG.GameplayTag> cueTags,
            ICollection<GameplayEffectValidationIssue> issues)
        {
            if (cueTags == null) return;
            var unique = new HashSet<WS_Modules.GAS.TAG.GameplayTag>();
            for (int i = 0; i < cueTags.Count; i++)
            {
                var tag = cueTags[i];
                if (!tag.IsValid)
                {
                    AddError(issues, $"CueTags[{i}] 不是有效的 GameplayTag。");
                    continue;
                }
                if (!unique.Add(tag))
                    AddError(issues, $"CueTags[{i}] 与前面的 CueTag 重复。");
                if (GameplayCueManager.Instance.IsInitialized &&
                    !GameplayCueManager.Instance.TryGetCue(tag, out _))
                    AddError(issues, $"CueTags[{i}] 在当前 CueDatabase 中没有对应 CueData。");
            }
        }

        // 优先使用 Attribute Editor Session Registry；没有会话选择时只接受项目唯一 Registry。
        private static GameplayAttributeRegistry ResolveRegistry(
            ICollection<GameplayEffectValidationIssue> issues)
        {
            GameplayAttributeRegistry registry =
                GameplayAttributeEditorSession.ResolveSingleRegistry(out string error);
            if (registry != null) return registry;
            AddError(issues, error);
            return null;
        }

        // validationSets 非空时严格使用作者列表；只有数组长度为 0 才扫描项目全部 Set。
        private List<GameplayAttributeSet> ResolveValidationSets(
            GameplayEffectData effect,
            ICollection<GameplayEffectValidationIssue> issues)
        {
            var result = new List<GameplayAttributeSet>();
            var serializedObject = new SerializedObject(effect);
            SerializedProperty selectedSets = serializedObject.FindProperty("validationSets");
            if (selectedSets.arraySize == 0)
            {
                result.AddRange(GetAllValidationSets());
                issues.Add(new GameplayEffectValidationIssue(
                    GameplayEffectValidationSeverity.Info,
                    $"未指定 Validation Sets，当前扫描项目全部 {result.Count} 个 AttributeSet。"));
                return result;
            }

            var unique = new HashSet<GameplayAttributeSet>();
            for (int i = 0; i < selectedSets.arraySize; i++)
            {
                var set = selectedSets.GetArrayElementAtIndex(i).objectReferenceValue as GameplayAttributeSet;
                if (set == null)
                {
                    AddError(issues, $"Validation Sets 第 {i} 项为 null。");
                    continue;
                }

                if (!unique.Add(set))
                {
                    AddError(issues, $"Validation Sets 重复引用 '{DescribeSet(set)}'。");
                    continue;
                }

                result.Add(set);
            }

            return result;
        }

        // 首次需要全项目校验时扫描 Set；缓存只保存资产引用并在 projectChanged 后失效。
        private IReadOnlyList<GameplayAttributeSet> GetAllValidationSets()
        {
            if (cachedValidationSets != null) return cachedValidationSets;
            cachedValidationSets = new List<GameplayAttributeSet>();
            foreach (string guid in AssetDatabase.FindAssets("t:GameplayAttributeSet"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameplayAttributeSet set =
                    AssetDatabase.LoadAssetAtPath<GameplayAttributeSet>(path);
                if (set != null) cachedValidationSets.Add(set);
            }

            cachedValidationSets.Sort(CompareSets);
            return cachedValidationSets;
        }

        // 校验每项公共字段、内置派生字段，以及每个目标 Set 中的 Attribute 存在性和类型。
        private static void ValidateModifiers(
            GameplayEffectData effect,
            GameplayAttributeRegistry registry,
            IReadOnlyList<GameplayAttributeSet> sets,
            ICollection<GameplayEffectValidationIssue> issues)
        {
            IReadOnlyList<GameplayEffectModifier> modifiers = effect.Modifiers;
            if (modifiers.Count == 0)
            {
                issues.Add(new GameplayEffectValidationIssue(
                    GameplayEffectValidationSeverity.Info,
                    "当前 GE 没有 Modifier；仅 Granted Tags 的非 Instant GE 仍然合法。"));
                return;
            }

            if (sets.Count == 0)
                AddError(issues, "没有可用的 GameplayAttributeSet，无法校验 Modifier 目标。");

            var serializedObject = new SerializedObject(effect);
            SerializedProperty serializedModifiers = serializedObject.FindProperty("modifiers");
            bool persistent = effect.DurationType != E_GameEffectDurationType.Instant &&
                              !effect.IsPeriodic;
            for (int i = 0; i < modifiers.Count; i++)
            {
                GameplayEffectModifier modifier = modifiers[i];
                if (modifier == null)
                {
                    AddError(issues, $"Modifier [{i}] 为 null 或派生类型已丢失。");
                    continue;
                }

                if (!modifier.Attribute.IsValid)
                    AddError(issues, $"Modifier [{i}] 必须选择一个已烘焙 Attribute。");
                else if (registry != null && !registry.TryGetNodeById(modifier.Attribute.Id, out _))
                    AddError(issues, $"Modifier [{i}] AttributeId {modifier.Attribute.Id} 未在当前 Registry 中烘焙。");

                if (!Enum.IsDefined(typeof(AttributeModifierType), modifier.Type))
                    AddError(issues, $"Modifier [{i}] Type 包含未定义枚举值。");

                SerializedProperty element = serializedModifiers.GetArrayElementAtIndex(i);
                ValidateBuiltInModifier(modifier, element, i, issues);
                if (!modifier.Attribute.IsValid) continue;

                for (int setIndex = 0; setIndex < sets.Count; setIndex++)
                {
                    GameplayAttributeSet set = sets[setIndex];
                    int definitionIndex = GameplayAttributeEditorService.FindDefinitionIndex(
                        set,
                        modifier.Attribute.Id);
                    if (definitionIndex < 0)
                    {
                        AddError(
                            issues,
                            $"Modifier [{i}] AttributeId {modifier.Attribute.Id} 不存在于 Set '{DescribeSet(set)}'。");
                        continue;
                    }

                    GameplayAttributeDefinition definition = set.Definitions[definitionIndex];
                    if (persistent && definition.Type != GameplayAttributeType.Stat)
                        AddError(
                            issues,
                            $"Modifier [{i}] 是持续 Modifier，但 Set '{DescribeSet(set)}' " +
                            $"将 AttributeId {modifier.Attribute.Id} 定义为 Resource。");
                }
            }
        }

        // 内置类型的私有作者字段通过 SerializedProperty 检查；自定义类型只遵守公共契约。
        private static void ValidateBuiltInModifier(
            GameplayEffectModifier modifier,
            SerializedProperty element,
            int index,
            ICollection<GameplayEffectValidationIssue> issues)
        {
            if (modifier is FixedGameplayEffectModifier)
            {
                SerializedProperty magnitude = element.FindPropertyRelative("magnitude");
                if (magnitude != null && !IsFinite(magnitude.floatValue))
                    AddError(issues, $"Modifier [{index}] Fixed Magnitude 必须为有限数值。");
                return;
            }

            if (modifier is CurveGameplayEffectModifier)
            {
                ValidateCurveModifier(element, index, issues);
                return;
            }

            if (modifier is LevelGameplayEffectModifier)
                ValidateLevelModifier(element, index, issues);
        }

        // Curve Modifier 保留原有基础值与等级倍率曲线校验。
        private static void ValidateCurveModifier(
            SerializedProperty element,
            int index,
            ICollection<GameplayEffectValidationIssue> issues)
        {
            SerializedProperty baseMagnitude = element.FindPropertyRelative("baseMagnitude");
            if (baseMagnitude != null && !IsFinite(baseMagnitude.floatValue))
                AddError(issues, $"Modifier [{index}] Curve Base Magnitude 必须为有限数值。");
            SerializedProperty curve = element.FindPropertyRelative("levelCurve");
            if (curve != null && curve.animationCurveValue == null)
                issues.Add(new GameplayEffectValidationIssue(
                    GameplayEffectValidationSeverity.Warning,
                    $"Modifier [{index}] 未配置 Curve，运行时使用倍率 1。"));
        }

        // Level Modifier 校验离散等级覆盖、唯一性与每项最终 Magnitude。
        private static void ValidateLevelModifier(
            SerializedProperty element,
            int index,
            ICollection<GameplayEffectValidationIssue> issues)
        {
            SerializedProperty entries = element.FindPropertyRelative("levelMagnitudes");
            if (entries == null || entries.arraySize == 0)
            {
                AddError(issues, $"Modifier [{index}] Level Magnitudes 不能为空。");
                return;
            }

            var configuredLevels = new HashSet<int>();
            bool containsLevelOne = false;
            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                SerializedProperty level = entry.FindPropertyRelative("level");
                SerializedProperty magnitude = entry.FindPropertyRelative("magnitude");
                int configuredLevel = level.intValue;
                if (configuredLevel < 1)
                    AddError(issues, $"Modifier [{index}] Level Magnitudes [{i}] 的等级必须至少为 1。");
                else if (!configuredLevels.Add(configuredLevel))
                    AddError(issues, $"Modifier [{index}] 存在重复等级 {configuredLevel}。");
                else if (configuredLevel == 1)
                    containsLevelOne = true;

                if (!IsFinite(magnitude.floatValue))
                    AddError(issues, $"Modifier [{index}] Level Magnitudes [{i}] 必须为有限数值。");
            }

            if (!containsLevelOne)
                AddError(issues, $"Modifier [{index}] Level Magnitudes 必须包含 Level 1。");
        }

        #endregion

        #region 内部辅助

        // SerializeReference 只接受可序列化、非抽象、非泛型且具有无参构造的派生类型。
        private static bool IsCreatableModifierType(Type type)
        {
            return type != null &&
                   typeof(GameplayEffectModifier).IsAssignableFrom(type) &&
                   !type.IsAbstract &&
                   !type.ContainsGenericParameters &&
                   type.IsDefined(typeof(SerializableAttribute), false) &&
                   type.GetConstructor(
                       BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                       null,
                       Type.EmptyTypes,
                       null) != null;
        }

        // GE 资产同名时使用路径稳定排序。
        private static int CompareEffects(GameplayEffectData left, GameplayEffectData right)
        {
            int nameOrder = string.Compare(left.name, right.name, StringComparison.OrdinalIgnoreCase);
            return nameOrder != 0
                ? nameOrder
                : string.Compare(
                    AssetDatabase.GetAssetPath(left),
                    AssetDatabase.GetAssetPath(right),
                    StringComparison.Ordinal);
        }

        // 全项目扫描的 Set 按资产路径排序，使校验输出稳定。
        private static int CompareSets(GameplayAttributeSet left, GameplayAttributeSet right) =>
            string.Compare(
                AssetDatabase.GetAssetPath(left),
                AssetDatabase.GetAssetPath(right),
                StringComparison.Ordinal);

        // 同时显示 Set 名称和资产路径，避免同名 Set 诊断不明确。
        private static string DescribeSet(GameplayAttributeSet set) =>
            $"{set.name} ({AssetDatabase.GetAssetPath(set)})";

        // 向结果集合添加统一错误项。
        private static void AddError(
            ICollection<GameplayEffectValidationIssue> issues,
            string message) =>
            issues.Add(new GameplayEffectValidationIssue(
                GameplayEffectValidationSeverity.Error,
                message));

        // GE 作者浮点输入禁止 NaN 和 Infinity。
        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        #endregion
    }
}
#endif
