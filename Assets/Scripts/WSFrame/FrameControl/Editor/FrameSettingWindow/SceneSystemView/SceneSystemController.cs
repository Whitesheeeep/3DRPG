using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.LogModule;
using WS_Modules.SceneModule;

namespace WS_Modules
{
    /// <summary>管理场景数据库编辑状态，并通过 SerializedObject 集中执行可撤销结构操作。</summary>
    internal sealed class SceneSystemController
    {
        #region Controller 字段

        // 偏好键常量用于恢复用户上次编排的数据库和任务资产目录。
        private const string DatabasePreferenceKey = "WSFrame.SceneSystem.DatabasePath";

        // 服务依赖：资产生命周期与引用分析独立于当前数据库的结构编辑。
        private readonly SceneLoadAssetService assetService = new();
        private SceneLoadDatabase database;

        #endregion

        #region 查询

        // 当前面板数据库和资产保存目录。
        /// <summary>获取当前正在编排的配置数据库。</summary>
        public SceneLoadDatabase Database => database;
        /// <summary>获取新建配置和任务资产使用的目录。</summary>
        public string AssetFolder => assetService.AssetFolder;

        /// <summary>设置当前数据库并记住其项目路径。</summary>
        /// <param name="value">要编辑的数据库资产。</param>
        public void SetDatabase(SceneLoadDatabase value)
        {
            database = value;
            EditorPrefs.SetString(DatabasePreferenceKey,
                database == null ? string.Empty : AssetDatabase.GetAssetPath(database));
        }

