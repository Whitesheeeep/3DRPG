#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using WS_Modules.GAS.GameplayCue;

namespace WS_Modules.GAS.Editor
{
    /// <summary>协调 Cue Database、资产服务和 UI View 的编辑器控制器。</summary>
    public sealed class GameplayCueEditorController : IDisposable
    {
        #region 字段与状态
        private readonly IGameplayCueEditorView view;
        private readonly GameplayCueEditorService service;
        private readonly Dictionary<GameplayCueData, GameplayCueValidationSeverity> validationStates = new();
        private readonly List<GameplayCueValidationIssue> validationIssues = new();
        private GameplayCueDatabase currentDatabase;
        private GameplayCueData currentCue;
        private GameplayCueData pendingPresentationCue;
        private string search = string.Empty;
        private bool validationScheduled;
        private bool presentationRefreshScheduled;
        private bool disposed;

        #endregion

        #region 属性与生命周期

        /// <summary>创建 Cue 编辑控制器并订阅 View 意图。</summary>
        /// <param name="view">Cue 编辑 View。</param>
        public GameplayCueEditorController(IGameplayCueEditorView view)
        {
            this.view = view ?? throw new ArgumentNullException(nameof(view));
            service = new GameplayCueEditorService();
            RegisterCallbacks();
        }

        /// <summary>获取当前数据库。</summary>
        public GameplayCueDatabase CurrentDatabase => currentDatabase;

        /// <summary>获取当前 CueData。</summary>
        public GameplayCueData CurrentCue => currentCue;

        /// <summary>切换数据库并刷新列表。</summary>
        /// <param name="database">目标数据库。</param>
        /// <param name="restoreSelection">是否恢复 SessionState。</param>
        public void SetDatabase(GameplayCueDatabase database, bool restoreSelection)
        {
            if (disposed) return;
            CancelScheduledPresentationRefresh();
            currentDatabase = database;
            ClearValidationCache();
            GameplayCueEditorSession.SetDatabase(database);
            search = restoreSelection ? GameplayCueEditorSession.GetSearch() : string.Empty;
            view.SetDatabase(database);
            view.SetSearch(search);

            GameplayCueData restoredCue = restoreSelection ? GameplayCueEditorSession.GetCue() : null;
            currentCue = IsRegistered(restoredCue) ? restoredCue : null;
            RefreshView();
            ScheduleValidation();
        }

        /// <summary>在当前数据库中选择 CueData。</summary>
        /// <param name="cue">要选择的 CueData。</param>
        /// <param name="restoreSelection">是否恢复当前 SessionState。</param>
        public void SetCue(GameplayCueData cue, bool restoreSelection)
        {
            if (disposed) return;
            CancelScheduledPresentationRefresh();
            currentCue = IsRegistered(cue) ? cue : null;
            if (restoreSelection && currentCue == null)
            {
                GameplayCueData restored = GameplayCueEditorSession.GetCue();
                currentCue = IsRegistered(restored) ? restored : null;
            }

            GameplayCueEditorSession.SetCue(currentCue);
            RefreshView();
        }

        /// <summary>释放事件订阅和待执行校验。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            UnregisterCallbacks();
            if (validationScheduled)
            {
                EditorApplication.delayCall -= RunScheduledValidation;
                validationScheduled = false;
            }
            CancelScheduledPresentationRefresh();
        }

        #endregion

        #region 事件订阅

