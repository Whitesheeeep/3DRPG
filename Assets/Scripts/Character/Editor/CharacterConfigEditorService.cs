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

namespace RPG.Character.Editor
{
    /// <summary>集中执行 CharacterDatabase 和 CharacterConfig 的编辑器事务。</summary>
    public sealed class CharacterConfigEditorService
    {
        #region 常量

        private const string AddressableGroupName = "CharacterPrefabs";
        private const string SpriteAtlasGroupName = "UISpriteAtlas";

        #endregion

        #region 数据库解析

        /// <summary>按会话、唯一数据库或配置归属解析角色数据库。</summary>
        /// <param name="config">可选角色配置。</param>
        /// <returns>可唯一确定时返回数据库，否则返回空。</returns>
        public CharacterDatabase ResolveDatabaseForConfig(CharacterConfig config)
        {
            if (config != null)
            {
                string[] guids = AssetDatabase.FindAssets("t:CharacterDatabase");
                CharacterDatabase match = null;
                for (int index = 0; index < guids.Length; index++)
                {
                    CharacterDatabase candidate = AssetDatabase.LoadAssetAtPath<CharacterDatabase>(AssetDatabase.GUIDToAssetPath(guids[index]));
                    if (candidate == null) continue;
                    for (int itemIndex = 0; itemIndex < candidate.Characters.Count; itemIndex++)
                    {
                        if (candidate.Characters[itemIndex] != config) continue;
                        if (match != null) return null;
                        match = candidate;
                        break;
                    }
                }
                if (match != null) return match;
            }
            return CharacterConfigEditorSession.ResolveDatabase();
        }

        #endregion

        #region CRUD: 创建、复制、加入数据库、移除数据库、删除

        /// <summary>创建带合法唯一初始 CharacterId 的配置并加入数据库。</summary>
        /// <param name="database">目标数据库。</param>
        /// <returns>新建配置。</returns>
        public CharacterConfig Create(CharacterDatabase database)
        {
            if (database == null) throw new InvalidOperationException("创建角色前必须选择 CharacterDatabase。");
            string folder = GetDatabaseFolder(database);
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("创建角色配置");
            string characterId = string.Empty;
            string path = string.Empty;
            CharacterConfig config = null;
            try
            {
                // 先分配最终 ID 再创建文件，避免临时资产名与 CharacterId 不一致。
                characterId = AllocateCharacterId(database);
                path = GetCharacterAssetPath(folder, characterId);
                config = ScriptableObject.CreateInstance<CharacterConfig>();
                config.name = characterId;
                AssetDatabase.CreateAsset(config, path);
                SerializedObject serializedObject = new SerializedObject(config);
                serializedObject.FindProperty("characterId").FindPropertyRelative("value").stringValue = characterId;
                serializedObject.FindProperty("characterName").stringValue = "新角色";
                serializedObject.FindProperty("rarity").intValue = (int)CharacterRarity.Five;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
                Undo.RegisterCreatedObjectUndo(config, "创建角色配置");
                AddToDatabase(database, config);
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
                Undo.CollapseUndoOperations(undoGroup);
                return config;
            }
            catch
            {
                if (!string.IsNullOrEmpty(path)) AssetDatabase.DeleteAsset(path);
                RestoreCharacterIdCounter(database, characterId);
                AssetDatabase.SaveAssets();
                throw;
            }
        }

        /// <summary>复制角色配置并生成新的稳定 CharacterId。</summary>
        /// <param name="database">目标数据库。</param>
        /// <param name="source">源配置。</param>
        /// <returns>复制后的配置。</returns>
        public CharacterConfig Duplicate(CharacterDatabase database, CharacterConfig source)
        {
            if (database == null || source == null) throw new InvalidOperationException("复制角色前必须选择数据库和角色。");
            string sourcePath = AssetDatabase.GetAssetPath(source);
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("复制角色配置");
            string characterId = string.Empty;
            string targetPath = string.Empty;
            CharacterConfig copy = null;
            try
            {
                characterId = AllocateCharacterId(database);
                string folder = GetDatabaseFolder(database);
                targetPath = GetCharacterAssetPath(folder, characterId);
                if (!AssetDatabase.CopyAsset(sourcePath, targetPath)) throw new InvalidOperationException($"无法复制角色配置：{sourcePath}。");
                AssetDatabase.ImportAsset(targetPath);
                copy = AssetDatabase.LoadAssetAtPath<CharacterConfig>(targetPath);
                if (copy == null) throw new InvalidOperationException($"复制后的角色配置无法加载：{targetPath}。");
                Undo.RegisterCreatedObjectUndo(copy, "复制角色配置");
                SerializedObject serializedObject = new SerializedObject(copy);
                serializedObject.FindProperty("characterId").FindPropertyRelative("value").stringValue = characterId;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
                copy.name = characterId;
                AddToDatabase(database, copy);
                AssetDatabase.SaveAssets();
                Undo.CollapseUndoOperations(undoGroup);
                return copy;
            }
            catch
            {
                if (!string.IsNullOrEmpty(targetPath)) AssetDatabase.DeleteAsset(targetPath);
                RestoreCharacterIdCounter(database, characterId);
                AssetDatabase.SaveAssets();
                throw;
            }
        }