        /// <summary>从 Editor 偏好恢复数据库和资产目录；不存在时选择项目内首个数据库。</summary>
        public void RestoreEditorSettings()
        {
            assetService.AssetFolder = SceneLoadingEditorSettings.instance.NodeAssetFolder;
            string rememberedPath = EditorPrefs.GetString(DatabasePreferenceKey, string.Empty);
            database = string.IsNullOrEmpty(rememberedPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<SceneLoadDatabase>(rememberedPath);
            if (database != null) return;

            string[] databaseGuids = AssetDatabase.FindAssets("t:SceneLoadDatabase");
            if (databaseGuids.Length > 0)
                SetDatabase(AssetDatabase.LoadAssetAtPath<SceneLoadDatabase>(AssetDatabase.GUIDToAssetPath(databaseGuids[0])));
        }

        /// <summary>验证项目 Assets 内的目录，并在设置值变化时同步、保存资产创建路径。</summary>
        /// <param name="folder">项目相对目录。</param>
        public void SetAssetFolder(string folder)
        {
            string normalizedFolder = assetService.NormalizeAssetFolder(folder);
            bool folderChanged = !string.Equals(assetService.AssetFolder, normalizedFolder,
                StringComparison.Ordinal);
            assetService.AssetFolder = normalizedFolder;

            SceneLoadingEditorSettings settings = SceneLoadingEditorSettings.instance;
            if (folderChanged || !string.Equals(settings.NodeAssetFolder, normalizedFolder,
                    StringComparison.Ordinal))
                settings.SetNodeAssetFolder(normalizedFolder);
            if (folderChanged)
                WSLog.Log($"[SceneSystemController] 新建资产目录已同步并保存，folder={normalizedFolder}。");
        }

        /// <summary>从项目编辑器设置同步资产服务目录；仅实际发生变化时写回设置。</summary>
        public void SynchronizeAssetFolderFromEditorSettings()
        {
            SetAssetFolder(SceneLoadingEditorSettings.instance.NodeAssetFolder);
        }

        /// <summary>从当前数据库的场景引用同步自动生成的 SceneName。</summary>
        /// <returns>至少一份配置的场景名称发生变化时返回 true。</returns>
        public bool SynchronizeDatabaseSceneNames()
        {
            if (database == null) return false;

            bool changed = false;
            foreach (SceneLoadConfig config in database.SceneConfigs)
                if (config != null && config.SynchronizeSceneNameFromReference())
                    changed = true;

            if (!changed) return false;
            AssetDatabase.SaveAssets();
            WSLog.Log($"[SceneSystemController] 已同步数据库场景名称，sceneCount={database.SceneConfigs.Count}。");
            return true;
        }

        /// <summary>获取编辑器可创建的组合任务和叶子任务类型。</summary>
        /// <returns>可实例化的任务类型。</returns>
        public IReadOnlyList<Type> GetCreatableTaskTypes() => assetService.GetCreatableTaskTypes();

        // 树和详情查询只读取配置资产；刷新或校验不会修改数据。
        /// <summary>按当前搜索词生成树数据，同时为每个引用位置分配独立行 ID。</summary>
        /// <param name="searchText">场景、节点名称或任务类型搜索词。</param>
        /// <returns>TreeView 根节点快照。</returns>
        public List<TreeViewItemData<SceneLoadTreeItem>> BuildTreeItems(string searchText)
        {
            var roots = new List<TreeViewItemData<SceneLoadTreeItem>>();
            if (database == null) return roots;

            int nextId = 1;
            string normalizedSearch = (searchText ?? string.Empty).Trim();
            for (int configIndex = 0; configIndex < database.SceneConfigs.Count; configIndex++)
            {
                SceneLoadConfig config = database.SceneConfigs[configIndex];
                if (config == null) continue;
                string configPath = $"{config.SceneId}/{config.DisplayName}[{configIndex}]";
                var childItems = new List<TreeViewItemData<SceneLoadTreeItem>>();
                bool configMatches = MatchesSearch(config.SceneId, normalizedSearch) ||
                                     MatchesSearch(config.DisplayName, normalizedSearch) ||
                                     MatchesSearch(config.SceneName, normalizedSearch);
                if (config.RootTask != null && TryBuildTaskItem(
                        config.RootTask, null, config, configIndex, 0, configPath, 1,
                        normalizedSearch, configMatches, new HashSet<SceneLoadTask>(), ref nextId,
                        out TreeViewItemData<SceneLoadTreeItem> rootTaskItem))
                    childItems.Add(rootTaskItem);
                if (!configMatches && normalizedSearch.Length > 0 && childItems.Count == 0) continue;

                var configItem = new SceneLoadTreeItem(nextId++, true, config, null, null, configIndex, configIndex,
                    configPath, 0, "SCENE");
                roots.Add(new TreeViewItemData<SceneLoadTreeItem>(configItem.Id, configItem, childItems));
            }
            return roots;
        }

        /// <summary>读取当前数据库任务树中对任务资产的引用位置，不扫描项目其他资产。</summary>
        /// <param name="task">待检查任务资产。</param>
        /// <returns>当前数据库内的引用路径和外部检查范围说明。</returns>
        public string GetReferenceSummary(SceneLoadTask task)
        {
            var references = new List<string>();
            foreach (TreeViewItemData<SceneLoadTreeItem> configItem in BuildTreeItems(string.Empty))
                foreach (TreeViewItemData<SceneLoadTreeItem> taskItem in configItem.children)
                    CollectTaskReferencePaths(taskItem, task, references);

            if (references.Count == 0)
                return "当前配置库中没有此任务的引用位置；其他数据库、场景与 Prefab 未扫描。";
            int displayCount = Math.Min(references.Count, 5);
            string summary = $"当前配置库中有 {references.Count} 个引用位置：\n";
            for (int index = 0; index < displayCount; index++) summary += $"• {references[index]}\n";
            if (references.Count > displayCount) summary += $"…以及另外 {references.Count - displayCount} 个位置";
            return summary + "\n其他数据库、场景对象与 Prefab 未扫描。";
        }

        /// <summary>统计当前配置数据库中对场景配置资产的直接引用数。</summary>
        /// <param name="config">待统计场景配置。</param>
        /// <returns>当前数据库中的引用次数。</returns>
        public int GetConfigReferenceCount(SceneLoadConfig config) => CountConfigReferencesInCurrentDatabase(config);

        /// <summary>生成按真实组合顺序展开的执行预览。</summary>
        /// <param name="config">待预览场景配置。</param>
        /// <returns>包含 Sequence 顺序和 Parallel 分支标记的文本。</returns>
        public string GetExecutionPreview(SceneLoadConfig config)
        {
            if (config == null || config.RootTask == null) return "根任务尚未设置。";
            var lines = new List<string>();
            AppendExecutionLines(config.RootTask, 0, "Root", new HashSet<SceneLoadTask>(), lines);
            return string.Join("\n", lines);
        }

        /// <summary>校验当前配置并返回可供详情面板展示的完整诊断。</summary>
        /// <param name="config">待校验场景配置。</param>
        /// <returns>校验结果。</returns>
        public SceneLoadValidationResult Validate(SceneLoadConfig config) =>
            SceneLoadValidator.Validate(config, database: database);

        #endregion

        #region 数据库配置 CRUD

        // 数据库创建入口。
        /// <summary>创建数据库资产并设为当前面板数据库。</summary>
        /// <returns>新建数据库。</returns>
        public SceneLoadDatabase CreateDatabase()
        {
            string folder = assetService.AssetFolder;
            assetService.EnsureFolder(folder);
            SceneLoadDatabase createdDatabase = assetService.CreateAsset<SceneLoadDatabase>(
                folder, "SceneLoadDatabase");
            SetDatabase(createdDatabase);
            AssetDatabase.SaveAssets();
            WSLog.Log($"[SceneSystemController] 已创建场景加载数据库，assetPath={AssetDatabase.GetAssetPath(createdDatabase)}。");
            return createdDatabase;
        }

        // 配置创建、加入、复制和当前库引用解除。
        /// <summary>创建一份带空 Sequence 根节点的新场景配置，并加入当前数据库。</summary>
        /// <returns>新建配置。</returns>
        public SceneLoadConfig CreateConfig()
        {
            EnsureDatabase();
            SceneLoadConfig config = assetService.CreateAsset<SceneLoadConfig>(assetService.AssetFolder, "SceneLoadConfig");
            SceneLoadTask root = assetService.CreateTaskAsset(typeof(SequenceSceneLoadTask), "SequenceSceneLoadTask");
            var serializedConfig = new SerializedObject(config);
            serializedConfig.FindProperty("rootTask").objectReferenceValue = root;
            serializedConfig.FindProperty("displayName").stringValue = "新场景配置";
            serializedConfig.ApplyModifiedPropertiesWithoutUndo();
            string configGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(config));
            serializedConfig.FindProperty("sceneId").stringValue = "scene." + configGuid.Substring(0, Math.Min(8, configGuid.Length));
            serializedConfig.ApplyModifiedPropertiesWithoutUndo();
            AddConfigReference(config);
            SaveStructure();
            WSLog.Log($"[SceneSystemController] 已创建场景配置，sceneId={config.SceneId}。");
            return config;
        }