        // 注册 View、Undo 和项目变化回调。
        private void RegisterCallbacks()
        {
            view.DatabaseChanged += OnDatabaseChanged;
            view.SearchChanged += OnSearchChanged;
            view.CueSelectionChanged += OnCueSelectionChanged;
            view.CreateDatabaseRequested += OnCreateDatabaseRequested;
            view.CreateCueRequested += OnCreateCueRequested;
            view.AddExistingCueRequested += OnAddExistingCueRequested;
            view.DuplicateCueRequested += OnDuplicateCueRequested;
            view.RemoveFromDatabaseRequested += OnRemoveCueRequested;
            view.DeleteCueRequested += OnDeleteCueRequested;
            view.PingCueRequested += service.PingCue;
            view.PingPrefabRequested += service.PingPrefab;
            view.RenameCueSubmitted += OnRenameCueSubmitted;
            view.CueSerializedChanged += OnCueSerializedChanged;
            view.RefreshRequested += OnRefreshRequested;
            EditorApplication.projectChanged += OnProjectChanged;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        // 释放所有回调，防止主窗口切换后旧 Controller 继续操作资产。
        private void UnregisterCallbacks()
        {
            view.DatabaseChanged -= OnDatabaseChanged;
            view.SearchChanged -= OnSearchChanged;
            view.CueSelectionChanged -= OnCueSelectionChanged;
            view.CreateDatabaseRequested -= OnCreateDatabaseRequested;
            view.CreateCueRequested -= OnCreateCueRequested;
            view.AddExistingCueRequested -= OnAddExistingCueRequested;
            view.DuplicateCueRequested -= OnDuplicateCueRequested;
            view.RemoveFromDatabaseRequested -= OnRemoveCueRequested;
            view.DeleteCueRequested -= OnDeleteCueRequested;
            view.PingCueRequested -= service.PingCue;
            view.PingPrefabRequested -= service.PingPrefab;
            view.RenameCueSubmitted -= OnRenameCueSubmitted;
            view.CueSerializedChanged -= OnCueSerializedChanged;
            view.RefreshRequested -= OnRefreshRequested;
            EditorApplication.projectChanged -= OnProjectChanged;
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        #endregion

        #region 用户意图处理

        // 数据库 ObjectField 改变后重新建立当前页面状态。
        private void OnDatabaseChanged(GameplayCueDatabase database) => SetDatabase(database, false);

        // 搜索只改变列表，不修改 Database 内容。
        private void OnSearchChanged(string value)
        {
            search = value ?? string.Empty;
            GameplayCueEditorSession.SetSearch(search);
            RefreshCueList();
        }

        // 选择变化只绑定详情并保存 Cue GUID。
        private void OnCueSelectionChanged(GameplayCueData cue)
        {
            CancelScheduledPresentationRefresh();
            currentCue = IsRegistered(cue) ? cue : null;
            GameplayCueEditorSession.SetCue(currentCue);
            view.BindCue(currentCue);
            RenderCurrentIssues();
        }

        // 普通字段写回只排队局部显示刷新和校验，不重新创建当前详情 PropertyField。
        private void OnCueSerializedChanged()
        {
            if (disposed || currentCue == null) return;
            pendingPresentationCue = currentCue;
            if (!presentationRefreshScheduled)
            {
                presentationRefreshScheduled = true;
                EditorApplication.delayCall += RunScheduledPresentationRefresh;
            }

            ScheduleValidation();
        }

        // 创建数据库使用 Unity 标准项目路径对话框。
        private void OnCreateDatabaseRequested()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "创建 Gameplay Cue Database",
                "GameplayCueDatabase",
                "asset",
                "选择 Cue Database 保存路径。");
            if (string.IsNullOrEmpty(path)) return;

            GameplayCueDatabase database = service.CreateDatabase(path, out string error);
            if (database == null)
            {
                view.ShowError("创建 Cue Database 失败", error);
                return;
            }

            SetDatabase(database, false);
        }

        // 创建 CueData 并由 Service 自动加入当前 Database。
        private void OnCreateCueRequested()
        {
            if (currentDatabase == null)
            {
                view.ShowError("无法创建 Cue", "请先选择或创建 Gameplay Cue Database。");
                return;
            }

            string path = EditorUtility.SaveFilePanelInProject(
                "创建 Gameplay Cue",
                "GameplayCueData",
                "asset",
                "选择 CueData 保存路径。");
            if (string.IsNullOrEmpty(path)) return;

            if (!service.TryCreateCue(currentDatabase, path, out GameplayCueData cue, out string error))
            {
                view.ShowError("创建 Cue 失败", error);
                return;
            }

            currentCue = cue;
            GameplayCueEditorSession.SetCue(cue);
            ClearValidationCache();
            RefreshView();
            ScheduleValidation();
        }

