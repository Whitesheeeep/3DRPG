#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RPG.TaskSystemNS.Editor
{
    /// <summary>协调任务编辑器的筛选、选择、资产命令和 View 刷新。</summary>
    internal sealed class TaskConfigEditorController : IDisposable
    {
        #region 依赖字段

        // Controller 组合配置资产服务、持久化编辑器状态和唯一 View，不把 UI 控件写入领域配置。
        private readonly TaskConfigEditorView view;
        private readonly TaskConfigEditorService service;
        private readonly TaskConfigEditorSettings settings;

        #endregion

        #region 列表缓存

        private readonly List<TaskDefinitionEditorEntry> filteredDefinitions = new();

        #endregion

        #region 窗口状态

        private const string DatabaseSessionKey = "RPG.TaskConfig.DatabasePath";
        private const string SelectedSessionKey = "RPG.TaskConfig.SelectedTaskPath";
        private const string SearchSessionKey = "RPG.TaskConfig.Search";
        private const string ScopeSessionKey = "RPG.TaskConfig.Scope";
        private const string CategorySessionKey = "RPG.TaskConfig.Category";
        private const string SortSessionKey = "RPG.TaskConfig.Sort";
        private const string DirectionSessionKey = "RPG.TaskConfig.Direction";
        private const string SuffixCategorySessionKey = "RPG.TaskConfig.SuffixCategory";

        private TaskDatabase database;
        private TaskDefinition selectedDefinition;
        private string search;
        private string scope;
        private string categoryFilter;
        private string sort;
        private string sortDirection;
        private string suffixCategory;
        private bool disposed;
        private bool refreshScheduled;
        private bool scheduledRefreshRebindDetails;

        #endregion

        #region 生命周期

        /// <summary>连接 View 命令、恢复窗口状态并执行首次刷新。</summary>
        /// <param name="view">窗口 View。</param>
        /// <param name="service">任务资产服务。</param>
        /// <param name="settings">TaskId 创建设置。</param>
        internal TaskConfigEditorController(TaskConfigEditorView view, TaskConfigEditorService service, TaskConfigEditorSettings settings)
        {
            this.view = view ?? throw new ArgumentNullException(nameof(view));
            this.service = service ?? throw new ArgumentNullException(nameof(service));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            database = service.ResolveDatabase();
            selectedDefinition = AssetDatabase.LoadAssetAtPath<TaskDefinition>(SessionState.GetString(SelectedSessionKey, string.Empty));
            search = SessionState.GetString(SearchSessionKey, string.Empty);
            scope = SessionState.GetString(ScopeSessionKey, "当前数据库");
            categoryFilter = SessionState.GetString(CategorySessionKey, "全部分类");
            sort = SessionState.GetString(SortSessionKey, "TaskId");
            sortDirection = SessionState.GetString(DirectionSessionKey, "升序");
            suffixCategory = SessionState.GetString(SuffixCategorySessionKey, "主线");
            BindViewEvents(true);
            Undo.undoRedoEvent += OnUndoRedo;
            EditorApplication.projectChanged += OnProjectChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            view.SetDatabase(database);
            view.SetTaskIdSourceDatabase(settings.TaskIdSourceDatabase);
            view.RestoreFilters(search, scope, categoryFilter, sort, sortDirection, suffixCategory);
            RefreshDefinitions(true);
            view.SetReadOnly(EditorApplication.isPlayingOrWillChangePlaymode);
            Debug.Log($"[TaskConfigEditor] 窗口 Controller 已连接，database={(database == null ? "none" : database.name)}。");
        }

        /// <summary>解除 View、Unity 编辑器事件和延迟刷新的全部订阅。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            BindViewEvents(false);
            Undo.undoRedoEvent -= OnUndoRedo;
            EditorApplication.projectChanged -= OnProjectChanged;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.delayCall -= ExecuteScheduledRefresh;
            view.Dispose();
            Debug.Log("[TaskConfigEditor] 窗口 Controller 已释放事件订阅。");
        }

        /// <summary>显示指定数据库并更新当前编辑器会话状态。</summary>
        /// <param name="targetDatabase">待显示任务数据库。</param>
        internal void OpenDatabase(TaskDatabase targetDatabase)
        {
            if (targetDatabase == null) return;
            database = targetDatabase;
            selectedDefinition = null;
            service.RememberDatabase(database);
            view.SetDatabase(database);
            RefreshDefinitions(true);
        }

        /// <summary>切换到项目全部任务范围并选中指定任务资产。</summary>
        /// <param name="definition">待显示任务。</param>
        internal void OpenDefinition(TaskDefinition definition)
        {
            if (definition == null) return;
            selectedDefinition = definition;
            scope = "项目全部任务";
            SessionState.SetString(ScopeSessionKey, scope);
            SessionState.SetString(SelectedSessionKey, AssetDatabase.GetAssetPath(definition));
            RefreshDefinitions(true);
        }

        #endregion

        #region View 事件连接

        /// <summary>连接或解除 View 用户意图事件。</summary>
        /// <param name="connect">true 时连接，false 时解除。</param>
        private void BindViewEvents(bool connect)
        {
            if (connect)
            {
                view.DatabaseChanged += OnDatabaseChanged;
                view.SearchChanged += OnSearchChanged;
                view.ScopeChanged += OnScopeChanged;
                view.CategoryFilterChanged += OnCategoryFilterChanged;
                view.SortChanged += OnSortChanged;
                view.DefinitionSelected += OnDefinitionSelected;
                view.CreateRequested += OnCreateRequested;
                view.TaskCommandRequested += OnTaskCommandRequested;
                view.ValidateRequested += OnValidateRequested;
                view.RefreshRequested += OnRefreshRequested;
                view.SuffixSaveRequested += OnSuffixSaveRequested;
                view.SuffixRemoveRequested += OnSuffixRemoveRequested;
                view.DefinitionFolderChanged += OnDefinitionFolderChanged;
                view.TaskIdSourceDatabaseChanged += OnTaskIdSourceDatabaseChanged;
                view.PropertiesChanged += OnPropertiesChanged;
            }
            else
            {
                view.DatabaseChanged -= OnDatabaseChanged;
                view.SearchChanged -= OnSearchChanged;
                view.ScopeChanged -= OnScopeChanged;
                view.CategoryFilterChanged -= OnCategoryFilterChanged;
                view.SortChanged -= OnSortChanged;
                view.DefinitionSelected -= OnDefinitionSelected;
                view.CreateRequested -= OnCreateRequested;
                view.TaskCommandRequested -= OnTaskCommandRequested;
                view.ValidateRequested -= OnValidateRequested;
                view.RefreshRequested -= OnRefreshRequested;
                view.SuffixSaveRequested -= OnSuffixSaveRequested;
                view.SuffixRemoveRequested -= OnSuffixRemoveRequested;
                view.DefinitionFolderChanged -= OnDefinitionFolderChanged;
                view.TaskIdSourceDatabaseChanged -= OnTaskIdSourceDatabaseChanged;
                view.PropertiesChanged -= OnPropertiesChanged;
            }
        }

        #endregion

        #region 筛选與列表刷新

        /// <summary>根据范围、搜索、分类和排序状态重建列表。</summary>
        /// <param name="rebindDetails">是否重新绑定当前详情对象。</param>
        private void RefreshDefinitions(bool rebindDetails)
        {
            TaskDefinition previousSelection = selectedDefinition;
            filteredDefinitions.Clear();
            List<TaskDefinitionEditorEntry> sourceDefinitions = service.GetDefinitions(database, scope);
            IEnumerable<TaskDefinitionEditorEntry> query = sourceDefinitions.Where(MatchesFilters);
            IOrderedEnumerable<TaskDefinitionEditorEntry> ordered = sort switch
            {
                "分类" => query.OrderBy(entry => entry.CategoryId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.TaskId, StringComparer.OrdinalIgnoreCase),
                "标题" => query.OrderBy(entry => entry.Definition.Title, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.TaskId, StringComparer.OrdinalIgnoreCase),
                _ => query.OrderBy(entry => entry.TaskId, StringComparer.OrdinalIgnoreCase)
            };
            List<TaskDefinitionEditorEntry> sortedDefinitions = ordered.ToList();
            if (sortDirection == "降序") sortedDefinitions.Reverse();
            filteredDefinitions.AddRange(sortedDefinitions);
            if (selectedDefinition != null && !filteredDefinitions.Any(entry => entry.Definition == selectedDefinition)) selectedDefinition = null;
            if (selectedDefinition == null && filteredDefinitions.Count > 0) selectedDefinition = filteredDefinitions[0].Definition;

            view.RenderDefinitions(filteredDefinitions, selectedDefinition);
            view.SetListStatus(database == null
                ? $"显示 {filteredDefinitions.Count} 项；未选择 TaskDatabase，新建和复制已禁用。"
                : $"数据库：{database.name} · 显示 {filteredDefinitions.Count} / {sourceDefinitions.Count} 项。");
            if (rebindDetails || previousSelection != selectedDefinition) BindSelectedDefinition();
        }

        /// <summary>判断任务是否满足当前搜索文本和分类筛选。</summary>
        /// <param name="entry">待筛选任务及其原始序列化字段。</param>
        /// <returns>匹配时返回 true。</returns>
        private bool MatchesFilters(TaskDefinitionEditorEntry entry)
        {
            if (!string.Equals(categoryFilter, "全部分类", StringComparison.Ordinal))
            {
                if (!TaskCategoryCatalog.TryGetDisplayName(entry.CategoryId, out string displayName) || displayName != categoryFilter)
                    return false;
            }
            if (string.IsNullOrWhiteSpace(search)) return true;
            string query = search.Trim();
            return entry.TaskId.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   entry.Definition.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   entry.AssetPath.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>在视图选择任务时记住路径并绑定详情。</summary>
        /// <param name="definition">当前任务选择。</param>
        private void OnDefinitionSelected(TaskDefinition definition)
        {
            if (selectedDefinition == definition) return;
            selectedDefinition = definition;
            SessionState.SetString(SelectedSessionKey, definition == null ? string.Empty : AssetDatabase.GetAssetPath(definition));
            BindSelectedDefinition();
        }

        /// <summary>将当前任务配置和校验结果绑定到详情面板。</summary>
        private void BindSelectedDefinition()
        {
            TaskDefinitionEditorEntry entry = service.GetDefinitionEntry(database, selectedDefinition);
            string validation = service.ValidateDefinition(database, selectedDefinition);
            view.BindDefinition(entry, validation);
            SessionState.SetString(SelectedSessionKey,
                selectedDefinition == null ? string.Empty : AssetDatabase.GetAssetPath(selectedDefinition));
        }

        #endregion

        #region 输入处理

        /// <summary>切换任务数据库并刷新列表和详情。</summary>
        /// <param name="targetDatabase">新数据库。</param>
        private void OnDatabaseChanged(TaskDatabase targetDatabase)
        {
            database = targetDatabase;
            selectedDefinition = null;
            service.RememberDatabase(database);
            RefreshDefinitions(true);
        }

        /// <summary>保存 TaskId 下拉候选来源并通知缓存重新建立索引。</summary>
        /// <param name="targetDatabase">明确选择的候选数据库。</param>
        private void OnTaskIdSourceDatabaseChanged(TaskDatabase targetDatabase)
        {
            if (settings.TaskIdSourceDatabase == targetDatabase) return;

            Undo.RecordObject(settings, "设置 TaskId 候选数据库");
            settings.SetTaskIdSourceDatabase(targetDatabase);
            TaskIdEditorCatalog.Invalidate();
            view.SetTaskIdSourceDatabase(targetDatabase);
            view.SetListStatus(targetDatabase == null
                ? "TaskId 候选数据库未设置，前置任务下拉框将保持禁用。"
                : $"TaskId 候选数据库已设置：{targetDatabase.name}");
        }

        /// <summary>更新搜索条件并记住用户输入。</summary>
        /// <param name="value">搜索文本。</param>
        private void OnSearchChanged(string value)
        {
            search = value ?? string.Empty;
            SessionState.SetString(SearchSessionKey, search);
            RefreshDefinitions(false);
        }

        /// <summary>切換列表资产范围。</summary>
        /// <param name="value">当前数据库、项目全部任务或未登记任务。</param>
        private void OnScopeChanged(string value)
        {
            scope = value;
            SessionState.SetString(ScopeSessionKey, scope);
            RefreshDefinitions(true);
        }

        /// <summary>切换任务分类筛选并保存会话状态。</summary>
        /// <param name="value">分类显示名称。</param>
        private void OnCategoryFilterChanged(string value)
        {
            categoryFilter = value;
            SessionState.SetString(CategorySessionKey, categoryFilter);
            RefreshDefinitions(true);
        }

        /// <summary>切换排序字段或排序方向。</summary>
        /// <param name="field">TaskId、分类或标题。</param>
        /// <param name="direction">升序或降序。</param>
        private void OnSortChanged(string field, string direction)
        {
            sort = field;
            sortDirection = direction;
            SessionState.SetString(SortSessionKey, sort);
            SessionState.SetString(DirectionSessionKey, sortDirection);
            RefreshDefinitions(false);
        }

        /// <summary>创建新任务并加入当前数据库。</summary>
        /// <param name="categoryId">任务分类。</param>
        /// <param name="suffixId">可选 TaskId 后缀。</param>
        private void OnCreateRequested(string categoryId, string suffixId)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || database == null) return;
            try
            {
                selectedDefinition = service.CreateDefinition(database, categoryId, suffixId);
                scope = "当前数据库";
                SessionState.SetString(ScopeSessionKey, scope);
                RefreshDefinitions(true);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[TaskConfigEditor] 新建任务失败，category={categoryId}：{exception.Message}");
                view.SetListStatus($"新建失败：{exception.Message}");
            }
        }

        /// <summary>根据行菜单命令执行资产操作。</summary>
        /// <param name="definition">命令目标。</param>
        /// <param name="command">操作名称及可能的重命名参数。</param>
        private void OnTaskCommandRequested(TaskDefinition definition, string command)
        {
            if (definition == null) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode && command != "locate") return;
            try
            {
                if (command == "locate") service.PingDefinition(definition);
                else if (command == "duplicate") selectedDefinition = service.DuplicateDefinition(database, definition);
                else if (command == "add") service.AddToDatabase(database, definition);
                else if (command == "remove") service.RemoveFromDatabase(database, definition);
                else if (command == "delete")
                {
                    string rawTaskId = TaskConfigEditorService.GetRawTaskId(definition);
                    if (!EditorUtility.DisplayDialog("删除任务资产", $"永久删除 {rawTaskId}？此操作不能通过 Undo 恢复。", "删除资产", "取消")) return;
                    if (selectedDefinition == definition) selectedDefinition = null;
                    service.DeleteDefinition(definition);
                }
                else if (command.StartsWith("rename:", StringComparison.Ordinal))
                {
                    string title = command.Substring("rename:".Length);
                    string error = service.RenameDefinition(definition, title);
                    if (!string.IsNullOrEmpty(error)) throw new IOException(error);
                }

                RefreshDefinitions(command == "duplicate" || command == "delete" || command.StartsWith("rename:", StringComparison.Ordinal) || command == "add" || command == "remove");
            }
            catch (Exception exception)
            {
                Debug.LogError($"[TaskConfigEditor] 任务命令失败，taskId={TaskConfigEditorService.GetRawTaskId(definition)}，command={command}：{exception.Message}", definition);
                view.SetListStatus($"操作失败：{exception.Message}");
            }
        }

        /// <summary>执行数据库校验并显示当前任务的局部校验结果。</summary>
        private void OnValidateRequested()
        {
            string databaseResult = service.ValidateDatabase(database);
            view.SetListStatus(databaseResult);
            view.SetDetailStatus(service.ValidateDefinition(database, selectedDefinition));
        }

        /// <summary>刷新任务资产索引并重新绑定当前详情。</summary>
        private void OnRefreshRequested() => RefreshDefinitions(true);

        /// <summary>新增或更新 TaskId 后缀选项。</summary>
        /// <param name="categoryId">任务分类。</param>
        /// <param name="oldSuffixId">被更新的旧标识；新增时为空。</param>
        /// <param name="newSuffixId">用户输入的稳定后缀。</param>
        /// <param name="displayName">后缀说明。</param>
        /// <param name="updating">是否更新已有选项。</param>
        private void OnSuffixSaveRequested(string categoryId, string oldSuffixId, string newSuffixId, string displayName, bool updating)
        {
            try
            {
                if (updating) settings.UpdateSuffix(categoryId, oldSuffixId, newSuffixId, displayName);
                else settings.AddSuffix(categoryId, newSuffixId, displayName);
                view.RefreshSuffixes(categoryId);
                Debug.Log($"[TaskConfigEditor] 已保存 TaskId 后缀，category={categoryId}，suffix={newSuffixId}。");
            }
            catch (Exception exception)
            {
                Debug.LogError($"[TaskConfigEditor] 保存后缀失败，category={categoryId}：{exception.Message}");
                view.SetListStatus($"保存后缀失败：{exception.Message}");
            }
        }

        /// <summary>删除 TaskId 后缀设置并刷新选择列表。</summary>
        /// <param name="categoryId">任务分类。</param>
        /// <param name="suffixId">待删除稳定文本。</param>
        private void OnSuffixRemoveRequested(string categoryId, string suffixId)
        {
            settings.RemoveSuffix(categoryId, suffixId);
            view.RefreshSuffixes(categoryId);
        }

        /// <summary>持久化新任务资产目录。</summary>
        /// <param name="folder">项目 Assets 下的目标目录。</param>
        private void OnDefinitionFolderChanged(string folder)
        {
            try
            {
                settings.SetDefinitionFolder(folder);
                view.SetListStatus($"新任务资产目录已保存：{settings.DefinitionFolder}");
            }
            catch (Exception exception)
            {
                view.SetListStatus($"目录设置失败：{exception.Message}");
                Debug.LogError($"[TaskConfigEditor] 资产目录设置失败：{exception.Message}");
            }
        }

        /// <summary>任务属性变化后刷新派生列表数据与错误提示。</summary>
        /// <param name="definition">发生配置变化的任务。</param>
        private void OnPropertiesChanged(TaskDefinition definition)
        {
            if (definition != selectedDefinition) return;
            view.SetDetailStatus(service.ValidateDefinition(database, definition));
            ScheduleRefresh(false);
        }

        #endregion

        #region Unity 生命周期回调

        /// <summary>监听项目资产变化并安排一次列表更新。</summary>
        private void OnProjectChanged() => ScheduleRefresh();

        /// <summary>撤销或重做后重新绑定序列化对象。</summary>
        /// <param name="undoRedoInfo">Undo 系统提供的分组说明。</param>
        private void OnUndoRedo(in UndoRedoInfo undoRedoInfo)
        {
            TaskIdEditorCatalog.Invalidate();
            view.SetTaskIdSourceDatabase(settings.TaskIdSourceDatabase);
            ScheduleRefresh();
        }

        /// <summary>在进入或退出 Play Mode 后切换编辑器只读状态。</summary>
        /// <param name="state">Play Mode 状态转换。</param>
        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            view.SetReadOnly(EditorApplication.isPlayingOrWillChangePlaymode);
            ScheduleRefresh();
        }

        /// <summary>将多次资产或 Undo 通知合并为一次延迟刷新。</summary>
        /// <param name="rebindDetails">刷新后是否重建当前 SerializedObject 绑定。</param>
        private void ScheduleRefresh(bool rebindDetails = true)
        {
            scheduledRefreshRebindDetails |= rebindDetails;
            if (refreshScheduled) return;
            refreshScheduled = true;
            EditorApplication.delayCall += ExecuteScheduledRefresh;
        }

        /// <summary>执行合并后的刷新并允许后续项目变更重新排队。</summary>
        private void ExecuteScheduledRefresh()
        {
            EditorApplication.delayCall -= ExecuteScheduledRefresh;
            refreshScheduled = false;
            bool rebindDetails = scheduledRefreshRebindDetails;
            scheduledRefreshRebindDetails = false;
            if (!disposed) RefreshDefinitions(rebindDetails);
        }

        #endregion
    }
}
#endif
