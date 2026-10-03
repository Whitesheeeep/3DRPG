#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RPG.CurrencySystemNS;
using RPG.RewardSystemNS;
using UnityEditor;
using UnityEngine;

namespace RPG.TaskSystemNS.Editor
{
    /// <summary>封装任务编辑器对任务资产、数据库和 AssetDatabase 的操作。</summary>
    internal sealed class TaskConfigEditorService
    {
        #region 常量

        private const string DatabaseSessionKey = "RPG.TaskConfig.DatabasePath";

        #endregion

        #region 依赖字段

        private readonly TaskConfigEditorSettings settings;

        /// <summary>创建资产服务并接入持久化编辑器设置。</summary>
        /// <param name="settings">后缀与编号设置。</param>
        internal TaskConfigEditorService(TaskConfigEditorSettings settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>解析上次使用或项目中唯一的 TaskDatabase。</summary>
        /// <returns>找到的数据库，存在多个候选且没有会话选择时返回 null。</returns>
        internal TaskDatabase ResolveDatabase()
        {
            string savedPath = SessionState.GetString(DatabaseSessionKey, string.Empty);
            TaskDatabase savedDatabase = AssetDatabase.LoadAssetAtPath<TaskDatabase>(savedPath);
            if (savedDatabase != null) return savedDatabase;
            string[] databaseGuids = AssetDatabase.FindAssets("t:TaskDatabase");
            TaskDatabase onlyDatabase = null;
            for (int index = 0; index < databaseGuids.Length; index++)
            {
                TaskDatabase candidate = LoadAtGuid<TaskDatabase>(databaseGuids[index]);
                if (candidate == null) continue;
                if (onlyDatabase != null) return null;
                onlyDatabase = candidate;
            }

            return onlyDatabase;
        }

        /// <summary>记住本窗口当前任务数据库。</summary>
        /// <param name="database">所选数据库。</param>
        internal void RememberDatabase(TaskDatabase database)
        {
            SessionState.SetString(DatabaseSessionKey, database == null ? string.Empty : AssetDatabase.GetAssetPath(database));
        }

        /// <summary>查询当前数据库、项目全部定义或未登记定义。</summary>
        /// <param name="database">当前数据库。</param>
        /// <param name="scope">列表数据范围。</param>
        /// <returns>去除空引用并按资产路径稳定排序的任务定义。</returns>
        internal List<TaskDefinitionEditorEntry> GetDefinitions(TaskDatabase database, string scope)
        {
            List<TaskDefinition> projectDefinitions = FindAllDefinitions();
            HashSet<TaskDefinition> registeredDefinitionSet = new(database == null ? Array.Empty<TaskDefinition>() : database.Definitions);
            IEnumerable<TaskDefinition> definitions = scope switch
            {
                "当前数据库" => registeredDefinitionSet.Where(item => item != null),
                "未加入当前数据库" => projectDefinitions.Where(item => !registeredDefinitionSet.Contains(item)),
                _ => projectDefinitions
            };

            return definitions
                .Select(definition => new TaskDefinitionEditorEntry(
                    definition,
                    registeredDefinitionSet.Contains(definition)))
                .ToList();
        }

        /// <summary>读取指定任务的原始 ID 与分类，供详情和命令日志安全展示。</summary>
        /// <param name="database">当前任务数据库。</param>
        /// <param name="definition">任务定义资产。</param>
        /// <returns>资产编辑器摘要。</returns>
        internal TaskDefinitionEditorEntry GetDefinitionEntry(TaskDatabase database, TaskDefinition definition)
        {
            if (definition == null) return null;
            return new TaskDefinitionEditorEntry(definition, IsRegistered(database, definition));
        }

        /// <summary>枚举项目中的所有任务定义资产。</summary>
        /// <returns>按资产路径排序的任务定义集合。</returns>
        internal List<TaskDefinition> FindAllDefinitions()
        {
            string[] definitionGuids = AssetDatabase.FindAssets("t:TaskDefinition");
            var definitions = new List<TaskDefinition>(definitionGuids.Length);
            for (int index = 0; index < definitionGuids.Length; index++)
            {
                TaskDefinition definition = LoadAtGuid<TaskDefinition>(definitionGuids[index]);
                if (definition != null) definitions.Add(definition);
            }

            return definitions.OrderBy(AssetDatabase.GetAssetPath, StringComparer.Ordinal).ToList();
        }

        /// <summary>判断任务资产是否已登记在指定数据库中。</summary>
        /// <param name="database">任务数据库。</param>
        /// <param name="definition">任务定义。</param>
        /// <returns>已登记时返回 true。</returns>
        internal bool IsRegistered(TaskDatabase database, TaskDefinition definition)
        {
            if (database == null || definition == null) return false;
            for (int index = 0; index < database.Definitions.Count; index++)
                if (database.Definitions[index] == definition) return true;
            return false;
        }

        #endregion

        #region 新建与复制

        /// <summary>创建带首阶段、待配置目标和摩拉奖励的新任务资产。</summary>
        /// <param name="database">要登记资产的任务数据库。</param>
        /// <param name="categoryId">所选分类。</param>
        /// <param name="suffixId">所选 TaskId 后缀，可为空。</param>
        /// <returns>已保存并登记的任务定义。</returns>
        internal TaskDefinition CreateDefinition(TaskDatabase database, string categoryId, string suffixId)
        {
            RequireDatabase(database);
            string taskId = AllocateId(categoryId, suffixId);
            TaskDefinition definition = ScriptableObject.CreateInstance<TaskDefinition>();
            definition.name = taskId;
            try
            {
                // 先在临时 ScriptableObject 上完成所有序列化字段，避免 AssetDatabase 扫描到空 ID 草稿。
                using var serializedDefinition = new SerializedObject(definition);
                serializedDefinition.FindProperty("taskId").stringValue = taskId;
                serializedDefinition.FindProperty("categoryId").stringValue = categoryId;
                serializedDefinition.FindProperty("title").stringValue = "新任务";
                serializedDefinition.FindProperty("description").stringValue = string.Empty;
                AddInitialStage(serializedDefinition);
                AddInitialMoraReward(serializedDefinition);
                serializedDefinition.ApplyModifiedPropertiesWithoutUndo();

                string folder = EnsureDefinitionFolder();
                string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{taskId}.asset");
                AssetDatabase.CreateAsset(definition, assetPath);
                EditorUtility.SetDirty(definition);
                AddToDatabase(database, definition);
                AssetDatabase.SaveAssets();
                Debug.Log($"[TaskConfigEditor] 已创建任务资产，taskId={taskId}，database={database.name}。", definition);
                return definition;
            }
            catch (Exception creationException)
            {
                // 回滚仅作用于本次创建的临时对象，不触碰项目中已有的无效任务资产。
                try
                {
                    if (IsRegistered(database, definition)) RemoveFromDatabase(database, definition);
                    if (AssetDatabase.Contains(definition))
                    {
                        string createdPath = AssetDatabase.GetAssetPath(definition);
                        if (!string.IsNullOrEmpty(createdPath) && !AssetDatabase.DeleteAsset(createdPath))
                            throw new IOException($"无法清理未完成任务资产：{createdPath}。");
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(definition);
                    }
                }
                catch (Exception rollbackException)
                {
                    throw new AggregateException("新建任务失败，且回滚未完成。", creationException, rollbackException);
                }

                throw;
            }
        }

        /// <summary>复制任务资产并为副本分配新的稳定 TaskId。</summary>
        /// <param name="database">要登记副本的任务数据库。</param>
        /// <param name="source">源任务定义。</param>
        /// <returns>复制后的任务定义。</returns>
        internal TaskDefinition DuplicateDefinition(TaskDatabase database, TaskDefinition source)
        {
            RequireDatabase(database);
            if (source == null) throw new ArgumentNullException(nameof(source));
            string sourcePath = AssetDatabase.GetAssetPath(source);
            string folder = EnsureDefinitionFolder();
            TaskDefinitionEditorEntry sourceEntry = GetDefinitionEntry(database, source);
            if (!sourceEntry.HasValidCategory)
                throw new InvalidOperationException($"无法复制任务，源资产分类无效：{sourceEntry.CategoryId}。");
            string taskId = AllocateId(sourceEntry.CategoryId,
                sourceEntry.HasValidTaskId ? ExtractSuffix(sourceEntry.TaskId, sourceEntry.CategoryId) : string.Empty);
            string destination = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{taskId}.asset");
            if (!AssetDatabase.CopyAsset(sourcePath, destination))
                throw new IOException($"无法复制任务资产：{sourcePath}。");

            TaskDefinition duplicate = AssetDatabase.LoadAssetAtPath<TaskDefinition>(destination);
            Undo.RecordObject(duplicate, "复制任务配置");
            var serializedDuplicate = new SerializedObject(duplicate);
            serializedDuplicate.FindProperty("taskId").stringValue = taskId;
            serializedDuplicate.FindProperty("title").stringValue = $"{source.Title} 副本";
            serializedDuplicate.ApplyModifiedProperties();
            AddToDatabase(database, duplicate);
            AssetDatabase.SaveAssets();
            Debug.Log($"[TaskConfigEditor] 已复制任务配置，source={sourceEntry.TaskId}，copy={taskId}。", duplicate);
            return duplicate;
        }

        #endregion

        #region 数据库登记与资产维护

        /// <summary>将定义加入数据库；已存在的引用不重复添加。</summary>
        /// <param name="database">目标数据库。</param>
        /// <param name="definition">任务定义。</param>
        internal void AddToDatabase(TaskDatabase database, TaskDefinition definition)
        {
            RequireDatabase(database);
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (IsRegistered(database, definition)) return;
            var serializedDatabase = new SerializedObject(database);
            SerializedProperty definitions = serializedDatabase.FindProperty("definitions");
            definitions.InsertArrayElementAtIndex(definitions.arraySize);
            definitions.GetArrayElementAtIndex(definitions.arraySize - 1).objectReferenceValue = definition;
            serializedDatabase.ApplyModifiedProperties();
            EditorUtility.SetDirty(database);
            Debug.Log($"[TaskConfigEditor] 已将任务加入数据库，taskId={GetRawTaskId(definition)}，database={database.name}。", database);
        }

        /// <summary>从数据库移除任务引用，但保留任务资产。</summary>
        /// <param name="database">目标数据库。</param>
        /// <param name="definition">任务定义。</param>
        internal void RemoveFromDatabase(TaskDatabase database, TaskDefinition definition)
        {
            if (database == null || definition == null) return;
            var serializedDatabase = new SerializedObject(database);
            SerializedProperty definitions = serializedDatabase.FindProperty("definitions");
            for (int index = definitions.arraySize - 1; index >= 0; index--)
            {
                if (definitions.GetArrayElementAtIndex(index).objectReferenceValue != definition) continue;
                definitions.DeleteArrayElementAtIndex(index);
                if (index < definitions.arraySize && definitions.GetArrayElementAtIndex(index).objectReferenceValue == null)
                    definitions.DeleteArrayElementAtIndex(index);
            }

            serializedDatabase.ApplyModifiedProperties();
            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssets();
            Debug.Log($"[TaskConfigEditor] 已从数据库移除任务引用，taskId={GetRawTaskId(definition)}，database={database.name}。", database);
        }

        /// <summary>同步任务标题和资产文件名。</summary>
        /// <param name="definition">任务定义。</param>
        /// <param name="newTitle">新任务标题。</param>
        /// <returns>重命名失败时返回 Unity 提供的错误消息。</returns>
        internal string RenameDefinition(TaskDefinition definition, string newTitle)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            string title = string.IsNullOrWhiteSpace(newTitle) ? definition.Title : newTitle.Trim();
            string path = AssetDatabase.GetAssetPath(definition);
            string fileName = SanitizeFileName(title);
            string renameError = AssetDatabase.RenameAsset(path, fileName);
            if (!string.IsNullOrEmpty(renameError)) return renameError;

            Undo.RecordObject(definition, "重命名任务");
            using var serializedDefinition = new SerializedObject(definition);
            serializedDefinition.FindProperty("title").stringValue = title;
            serializedDefinition.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log($"[TaskConfigEditor] 已重命名任务，taskId={GetRawTaskId(definition)}，title={title}。", definition);
            return string.Empty;
        }