        // 通过文件选择器选择已有 CueData，避免在 Controller 中暴露 ObjectField。
        private void OnAddExistingCueRequested()
        {
            if (currentDatabase == null)
            {
                view.ShowError("无法添加 Cue", "请先选择 Gameplay Cue Database。");
                return;
            }

            string selectedPath = EditorUtility.OpenFilePanel("添加 GameplayCueData", "Assets", "asset");
            if (string.IsNullOrEmpty(selectedPath)) return;
            string assetPath = ToProjectAssetPath(selectedPath);
            GameplayCueData cue = AssetDatabase.LoadAssetAtPath<GameplayCueData>(assetPath);
            if (cue == null)
            {
                view.ShowError("添加 Cue 失败", "所选文件不是 GameplayCueData 资产。");
                return;
            }

            if (!service.TryAddCue(currentDatabase, cue, out string error))
            {
                view.ShowError("添加 Cue 失败", error);
                return;
            }

            currentCue = cue;
            GameplayCueEditorSession.SetCue(cue);
            ClearValidationCache();
            RefreshView();
            ScheduleValidation();
        }

        // 复制当前 CueData，副本沿用原作者配置并自动注册到当前 Database。
        private void OnDuplicateCueRequested()
        {
            if (currentDatabase == null || currentCue == null) return;
            if (!service.TryDuplicateCue(
                    currentDatabase,
                    currentCue,
                    out GameplayCueData copy,
                    out string error))
            {
                view.ShowError("复制 Cue 失败", error);
                return;
            }

            currentCue = copy;
            GameplayCueEditorSession.SetCue(copy);
            ClearValidationCache();
            RefreshView();
            ScheduleValidation();
        }

        // 仅解除注册，不删除资产。
        private void OnRemoveCueRequested(GameplayCueData cue)
        {
            if (cue == null || !view.Confirm("移除 Cue", $"仅从当前 Database 移除 {cue.name}？")) return;
            if (!service.TryRemoveCue(currentDatabase, cue, out string error))
            {
                view.ShowError("移除 Cue 失败", error);
                return;
            }

            if (ReferenceEquals(currentCue, cue)) currentCue = null;
            ClearValidationCache();
            RefreshView();
            ScheduleValidation();
        }

        // 从 Database 移除后将资产移动到回收站。
        private void OnDeleteCueRequested(GameplayCueData cue)
        {
            if (cue == null || !view.Confirm("删除 Cue", $"将 {cue.name} 移入回收站并解除注册？")) return;
            if (!service.TryDeleteCue(currentDatabase, cue, out string error))
            {
                view.ShowError("删除 Cue 失败", error);
                return;
            }

            if (ReferenceEquals(currentCue, cue)) currentCue = null;
            ClearValidationCache();
            RefreshView();
            ScheduleValidation();
        }

        // 重命名失败时恢复输入框，成功后重新排序和校验。
        private void OnRenameCueSubmitted(GameplayCueRenameRequest request)
        {
            if (!service.TryRenameCue(request.Cue, request.Name, out string error))
            {
                view.RestoreCueRename(request.Cue, request.Name);
                view.ShowError("Cue 重命名失败", error);
                return;
            }

            RefreshView();
            ScheduleValidation();
        }

        // 手动刷新立即重绘并把校验合并到下一次 Editor 更新。
        private void OnRefreshRequested()
        {
            CancelScheduledPresentationRefresh();
            RefreshView();
            ScheduleValidation();
        }

        // 项目资产变化不在 Unity 回调中直接重建 PropertyField。
        private void OnProjectChanged()
        {
            CancelScheduledPresentationRefresh();
            ClearValidationCache();
            RefreshView();
            ScheduleValidation();
        }

        // Undo/Redo 后重新读取 Database 和当前详情。
        private void OnUndoRedo()
        {
            CancelScheduledPresentationRefresh();
            ClearValidationCache();
            RefreshView();
            ScheduleValidation();
        }

        #endregion

        #region 刷新、校验与内部辅助

        // 刷新可见列表和详情绑定。
        private void RefreshView()
        {
            if (disposed) return;
            view.SetDatabase(currentDatabase);
            view.SetSearch(search);
            RefreshCueList();
            view.BindCue(currentCue);
            RenderCurrentIssues();
        }