        /// <summary>将角色配置加入数据库，重复引用时拒绝写入。</summary>
        public void AddToDatabase(CharacterDatabase database, CharacterConfig config)
        {
            if (database == null || config == null) throw new ArgumentNullException();
            for (int index = 0; index < database.Characters.Count; index++)
                if (database.Characters[index] == config) return;
            Undo.RecordObject(database, "加入角色配置");
            SerializedObject serializedObject = new SerializedObject(database);
            SerializedProperty characters = serializedObject.FindProperty("characters");
            characters.arraySize++;
            characters.GetArrayElementAtIndex(characters.arraySize - 1).objectReferenceValue = config;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(database);
        }

        /// <summary>从数据库移除角色但保留配置资产。</summary>
        public void RemoveFromDatabase(CharacterDatabase database, CharacterConfig config)
        {
            if (database == null || config == null) return;
            Undo.RecordObject(database, "移出角色配置");
            SerializedObject serializedObject = new SerializedObject(database);
            SerializedProperty characters = serializedObject.FindProperty("characters");
            for (int index = characters.arraySize - 1; index >= 0; index--)
                if (characters.GetArrayElementAtIndex(index).objectReferenceValue == config)
                    characters.DeleteArrayElementAtIndex(index);
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(database);
        }

        /// <summary>移除数据库引用并删除角色配置资产。</summary>
        public void Delete(CharacterDatabase database, CharacterConfig config)
        {
            if (database == null || config == null) return;
            RemoveFromDatabase(database, config);
            string path = AssetDatabase.GetAssetPath(config);
            if (!string.IsNullOrEmpty(path)) AssetDatabase.DeleteAsset(path);
            AssetDatabase.SaveAssets();
        }

        #endregion

        #region 字段提交

        /// <summary>反查 Sprite 所属图集并提交预览、Address 与运行时名称。</summary>
        /// <param name="config">角色配置。</param>
        /// <param name="sprite">新的 Sprite，可为空表示清空。</param>
        /// <param name="sideIcon">是否为侧面头像。</param>
        /// <returns>包含自动解析结果的中文状态。</returns>
        public string SetPreviewSprite(CharacterConfig config, Sprite sprite, bool sideIcon)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            string atlasAddress = string.Empty;
            string spriteName = string.Empty;
            if (sprite != null)
            {
                SpriteAtlasLookupResult lookup = sprite.FindSpriteAtlasReference();
                if (lookup.Status == SpriteAtlasLookupStatus.NotFound)
                    throw new InvalidOperationException($"Sprite“{sprite.name}”未被任何默认 Atlas 收录。");
                if (lookup.Status == SpriteAtlasLookupStatus.Ambiguous)
                    throw new InvalidOperationException($"Sprite“{sprite.name}”同时被多个 Atlas 收录：{string.Join(", ", lookup.MatchingAtlasPaths)}。");
                atlasAddress = ResolveAtlasAddress(lookup.Atlas);
                spriteName = lookup.SpriteName;
            }

            Undo.RecordObject(config, sideIcon ? "修改侧面头像" : "修改角色头像");
            SerializedObject serializedObject = new SerializedObject(config);
            serializedObject.FindProperty(sideIcon ? "editorSideIcon" : "editorAvatar").objectReferenceValue = sprite;
            serializedObject.FindProperty(sideIcon ? "sideIconAddress" : "avatarAddress").stringValue = atlasAddress;
            serializedObject.FindProperty(sideIcon ? "sideIconSpriteName" : "avatarSpriteName").stringValue = spriteName;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            return sprite == null
                ? $"已清除{(sideIcon ? "侧面头像" : "角色头像")}及其 Address 和 SpriteName。"
                : $"已自动填充图集“{atlasAddress}”和 Sprite“{spriteName}”。";
        }