        /// <summary>删除任务资产，并先清理它在当前数据库中的引用。</summary>
        /// <param name="definition">待删除任务。</param>
        internal void DeleteDefinition(TaskDefinition definition)
        {
            if (definition == null) return;
            string taskId = GetRawTaskId(definition);
            // 删除资产前清理所有数据库引用，避免其他数据库留下 Missing 引用。
            string[] databaseGuids = AssetDatabase.FindAssets("t:TaskDatabase");
            for (int index = 0; index < databaseGuids.Length; index++)
            {
                TaskDatabase registeredDatabase = LoadAtGuid<TaskDatabase>(databaseGuids[index]);
                if (registeredDatabase != null && IsRegistered(registeredDatabase, definition))
                    RemoveFromDatabase(registeredDatabase, definition);
            }

            string path = AssetDatabase.GetAssetPath(definition);
            if (!AssetDatabase.DeleteAsset(path)) throw new IOException($"无法删除任务资产：{path}。");
            Debug.Log($"[TaskConfigEditor] 已删除任务资产，taskId={taskId}。");
        }

        /// <summary>定位任务资产并在 Project 窗口中选中。</summary>
        /// <param name="definition">待定位任务。</param>
        internal void PingDefinition(TaskDefinition definition)
        {
            if (definition == null) return;
            EditorGUIUtility.PingObject(definition);
            Selection.activeObject = definition;
            Debug.Log($"[TaskConfigEditor] 已定位任务资产，taskId={GetRawTaskId(definition)}。", definition);
        }