        /// <summary>把已有场景配置加入数据库；重复引用不会插入第二次。</summary>
        /// <param name="config">要加入的配置资产。</param>
        public void AddExistingConfig(SceneLoadConfig config)
        {
            EnsureDatabase();
            if (config == null) throw new ArgumentNullException(nameof(config));
            foreach (SceneLoadConfig existing in database.SceneConfigs)
                if (ReferenceEquals(existing, config)) return;
            AddConfigReference(config);
            SaveStructure();
            WSLog.Log($"[SceneSystemController] 已加入场景配置，sceneId={config.SceneId}。");
        }

        /// <summary>複製配置和任务树，并把独立副本加入当前数据库。</summary>
        /// <param name="source">复制来源。</param>
        /// <returns>新的独立配置。</returns>
        public SceneLoadConfig CopyConfig(SceneLoadConfig source)
        {
            EnsureDatabase();
            SceneLoadConfig copy = assetService.CloneConfig(source);
            AddConfigReference(copy);
            SaveStructure();
            WSLog.Log($"[SceneSystemController] 已复制场景配置和任务树，source={source.SceneId}，copy={copy.SceneId}。");
            return copy;
        }

        /// <summary>只从当前数据库移除配置引用；场景配置资产及其任务资产不受影响。</summary>
        /// <param name="config">要解除引用的配置。</param>
        public void RemoveConfigReference(SceneLoadConfig config)
        {
            int index = FindConfigIndex(config);
            if (index < 0) return;
            SerializedObject serializedDatabase = new SerializedObject(database);
            SerializedProperty configArray = serializedDatabase.FindProperty("sceneConfigs");
            Undo.RecordObject(database, "移除场景配置引用");
            DeleteArrayElement(configArray, index);
            serializedDatabase.ApplyModifiedProperties();
            SaveStructure();
            WSLog.Log($"[SceneSystemController] 已从数据库移除配置引用，sceneId={config.SceneId}。");
        }

        /// <summary>生成只回收配置资产的确认说明，并提醒其他引用可能失效。</summary>
        /// <param name="config">准备删除的场景配置。</param>
        /// <returns>显示给用户的删除内容说明。</returns>
        public string GetConfigDeletionSummary(SceneLoadConfig config) =>
            $"将仅删除配置资产：{AssetDatabase.GetAssetPath(config)}。\n" +
            "任务资产会保留；此配置在其他位置的引用可能变为 Missing。";

        /// <summary>先回收选中的配置资产，再解除数据库中对应的单一引用。</summary>
        /// <param name="item">确认删除的配置行及其真实数据库位置。</param>
        public bool DeleteConfigAsset(SceneLoadTreeItem item)
        {
            SceneLoadConfig config = item.Config;
            string sceneId = config.SceneId;
            int configIndex = item.ConfigIndex;
            if (database == null || configIndex < 0 || configIndex >= database.SceneConfigs.Count ||
                !ReferenceEquals(database.SceneConfigs[configIndex], config))
            {
                WSLog.LogError($"[SceneSystemController] 场景配置引用在删除前已变化，sceneId={config.SceneId}；资产和数据库均未修改。");
                return false;
            }

            string configPath = AssetDatabase.GetAssetPath(config);
            if (string.IsNullOrEmpty(configPath) || !AssetDatabase.MoveAssetToTrash(configPath))
            {
                WSLog.LogError($"[SceneSystemController] 场景配置移入回收站失败，sceneId={config.SceneId}，assetPath={configPath}；数据库引用保持不变。");
                return false;
            }

            SerializedObject serializedDatabase = new SerializedObject(database);
            SerializedProperty configArray = serializedDatabase.FindProperty("sceneConfigs");
            Undo.RecordObject(database, "删除场景配置资产引用");
            DeleteArrayElement(configArray, configIndex);
            serializedDatabase.ApplyModifiedProperties();
            SaveStructure();
            WSLog.Log($"[SceneSystemController] 场景配置资产已移入回收站，sceneId={sceneId}；任务资产保留。");
            return true;
        }

        #endregion

        #region 任务树结构编辑

