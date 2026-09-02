#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.U2D;
using WS_Modules.EditorExtensions;

namespace RPG.ItemSystem.Editor
{
    /// <summary>封装 Item 配置窗口对 AssetDatabase 和 SerializedObject 的写入操作。</summary>
    internal sealed class ItemConfigEditorService
    {
        #region 常量

        private const string DefinitionsFolderName = "Definitions";
        private const string SpriteAtlasGroupName = "UISpriteAtlas";

        /// <summary>物品定义重命名在 Undo 面板中使用的固定名称。</summary>
        internal const string RenameDefinitionUndoName = "重命名物品定义";

        #endregion

        #region 数据库查询

        /// <summary>解析窗口默认使用的 ItemDatabase。</summary>
        /// <returns>会话中记录的数据库，或项目中第一个数据库。</returns>
        internal ItemDatabase ResolveDatabase()
        {
            ItemDatabase sessionDatabase = AssetDatabase.LoadAssetAtPath<ItemDatabase>(ItemConfigEditorSession.DatabasePath);
            if (sessionDatabase != null) return sessionDatabase;
            string[] guids = AssetDatabase.FindAssets("t:ItemDatabase");
            for (int index = 0; index < guids.Length; index++)
            {
                ItemDatabase database = AssetDatabase.LoadAssetAtPath<ItemDatabase>(AssetDatabase.GUIDToAssetPath(guids[index]));
                if (database != null) return database;
            }
            return null;
        }

        /// <summary>查找包含指定定义的数据库。</summary>
        /// <param name="definition">物品定义。</param>
        /// <returns>包含定义的数据库，找不到时返回 null。</returns>
        internal ItemDatabase FindDatabase(ItemDefinition definition)
        {
            if (definition == null) return null;
            string[] guids = AssetDatabase.FindAssets("t:ItemDatabase");
            for (int index = 0; index < guids.Length; index++)
            {
                ItemDatabase database = AssetDatabase.LoadAssetAtPath<ItemDatabase>(AssetDatabase.GUIDToAssetPath(guids[index]));
                if (database == null) continue;
                for (int definitionIndex = 0; definitionIndex < database.Definitions.Count; definitionIndex++)
                    if (database.Definitions[definitionIndex] == definition) return database;
            }
            return null;
        }

        #endregion

        #region 资产命令