        #endregion

        #region 校验与序号

        /// <summary>校验数据库并返回编辑器可读的结果消息。</summary>
        /// <param name="database">待验证数据库。</param>
        /// <returns>成功摘要或配置错误。</returns>
        internal string ValidateDatabase(TaskDatabase database)
        {
            if (database == null) return "请选择 TaskDatabase。";
            try
            {
                database.ValidateAndBuildIndex();
                return $"验证通过：{database.Definitions.Count} 个任务定义。";
            }
            catch (Exception exception)
            {
                Debug.LogError($"[TaskConfigEditor] 数据库校验失败，database={database.name}：{exception.Message}", database);
                return $"数据库校验失败：{exception.Message}";
            }
        }

        /// <summary>验证任务并检查其前置任务是否登记在当前数据库。</summary>
        /// <param name="database">当前数据库。</param>
        /// <param name="definition">待校验任务。</param>
        /// <returns>成功提示或具体配置错误。</returns>
        internal string ValidateDefinition(TaskDatabase database, TaskDefinition definition)
        {
            if (definition == null) return "请选择任务定义。";
            try
            {
                TaskDefinitionEditorEntry entry = GetDefinitionEntry(database, definition);
                if (!entry.HasValidTaskId || !entry.HasValidCategory)
                    return $"任务配置失败：{entry.ConfigurationIssue}。";

                definition.Validate();
                for (int index = 0; index < definition.UnlockConditions.Count; index++)
                {
                    if (definition.UnlockConditions[index] is not TaskPrerequisiteCompletedConditionDefinition prerequisite) continue;
                    bool existsInDatabase = database != null && database.Definitions.Any(item =>
                        item != null && string.Equals(GetRawTaskId(item), prerequisite.PrerequisiteTaskId.Value, StringComparison.Ordinal));
                    if (!existsInDatabase) return $"前置任务 {prerequisite.PrerequisiteTaskId} 未登记在当前数据库。";
                }

                return $"任务 {definition.TaskId} 配置通过。";
            }
            catch (Exception exception)
            {
                return $"任务校验失败：{exception.Message}";
            }
        }