        // 搜索或列表内容变化时只刷新左侧数据，不触碰当前详情绑定。
        private void RefreshCueList()
        {
            if (disposed) return;
            view.RenderCues(service.FindVisibleCues(currentDatabase, search), currentCue);
        }

        // 立即渲染当前 Cue 的缓存校验结果；完整校验由 delayCall 负责。
        private void RenderCurrentIssues()
        {
            view.RenderValidation(CollectCurrentIssues(validationIssues));
            view.RenderValidationStates(validationStates);
        }

        // 提取当前 Cue 的问题，数据库级问题在列表之外显示。
        private List<GameplayCueValidationIssue> CollectCurrentIssues(
            IReadOnlyList<GameplayCueValidationIssue> issues)
        {
            var currentIssues = new List<GameplayCueValidationIssue>();
            if (issues == null) return currentIssues;
            for (int i = 0; i < issues.Count; i++)
            {
                if (issues[i].Cue == null || ReferenceEquals(issues[i].Cue, currentCue))
                    currentIssues.Add(issues[i]);
            }

            return currentIssues;
        }

        // 合并同一 Editor 周期内的重复校验请求。
        private void ScheduleValidation()
        {
            if (disposed || validationScheduled) return;
            validationScheduled = true;
            EditorApplication.delayCall += RunScheduledValidation;
        }

        // 执行校验、归并行严重程度并刷新当前详情。
        private void RunScheduledValidation()
        {
            validationScheduled = false;
            if (disposed) return;

            List<GameplayCueValidationIssue> issues = service.Validate(currentDatabase);
            validationIssues.Clear();
            validationIssues.AddRange(issues);
            validationStates.Clear();
            for (int i = 0; i < issues.Count; i++)
            {
                GameplayCueValidationIssue issue = issues[i];
                if (issue.Cue == null) continue;
                GameplayCueValidationSeverity current = validationStates.TryGetValue(issue.Cue, out GameplayCueValidationSeverity value)
                    ? value
                    : GameplayCueValidationSeverity.Info;
                if (issue.Severity > current) validationStates[issue.Cue] = issue.Severity;
            }

            view.RenderValidationStates(validationStates);
            view.RenderValidation(CollectCurrentIssues(validationIssues));
        }

        // 在下一次 Editor 更新中局部刷新字段派生显示；同一周期的多次字段事件只执行一次。
        private void RunScheduledPresentationRefresh()
        {
            presentationRefreshScheduled = false;
            GameplayCueData cue = pendingPresentationCue;
            pendingPresentationCue = null;
            if (disposed || cue == null || !ReferenceEquals(cue, currentCue)) return;
            view.RefreshCuePresentation(cue);
        }

        // 取消尚未执行的局部显示刷新，避免旧 Cue 在切换选择后更新新页面。
        private void CancelScheduledPresentationRefresh()
        {
            if (presentationRefreshScheduled)
                EditorApplication.delayCall -= RunScheduledPresentationRefresh;
            presentationRefreshScheduled = false;
            pendingPresentationCue = null;
        }

        // 数据库或资产结构变化后清空旧校验，避免短暂显示其他目标的问题。
        private void ClearValidationCache()
        {
            validationIssues.Clear();
            validationStates.Clear();
        }

        // 判断 CueData 是否仍然注册在当前 Database 中。
        private bool IsRegistered(GameplayCueData cue)
        {
            if (cue == null || currentDatabase == null) return false;
            IReadOnlyList<GameplayCueData> cues = currentDatabase.Cues;
            for (int i = 0; i < cues.Count; i++)
                if (ReferenceEquals(cues[i], cue)) return true;
            return false;
        }

        // 将系统文件选择器返回的绝对路径转换为 Unity Assets 路径。
        private static string ToProjectAssetPath(string absolutePath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName.Replace('\\', '/');
            string normalized = absolutePath.Replace('\\', '/');
            string prefix = projectRoot + "/";
            return normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? normalized.Substring(prefix.Length)
                : string.Empty;
        }

        #endregion
    }
}
#endif