        /// <summary>为配置设置新的根任务引用。</summary>
        /// <param name="config">目标场景配置。</param>
        /// <param name="task">新的根任务。</param>
        public void SetRootTask(SceneLoadConfig config, SceneLoadTask task)
        {
            SerializedObject serializedConfig = new SerializedObject(config);
            Undo.RecordObject(config, "设置场景根任务");
            serializedConfig.FindProperty("rootTask").objectReferenceValue = task;
            serializedConfig.ApplyModifiedProperties();
            SaveStructure();
        }

        /// <summary>创建任务资产并添加到组合末尾，或将其设为场景配置根任务。</summary>
        /// <param name="item">目标配置或组合节点。</param>
        /// <param name="taskType">新建任务类型。</param>
        /// <returns>新建任务。</returns>
        public SceneLoadTask CreateTask(SceneLoadTreeItem item, Type taskType)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            SceneLoadTask task = assetService.CreateTaskAsset(taskType, taskType.Name);
            if (item.IsConfiguration) SetRootTask(item.Config, task);
            else AppendChild(item.Task, task, -1);
            SaveStructure();
            WSLog.Log($"[SceneSystemController] 已创建任务节点，taskType={taskType.Name}，assetPath={AssetDatabase.GetAssetPath(task)}。");
            return task;
        }

        /// <summary>把已有任务资产设为根任务或追加到当前组合节点。</summary>
        /// <param name="item">配置或组合目标。</param>
        /// <param name="task">要建立引用的任务资产。</param>
        public void AddExistingTask(SceneLoadTreeItem item, SceneLoadTask task)
        {
            if (item == null || task == null) throw new ArgumentNullException(item == null ? nameof(item) : nameof(task));
            if (item.IsConfiguration) SetRootTask(item.Config, task);
            else AppendChild(item.Task, task, -1);
            SaveStructure();
            WSLog.Log($"[SceneSystemController] 已引用现有任务资产，task={task.name}，target={item.ReferencePath}。");
        }

        /// <summary>只解除当前配置或父组合中的任务引用。</summary>
        /// <param name="item">要解除的任务引用位置。</param>
        public void RemoveTaskReference(SceneLoadTreeItem item)
        {
            if (item == null) return;
            string taskName = item.Task == null ? "已删除的任务资产" : item.Task.name;
            if (item.ParentTask == null)
            {
                SetRootTask(item.Config, null);
                WSLog.Log($"[SceneSystemController] 已移除任务根引用，task={taskName}，path={item.ReferencePath}。");
                return;
            }
            RemoveChildAt(item.ParentTask, item.SiblingIndex, "移除场景任务引用");
            SaveStructure();
            WSLog.Log($"[SceneSystemController] 已移除任务引用，task={taskName}，path={item.ReferencePath}。");
        }

        /// <summary>生成仅删除当前任务资产的确认说明，并指出外部引用风险。</summary>
        /// <param name="item">待删除资产对应的引用位置。</param>
        /// <returns>显示给用户的删除内容说明。</returns>
        public string GetTaskDeletionSummary(SceneLoadTreeItem item) =>
            $"将仅删除任务资产：{AssetDatabase.GetAssetPath(item.Task)}。\n" +
            "子任务资产会保留；该任务在其他位置的引用可能变为 Missing。";

        /// <summary>仅将选中的任务资产移入回收站，成功后解除当前引用并保留所有子任务资产。</summary>
        /// <param name="item">确认删除的任务引用位置。</param>
        /// <returns>任务资产已进入回收站时返回 true。</returns>
        public bool DeleteTaskAsset(SceneLoadTreeItem item)
        {
            SceneLoadTask task = item.Task;
            string taskName = task.name;
            string taskPath = AssetDatabase.GetAssetPath(task);
            if (string.IsNullOrEmpty(taskPath) || !AssetDatabase.MoveAssetToTrash(taskPath))
            {
                WSLog.LogError($"[SceneSystemController] 场景任务移入回收站失败，task={taskName}，path={item.ReferencePath}，assetPath={taskPath}；任务引用保持不变。");
                return false;
            }

            RemoveTaskReference(item);
            WSLog.Log($"[SceneSystemController] 场景任务资产已移入回收站，task={taskName}，path={item.ReferencePath}；子任务资产保留。");
            return true;
        }

        /// <summary>在当前任务同级创建并插入一个共享关系独立的子树副本。</summary>
        /// <param name="item">复制来源的引用位置。</param>
        /// <returns>复制出来的任务根节点。</returns>
        public SceneLoadTask CopyTaskSibling(SceneLoadTreeItem item)
        {
            if (item?.Task == null || item.ParentTask == null)
                throw new InvalidOperationException("根任务没有同级容器，请使用复制场景配置建立独立根任务。");
            SceneLoadTask clone = assetService.CloneTaskSubtree(item.Task);
            InsertChild(item.ParentTask, clone, item.SiblingIndex + 1);
            SaveStructure();
            WSLog.Log($"[SceneSystemController] 已复制任务子树，source={item.Task.name}，copy={clone.name}。");
            return clone;
        }