        /// <summary>根据编辑器预览 Sprite 自动同步 Definition 的 Atlas Address 和 Sprite 名称。</summary>
        /// <param name="definition">需要写入图标引用的物品定义。</param>
        /// <param name="previewIcon">用户选择的预览 Sprite；为空时清空运行时引用。</param>
        /// <returns>同步结果的中文状态。</returns>
        /// <exception cref="ArgumentNullException">物品定义为空时抛出。</exception>
        /// <exception cref="InvalidOperationException">Sprite 未收录、Atlas 冲突或 Addressables 配置不完整时抛出。</exception>
        internal string SynchronizeIconReference(ItemDefinition definition, Sprite previewIcon)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("设置物品图标");
            try
            {
                if (previewIcon == null)
                {
                    SetIconReference(definition, string.Empty, string.Empty);
                    return "已清除编辑器预览图标及运行时图标引用。";
                }

                SpriteAtlasLookupResult lookup = previewIcon.FindSpriteAtlasReference();
                if (lookup.Status == SpriteAtlasLookupStatus.NotFound)
                    return ClearIconReferenceAndThrow(definition, $"预览 Sprite“{previewIcon.name}”未被任何默认 Sprite Atlas 收录，已清空图标引用。");
                if (lookup.Status == SpriteAtlasLookupStatus.Ambiguous)
                {
                    string paths = string.Join("、", lookup.MatchingAtlasPaths);
                    return ClearIconReferenceAndThrow(definition, $"预览 Sprite“{previewIcon.name}”同时被多个 Sprite Atlas 收录：{paths}。已清空图标引用，请先移除重复收录。");
                }

                string address;
                try
                {
                    address = ResolveSpriteAtlasAddress(lookup.Atlas);
                }
                catch (InvalidOperationException)
                {
                    // Atlas 已找到但 Addressables 配置不完整时也清空旧引用，避免留下与预览图不匹配的地址。
                    SetIconReference(definition, string.Empty, string.Empty);
                    throw;
                }
                SetIconReference(definition, address, lookup.SpriteName);
                return $"已自动填充图标图集“{address}”和 Sprite“{lookup.SpriteName}”。";
            }
            finally
            {
                // 将预览 Sprite 的原生绑定 Undo 与本次两个字符串字段写入合并为一个操作。
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        /// <summary>解析 Atlas 在 UISpriteAtlas Addressables Group 中的 Address。</summary>
        /// <param name="atlas">已唯一匹配的 Sprite Atlas。</param>
        /// <returns>Addressables 条目地址。</returns>
        /// <exception cref="InvalidOperationException">Addressables 设置、条目或分组不符合约定时抛出。</exception>
        private static string ResolveSpriteAtlasAddress(SpriteAtlas atlas)
        {
            string atlasPath = AssetDatabase.GetAssetPath(atlas);
            if (string.IsNullOrEmpty(atlasPath))
                throw new InvalidOperationException("匹配到的 Sprite Atlas 没有有效资产路径，无法解析 Address。");

            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
                throw new InvalidOperationException("项目没有 Addressables 设置，无法解析 Sprite Atlas Address。");

            string atlasGuid = AssetDatabase.AssetPathToGUID(atlasPath);
            AddressableAssetEntry entry = settings.FindAssetEntry(atlasGuid);
            if (entry == null)
                throw new InvalidOperationException($"Sprite Atlas“{atlasPath}”未加入 Addressables，已清空图标引用。");
            if (entry.parentGroup == null || !string.Equals(entry.parentGroup.Name, SpriteAtlasGroupName, StringComparison.Ordinal))
                throw new InvalidOperationException($"Sprite Atlas“{atlasPath}”不在 Addressables 分组“{SpriteAtlasGroupName}”中，已清空图标引用。");
            if (string.IsNullOrWhiteSpace(entry.address))
                throw new InvalidOperationException($"Sprite Atlas“{atlasPath}”的 Address 为空，已清空图标引用。");
            return entry.address;
        }

        /// <summary>清空运行时引用并抛出带上下文的配置错误。</summary>
        /// <param name="definition">需要清空的物品定义。</param>
        /// <param name="message">要报告的错误信息。</param>
        /// <returns>该方法不会正常返回。</returns>
        /// <exception cref="InvalidOperationException">始终抛出传入的配置错误。</exception>
        private static string ClearIconReferenceAndThrow(ItemDefinition definition, string message)
        {
            SetIconReference(definition, string.Empty, string.Empty);
            throw new InvalidOperationException(message);
        }

        /// <summary>通过 SerializedObject 写入图标引用，并保留原生 Undo 能力。</summary>
        /// <param name="definition">目标物品定义。</param>
        /// <param name="address">Atlas Address。</param>
        /// <param name="spriteName">Atlas 内 Sprite 名称。</param>
        private static void SetIconReference(ItemDefinition definition, string address, string spriteName)
        {
            SerializedObject serialized = new SerializedObject(definition);
            SerializedProperty addressProperty = serialized.FindProperty("iconAddress");
            SerializedProperty spriteNameProperty = serialized.FindProperty("iconSpriteName");
            if (addressProperty == null || spriteNameProperty == null)
                throw new InvalidOperationException("物品定义缺少图标 Address 或 Sprite 名称字段。");

            string normalizedAddress = address ?? string.Empty;
            string normalizedSpriteName = spriteName ?? string.Empty;
            if (addressProperty.stringValue == normalizedAddress && spriteNameProperty.stringValue == normalizedSpriteName) return;

            Undo.RecordObject(definition, "设置物品图标");
            addressProperty.stringValue = normalizedAddress;
            spriteNameProperty.stringValue = normalizedSpriteName;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssets();
        }

        /// <summary>提交显示名称并同步重命名对应的定义资产。</summary>
        /// <param name="definition">待修改的物品定义。</param>
        /// <param name="requestedDisplayName">用户输入的显示名称。</param>
        /// <returns>本次重命名对应的 Undo Group ID。</returns>
        /// <exception cref="InvalidOperationException">名称或资产路径不合法，或资产重命名失败时抛出。</exception>
        internal int RenameDefinition(ItemDefinition definition, string requestedDisplayName)
        {
            if (definition == null) throw new InvalidOperationException("重命名前必须选择物品定义。");
            string displayName = NormalizeAssetFileName(requestedDisplayName);
            if (string.IsNullOrEmpty(displayName)) throw new InvalidOperationException("物品显示名称不能为空或不能作为资产文件名。");

            string assetPath = AssetDatabase.GetAssetPath(definition);
            if (string.IsNullOrEmpty(assetPath)) throw new InvalidOperationException("当前物品定义没有有效的资产路径，无法重命名。");
            string oldAssetName = Path.GetFileNameWithoutExtension(assetPath);
            string oldDisplayName = definition.DisplayName ?? string.Empty;
            string oldObjectName = definition.name;
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(RenameDefinitionUndoName);
            // 只记录当前 Definition；资产路径不由 Unity Undo 直接管理，完成后由同一目标的 m_Name 恢复。
            Undo.RecordObject(definition, RenameDefinitionUndoName);
            bool assetRenamed = false;
            string renamedAssetPath = assetPath;
            try
            {
                renamedAssetPath = RenameDefinitionAssetFile(assetPath, displayName);
                assetRenamed = !string.Equals(renamedAssetPath, assetPath, StringComparison.OrdinalIgnoreCase);

                SerializedObject serialized = new SerializedObject(definition);
                SerializedProperty displayNameProperty = serialized.FindProperty("displayName");
                if (displayNameProperty == null) throw new InvalidOperationException("物品定义缺少显示名称字段。");
                displayNameProperty.stringValue = requestedDisplayName?.Trim() ?? string.Empty;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                // Unity 通常会随 RenameAsset 更新对象名；这里显式使用最终文件名，覆盖重名时生成的唯一后缀。
                definition.name = Path.GetFileNameWithoutExtension(renamedAssetPath);
                EditorUtility.SetDirty(definition);
                AssetDatabase.SaveAssets();
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(undoGroup);
                return undoGroup;
            }
            catch
            {
                // 显示名称写入失败时尽力把文件恢复为原名，避免资源路径和序列化数据分裂。
                if (assetRenamed)
                {
                    string restoreError = AssetDatabase.RenameAsset(renamedAssetPath, oldAssetName);
                    if (!string.IsNullOrEmpty(restoreError)) Debug.LogError($"恢复物品资产名称失败：{restoreError}");
                }
                // 资产路径或序列化字段写入失败时恢复内存对象状态，避免详情输入框与资源内容分裂。
                definition.name = oldObjectName;
                SerializedObject rollback = new SerializedObject(definition);
                SerializedProperty rollbackDisplayName = rollback.FindProperty("displayName");
                if (rollbackDisplayName != null)
                {
                    rollbackDisplayName.stringValue = oldDisplayName;
                    rollback.ApplyModifiedPropertiesWithoutUndo();
                }
                throw;
            }
            finally
            {
                // 重命名组完成后推进到新的空组，避免后续 UI Toolkit 字段修改继续复用本组名称或组号。
                Undo.IncrementCurrentGroup();
            }
        }

        /// <summary>按当前 Definition 的 Unity 对象名称恢复该对象的资产文件路径。</summary>
        /// <param name="definition">Undo 或 Redo 后需要恢复路径的单个物品定义。</param>
        /// <exception cref="InvalidOperationException">对象名称、资产路径或目标文件名无效时抛出。</exception>
        internal void SynchronizeDefinitionAssetPathAfterUndo(ItemDefinition definition)
        {
            if (definition == null) return;
            string assetPath = AssetDatabase.GetAssetPath(definition);
            if (string.IsNullOrEmpty(assetPath)) throw new InvalidOperationException("Undo 后的物品定义没有有效资产路径，无法恢复文件名。");

            string targetName = definition.name?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(targetName)) throw new InvalidOperationException("Undo 后的物品定义对象名称为空，无法恢复文件名。");
            if (!string.Equals(NormalizeAssetFileName(targetName), targetName, StringComparison.Ordinal))
                throw new InvalidOperationException($"Undo 后的物品定义对象名称“{targetName}”不能作为有效资产文件名。");

            string currentName = Path.GetFileNameWithoutExtension(assetPath);
            if (string.Equals(currentName, targetName, StringComparison.OrdinalIgnoreCase)) return;

            string directory = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(directory)) throw new InvalidOperationException("Undo 后的物品定义资产必须位于有效目录中。");
            string targetPath = $"{directory}/{targetName}.asset";
            UnityEngine.Object existingAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(targetPath);
            if (existingAsset != null && existingAsset != definition)
                throw new InvalidOperationException($"Undo 后无法将物品定义恢复为“{targetName}.asset”：目标路径已被其他资产占用。");