        #endregion

        #region 校验与资源辅助

        /// <summary>执行当前角色的运行时字段、Addressables、Prefab 和图集校验。</summary>
        public void ValidateConfig(CharacterConfig config)
        {
            if (config == null) throw new InvalidOperationException("没有选择 CharacterConfig。");
            config.Validate();
            string assetPath = AssetDatabase.GetAssetPath(config);
            string assetFileName = Path.GetFileNameWithoutExtension(assetPath);
            if (!string.Equals(assetFileName, config.CharacterId.ToString(), StringComparison.Ordinal) ||
                !string.Equals(config.name, config.CharacterId.ToString(), StringComparison.Ordinal))
                throw new InvalidOperationException($"角色资产身份不一致：文件名和对象名必须等于 CharacterId '{config.CharacterId}'。");
            ValidateSpriteReference(config.SideIconAddress, config.SideIconSpriteName, "侧面头像");
            ValidateSpriteReference(config.AvatarAddress, config.AvatarSpriteName, "角色头像");
            ValidatePrefab(config);
        }

        /// <summary>执行整个数据库的严格校验。</summary>
        public void ValidateDatabase(CharacterDatabase database)
        {
            if (database == null) throw new InvalidOperationException("没有选择 CharacterDatabase。");
            database.ValidateAndBuildIndex();
            for (int index = 0; index < database.Characters.Count; index++) ValidateConfig(database.Characters[index]);
        }

        /// <summary>从角色数据库计数器分配下一个稳定 CharacterId。</summary>
        /// <param name="database">目标角色数据库。</param>
        /// <returns>形如 character_0001 的新角色 ID。</returns>
        private static string AllocateCharacterId(CharacterDatabase database)
        {
            var existingIds = new List<string>(database.Characters.Count);
            for (int index = 0; index < database.Characters.Count; index++)
            {
                CharacterConfig config = database.Characters[index];
                if (config != null) existingIds.Add(config.CharacterId.ToString());
            }

            SerializedObject databaseSerialized = new SerializedObject(database);
            Undo.RecordObject(database, "分配角色编号");
            return ConfigEditorStableIdUtility.AllocateNextId(
                "character",
                databaseSerialized,
                "nextCharacterIdNumber",
                existingIds);
        }

        /// <summary>异常回滚时恢复本次已推进的角色编号计数器。</summary>
        /// <param name="database">目标角色数据库。</param>
        /// <param name="allocatedId">本次分配的角色 ID。</param>
        private static void RestoreCharacterIdCounter(CharacterDatabase database, string allocatedId)
        {
            if (database == null || string.IsNullOrEmpty(allocatedId)) return;
            if (!ConfigEditorStableIdUtility.TryParseNumber(allocatedId, "character", out int number)) return;
            SerializedObject serialized = new SerializedObject(database);
            SerializedProperty counter = serialized.FindProperty("nextCharacterIdNumber");
            if (counter == null || counter.intValue != number + 1) return;
            counter.intValue = number;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(database);
        }

        /// <summary>查找唯一包含指定配置的数据库。</summary>
        private static string GetDatabaseFolder(CharacterDatabase database)
        {
            string path = AssetDatabase.GetAssetPath(database);
            string folder = Path.GetDirectoryName(path);
            return string.IsNullOrEmpty(folder) ? "Assets" : folder.Replace('\\', '/');
        }

        /// <summary>组合并验证 CharacterId 对应的固定资产路径，禁止 Unity 自动追加数字后缀。</summary>
        /// <param name="folder">角色数据库所在目录。</param>
        /// <param name="characterId">已分配的稳定角色 ID。</param>
        /// <returns>以 CharacterId 命名的目标资产路径。</returns>
        /// <exception cref="InvalidOperationException">目标路径已被其他资源占用时抛出。</exception>
        private static string GetCharacterAssetPath(string folder, string characterId)
        {
            string path = $"{folder}/{characterId}.asset";
            if (File.Exists(path) || AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null)
                throw new InvalidOperationException($"角色 ID“{characterId}”对应的资产路径已存在：{path}。");
            return path;
        }