        /// <summary>通过资产服务重命名任务文件和对象；共享引用继续指向原资产 GUID。</summary>
        /// <param name="task">目标任务资产。</param>
        /// <param name="newName">新的任务名称，同时作为 .asset 文件名。</param>
        public void RenameTask(SceneLoadTask task, string newName)
        {
            // 文件名由 AssetDatabase 管理，不把只修改 ScriptableObject 名称的状态记录为 Undo。
            assetService.RenameTaskAsset(task, newName);
        }

        /// <summary>将任务引用拖到目标节点前、后或组合内部，并保持原有资产身份。</summary>
        /// <param name="source">拖动来源行。</param>
        /// <param name="target">落点目标行。</param>
        /// <param name="placement">落点相对位置。</param>
        /// <param name="newParentTask">成功移动后的真实父组合。</param>
        /// <param name="newSiblingIndex">成功移动后的真实同级位置。</param>
        /// <returns>引用关系发生变化时返回 true。</returns>
        public bool MoveTaskReference(
            SceneLoadTreeItem source,
            SceneLoadTreeItem target,
            E_SceneLoadDropPlacement placement,
            out SceneLoadTask newParentTask,
            out int newSiblingIndex)
        {
            newParentTask = null;
            newSiblingIndex = -1;
            if (!TryCalculateMove(source, target, placement,
                    out SceneLoadTask destinationParent,
                    out List<SceneLoadTask> sourceChildren,
                    out List<SceneLoadTask> destinationChildren,
                    out int insertIndex)) return false;

            sourceChildren.RemoveAt(source.SiblingIndex);
            if (!ReferenceEquals(source.ParentTask, destinationParent))
                destinationChildren = GetChildren(destinationParent);
            insertIndex = Mathf.Clamp(insertIndex, 0, destinationChildren.Count);
            destinationChildren.Insert(insertIndex, source.Task);
            WriteChildren(source.ParentTask, sourceChildren, "调整场景任务顺序");
            if (!ReferenceEquals(source.ParentTask, destinationParent))
                WriteChildren(destinationParent, destinationChildren, "调整场景任务顺序");
            SaveStructure();
            newParentTask = destinationParent;
            newSiblingIndex = insertIndex;
            WSLog.Log($"[SceneSystemController] 已移动任务引用，task={source.Task.name}，from={source.ReferencePath}，to={target.ReferencePath}，placement={placement}。");
            return true;
        }

        /// <summary>判断拖拽预览位置是否能执行，并与松开时使用同一组真实列表索引规则。</summary>
        /// <param name="source">拖动来源行。</param>
        /// <param name="target">当前指针命中的目标行。</param>
        /// <param name="placement">来源相对于目标的放置位置。</param>
        /// <returns>该落点可以改变引用结构时返回 true。</returns>
        public bool CanMoveTaskReference(
            SceneLoadTreeItem source,
            SceneLoadTreeItem target,
            E_SceneLoadDropPlacement placement) =>
            TryCalculateMove(source, target, placement, out _, out _, out _, out _);

        /// <summary>验证来源、容器、目标索引与去环条件，并计算移除来源后的插入位置。</summary>
        /// <param name="source">拖动来源行。</param>
        /// <param name="target">落点目标行。</param>
        /// <param name="placement">来源相对于目标的位置。</param>
        /// <param name="destinationParent">最终父组合。</param>
        /// <param name="sourceChildren">来源容器的实际 children 列表。</param>
        /// <param name="destinationChildren">目标容器的实际 children 列表。</param>
        /// <param name="insertIndex">移除来源后应插入的索引。</param>
        /// <returns>引用位置与目标结构有效且并非原位拖动时返回 true。</returns>
        private bool TryCalculateMove(
            SceneLoadTreeItem source,
            SceneLoadTreeItem target,
            E_SceneLoadDropPlacement placement,
            out SceneLoadTask destinationParent,
            out List<SceneLoadTask> sourceChildren,
            out List<SceneLoadTask> destinationChildren,
            out int insertIndex)
        {
            destinationParent = null;
            sourceChildren = null;
            destinationChildren = null;
            insertIndex = -1;
            if (EditorApplication.isPlaying || source?.Task == null || target?.Task == null ||
                source.ParentTask == null ||
                !ReferenceEquals(source.Config, target.Config) ||
                (ReferenceEquals(source.ParentTask, target.ParentTask) && source.SiblingIndex == target.SiblingIndex))
                return false;

            if (placement == E_SceneLoadDropPlacement.Inside)
            {
                destinationParent = target.Task;
                if (!IsComposite(destinationParent) || ContainsTask(source.Task, destinationParent)) return false;
            }
            else
            {
                destinationParent = target.ParentTask;
                if (destinationParent == null || !IsComposite(destinationParent) ||
                    ContainsTask(source.Task, destinationParent)) return false;
            }

            sourceChildren = GetChildren(source.ParentTask);
            if (source.SiblingIndex < 0 || source.SiblingIndex >= sourceChildren.Count ||
                !ReferenceEquals(sourceChildren[source.SiblingIndex], source.Task)) return false;

            destinationChildren = ReferenceEquals(source.ParentTask, destinationParent)
                ? sourceChildren
                : GetChildren(destinationParent);
            if (placement == E_SceneLoadDropPlacement.Inside)
            {
                insertIndex = destinationChildren.Count;
            }
            else
            {
                if (target.SiblingIndex < 0 || target.SiblingIndex >= destinationChildren.Count ||
                    !ReferenceEquals(destinationChildren[target.SiblingIndex], target.Task)) return false;
                insertIndex = target.SiblingIndex + (placement == E_SceneLoadDropPlacement.After ? 1 : 0);
            }

            if (!ReferenceEquals(source.ParentTask, destinationParent)) return true;
            if (source.SiblingIndex < insertIndex) insertIndex--;
            return source.SiblingIndex != insertIndex;
        }