        /// <summary>根据全部任务资产和后缀设置分配唯一 TaskId。</summary>
        /// <param name="categoryId">任务分类。</param>
        /// <param name="suffixId">稳定后缀，可为空。</param>
        /// <returns>本次预留并返回的 TaskId。</returns>
        private string AllocateId(string categoryId, string suffixId)
        {
            if (!TaskCategoryCatalog.IsDefined(categoryId))
                throw new ArgumentException($"任务分类未登记：{categoryId}。", nameof(categoryId));

            int number = settings.ReserveNextNumber(categoryId, FindHighestNumber(categoryId) + 1);
            string taskId = ComposeTaskId(categoryId, number, suffixId);
            HashSet<string> existingTaskIds = new(FindAllDefinitions().Select(GetRawTaskId), StringComparer.Ordinal);
            while (existingTaskIds.Contains(taskId))
            {
                number = settings.ReserveNextNumber(categoryId, number + 1);
                taskId = ComposeTaskId(categoryId, number, suffixId);
            }

            if (!TaskId.TryCreate(taskId, out _))
                throw new InvalidOperationException($"生成的任务 ID 无效：{taskId}。");

            return taskId;
        }

        /// <summary>扫描分类下已存在资产中的最高 TaskId 编号。</summary>
        /// <param name="categoryId">任务分类。</param>
        /// <returns>最高编号，没有已知编号时返回零。</returns>
        private int FindHighestNumber(string categoryId)
        {
            Regex taskIdPattern = new($"^{Regex.Escape(categoryId)}_(\\d{{3,}})(?:_.*)?$", RegexOptions.CultureInvariant);
            int highest = 0;
            List<TaskDefinition> definitions = FindAllDefinitions();
            for (int index = 0; index < definitions.Count; index++)
            {
                Match match = taskIdPattern.Match(GetRawTaskId(definitions[index]));
                if (match.Success && int.TryParse(match.Groups[1].Value, out int number)) highest = Math.Max(highest, number);
            }

            return highest;
        }