            bool sourceNameAligned = false;
            try
            {
                // RenameAsset 要求源资产的 Main Object Name 与当前文件名一致；Undo 后先临时对齐，避免 Unity 报告不一致。
                SetDefinitionObjectNameWithoutUndo(definition, currentName);
                sourceNameAligned = true;
                string error = AssetDatabase.RenameAsset(assetPath, targetName);
                if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException($"Undo 后恢复物品资产名称失败：{error}");
                // RenameAsset 通常会同步 m_Name，但这里再次明确写入目标名，覆盖 Unity 版本或冲突路径的差异。
                SetDefinitionObjectNameWithoutUndo(definition, targetName);
            }
            catch
            {
                if (sourceNameAligned)
                {
                    try
                    {
                        // 路径恢复失败时保留 Undo/Redo 已产生的对象名，避免把错误恢复过程写成新的状态。
                        SetDefinitionObjectNameWithoutUndo(definition, targetName);
                    }
                    catch (Exception restoreException)
                    {
                        Debug.LogError($"恢复 Undo 后物品对象名称失败：{restoreException.Message}");
                    }
                }
                throw;
            }
        }

        /// <summary>在不注册 Undo 的情况下设置 Definition 的 Main Object Name 并保存。</summary>
        /// <param name="definition">需要设置对象名的物品定义。</param>
        /// <param name="objectName">要写入的对象名。</param>
        private static void SetDefinitionObjectNameWithoutUndo(ItemDefinition definition, string objectName)
        {
            SerializedObject serialized = new SerializedObject(definition);
            SerializedProperty nameProperty = serialized.FindProperty("m_Name");
            if (nameProperty == null) throw new InvalidOperationException("物品定义缺少 Main Object Name 序列化字段。");
            if (string.Equals(nameProperty.stringValue, objectName, StringComparison.Ordinal)) return;

            nameProperty.stringValue = objectName;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssets();
        }

