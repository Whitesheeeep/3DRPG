#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using WS_Modules.GAS.TAG;

namespace WSFrame.GAS.Editor
{
    /// <summary>协调 Gameplay Tag Editor 的用户意图、作者数据服务和 View 刷新。</summary>
    public sealed class GameplayTagEditorController : IDisposable
    {
        #region 字段
        private readonly IGameplayTagEditorView view;
        private GameplayTagDatabase database;
        private GameplayTagEditorService service;

        private string selectedGuid = string.Empty;
        private string renamingGuid = string.Empty;
        private string search = string.Empty;

        private readonly HashSet<string> expandedGuids = new(StringComparer.Ordinal);
        private readonly HashSet<string> renderedGuids = new(StringComparer.Ordinal);
        #endregion

        /// <summary>创建 Controller 并订阅 View 用户意图。</summary>
        /// <param name="view">不暴露 UI Toolkit 控件的编辑器 View。</param>
        public GameplayTagEditorController(IGameplayTagEditorView view)
        {
            this.view = view ?? throw new ArgumentNullException(nameof(view));
            SubscribeView();
            Undo.undoRedoPerformed += OnUndoRedo;
            search = GameplayTagEditorSession.GetSearch();
            LoadExpandedSession();
            view.SetSearchText(search);
            RefreshAll();
        }

        #region 生命周期
        /// <summary>保存展开状态并注销 View 与 Undo 事件。</summary>
        public void Dispose()
        {
            CaptureExpandedState();
            Undo.undoRedoPerformed -= OnUndoRedo;
            UnsubscribeView();
        }
        #endregion

        #region 数据库与刷新
        /// <summary>切换数据库并可恢复 SessionState 中的节点选择。</summary>
        /// <param name="value">目标数据库。</param>
        /// <param name="restoreSelection">是否恢复上次选择的节点 Guid。</param>
        public void SetDatabase(GameplayTagDatabase value, bool restoreSelection)
        {
            if (database != value) CaptureExpandedState();
            database = value;
            service = database == null ? null : new GameplayTagEditorService(database);
            view.SetDatabase(database);
            GameplayTagEditorSession.SetDatabase(database);
            selectedGuid = restoreSelection ? GameplayTagEditorSession.GetSelectedNodeGuid() : string.Empty;
            if (service?.FindNode(selectedGuid) == null) selectedGuid = string.Empty;
            if (!restoreSelection) GameplayTagEditorSession.SetSelectedNodeGuid(string.Empty);
            renamingGuid = string.Empty;
            RefreshAll();
        }

        // 刷新全部展示投影。
        private void RefreshAll()
        {
            RefreshTree();
            RefreshDetails();
            RefreshValidation();
            RefreshBakeState();
        }

        // 根据作者数据、搜索和会话状态创建中立树投影。
        private void RefreshTree()
        {
            CaptureExpandedState();
            renderedGuids.Clear();
            IReadOnlyList<GameplayTagTreeViewData> roots = Array.Empty<GameplayTagTreeViewData>();
            if (service != null)
            {
                HashSet<string> visible = BuildVisibleGuids(search);
                var children = database.EditorNodes
                    .Where(node => node != null && visible.Contains(node.Guid))
                    .GroupBy(node => node.ParentGuid ?? string.Empty)
                    .ToDictionary(group => group.Key,
                        group => group.OrderBy(node => node.Name, StringComparer.Ordinal).ToList());
                roots = BuildTreeViewData(string.Empty, 0, children);
            }
            view.RenderTree(roots, selectedGuid, expandedGuids, renamingGuid);
        }

        // 搜索结果保留命中节点祖先，使过滤树仍表达真实层级。
        private HashSet<string> BuildVisibleGuids(string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText))
                return database.EditorNodes.Where(node => node != null)
                    .Select(node => node.Guid).ToHashSet(StringComparer.Ordinal);