        /// <summary>按父组合真实列表判断任务能否移动，不受 TreeView 搜索过滤影响。</summary>
        /// <param name="item">需要调整的任务引用行。</param>
        /// <param name="offset">上移或下移步数，使用 -1 或 +1。</param>
        /// <returns>当前真实同级列表中存在可移动目标时返回 true。</returns>
        public bool CanMoveTaskByOffset(SceneLoadTreeItem item, int offset)
        {
            if (item?.Task == null || item.ParentTask == null || offset == 0) return false;
            List<SceneLoadTask> children = GetChildren(item.ParentTask);
            int targetIndex = item.SiblingIndex + offset;
            return item.SiblingIndex >= 0 && item.SiblingIndex < children.Count &&
                   targetIndex >= 0 && targetIndex < children.Count &&
                   ReferenceEquals(children[item.SiblingIndex], item.Task);
        }

        /// <summary>按父组合的真实 children 顺序调整任务位置，不受 TreeView 搜索过滤影响。</summary>
        /// <param name="item">需要调整的任务引用行。</param>
        /// <param name="offset">上移或下移步数，使用 -1 或 +1。</param>
        /// <returns>成功改变顺序时返回 true。</returns>
        public bool MoveTaskByOffset(SceneLoadTreeItem item, int offset)
        {
            if (!CanMoveTaskByOffset(item, offset)) return false;
            List<SceneLoadTask> children = GetChildren(item.ParentTask);
            int targetIndex = item.SiblingIndex + offset;
            children.RemoveAt(item.SiblingIndex);
            children.Insert(targetIndex, item.Task);
            WriteChildren(item.ParentTask, children, "调整场景任务顺序");
            SaveStructure();
            WSLog.Log($"[SceneSystemController] 已调整任务顺序，task={item.Task.name}，from={item.SiblingIndex}，to={targetIndex}，parent={item.ParentTask.name}。");
            return true;
        }

        /// <summary>将一个 Project 拖入的任务资产追加至目标组合节点或空配置根节点。</summary>
        /// <param name="target">树中目标行。</param>
        /// <param name="task">Project 中拖入的任务资产。</param>
        /// <returns>建立引用时返回 true。</returns>
        public bool DropExistingTask(SceneLoadTreeItem target, SceneLoadTask task)
        {
            if (target == null || task == null ||
                (!target.IsConfiguration && !IsComposite(target.Task))) return false;
            if (target.IsConfiguration)
            {
                if (target.Config.RootTask != null) return false;
                SetRootTask(target.Config, task);
            }
            else
            {
                if (ContainsTask(task, target.Task)) return false;
                AppendChild(target.Task, task, -1);
            }
            SaveStructure();
            return true;
        }

        /// <summary>定位配置或任务对应的项目资产。</summary>
        /// <param name="item">树行。</param>
        public static void PingAsset(SceneLoadTreeItem item)
        {
            if (item == null)
            {
                WSLog.LogWarning("[SceneSystemController] 定位场景资产失败，TreeView 行数据为空。");
                return;
            }

            // 行类型决定定位对象，Task 行即使携带所属 Config 也必须定位任务资产本身。
            UnityEngine.Object asset = item.IsConfiguration ? item.Config : item.Task;
            if (asset == null)
            {
                WSLog.LogWarning($"[SceneSystemController] 定位场景资产失败，assetType={(item.IsConfiguration ? "SceneLoadConfig" : "SceneLoadTask")}，referencePath={item.ReferencePath}。");
                return;
            }

            EditorUtility.FocusProjectWindow();
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            WSLog.Log($"[SceneSystemController] 已在 Project 窗口定位场景资产，assetPath={AssetDatabase.GetAssetPath(asset)}。");
        }

        #endregion

        #region 序列化结构辅助