        /// <summary>从现有 TaskId 提取后缀以供复制任务使用。</summary>
        /// <param name="taskId">源 TaskId。</param>
        /// <returns>后缀文本；没有后缀时返回空字符串。</returns>
        private static string ExtractSuffix(string taskId, string categoryId)
        {
            Match match = Regex.Match(taskId, $"^{Regex.Escape(categoryId)}_\\d{{3,}}(?:_(.*))?$", RegexOptions.CultureInvariant);
            return match.Success && match.Groups[1].Success ? match.Groups[1].Value : string.Empty;
        }

        /// <summary>构造符合类别、三位以上数字和可选后缀格式的 TaskId。</summary>
        /// <param name="categoryId">分类稳定标识。</param>
        /// <param name="number">分类共享递增编号。</param>
        /// <param name="suffixId">稳定后缀。</param>
        /// <returns>新 TaskId。</returns>
        private static string ComposeTaskId(string categoryId, int number, string suffixId)
        {
            string taskId = $"{categoryId}_{number:000}";
            return string.IsNullOrEmpty(suffixId) ? taskId : $"{taskId}_{suffixId}";
        }

        /// <summary>读取序列化任务 ID，不通过会拒绝无效值的运行时值对象。</summary>
        /// <param name="definition">任务定义资产。</param>
        /// <returns>资产保存的原始 ID。</returns>
        internal static string GetRawTaskId(TaskDefinition definition)
        {
            if (definition == null) return string.Empty;
            using var serializedDefinition = new SerializedObject(definition);
            serializedDefinition.UpdateIfRequiredOrScript();
            return serializedDefinition.FindProperty("taskId").stringValue;
        }

        #endregion

        #region 初始配置与路径辅助

        /// <summary>为新定义创建一个等待用户配置目标的首阶段。</summary>
        /// <param name="serializedDefinition">新任务序列化对象。</param>
        private static void AddInitialStage(SerializedObject serializedDefinition)
        {
            SerializedProperty stages = serializedDefinition.FindProperty("stages");
            stages.InsertArrayElementAtIndex(0);
            SerializedProperty stage = stages.GetArrayElementAtIndex(0);
            stage.FindPropertyRelative("stageId").stringValue = "stage_001";
            stage.FindPropertyRelative("title").stringValue = "第一阶段";
            stage.FindPropertyRelative("description").stringValue = string.Empty;
            stage.FindPropertyRelative("objectives").ClearArray();
        }