        /// <summary>将定义资产移动到名称对应的唯一文件路径并保留 GUID。</summary>
        /// <param name="assetPath">当前资产路径。</param>
        /// <param name="displayName">清理后的目标名称。</param>
        /// <returns>重命名后的资产路径。</returns>
        private static string RenameDefinitionAssetFile(string assetPath, string displayName)
        {
            string directory = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(directory)) throw new InvalidOperationException("物品定义资产必须位于有效目录中。");
            string currentName = Path.GetFileNameWithoutExtension(assetPath);
            string basePath = $"{directory}/{displayName}.asset";
            UnityEngine.Object existingBaseAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(basePath);
            // 同名资产已占用时允许 Unity 追加后缀；若当前资产已经持有该后缀，则保持路径稳定，避免每次刷新继续递增。
            if (existingBaseAsset != null && !string.Equals(assetPath, basePath, StringComparison.OrdinalIgnoreCase) &&
                currentName.StartsWith(displayName + " ", StringComparison.OrdinalIgnoreCase)) return assetPath;
            string candidatePath = existingBaseAsset == null || string.Equals(assetPath, basePath, StringComparison.OrdinalIgnoreCase)
                ? basePath
                : AssetDatabase.GenerateUniqueAssetPath(basePath);
            string targetName = Path.GetFileNameWithoutExtension(candidatePath);
            if (string.Equals(currentName, targetName, StringComparison.OrdinalIgnoreCase)) return assetPath;
            string error = AssetDatabase.RenameAsset(assetPath, targetName);
            if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException($"重命名物品资产失败：{error}");
            return $"{directory}/{targetName}.asset";
        }

        /// <summary>清理显示名称，使其能够安全地作为 Unity 资产文件名。</summary>
        /// <param name="value">原始名称。</param>
        /// <returns>清理后的名称。</returns>
        private static string NormalizeAssetFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            char[] invalidCharacters = Path.GetInvalidFileNameChars();
            var builder = new System.Text.StringBuilder(value.Trim());
            for (int index = 0; index < builder.Length; index++)
                if (Array.IndexOf(invalidCharacters, builder[index]) >= 0) builder[index] = '_';
            return builder.ToString().TrimEnd(' ', '.');
        }

        /// <summary>创建一个新的物品定义资产并加入数据库。</summary>
        /// <param name="definitionType">定义类型。</param>
        /// <param name="database">目标数据库。</param>
        /// <returns>新建的定义。</returns>
        internal ItemDefinition CreateDefinition(Type definitionType, ItemDatabase database)
        {
            if (definitionType != typeof(StackableItemDefinition) &&
                definitionType != typeof(DevelopmentItemDefinition) &&
                definitionType != typeof(WeaponDefinition) &&
                definitionType != typeof(ArtifactDefinition))
                throw new ArgumentException("只能创建普通物品、养成道具、武器或圣遗物定义。", nameof(definitionType));
            if (database == null) throw new InvalidOperationException("创建物品前必须选择 ItemDatabase。");

            string folder = EnsureDefinitionsFolder(database);
            string fileName = definitionType == typeof(WeaponDefinition) ? "NewWeapon" :
                definitionType == typeof(ArtifactDefinition) ? "NewArtifact" :
                definitionType == typeof(DevelopmentItemDefinition) ? "NewDevelopmentItem" : "NewItem";
            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{fileName}.asset");
            ItemDefinition definition = null;
            WeaponGrowthProfile growthProfile = null;
            string profilePath = string.Empty;
            bool databaseAdded = false;
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("创建物品定义");
            try
            {
                // 先创建资产文件，再注册 Undo，避免 Undo 在资产尚未落盘时销毁临时对象。
                definition = ScriptableObject.CreateInstance(definitionType) as ItemDefinition;
                AssetDatabase.CreateAsset(definition, assetPath);
                Undo.RegisterCreatedObjectUndo(definition, "创建物品定义");
                if (definition is WeaponDefinition || definition is ArtifactDefinition)
                {
                    bool isArtifact = definition is ArtifactDefinition;
                    profilePath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{(isArtifact ? "NewArtifactGrowthProfile" : "NewWeaponGrowthProfile")}.asset");
                    growthProfile = isArtifact
                        ? null
                        : ScriptableObject.CreateInstance<WeaponGrowthProfile>();
                    UnityEngine.Object artifactProfile = isArtifact ? ScriptableObject.CreateInstance<ArtifactGrowthProfile>() : null;
                    if (isArtifact)
                    {
                        AssetDatabase.CreateAsset(artifactProfile, profilePath);
                        Undo.RegisterCreatedObjectUndo(artifactProfile, "创建圣遗物成长配置");
                    }
                    else
                    {
                        AssetDatabase.CreateAsset(growthProfile, profilePath);
                        Undo.RegisterCreatedObjectUndo(growthProfile, "创建武器成长配置");
                    }
                }

                SerializedObject serialized = new SerializedObject(definition);
                SetSerializedItemId(serialized, $"{GetDefinitionPrefix(definition)}_{Guid.NewGuid():N}");
                SetString(serialized, "displayName", definition is WeaponDefinition ? "新武器" :
                    definition is ArtifactDefinition ? "新圣遗物" :
                    definition is DevelopmentItemDefinition ? "新养成道具" : "新物品");
                SetEnum(serialized, "category", (int)GetDefinitionCategory(definition));
                ApplyDefaults(serialized, database, definition);
                if (definition is WeaponDefinition weapon)
                {
                    serialized.FindProperty("growthProfile").objectReferenceValue = growthProfile;
                }
                else if (definition is ArtifactDefinition artifact)
                {
                    UnityEngine.Object artifactProfile = AssetDatabase.LoadAssetAtPath<ArtifactGrowthProfile>(profilePath);
                    serialized.FindProperty("growthProfile").objectReferenceValue = artifactProfile;
                }
                else if (definition is DevelopmentItemDefinition)
                {
                    SetEnum(serialized, "developmentType", (int)DevelopmentItemType.CharacterExperience);
                    SetInt(serialized, "experienceValue", 100);
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                databaseAdded = true;
                // 标记为可能已写入，异常时也会尝试移除部分完成的数据库引用。
                AddDefinition(database, definition);
                AssetDatabase.SaveAssets();
                Undo.CollapseUndoOperations(undoGroup);
                EditorUtility.FocusProjectWindow();
                Selection.activeObject = definition;
                return definition;
            }
            catch
            {
                // 只回滚本次创建的数据库引用和明确资产，避免留下无法解析的 GUID。
                if (databaseAdded) RemoveDefinition(database, definition);
                if (!string.IsNullOrEmpty(profilePath)) AssetDatabase.DeleteAsset(profilePath);
                AssetDatabase.DeleteAsset(assetPath);
                AssetDatabase.SaveAssets();
                throw;
            }
        }

        /// <summary>复制当前定义，生成新的 ItemId 并加入数据库。</summary>
        /// <param name="source">源定义。</param>
        /// <param name="database">目标数据库。</param>
        /// <returns>复制后的定义。</returns>
        internal ItemDefinition DuplicateDefinition(ItemDefinition source, ItemDatabase database)
        {
            if (source == null) throw new InvalidOperationException("复制前必须选择物品。");
            if (database == null) throw new InvalidOperationException("复制前必须选择 ItemDatabase。");
            string sourcePath = AssetDatabase.GetAssetPath(source);
            string copyPath = AssetDatabase.GenerateUniqueAssetPath(sourcePath);
            string profileCopyPath = string.Empty;
            ItemDefinition copy = null;
            WeaponGrowthProfile profileCopy = null;
            bool databaseAdded = false;
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("复制物品定义");
            try
            {
                if (!AssetDatabase.CopyAsset(sourcePath, copyPath)) throw new InvalidOperationException($"无法复制物品资产：{sourcePath}。");
                AssetDatabase.ImportAsset(copyPath);
                copy = AssetDatabase.LoadAssetAtPath<ItemDefinition>(copyPath);
                if (copy == null) throw new InvalidOperationException($"复制后的物品资产无法加载：{copyPath}。");
                Undo.RegisterCreatedObjectUndo(copy, "复制物品定义");
                SerializedObject serialized = new SerializedObject(copy);
                SetSerializedItemId(serialized, $"{GetDefinitionPrefix(copy)}_{Guid.NewGuid():N}");
                SetString(serialized, "displayName", $"{source.DisplayName} 副本");
                if (source is WeaponDefinition sourceWeapon && copy is WeaponDefinition)
                {
                    WeaponGrowthProfile sourceProfile = sourceWeapon.GrowthProfile;
                    if (sourceProfile == null) throw new InvalidOperationException("源武器缺少 WeaponGrowthProfile，无法复制。");
                    string profilePath = AssetDatabase.GetAssetPath(sourceProfile);
                    profileCopyPath = AssetDatabase.GenerateUniqueAssetPath(profilePath);
                    if (!AssetDatabase.CopyAsset(profilePath, profileCopyPath)) throw new InvalidOperationException($"无法复制武器成长配置：{profilePath}。");
                    AssetDatabase.ImportAsset(profileCopyPath);
                    profileCopy = AssetDatabase.LoadAssetAtPath<WeaponGrowthProfile>(profileCopyPath);
                    if (profileCopy == null) throw new InvalidOperationException($"复制后的武器成长配置无法加载：{profileCopyPath}。");
                    serialized.FindProperty("growthProfile").objectReferenceValue = profileCopy;
                    Undo.RegisterCreatedObjectUndo(profileCopy, "复制武器成长配置");
                }
                else if (source is ArtifactDefinition sourceArtifact && copy is ArtifactDefinition)
                {
                    ArtifactGrowthProfile sourceProfile = sourceArtifact.GrowthProfile;
                    if (sourceProfile == null) throw new InvalidOperationException("源圣遗物缺少 ArtifactGrowthProfile，无法复制。");
                    string profilePath = AssetDatabase.GetAssetPath(sourceProfile);
                    profileCopyPath = AssetDatabase.GenerateUniqueAssetPath(profilePath);
                    if (!AssetDatabase.CopyAsset(profilePath, profileCopyPath)) throw new InvalidOperationException($"无法复制圣遗物成长配置：{profilePath}。");
                    AssetDatabase.ImportAsset(profileCopyPath);
                    ArtifactGrowthProfile artifactProfileCopy = AssetDatabase.LoadAssetAtPath<ArtifactGrowthProfile>(profileCopyPath);
                    if (artifactProfileCopy == null) throw new InvalidOperationException($"复制后的圣遗物成长配置无法加载：{profileCopyPath}。");
                    serialized.FindProperty("growthProfile").objectReferenceValue = artifactProfileCopy;
                    Undo.RegisterCreatedObjectUndo(artifactProfileCopy, "复制圣遗物成长配置");
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                databaseAdded = true;
                AddDefinition(database, copy);
                AssetDatabase.SaveAssets();
                Undo.CollapseUndoOperations(undoGroup);
                return copy;
            }
            catch
            {
                // 复制过程中任一步失败都删除本次明确创建的资产和可能已写入的数据库引用。
                if (databaseAdded) RemoveDefinition(database, copy);
                if (!string.IsNullOrEmpty(profileCopyPath)) AssetDatabase.DeleteAsset(profileCopyPath);
                AssetDatabase.DeleteAsset(copyPath);
                AssetDatabase.SaveAssets();
                throw;
            }
        }

        /// <summary>从数据库列表移除定义但保留资产文件。</summary>
        /// <param name="database">数据库。</param>
        /// <param name="definition">待移除定义。</param>
        internal void RemoveDefinition(ItemDatabase database, ItemDefinition definition)
        {
            if (database == null || definition == null) return;
            Undo.RecordObject(database, "从物品数据库移除定义");
            SerializedObject serialized = new SerializedObject(database);
            SerializedProperty list = serialized.FindProperty("definitions");
            for (int index = 0; index < list.arraySize; index++)
            {
                if (list.GetArrayElementAtIndex(index).objectReferenceValue != definition) continue;
                list.DeleteArrayElementAtIndex(index);
                break;
            }
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssets();
        }

        /// <summary>将定义资产移入 Unity 回收站并从数据库移除。</summary>
        /// <param name="database">数据库。</param>
        /// <param name="definition">待删除定义。</param>
        internal void DeleteDefinition(ItemDatabase database, ItemDefinition definition)
        {
            if (definition == null) return;
            RemoveDefinition(database, definition);
            string path = AssetDatabase.GetAssetPath(definition);
            if (!string.IsNullOrEmpty(path)) AssetDatabase.MoveAssetToTrash(path);
            AssetDatabase.SaveAssets();
        }

        /// <summary>仅对当前选中的定义应用分类默认值。</summary>
        /// <param name="database">数据库。</param>
        /// <param name="definition">当前定义。</param>
        internal void ApplyCategoryDefaults(ItemDatabase database, ItemDefinition definition)
        {
            if (database == null || definition == null) throw new InvalidOperationException("应用默认值前必须选择数据库和物品。");
            Undo.RecordObject(definition, "应用物品类型默认值");
            SerializedObject serialized = new SerializedObject(definition);
            if (database.TryGetCategoryDefault(definition.Category, out ItemCategoryDefaultRule rule))
            {
                ApplyDefaults(serialized, rule, definition);
            }
            else
            {
                // 新增分类在旧数据库资产尚未补充规则时采用内置安全默认值，不改变用户已配置的养成用途。
                SetEnum(serialized, "rarity", (int)(definition is ArtifactDefinition ? ItemRarity.Five : ItemRarity.One));
                SetInt(serialized, "sortPriority", 100);
                if (definition is StackableItemDefinition) SetInt(serialized, "maxQuantity", 9999);
            }
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssets();
        }

        /// <summary>将武器最大等级同步到其独立成长配置。</summary>
        /// <param name="weapon">待同步的武器定义。</param>
        /// <returns>发生 Profile 修改时返回 true。</returns>
        internal bool SynchronizeGrowthProfileMaxLevel(WeaponDefinition weapon)
        {
            if (weapon == null || weapon.GrowthProfile == null) return false;
            WeaponGrowthProfile profile = weapon.GrowthProfile;
            if (profile.MaxLevel == weapon.MaxLevel) return false;
            string[] weaponGuids = AssetDatabase.FindAssets("t:WeaponDefinition");
            for (int index = 0; index < weaponGuids.Length; index++)
            {
                WeaponDefinition other = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(AssetDatabase.GUIDToAssetPath(weaponGuids[index]));
                if (other == null || other == weapon || other.GrowthProfile != profile || other.MaxLevel == weapon.MaxLevel) continue;
                throw new InvalidOperationException($"成长配置“{profile.name}”被不同最大等级的武器共享，请先为当前武器创建独立成长配置。");
            }
            Undo.RecordObjects(new UnityEngine.Object[] { weapon, profile }, "同步武器成长最大等级");
            SerializedObject serialized = new SerializedObject(profile);
            SerializedProperty maxLevel = serialized.FindProperty("maxLevel");
            if (maxLevel == null) throw new InvalidOperationException("成长配置缺少最大等级字段。");
            maxLevel.intValue = weapon.MaxLevel;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return true;
        }

        #endregion

        #region 校验与内部辅助

        /// <summary>验证数据库并返回成功文本。</summary>
        /// <param name="database">数据库。</param>
        /// <returns>验证成功提示。</returns>
        internal string ValidateDatabase(ItemDatabase database)
        {
            if (database == null) throw new InvalidOperationException("请先选择 ItemDatabase。");
            database.ValidateAndBuildIndex();
            return $"验证通过：{database.Definitions.Count} 个物品定义。";
        }

        /// <summary>把定义加入数据库序列化列表。</summary>
        /// <param name="database">数据库。</param>
        /// <param name="definition">物品定义。</param>
        private static void AddDefinition(ItemDatabase database, ItemDefinition definition)
        {
            // 数据库列表是配置资产的唯一索引入口，先记录数据库 Undo 再写入引用。
            Undo.RecordObject(database, "更新物品数据库");
            SerializedObject serialized = new SerializedObject(database);
            SerializedProperty list = serialized.FindProperty("definitions");
            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = definition;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(database);
        }

        /// <summary>应用数据库规则到 SerializedObject。</summary>
        /// <param name="serialized">定义序列化对象。</param>
        /// <param name="database">数据库。</param>
        /// <param name="definition">定义。</param>
        private static void ApplyDefaults(SerializedObject serialized, ItemDatabase database, ItemDefinition definition)
        {
            if (database.TryGetCategoryDefault(GetDefinitionCategory(definition), out ItemCategoryDefaultRule rule))
                ApplyDefaults(serialized, rule, definition);
            else
            {
                SetEnum(serialized, "rarity", (int)(definition is ArtifactDefinition ? ItemRarity.Five : ItemRarity.One));
                SetInt(serialized, "sortPriority", 100);
                if (definition is StackableItemDefinition) SetInt(serialized, "maxQuantity", 9999);
            }
        }

        /// <summary>应用单条分类规则到定义。</summary>
        /// <param name="serialized">定义序列化对象。</param>
        /// <param name="rule">分类规则。</param>
        /// <param name="definition">定义。</param>
        private static void ApplyDefaults(SerializedObject serialized, ItemCategoryDefaultRule rule, ItemDefinition definition)
        {
            SetEnum(serialized, "rarity", (int)rule.DefaultRarity);
            SetInt(serialized, "sortPriority", rule.DefaultSortPriority);
            if (definition is StackableItemDefinition) SetInt(serialized, "maxQuantity", rule.DefaultMaxQuantity);
        }

        /// <summary>根据 Definition 运行时类型获取其固定顶层分类。</summary>
        /// <param name="definition">物品定义。</param>
        /// <returns>对应分类。</returns>
        private static ItemCategory GetDefinitionCategory(ItemDefinition definition)
        {
            return definition switch
            {
                WeaponDefinition => ItemCategory.Weapon,
                ArtifactDefinition => ItemCategory.Artifact,
                DevelopmentItemDefinition => ItemCategory.DevelopmentItem,
                _ => ItemCategory.Material
            };
        }

        /// <summary>获取创建或复制定义使用的稳定 ItemId 前缀。</summary>
        /// <param name="definition">物品定义。</param>
        /// <returns>定义类型前缀。</returns>
        private static string GetDefinitionPrefix(ItemDefinition definition)
        {
            return definition switch
            {
                WeaponDefinition => "weapon",
                ArtifactDefinition => "artifact",
                DevelopmentItemDefinition => "development",
                _ => "item"
            };
        }

        /// <summary>设置序列化 ItemId 的内部字符串。</summary>
        /// <param name="serialized">定义序列化对象。</param>
        /// <param name="value">标识值。</param>
        private static void SetSerializedItemId(SerializedObject serialized, string value)
        {
            SerializedProperty itemId = serialized.FindProperty("itemId");
            itemId.FindPropertyRelative("value").stringValue = value;
        }

        /// <summary>设置序列化字符串字段。</summary>
        /// <param name="serialized">序列化对象。</param>
        /// <param name="propertyName">字段名。</param>
        /// <param name="value">字符串。</param>
        private static void SetString(SerializedObject serialized, string propertyName, string value) => serialized.FindProperty(propertyName).stringValue = value;

        /// <summary>设置序列化整数枚举字段。</summary>
        /// <param name="serialized">序列化对象。</param>
        /// <param name="propertyName">字段名。</param>
        /// <param name="value">枚举整数值。</param>
        private static void SetEnum(SerializedObject serialized, string propertyName, int value) => serialized.FindProperty(propertyName).intValue = value;

        /// <summary>设置序列化整数。</summary>
        /// <param name="serialized">序列化对象。</param>
        /// <param name="propertyName">字段名。</param>
        /// <param name="value">整数值。</param>
        private static void SetInt(SerializedObject serialized, string propertyName, int value) => serialized.FindProperty(propertyName).intValue = value;

        /// <summary>确保数据库旁边存在定义目录。</summary>
        /// <param name="database">数据库。</param>
        /// <returns>定义目录路径。</returns>
        private static string EnsureDefinitionsFolder(ItemDatabase database)
        {
            string databasePath = AssetDatabase.GetAssetPath(database);
            string parent = Path.GetDirectoryName(databasePath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent)) throw new InvalidOperationException("ItemDatabase 必须位于 Assets 目录中。");
            string folder = $"{parent}/{DefinitionsFolderName}";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(parent, DefinitionsFolderName);
            return folder;
        }

        #endregion
    }
}
#endif