        /// <summary>按搜索条件递归生成独立引用行，并使用祖先集合截断资产引用环。</summary>
        private bool TryBuildTaskItem(
            SceneLoadTask task,
            SceneLoadTask parentTask,
            SceneLoadConfig config,
            int configIndex,
            int siblingIndex,
            string parentPath,
            int depth,
            string searchText,
            bool parentMatched,
            HashSet<SceneLoadTask> ancestors,
            ref int nextId,
            out TreeViewItemData<SceneLoadTreeItem> item)
        {
            item = default;
            if (task == null) return false;
            bool matches = parentMatched || MatchesSearch(task.name, searchText) || MatchesSearch(task.GetType().Name, searchText);
            string path = $"{parentPath}/{task.name}[{siblingIndex}]";
            var childItems = new List<TreeViewItemData<SceneLoadTreeItem>>();
            bool isCycle = ancestors.Contains(task);
            if (!isCycle)
            {
                ancestors.Add(task);
                IReadOnlyList<SceneLoadTask> children = GetChildren(task);
                for (int index = 0; index < children.Count; index++)
                    if (TryBuildTaskItem(children[index], task, config, configIndex, index, path, depth + 1,
                            searchText, matches, ancestors, ref nextId,
                            out TreeViewItemData<SceneLoadTreeItem> childItem))
                        childItems.Add(childItem);
                ancestors.Remove(task);
            }
            if (!matches && childItems.Count == 0) return false;

            string typeLabel = task is SequenceSceneLoadTask ? "SEQUENCE" :
                task is ParallelSceneLoadTask ? "PARALLEL" : "TASK";
            if (isCycle) typeLabel += " · CYCLE";
            var treeItem = new SceneLoadTreeItem(nextId++, false, config, task, parentTask, configIndex,
                siblingIndex, path, depth, typeLabel);
            item = new TreeViewItemData<SceneLoadTreeItem>(treeItem.Id, treeItem, childItems);
            return true;
        }

        /// <summary>收集当前配置树内指向指定任务资产的所有独立引用行。</summary>
        /// <param name="treeItem">需要递归检查的树节点。</param>
        /// <param name="targetTask">正在查看的任务资产。</param>
        /// <param name="referencePaths">接收匹配任务的引用路径。</param>
        private static void CollectTaskReferencePaths(
            TreeViewItemData<SceneLoadTreeItem> treeItem,
            SceneLoadTask targetTask,
            List<string> referencePaths)
        {
            if (ReferenceEquals(treeItem.data.Task, targetTask))
                referencePaths.Add(treeItem.data.ReferencePath);
            foreach (TreeViewItemData<SceneLoadTreeItem> childItem in treeItem.children)
                CollectTaskReferencePaths(childItem, targetTask, referencePaths);
        }

        /// <summary>按路径顺序将执行任务展开为说明文本，并在引用环处停止递归。</summary>
        private static void AppendExecutionLines(
            SceneLoadTask task,
            int depth,
            string branchPrefix,
            HashSet<SceneLoadTask> ancestors,
            List<string> lines)
        {
            if (task == null)
            {
                lines.Add(new string(' ', depth * 2) + "! 缺失任务引用");
                return;
            }
            string indent = new string(' ', depth * 2);
            lines.Add($"{indent}{branchPrefix} {task.name} · {task.GetType().Name}");
            if (!ancestors.Add(task))
            {
                lines.Add(indent + "  ! 检测到循环引用");
                return;
            }
            IReadOnlyList<SceneLoadTask> children = GetChildren(task);
            for (int index = 0; index < children.Count; index++)
            {
                string childPrefix = task is ParallelSceneLoadTask ? $"├─ 并行分支 {index + 1}:" : $"{index + 1}.";
                AppendExecutionLines(children[index], depth + 1, childPrefix, ancestors, lines);
            }
            ancestors.Remove(task);
        }

        /// <summary>在当前数据库中追加场景配置引用。</summary>
        private void AddConfigReference(SceneLoadConfig config)
        {
            SerializedObject serializedDatabase = new SerializedObject(database);
            SerializedProperty configArray = serializedDatabase.FindProperty("sceneConfigs");
            Undo.RecordObject(database, "加入场景配置");
            int newIndex = configArray.arraySize++;
            configArray.GetArrayElementAtIndex(newIndex).objectReferenceValue = config;
            serializedDatabase.ApplyModifiedProperties();
        }

        /// <summary>在指定配置任务列表中插入子任务引用。</summary>
        private static void InsertChild(SceneLoadTask parent, SceneLoadTask child, int index)
        {
            List<SceneLoadTask> children = GetChildren(parent);
            children.Insert(Mathf.Clamp(index, 0, children.Count), child);
            WriteChildren(parent, children, "添加场景子任务");
        }

        /// <summary>在组合末尾或指定位置写入 children 序列化数组。</summary>
        private static void AppendChild(SceneLoadTask parent, SceneLoadTask child, int index)
        {
            if (!IsComposite(parent)) throw new InvalidOperationException($"任务 '{parent.name}' 不支持子节点。");
            InsertChild(parent, child, index < 0 ? GetChildren(parent).Count : index);
        }

