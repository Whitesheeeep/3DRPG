#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using WS_Modules.GAS.GameplayCue;
using WS_Modules.GAS.TAG;
using WS_Modules.Pooling;

namespace WS_Modules.GAS.Editor
{
    /// <summary>集中处理 Cue 资产、Database 列表、Undo 和校验的编辑器服务。</summary>
    public sealed class GameplayCueEditorService
    {
        #region 资产操作

        /// <summary>扫描项目中的 Cue Database 资产。</summary>
        /// <returns>按资产路径排序的数据库列表。</returns>
        public List<GameplayCueDatabase> FindAllDatabases()
        {
            var databases = new List<GameplayCueDatabase>();
            foreach (string guid in AssetDatabase.FindAssets("t:GameplayCueDatabase"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameplayCueDatabase database = AssetDatabase.LoadAssetAtPath<GameplayCueDatabase>(path);
                if (database != null) databases.Add(database);
            }

            databases.Sort((left, right) => string.Compare(
                AssetDatabase.GetAssetPath(left),
                AssetDatabase.GetAssetPath(right),
                StringComparison.Ordinal));
            return databases;
        }

        /// <summary>扫描项目中注册了指定 CueData 的 Database。</summary>
        /// <param name="cue">待查找的 CueData。</param>
        /// <returns>包含该 CueData 的数据库列表。</returns>
        public List<GameplayCueDatabase> FindDatabasesContaining(GameplayCueData cue)
        {
            var result = new List<GameplayCueDatabase>();
            if (cue == null) return result;

            foreach (GameplayCueDatabase database in FindAllDatabases())
            {
                IReadOnlyList<GameplayCueData> cues = database.Cues ?? Array.Empty<GameplayCueData>();
                for (int i = 0; i < cues.Count; i++)
                {
                    if (!ReferenceEquals(cues[i], cue)) continue;
                    result.Add(database);
                    break;
                }
            }

            return result;
        }

        /// <summary>创建新的 Cue Database 资产。</summary>
        /// <param name="assetPath">项目内目标路径。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>创建的数据库，失败时返回空。</returns>
        public GameplayCueDatabase CreateDatabase(string assetPath, out string error)
        {
            if (!IsProjectAssetPath(assetPath))
            {
                error = "Cue Database 必须创建在 Assets 目录下。";
                return null;
            }

            string uniquePath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
            GameplayCueDatabase database = ScriptableObject.CreateInstance<GameplayCueDatabase>();
            AssetDatabase.CreateAsset(database, uniquePath);
            Undo.RegisterCreatedObjectUndo(database, "创建 Gameplay Cue Database");
            AssetDatabase.SaveAssets();
            Selection.activeObject = database;
            error = string.Empty;
            return database;
        }

        /// <summary>创建 CueData 并自动注册到当前 Database。</summary>
        /// <param name="database">目标数据库。</param>
        /// <param name="assetPath">项目内目标路径。</param>
        /// <param name="cue">创建的 CueData。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>创建成功时返回 true。</returns>
        public bool TryCreateCue(
            GameplayCueDatabase database,
            string assetPath,
            out GameplayCueData cue,
            out string error)
        {
            cue = null;
            if (database == null)
            {
                error = "请先选择 Gameplay Cue Database。";
                return false;
            }

            if (!IsProjectAssetPath(assetPath))
            {
                error = "GameplayCueData 必须创建在 Assets 目录下。";
                return false;
            }

            string uniquePath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
            GameplayCueData createdCue = ScriptableObject.CreateInstance<GameplayCueData>();
            cue = createdCue;
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("创建 Gameplay Cue");
            AssetDatabase.CreateAsset(createdCue, uniquePath);
            Undo.RegisterCreatedObjectUndo(createdCue, "创建 Gameplay Cue");

            if (!TryModifyDatabase(
                    database,
                    "注册 Gameplay Cue",
                    property => AppendCue(property, createdCue),
                    out error))
            {
                AssetDatabase.DeleteAsset(uniquePath);
                cue = null;
                return false;
            }

            Undo.CollapseUndoOperations(undoGroup);
            AssetDatabase.SaveAssets();
            Selection.activeObject = cue;
            error = string.Empty;
            return true;
        }

        /// <summary>将已有 CueData 添加到当前 Database。</summary>
        /// <param name="database">目标数据库。</param>
        /// <param name="cue">待添加 CueData。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>添加成功时返回 true。</returns>
        public bool TryAddCue(
            GameplayCueDatabase database,
            GameplayCueData cue,
            out string error)
        {
            if (database == null || cue == null)
            {
                error = "Database 和 CueData 都不能为空。";
                return false;
            }

            IReadOnlyList<GameplayCueData> cues = database.Cues ?? Array.Empty<GameplayCueData>();
            for (int i = 0; i < cues.Count; i++)
            {
                if (ReferenceEquals(cues[i], cue))
                {
                    error = "该 CueData 已经注册在当前 Database 中。";
                    return false;
                }
            }

            return TryModifyDatabase(
                database,
                "添加 Gameplay Cue",
                property => AppendCue(property, cue),
                out error);
        }

        /// <summary>仅从 Database 中移除 CueData，不删除资产。</summary>
        /// <param name="database">目标数据库。</param>
        /// <param name="cue">待移除 CueData。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>移除成功时返回 true。</returns>
        public bool TryRemoveCue(
            GameplayCueDatabase database,
            GameplayCueData cue,
            out string error)
        {
            if (!ContainsCue(database, cue))
            {
                error = "指定的 CueData 不在当前 Database 中。";
                return false;
            }

            return TryModifyDatabase(
                database,
                "从 Database 移除 Gameplay Cue",
                property => RemoveCue(property, cue),
                out error);
        }

        /// <summary>从 Database 移除 CueData 后将资产移入回收站。</summary>
        /// <param name="database">目标数据库。</param>
        /// <param name="cue">待删除 CueData。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>删除成功时返回 true。</returns>
        public bool TryDeleteCue(
            GameplayCueDatabase database,
            GameplayCueData cue,
            out string error)
        {
            if (!TryRemoveCue(database, cue, out error)) return false;
            string path = AssetDatabase.GetAssetPath(cue);
            if (string.IsNullOrEmpty(path) || !AssetDatabase.MoveAssetToTrash(path))
            {
                error = $"无法将 CueData 移入回收站：{path}";
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>通过 AssetDatabase 重命名 CueData，并保留 GUID。</summary>
        /// <param name="cue">待重命名 CueData。</param>
        /// <param name="newName">新名称。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>重命名成功时返回 true。</returns>
        public bool TryRenameCue(GameplayCueData cue, string newName, out string error)
        {
            if (cue == null)
            {
                error = "未指定要重命名的 CueData。";
                return false;
            }

            string trimmed = newName?.Trim() ?? string.Empty;
            if (trimmed.Length == 0)
            {
                error = "CueData 名称不能为空。";
                return false;
            }

            if (string.Equals(cue.name, trimmed, StringComparison.Ordinal))
            {
                error = string.Empty;
                return true;
            }

            string path = AssetDatabase.GetAssetPath(cue);
            if (string.IsNullOrEmpty(path))
            {
                error = "CueData 不是可重命名的项目资产。";
                return false;
            }

            error = AssetDatabase.RenameAsset(path, trimmed);
            return string.IsNullOrEmpty(error);
        }

        /// <summary>复制 CueData 并将副本注册到当前 Database。</summary>
        /// <param name="database">目标数据库。</param>
        /// <param name="source">原始 CueData。</param>
        /// <param name="copy">复制出的 CueData。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>复制成功时返回 true。</returns>
        public bool TryDuplicateCue(
            GameplayCueDatabase database,
            GameplayCueData source,
            out GameplayCueData copy,
            out string error)
        {
            copy = null;
            if (database == null || source == null)
            {
                error = "Database 和源 CueData 都不能为空。";
                return false;
            }

            string sourcePath = AssetDatabase.GetAssetPath(source);
            string directory = Path.GetDirectoryName(sourcePath)?.Replace('\\', '/');
            string copyPath = AssetDatabase.GenerateUniqueAssetPath($"{directory}/{source.name} Copy.asset");
            if (!AssetDatabase.CopyAsset(sourcePath, copyPath))
            {
                error = $"无法复制 CueData：{sourcePath}";
                return false;
            }

            copy = AssetDatabase.LoadAssetAtPath<GameplayCueData>(copyPath);
            if (copy == null)
            {
                AssetDatabase.DeleteAsset(copyPath);
                copy = null;
                error = "复制后的 CueData 无法重新加载。";
                return false;
            }

            if (!TryAddCue(database, copy, out error))
            {
                AssetDatabase.DeleteAsset(copyPath);
                copy = null;
                return false;
            }

            Undo.RegisterCreatedObjectUndo(copy, "复制 Gameplay Cue");
            AssetDatabase.SaveAssets();
            error = string.Empty;
            return true;
        }

        /// <summary>在 Project 窗口定位 CueData。</summary>
        /// <param name="cue">目标 CueData。</param>
        public void PingCue(GameplayCueData cue)
        {
            if (cue != null) EditorGUIUtility.PingObject(cue);
        }

        /// <summary>在 Project 窗口定位 Fallback Prefab。</summary>
        /// <param name="cue">目标 CueData。</param>
        public void PingPrefab(GameplayCueData cue)
        {
            if (cue == null) return;

            try
            {
                GameObject fallbackPrefab = cue.FallbackPrefab;
                if (fallbackPrefab != null) EditorGUIUtility.PingObject(fallbackPrefab);
            }
            catch (MissingReferenceException)
            {
                Debug.LogWarning($"GameplayCueData '{cue.name}' 的 Fallback Prefab 引用已失效，请重新指定或清空该字段。", cue);
            }
        }

        #endregion

        #region 列表与校验

        /// <summary>根据搜索文本取得当前数据库的 Cue 引用列表。</summary>
        /// <param name="database">当前数据库。</param>
        /// <param name="search">搜索文本。</param>
        /// <returns>可见 Cue 引用列表。</returns>
        public List<GameplayCueData> FindVisibleCues(GameplayCueDatabase database, string search)
        {
            var result = new List<GameplayCueData>();
            if (database == null) return result;

            string filter = search?.Trim() ?? string.Empty;
            IReadOnlyList<GameplayCueData> cues = database.Cues ?? Array.Empty<GameplayCueData>();
            for (int i = 0; i < cues.Count; i++)
            {
                GameplayCueData cue = cues[i];
                if (cue == null) continue;
                if (filter.Length == 0 ||
                    cue.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    cue.CueTag.ToString().IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    result.Add(cue);
            }

            return result;
        }

        /// <summary>校验当前 Database 和其中的所有 CueData。</summary>
        /// <param name="database">待校验数据库。</param>
        /// <returns>按数据库顺序返回校验问题。</returns>
        public List<GameplayCueValidationIssue> Validate(GameplayCueDatabase database)
        {
            var issues = new List<GameplayCueValidationIssue>();
            if (database == null)
            {
                issues.Add(new GameplayCueValidationIssue(
                    GameplayCueValidationSeverity.Info,
                    null,
                    "尚未选择 Gameplay Cue Database。"));
                return issues;
            }

            var registered = new HashSet<GameplayCueData>();
            var tags = new HashSet<GameplayTag>();
            IReadOnlyList<GameplayCueData> cues = database.Cues ?? Array.Empty<GameplayCueData>();
            for (int i = 0; i < cues.Count; i++)
            {
                GameplayCueData cue = cues[i];
                if (cue == null)
                {
                    issues.Add(new GameplayCueValidationIssue(
                        GameplayCueValidationSeverity.Error,
                        null,
                        $"Database 的 Cues[{i}] 为空。"));
                    continue;
                }

                if (!registered.Add(cue))
                    issues.Add(Error(cue, "同一个 CueData 被重复注册。"));
                if (!cue.CueTag.IsValid)
                    issues.Add(Error(cue, "CueTag 无效，请通过 Tag PropertyDrawer 选择已烘焙标签。"));
                else if (!tags.Add(cue.CueTag))
                    issues.Add(Error(cue, $"当前 Database 中存在重复 CueTag：{cue.CueTag}。"));

                ValidateCue(cue, issues);
            }

            return issues;
        }

        #endregion

        #region 内部辅助

        /// <summary>校验 CueData 的资源边界和可回收表现组件，不加载 Addressable 或生成对象。</summary>
        /// <param name="cue">待校验的 CueData。</param>
        /// <param name="issues">接收校验问题的集合。</param>
        private static void ValidateCue(
            GameplayCueData cue,
            ICollection<GameplayCueValidationIssue> issues)
        {
            bool hasFallbackPrefab = false;
            bool hasBehaviour = false;
            bool hasPoolIdentity = false;
            bool poolKeyMissing = false;
            bool poolKeyMismatch = false;
            bool invalidPrefabReference = false;
            try
            {
                GameObject fallbackPrefab = cue.FallbackPrefab;
                hasFallbackPrefab = fallbackPrefab != null;
                if (hasFallbackPrefab)
                {
                    AssetDatabase.GetAssetPath(fallbackPrefab);
                    hasBehaviour = fallbackPrefab.TryGetComponent<GameplayCueBehaviour>(out _);
                    IGameObjectPoolable poolable = fallbackPrefab.GetComponent<IGameObjectPoolable>();
                    hasPoolIdentity = poolable != null;
                    poolKeyMissing = hasPoolIdentity && string.IsNullOrWhiteSpace(poolable.Key);
                    poolKeyMismatch = hasPoolIdentity &&
                                      !poolKeyMissing &&
                                      !string.IsNullOrWhiteSpace(cue.AddressableKey) &&
                                      poolable.Key != cue.AddressableKey;
                }
            }
            catch (MissingReferenceException)
            {
                invalidPrefabReference = true;
                issues.Add(Error(cue, "Fallback Prefab 引用已失效，请重新指定或清空该字段。"));
            }

            if (string.IsNullOrWhiteSpace(cue.AddressableKey) && !hasFallbackPrefab && !invalidPrefabReference)
                issues.Add(Error(cue, "必须配置 Addressable Key 或 Fallback Prefab。"));

            if (!invalidPrefabReference && hasFallbackPrefab && !hasBehaviour)
                issues.Add(Error(cue, "Fallback Prefab 缺少 GameplayCueBehaviour。"));

            if (!invalidPrefabReference && hasFallbackPrefab && !hasPoolIdentity)
                issues.Add(Error(cue, "Fallback Prefab 必须实现 IGameObjectPoolable。"));
            else if (!invalidPrefabReference && hasFallbackPrefab && poolKeyMissing)
                issues.Add(Error(cue, "Fallback Prefab 的 IGameObjectPoolable.Key 不能为空。"));
            else if (!invalidPrefabReference && hasFallbackPrefab && poolKeyMismatch)
                issues.Add(Error(
                    cue,
                    $"Fallback Prefab Key 与 Addressable Key '{cue.AddressableKey}' 不一致。"));

            if (ContainsInvalid(cue.LocalPosition) || ContainsInvalid(cue.LocalRotation.eulerAngles))
                issues.Add(Error(cue, "位置或旋转偏移包含 NaN/Infinity。"));

            if (!string.IsNullOrWhiteSpace(cue.AddressableKey) && !hasFallbackPrefab && !invalidPrefabReference)
                issues.Add(new GameplayCueValidationIssue(
                    GameplayCueValidationSeverity.Info,
                    cue,
                    "仅配置 Addressable Key，编辑器不会验证远端资源是否存在。"));
        }

        // 统一创建数据库校验错误，保证问题都能定位到 CueData。
        private static GameplayCueValidationIssue Error(GameplayCueData cue, string message) =>
            new(GameplayCueValidationSeverity.Error, cue, message);

        // 通过 SerializedObject 修改 Database，避免直接访问其私有作者字段。
        private static bool TryModifyDatabase(
            GameplayCueDatabase database,
            string undoName,
            Action<SerializedProperty> modification,
            out string error)
        {
            error = string.Empty;
            if (database == null)
            {
                error = "未选择 Gameplay Cue Database。";
                return false;
            }

            SerializedObject serializedObject = new SerializedObject(database);
            SerializedProperty cues = serializedObject.FindProperty("cues");
            if (cues == null || !cues.isArray)
            {
                error = "GameplayCueDatabase 的 cues 序列化字段不存在。";
                return false;
            }

            Undo.RecordObject(database, undoName);
            modification(cues);
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssets();
            return true;
        }

        // 在数组末尾追加对象引用。
        private static void AppendCue(SerializedProperty cues, GameplayCueData cue)
        {
            int index = cues.arraySize;
            cues.InsertArrayElementAtIndex(index);
            cues.GetArrayElementAtIndex(index).objectReferenceValue = cue;
        }

        // 从数组中移除第一个匹配的对象引用。
        private static void RemoveCue(SerializedProperty cues, GameplayCueData cue)
        {
            for (int i = 0; i < cues.arraySize; i++)
            {
                if (!ReferenceEquals(cues.GetArrayElementAtIndex(i).objectReferenceValue, cue)) continue;
                cues.DeleteArrayElementAtIndex(i);
                return;
            }
        }

        // 判断对象引用是否已经注册，避免对无效移除请求创建空 Undo 记录。
        private static bool ContainsCue(GameplayCueDatabase database, GameplayCueData cue)
        {
            if (database == null || cue == null) return false;
            IReadOnlyList<GameplayCueData> cues = database.Cues ?? Array.Empty<GameplayCueData>();
            for (int i = 0; i < cues.Count; i++)
                if (ReferenceEquals(cues[i], cue)) return true;
            return false;
        }

        // 检查是否为项目内有效资产路径。
        private static bool IsProjectAssetPath(string path) =>
            !string.IsNullOrWhiteSpace(path) &&
            path.StartsWith("Assets/", StringComparison.Ordinal) &&
            !Path.IsPathRooted(path);

        // 检查向量的每个分量是否为有限值。
        private static bool ContainsInvalid(Vector3 value) =>
            float.IsNaN(value.x) || float.IsInfinity(value.x) ||
            float.IsNaN(value.y) || float.IsInfinity(value.y) ||
            float.IsNaN(value.z) || float.IsInfinity(value.z);

        #endregion
    }
}
#endif
