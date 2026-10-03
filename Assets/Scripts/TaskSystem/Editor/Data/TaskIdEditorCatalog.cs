#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RPG.TaskSystemNS.Editor
{
    /// <summary>保存任务 ID 选择器展示所需的任务定义与唯一标识。</summary>
    internal sealed class TaskIdEditorCatalogEntry
    {
        #region 属性

        /// <summary>获取稳定任务 ID。</summary>
        internal string TaskId { get; }

        /// <summary>获取同时包含标题和稳定 ID 的编辑器标签。</summary>
        internal string DisplayLabel { get; }

        /// <summary>获取对应的任务配置资产。</summary>
        internal TaskDefinition Definition { get; }

        #endregion

        #region 构造

        /// <summary>为一个通过唯一 ID 校验的任务资产创建候选项。</summary>
        /// <param name="taskId">原始序列化任务 ID。</param>
        /// <param name="definition">任务配置资产。</param>
        internal TaskIdEditorCatalogEntry(string taskId, TaskDefinition definition)
        {
            TaskId = taskId;
            Definition = definition;
            string title = string.IsNullOrWhiteSpace(definition.Title) ? "无标题" : definition.Title.Trim();
            DisplayLabel = $"{taskId}（{title}）";
        }

        #endregion
    }

    /// <summary>缓存显式指定数据库中的有效 TaskId，并向 Drawer 合并发送刷新通知。</summary>
    [InitializeOnLoad]
    internal static class TaskIdEditorCatalog
    {
        #region 缓存状态

        private static readonly List<TaskIdEditorCatalogEntry> entryList = new();
        // key：原始稳定 TaskId；value：唯一对应的任务定义资产。冲突 ID 不进入该索引。
        private static readonly Dictionary<string, TaskDefinition> definitionByTaskIdMap =
            new(StringComparer.Ordinal);
        private static readonly HashSet<string> conflictedTaskIdSet = new(StringComparer.Ordinal);
        private static readonly List<string> configurationIssueList = new();
        private static bool cacheDirty = true;
        private static bool notificationScheduled;

        #endregion

        #region 事件

        /// <summary>通知已显示的 TaskId Drawer 刷新候选项。</summary>
        internal static event Action Changed;

        #endregion

        #region 初始化与查询

        /// <summary>订阅项目资源、Undo 与序列化修改通知，避免逐帧扫描候选数据库。</summary>
        static TaskIdEditorCatalog()
        {
            EditorApplication.projectChanged += OnProjectChanged;
            Undo.undoRedoEvent += OnUndoRedo;
            Undo.postprocessModifications += OnPostprocessModifications;
            Debug.Log("[TaskIdEditorCatalog] 已连接项目资源与 Undo 变更监听。");
        }

        /// <summary>获取设置中明确选择的候选数据库。</summary>
        internal static TaskDatabase SourceDatabase => TaskConfigEditorSettings.instance.TaskIdSourceDatabase;

        /// <summary>获取按 TaskId 排序的候选项。</summary>
        internal static IReadOnlyList<TaskIdEditorCatalogEntry> Entries
        {
            get
            {
                EnsureCache();
                return entryList;
            }
        }

        /// <summary>获取本次候选库扫描发现的无效资产或 ID 冲突摘要。</summary>
        internal static IReadOnlyList<string> ConfigurationIssues
        {
            get
            {
                EnsureCache();
                return configurationIssueList;
            }
        }

        /// <summary>按稳定 ID 查找一个可唯一引用的任务定义。</summary>
        /// <param name="taskId">待查询的原始 TaskId。</param>
        /// <param name="definition">唯一候选任务定义。</param>
        /// <returns>存在未冲突候选时返回 true。</returns>
        internal static bool TryGetDefinition(string taskId, out TaskDefinition definition)
        {
            EnsureCache();
            if (!string.IsNullOrEmpty(taskId) && definitionByTaskIdMap.TryGetValue(taskId, out definition)) return true;
            definition = null;
            return false;
        }

        /// <summary>判断指定 ID 是否因数据库内多个资产重复而被排除。</summary>
        /// <param name="taskId">待查询的原始 TaskId。</param>
        /// <returns>该 ID 存在资产冲突时返回 true。</returns>
        internal static bool IsConflicted(string taskId)
        {
            EnsureCache();
            return !string.IsNullOrEmpty(taskId) && conflictedTaskIdSet.Contains(taskId);
        }

        /// <summary>使候选索引失效并合并延迟通知，供数据库、资产与来源变化调用。</summary>
        internal static void Invalidate()
        {
            cacheDirty = true;
            if (notificationScheduled) return;

            notificationScheduled = true;
            EditorApplication.delayCall += NotifyChanged;
        }

        #endregion

        #region 缓存建立

        /// <summary>仅在首次访问或收到明确变更通知后重建候选索引。</summary>
        private static void EnsureCache()
        {
            if (!cacheDirty) return;
            RebuildCache();
        }

        /// <summary>从指定数据库的序列化原始字段建立有效 ID 列表并报告冲突。</summary>
        private static void RebuildCache()
        {
            cacheDirty = false;
            entryList.Clear();
            definitionByTaskIdMap.Clear();
            conflictedTaskIdSet.Clear();
            configurationIssueList.Clear();

            TaskDatabase database = SourceDatabase;
            if (database == null) return;

            IReadOnlyList<TaskDefinition> definitions = database.Definitions;
            if (definitions == null)
            {
                configurationIssueList.Add($"数据库 {database.name} 的任务列表未初始化。");
                LogConfigurationIssues(database);
                return;
            }

            // 原始 ID 先分组；只有恰好对应一个资产的组才进入可选候选表。
            var entryListByTaskIdMap = new Dictionary<string, List<TaskIdEditorCatalogEntry>>(StringComparer.Ordinal);
            for (int index = 0; index < definitions.Count; index++)
            {
                TaskDefinition definition = definitions[index];
                if (definition == null)
                {
                    configurationIssueList.Add($"数据库 {database.name} 第 {index} 项为空。");
                    continue;
                }

                TaskDefinitionEditorEntry editorEntry = new TaskDefinitionEditorEntry(definition, true);
                if (!editorEntry.HasValidTaskId)
                {
                    string rawId = string.IsNullOrWhiteSpace(editorEntry.TaskId) ? "（空）" : editorEntry.TaskId;
                    configurationIssueList.Add($"忽略无效 TaskId：{editorEntry.AssetPath}，值={rawId}。");
                    continue;
                }

                if (!entryListByTaskIdMap.TryGetValue(editorEntry.TaskId, out List<TaskIdEditorCatalogEntry> sameIdEntries))
                {
                    sameIdEntries = new List<TaskIdEditorCatalogEntry>();
                    entryListByTaskIdMap.Add(editorEntry.TaskId, sameIdEntries);
                }

                if (sameIdEntries.Exists(entry => entry.Definition == definition)) continue;
                sameIdEntries.Add(new TaskIdEditorCatalogEntry(editorEntry.TaskId, definition));
            }

            foreach (KeyValuePair<string, List<TaskIdEditorCatalogEntry>> entriesByTaskId in entryListByTaskIdMap)
            {
                List<TaskIdEditorCatalogEntry> sameIdEntries = entriesByTaskId.Value;
                if (sameIdEntries.Count != 1)
                {
                    conflictedTaskIdSet.Add(entriesByTaskId.Key);
                    string assetPaths = string.Join("、", sameIdEntries.ConvertAll(entry => AssetDatabase.GetAssetPath(entry.Definition)));
                    configurationIssueList.Add($"TaskId {entriesByTaskId.Key} 被多个任务资产使用：{assetPaths}。");
                    continue;
                }

                TaskIdEditorCatalogEntry catalogEntry = sameIdEntries[0];
                entryList.Add(catalogEntry);
                definitionByTaskIdMap.Add(catalogEntry.TaskId, catalogEntry.Definition);
            }

            entryList.Sort((left, right) => string.CompareOrdinal(left.TaskId, right.TaskId));
            Debug.Log(
                $"[TaskIdEditorCatalog] 已重建任务引用候选，database={database.name}，candidateCount={entryList.Count}，conflictCount={conflictedTaskIdSet.Count}，issueCount={configurationIssueList.Count}。",
                database);
            LogConfigurationIssues(database);
        }

        /// <summary>把无效草稿和冲突汇总写入 Console，避免每次字段绘制重复输出。</summary>
        /// <param name="database">正在扫描的候选数据库。</param>
        private static void LogConfigurationIssues(TaskDatabase database)
        {
            if (configurationIssueList.Count == 0) return;

            int shownCount = Math.Min(configurationIssueList.Count, 5);
            string details = string.Join("；", configurationIssueList.GetRange(0, shownCount));
            if (configurationIssueList.Count > shownCount)
            {
                details += $"；另有 {configurationIssueList.Count - shownCount} 项问题";
            }

            Debug.LogWarning(
                $"[TaskIdEditorCatalog] 候选数据库 {database.name} 发现 {configurationIssueList.Count} 项配置问题：{details}",
                database);
        }

        #endregion

        #region 编辑器变更回调

        /// <summary>项目资产增删改后失效候选缓存，并安排已挂载字段刷新。</summary>
        private static void OnProjectChanged() => Invalidate();

        /// <summary>Undo 或 Redo 后使候选索引失效。</summary>
        /// <param name="undoRedoInfo">Unity Undo 系统提供的分组信息。</param>
        private static void OnUndoRedo(in UndoRedoInfo undoRedoInfo) => Invalidate();

        /// <summary>配置对象通过序列化 Inspector 修改时及时失效缓存。</summary>
        /// <param name="modifications">Unity Undo 系统即将应用的序列化修改。</param>
        /// <returns>保持原序列的修改数组。</returns>
        private static UndoPropertyModification[] OnPostprocessModifications(UndoPropertyModification[] modifications)
        {
            for (int index = 0; index < modifications.Length; index++)
            {
                UnityEngine.Object modifiedObject = modifications[index].currentValue?.target;
                if (modifiedObject is TaskDefinition || modifiedObject is TaskDatabase)
                {
                    Invalidate();
                    break;
                }
            }

            return modifications;
        }

        /// <summary>在主线程延迟通知 Drawer，避免资源回调期间重建序列化界面。</summary>
        private static void NotifyChanged()
        {
            notificationScheduled = false;
            Changed?.Invoke();
        }

        #endregion
    }
}
#endif