        /// <summary>删除指定 children 元素并留下 Undo/Redo 记录。</summary>
        private static void RemoveChildAt(SceneLoadTask parent, int index, string undoName)
        {
            SerializedObject serializedParent = new SerializedObject(parent);
            SerializedProperty children = serializedParent.FindProperty("children");
            if (children == null || index < 0 || index >= children.arraySize) return;
            Undo.RecordObject(parent, undoName);
            DeleteArrayElement(children, index);
            serializedParent.ApplyModifiedProperties();
        }

        /// <summary>將组合的完整顺序快照写回资产，并为列表变化记录 Undo。</summary>
        private static void WriteChildren(SceneLoadTask parent, IReadOnlyList<SceneLoadTask> childTasks, string undoName)
        {
            SerializedObject serializedParent = new SerializedObject(parent);
            SerializedProperty children = serializedParent.FindProperty("children");
            if (children == null) throw new InvalidOperationException($"组合任务 '{parent.name}' 没有 children 序列化列表。");
            Undo.RecordObject(parent, undoName);
            children.arraySize = childTasks.Count;
            for (int index = 0; index < childTasks.Count; index++)
                children.GetArrayElementAtIndex(index).objectReferenceValue = childTasks[index];
            serializedParent.ApplyModifiedProperties();
        }

        /// <summary>从对象引用数组中删除元素，确保删除操作对应的是结构而非先清空引用。</summary>
        private static void DeleteArrayElement(SerializedProperty array, int index)
        {
            SerializedProperty element = array.GetArrayElementAtIndex(index);
            element.objectReferenceValue = null;
            array.DeleteArrayElementAtIndex(index);
        }

        /// <summary>读取组合 children 序列化列表；叶子任务返回空列表。</summary>
        private static List<SceneLoadTask> GetChildren(SceneLoadTask task)
        {
            if (task == null) return new List<SceneLoadTask>();
            SerializedObject serializedTask = new SerializedObject(task);
            SerializedProperty children = serializedTask.FindProperty("children");
            var result = new List<SceneLoadTask>(children == null ? 0 : children.arraySize);
            if (children == null) return result;
            for (int index = 0; index < children.arraySize; index++)
                result.Add(children.GetArrayElementAtIndex(index).objectReferenceValue as SceneLoadTask);
            return result;
        }

        /// <summary>判断当前任务是否支持 children 编辑。</summary>
        private static bool IsComposite(SceneLoadTask task) =>
            task is SequenceSceneLoadTask || task is ParallelSceneLoadTask;

        /// <summary>判断目标组合是否已经位于源子树内，避免拖放产生环引用。</summary>
        private static bool ContainsTask(SceneLoadTask root, SceneLoadTask candidate)
        {
            var visited = new HashSet<SceneLoadTask>();
            var pending = new Stack<SceneLoadTask>();
            if (root != null) pending.Push(root);
            while (pending.Count > 0)
            {
                SceneLoadTask current = pending.Pop();
                if (ReferenceEquals(current, candidate)) return true;
                if (current == null || !visited.Add(current)) continue;
                foreach (SceneLoadTask child in GetChildren(current))
                    if (child != null) pending.Push(child);
            }
            return false;
        }

        /// <summary>比较搜索词与节点文本，不区分英文大小写。</summary>
        private static bool MatchesSearch(string value, string searchText) =>
            string.IsNullOrEmpty(searchText) ||
            (!string.IsNullOrEmpty(value) && value.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0);

        /// <summary>查找配置在数据库中的数组位置。</summary>
        private int FindConfigIndex(SceneLoadConfig config)
        {
            if (database == null || config == null) return -1;
            for (int index = 0; index < database.SceneConfigs.Count; index++)
                if (ReferenceEquals(database.SceneConfigs[index], config)) return index;
            return -1;
        }

        /// <summary>统计当前数据库对同一配置资产的重复直接引用。</summary>
        /// <param name="config">待统计配置资产。</param>
        /// <returns>当前数据库中引用次数。</returns>
        private int CountConfigReferencesInCurrentDatabase(SceneLoadConfig config)
        {
            int referenceCount = 0;
            if (database == null) return referenceCount;
            foreach (SceneLoadConfig candidate in database.SceneConfigs)
                if (ReferenceEquals(candidate, config)) referenceCount++;
            return referenceCount;
        }

        /// <summary>确认数据库已经创建或选中。</summary>
        private void EnsureDatabase()
        {
            if (database == null) throw new InvalidOperationException("请先选择或创建 SceneLoadDatabase。");
        }

        /// <summary>在结构性修改后统一保存资产，避免刷新、校验路径产生意外写入。</summary>
        private static void SaveStructure()
        {
            AssetDatabase.SaveAssets();
        }

        #endregion
    }

    /// <summary>描述树内拖拽任务引用的三种落点。</summary>
    internal enum E_SceneLoadDropPlacement
    {
        /// <summary>插入目标同级节点之前。</summary>
        Before,
        /// <summary>追加到目标组合节点内部。</summary>
        Inside,
        /// <summary>插入目标同级节点之后。</summary>
        After
    }
}