        /// <summary>按 Address 和 SpriteName 校验唯一的 UISpriteAtlas 引用。</summary>
        /// <param name="atlasAddress">待校验的 Address。</param>
        /// <param name="spriteName">待校验的 SpriteName。</param>
        /// <param name="fieldName">错误消息中的字段名称。</param>
        private static void ValidateSpriteReference(string atlasAddress, string spriteName, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(atlasAddress)) throw new InvalidOperationException($"{fieldName} Address 不能为空。");
            if (string.IsNullOrWhiteSpace(spriteName)) throw new InvalidOperationException($"{fieldName} 不能为空。");
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) throw new InvalidOperationException("项目没有 Addressables 设置，无法验证角色头像引用。");

            SpriteAtlas matchedAtlas = null;
            int matchCount = 0;
            for (int groupIndex = 0; groupIndex < settings.groups.Count; groupIndex++)
            {
                AddressableAssetGroup group = settings.groups[groupIndex];
                if (group == null || !string.Equals(group.Name, SpriteAtlasGroupName, StringComparison.Ordinal)) continue;
                foreach (AddressableAssetEntry entry in group.entries)
                {
                    if (entry == null || !string.Equals(entry.address, atlasAddress, StringComparison.Ordinal)) continue;
                    SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(AssetDatabase.GUIDToAssetPath(entry.guid));
                    if (atlas == null) throw new InvalidOperationException($"{fieldName} Address“{atlasAddress}”不是有效 SpriteAtlas。");
                    matchedAtlas = atlas;
                    matchCount++;
                }
            }
            if (matchCount == 0) throw new InvalidOperationException($"{fieldName} Address“{atlasAddress}”未在 Addressables 分组“{SpriteAtlasGroupName}”中找到。");
            if (matchCount > 1) throw new InvalidOperationException($"{fieldName} Address“{atlasAddress}”存在多个 SpriteAtlas 条目。");
            if (matchedAtlas.GetSprite(spriteName) == null)
                throw new InvalidOperationException($"{fieldName} Sprite“{spriteName}”未在 Atlas“{atlasAddress}”中找到。");
        }

        /// <summary>检查 Prefab Address、Actor 数量和 Config 引用。</summary>
        private static void ValidatePrefab(CharacterConfig config)
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) throw new InvalidOperationException("项目没有 Addressables 设置。");
            AddressableAssetEntry entry = settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(FindAddressPath(settings, config.PrefabAddress)));
            if (entry == null || entry.parentGroup == null || !string.Equals(entry.parentGroup.Name, AddressableGroupName, StringComparison.Ordinal))
                throw new InvalidOperationException($"Prefab Address“{config.PrefabAddress}”不在 Addressables 分组“{AddressableGroupName}”中。");
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(entry.guid));
            if (prefab == null) throw new InvalidOperationException($"Prefab Address“{config.PrefabAddress}”不是有效 Prefab。");
            CharacterActor[] actors = prefab.GetComponentsInChildren<CharacterActor>(true);
            if (actors.Length != 1) throw new InvalidOperationException($"Prefab“{prefab.name}”必须包含唯一 CharacterActor。");
            if (actors[0].Config != config) throw new InvalidOperationException($"Prefab“{prefab.name}”的 CharacterActor.Config 未指向当前 CharacterConfig。");
        }

        /// <summary>按 Address 查找 Addressables 资产路径。</summary>
        private static string FindAddressPath(AddressableAssetSettings settings, string address)
        {
            foreach (AddressableAssetGroup group in settings.groups)
            {
                if (group == null) continue;
                foreach (AddressableAssetEntry entry in group.entries)
                    if (string.Equals(entry.address, address, StringComparison.Ordinal)) return AssetDatabase.GUIDToAssetPath(entry.guid);
            }
            throw new InvalidOperationException($"未找到 Addressables Address：{address}。");
        }

        /// <summary>解析 Atlas 在 UISpriteAtlas 分组中的 Address。</summary>
        private static string ResolveAtlasAddress(SpriteAtlas atlas)
        {
            string path = AssetDatabase.GetAssetPath(atlas);
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) throw new InvalidOperationException("项目没有 Addressables 设置。");
            AddressableAssetEntry entry = settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(path));
            if (entry == null || entry.parentGroup == null || !string.Equals(entry.parentGroup.Name, SpriteAtlasGroupName, StringComparison.Ordinal))
                throw new InvalidOperationException($"Sprite Atlas“{path}”不在 Addressables 分组“{SpriteAtlasGroupName}”中。");
            if (string.IsNullOrWhiteSpace(entry.address))
                throw new InvalidOperationException($"Sprite Atlas“{path}”的 Address 为空。");
            return entry.address;
        }

        #endregion
    }
}
#endif