        /// <summary>为新任务设置一项可编辑的 1 摩拉奖励。</summary>
        /// <param name="serializedDefinition">新任务序列化对象。</param>
        private static void AddInitialMoraReward(SerializedObject serializedDefinition)
        {
            SerializedProperty rewards = serializedDefinition.FindProperty("rewards");
            rewards.InsertArrayElementAtIndex(0);
            rewards.GetArrayElementAtIndex(0).managedReferenceValue = new CurrencyRewardDefinition();
            serializedDefinition.ApplyModifiedPropertiesWithoutUndo();

            SerializedProperty currencyAmounts = rewards.GetArrayElementAtIndex(0).FindPropertyRelative("amounts");
            currencyAmounts.InsertArrayElementAtIndex(0);
            SerializedProperty entry = currencyAmounts.GetArrayElementAtIndex(0);
            SerializedProperty currencyId = entry.FindPropertyRelative("currencyId");
            for (int index = 0; index < currencyId.enumNames.Length; index++)
                if (currencyId.enumNames[index] == nameof(CurrencyId.Mola)) currencyId.enumValueIndex = index;
            entry.FindPropertyRelative("amount").intValue = 1;
        }

        /// <summary>创建并确认新任务资产目录存在。</summary>
        /// <returns>Assets 下的目标目录。</returns>
        /// <exception cref="IOException">无法通过 AssetDatabase 创建目录时抛出。</exception>
        private string EnsureDefinitionFolder()
        {
            string folder = settings.DefinitionFolder;
            if (folder != "Assets" && !folder.StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidOperationException("任务资产目录必须位于 Assets 下。");

            // 用 AssetDatabase 逐级登记缺少的目录，确保后续 CreateAsset 能立即解析目标路径。
            string[] folderSegments = folder.Split('/');
            string parentFolder = "Assets";
            for (int index = 1; index < folderSegments.Length; index++)
            {
                string childFolder = $"{parentFolder}/{folderSegments[index]}";
                if (!AssetDatabase.IsValidFolder(childFolder) &&
                    string.IsNullOrEmpty(AssetDatabase.CreateFolder(parentFolder, folderSegments[index])))
                {
                    throw new IOException($"无法创建任务资产目录：{childFolder}。");
                }

                parentFolder = childFolder;
            }

            return folder;
        }

        /// <summary>确保创建或复制操作已有数据库目标。</summary>
        /// <param name="database">目标数据库。</param>
        private static void RequireDatabase(TaskDatabase database)
        {
            if (database == null) throw new InvalidOperationException("请选择 TaskDatabase 后再创建或复制任务。");
        }

        /// <summary>通过资产 GUID 载入指定类型的资产。</summary>
        /// <typeparam name="TAsset">Unity 资产类型。</typeparam>
        /// <param name="assetGuid">资产 GUID。</param>
        /// <returns>已载入的资产或 null。</returns>
        private static TAsset LoadAtGuid<TAsset>(string assetGuid) where TAsset : UnityEngine.Object
        {
            return AssetDatabase.LoadAssetAtPath<TAsset>(AssetDatabase.GUIDToAssetPath(assetGuid));
        }

        /// <summary>将标题变为可用于任务资产文件名的安全文本。</summary>
        /// <param name="value">任务标题。</param>
        /// <returns>不包含文件系统非法字符的文件名。</returns>
        private static string SanitizeFileName(string value)
        {
            char[] invalidCharacters = Path.GetInvalidFileNameChars();
            string sanitized = new(value.Select(character => invalidCharacters.Contains(character) ? '_' : character).ToArray());
            return string.IsNullOrWhiteSpace(sanitized) ? "TaskDefinition" : sanitized.Trim();
        }

        #endregion
    }
}
#endif