            var visible = new HashSet<string>(StringComparer.Ordinal);
            foreach (GameplayTagEditorNode node in database.EditorNodes.Where(node => node != null))
            {
                if (service.GetPath(node).IndexOf(searchText, StringComparison.OrdinalIgnoreCase) < 0) continue;
                GameplayTagEditorNode current = node;
                while (current != null && visible.Add(current.Guid))
                    current = string.IsNullOrEmpty(current.ParentGuid)
                        ? null
                        : service.FindNode(current.ParentGuid);
            }
            return visible;
        }

        // 递归创建只读树投影；UI Toolkit 整数 ID 由具体 View 管理。
        private List<GameplayTagTreeViewData> BuildTreeViewData(string parentGuid, int depth,
            IReadOnlyDictionary<string, List<GameplayTagEditorNode>> children)
        {
            var result = new List<GameplayTagTreeViewData>();
            if (!children.TryGetValue(parentGuid, out List<GameplayTagEditorNode> childNodes)) return result;
            foreach (GameplayTagEditorNode node in childNodes)
            {
                renderedGuids.Add(node.Guid);
                result.Add(new GameplayTagTreeViewData(
                    node.Guid, node.Name, service.GetPath(node), node.ParentGuid, depth,
                    BuildTreeViewData(node.Guid, depth + 1, children)));
            }
            return result;
        }

        // 构造选中节点详情投影。
        private void RefreshDetails()
        {
            GameplayTagEditorNode selected = FindSelectedNode();
            if (selected == null)
            {
                view.RenderDetails(new GameplayTagDetailsViewData(
                    false, string.Empty, string.Empty, string.Empty, -1, string.Empty));
                return;
            }
            int tagId = database.BakedIdHistory.TryGetValue(selected.Guid, out int bakedId) ? bakedId : -1;
            view.RenderDetails(new GameplayTagDetailsViewData(
                true, selected.Guid, selected.Name, service.GetPath(selected), tagId, selected.Description));
        }

        // 查询 Service 校验并交由 View 展示。
        private void RefreshValidation() =>
            view.RenderValidation(service?.Validate() ?? new List<GameplayTagValidationIssue>());

        // 将数据库状态映射为与 Unity 控件无关的枚举。
        private void RefreshBakeState()
        {
            GameplayTagBakeViewState state = database == null
                ? GameplayTagBakeViewState.NoDatabase
                : database.BakeDirty ? GameplayTagBakeViewState.BakeRequired : GameplayTagBakeViewState.Baked;
            view.RenderBakeState(state);
        }
        #endregion

        #region 会话状态
        // 用当前可见树展开状态更新缓存，同时保留搜索隐藏节点的展开状态。
        private void CaptureExpandedState()
        {
            IReadOnlyCollection<string> currentExpanded = view.GetExpandedNodeGuids();
            expandedGuids.ExceptWith(renderedGuids);
            expandedGuids.UnionWith(currentExpanded);
            GameplayTagEditorSession.SetExpandedGuids(string.Join(",", expandedGuids));
        }

        // 从 SessionState 加载展开 Guid。
        private void LoadExpandedSession()
        {
            expandedGuids.Clear();
            string value = GameplayTagEditorSession.GetExpandedGuids();
            if (string.IsNullOrEmpty(value)) return;
            foreach (string guid in value.Split(','))
                if (!string.IsNullOrEmpty(guid)) expandedGuids.Add(guid);
        }
        #endregion

        #region View 订阅
        // 订阅全部用户意图，Controller 是唯一协调入口。
        private void SubscribeView()
        {
            view.DatabaseChanged += OnDatabaseChanged;
            view.SearchChanged += OnSearchChanged;
            view.SelectionChanged += OnSelectionChanged;
            view.AddRootRequested += OnAddRootRequested;
            view.AddChildRequested += OnAddChildRequested;
            view.DeleteRequested += OnDeleteRequested;
            view.BakeRequested += OnBakeRequested;
            view.RenameRequested += OnRenameRequested;
            view.RenameCancelled += OnRenameCancelled;
            view.NameEditRequested += OnNameEditRequested;
            view.PathEditRequested += OnPathEditRequested;
            view.DescriptionEditRequested += OnDescriptionEditRequested;
            view.MoveRequested += OnMoveRequested;
        }

        // 对称注销全部用户意图，避免窗口重建后重复响应。
        private void UnsubscribeView()
        {
            view.DatabaseChanged -= OnDatabaseChanged;
            view.SearchChanged -= OnSearchChanged;
            view.SelectionChanged -= OnSelectionChanged;
            view.AddRootRequested -= OnAddRootRequested;
            view.AddChildRequested -= OnAddChildRequested;
            view.DeleteRequested -= OnDeleteRequested;
            view.BakeRequested -= OnBakeRequested;
            view.RenameRequested -= OnRenameRequested;
            view.RenameCancelled -= OnRenameCancelled;
            view.NameEditRequested -= OnNameEditRequested;
            view.PathEditRequested -= OnPathEditRequested;
            view.DescriptionEditRequested -= OnDescriptionEditRequested;
            view.MoveRequested -= OnMoveRequested;
        }
        #endregion

        #region 事件处理
        // 用户切换数据库时清空旧数据库选择。
        private void OnDatabaseChanged(GameplayTagDatabase value) => SetDatabase(value, false);

        // 搜索变化后持久化并重建过滤树。
        private void OnSearchChanged(string value)
        {
            search = value ?? string.Empty;
            GameplayTagEditorSession.SetSearch(search);
            RefreshTree();
        }

        // Tree 单选变化后只保存 Guid，避免跨 Undo 持有失效节点。
        private void OnSelectionChanged(string guid)
        {
            selectedGuid = guid ?? string.Empty;
            GameplayTagEditorSession.SetSelectedNodeGuid(selectedGuid);
            RefreshDetails();
        }

        // 创建根节点并立即进入行内重命名。
        private void OnAddRootRequested() => AddNode(string.Empty);

        // 创建当前节点的子节点；没有选择时退化为创建根节点。
        private void OnAddChildRequested() => AddNode(selectedGuid);

        // 执行带确认的级联删除。
        private void OnDeleteRequested()
        {
            GameplayTagEditorNode selected = FindSelectedNode();
            if (selected == null) return;
            int count = service.CollectSubtreeGuids(selected.Guid).Count;
            string message = count > 1
                ? $"将级联删除 {count} 个 Tag，所有已分配 ID 永久废弃。\n根路径：{service.GetPath(selected)}"
                : $"删除 Tag：{service.GetPath(selected)}？";
            if (!view.ConfirmDelete(message)) return;
            service.DeleteSubtree(selected);
            selectedGuid = string.Empty;
            renamingGuid = string.Empty;
            GameplayTagEditorSession.SetSelectedNodeGuid(string.Empty);
            RefreshAll();
        }

        // 按意图携带的稳定 Guid 重新查询节点，避免依赖 TreeView 选择事件与双击事件的先后顺序。
        private void OnRenameRequested(string guid)
        {
            GameplayTagEditorNode node = service?.FindNode(guid ?? string.Empty);
            if (node == null) return;
            selectedGuid = node.Guid;
            renamingGuid = node.Guid;
            GameplayTagEditorSession.SetSelectedNodeGuid(selectedGuid);
            RefreshTree();
            RefreshDetails();
        }

        // Escape 取消指定节点的行内重命名。
        private void OnRenameCancelled(string guid)
        {
            if (renamingGuid != (guid ?? string.Empty)) return;
            renamingGuid = string.Empty;
            RefreshTree();
        }

        // 名称提交同时服务详情字段和 Tree 行内重命名。
        private void OnNameEditRequested(GameplayTagTextEditRequest request)
        {
            GameplayTagEditorNode node = service?.FindNode(request.NodeGuid);
            if (node == null) return;
            if (!service.TryRename(node, request.Value, out string error))
            {
                view.ShowError("Rename Gameplay Tag", error);
                RefreshDetails();
                RefreshTree();
                return;
            }
            selectedGuid = node.Guid;
            renamingGuid = string.Empty;
            GameplayTagEditorSession.SetSelectedNodeGuid(selectedGuid);
            RefreshAll();
        }

        // 完整 Path 提交可能创建父链并移动节点，成功后重建全部投影。
        private void OnPathEditRequested(GameplayTagTextEditRequest request)
        {
            GameplayTagEditorNode node = service?.FindNode(request.NodeGuid);
            if (node == null) return;
            if (!service.TrySetPath(node, request.Value, out string error))
            {
                view.ShowError("Set Gameplay Tag Path", error);
                RefreshDetails();
                return;
            }
            selectedGuid = node.Guid;
            GameplayTagEditorSession.SetSelectedNodeGuid(selectedGuid);
            RefreshAll();
        }

        // 描述不改变树关系，仅刷新校验状态。
        private void OnDescriptionEditRequested(GameplayTagTextEditRequest request)
        {
            GameplayTagEditorNode node = service?.FindNode(request.NodeGuid);
            if (node == null) return;
            service.SetDescription(node, request.Value);
            RefreshValidation();
        }

        // 把 View 解析出的父 Guid 交给 Service 完成业务校验。
        private void OnMoveRequested(GameplayTagMoveRequest request)
        {
            GameplayTagEditorNode node = service?.FindNode(request.NodeGuid);
            if (node == null) return;
            if (!service.TryMove(node, request.NewParentGuid, out string error))
                view.ShowError("Move Gameplay Tag", error);
            selectedGuid = node.Guid;
            GameplayTagEditorSession.SetSelectedNodeGuid(selectedGuid);
            RefreshAll();
        }

        // 执行完整烘焙并通过 View 显示结果。
        private void OnBakeRequested()
        {
            if (service == null) return;
            service.TryBake(out _, out string message);
            view.ShowBakeResult(message);
            RefreshAll();
        }

        // Undo/Redo 后按 Guid 重新验证选择并刷新全部展示。
        private void OnUndoRedo()
        {
            if (service?.FindNode(selectedGuid) == null) selectedGuid = string.Empty;
            GameplayTagEditorSession.SetSelectedNodeGuid(selectedGuid);
            RefreshAll();
        }
        #endregion

        #region 内部辅助
        // 创建节点、记录选择并进入行内重命名。
        private void AddNode(string parentGuid)
        {
            if (service == null) return;
            GameplayTagEditorNode node = service.AddNode(parentGuid);
            selectedGuid = node.Guid;
            renamingGuid = node.Guid;
            GameplayTagEditorSession.SetSelectedNodeGuid(selectedGuid);
            RefreshAll();
        }

        // 始终按 Guid 从 Service 重新查询当前节点。
        private GameplayTagEditorNode FindSelectedNode() => service?.FindNode(selectedGuid);
        #endregion
    }
}
#endif
