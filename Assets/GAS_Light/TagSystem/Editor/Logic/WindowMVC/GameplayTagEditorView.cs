#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.GAS.TAG;

namespace WSFrame.GAS.Editor
{
    /// <summary>使用 UI Toolkit 实现 Gameplay Tag Editor 的控件绑定、交互转发和视觉刷新。</summary>
    public sealed class GameplayTagEditorView : IGameplayTagEditorView
    {
        #region 字段
        private readonly VisualElement root;
        private readonly VisualTreeAsset rowTemplate;
        private ObjectField databaseField;
        private ToolbarSearchField searchField;
        private TreeView tagTree;
        private VisualElement treePanel;
        private VisualElement dropLevelIndicator;
        private Label dropLevelLabel;
        private Button addRootButton;
        private Button addChildButton;
        private Button deleteButton;
        private Button bakeButton;
        private Label bakeStateLabel;

        // Inspector 面板
        private TextField nameField;
        private TextField pathField;
        private TextField guidField;
        private IntegerField tagIdField;
        private TextField descriptionField;
        private HelpBox selectionHelp;
        private ScrollView validationScroll;
        private bool refreshing;
        private string selectedGuid = string.Empty;
        private string renamingGuid = string.Empty;
        private int renameRequestVersion;
        private bool disposed;
        private GameplayTagTreeViewData draggedNode;
        private RowState dropTargetRow;
        private TagDropPosition dropPosition;
        private string promotedParentGuid;
        private readonly Dictionary<int, GameplayTagTreeViewData> viewDataById = new();
        private readonly Dictionary<string, int> treeIdByGuid = new(StringComparer.Ordinal);
        private readonly HashSet<RowState> activeRows = new();
        #endregion

        #region VE 的相关 tooltip
        private const string NAME_TOOLTIP = "Gameplay Tag 的名称，必须唯一且不为空。应该与 Path 中的最后一级名称一致";
        private const string PATH_TOOLTIP = "Gameplay Tag 的完整路径，必须唯一且不为空。由父级路径和名称组成，使用 '.' 分隔。拖拽后自动修改层级命名。";
        private const string GUID_TOOLTIP = "Gameplay Tag 的唯一标识符，由系统自动生成，不应手动修改";
        #endregion

        #region 事件
        /// <inheritdoc />
        public event Action<GameplayTagDatabase> DatabaseChanged;
        /// <inheritdoc />
        public event Action<string> SearchChanged;
        /// <inheritdoc />
        public event Action<string> SelectionChanged;
        /// <inheritdoc />
        public event Action AddRootRequested;
        /// <inheritdoc />
        public event Action AddChildRequested;
        /// <inheritdoc />
        public event Action DeleteRequested;
        /// <inheritdoc />
        public event Action BakeRequested;
        /// <inheritdoc />
        public event Action<string> RenameRequested;
        /// <inheritdoc />
        public event Action<string> RenameCancelled;
        /// <inheritdoc />
        public event Action<GameplayTagTextEditRequest> NameEditRequested;
        /// <inheritdoc />
        public event Action<GameplayTagTextEditRequest> PathEditRequested;
        /// <inheritdoc />
        public event Action<GameplayTagTextEditRequest> DescriptionEditRequested;
        /// <inheritdoc />
        public event Action<GameplayTagMoveRequest> MoveRequested;
        #endregion

        /// <summary>创建并初始化 UI Toolkit View。</summary>
        public GameplayTagEditorView(VisualElement root, VisualTreeAsset rowTemplate)
        {
            this.root = root ?? throw new ArgumentNullException(nameof(root));
            this.rowTemplate = rowTemplate ?? throw new ArgumentNullException(nameof(rowTemplate));
            QueryElements();
            ConfigureControls();
            RegisterEvents();
        }

        #region 生命周期
        /// <summary>注销窗口级控件事件并清理 TreeView 委托。</summary>
        public void Dispose()
        {
            disposed = true;
            renameRequestVersion++;
            databaseField.UnregisterValueChangedCallback(OnDatabaseChanged);
            searchField.UnregisterValueChangedCallback(OnSearchChanged);
            addRootButton.clicked -= OnAddRootClicked;
            addChildButton.clicked -= OnAddChildClicked;
            deleteButton.clicked -= OnDeleteClicked;
            bakeButton.clicked -= OnBakeClicked;
            tagTree.selectionChanged -= OnSelectionChanged;
            root.UnregisterCallback<KeyDownEvent>(OnRootKeyDown, TrickleDown.TrickleDown);
            nameField.UnregisterValueChangedCallback(OnNameChanged);
            pathField.UnregisterValueChangedCallback(OnPathChanged);
            descriptionField.UnregisterValueChangedCallback(OnDescriptionChanged);
            tagTree.makeItem = null;
            tagTree.bindItem = null;
            CancelDrag();
            foreach (RowState row in activeRows) row.CancelTransientState();
            activeRows.Clear();
        }
        #endregion

        #region 展示操作
        /// <inheritdoc />
        public void SetDatabase(GameplayTagDatabase database) => databaseField.SetValueWithoutNotify(database);

        /// <inheritdoc />
        public void SetSearchText(string search) => searchField.SetValueWithoutNotify(search ?? string.Empty);

        /// <inheritdoc />
        public void RenderTree(IReadOnlyList<GameplayTagTreeViewData> roots, string selectedNodeGuid,
            IReadOnlyCollection<string> expandedGuids, string inlineRenamingGuid)
        {
            refreshing = true;
            selectedGuid = selectedNodeGuid ?? string.Empty;
            renamingGuid = inlineRenamingGuid ?? string.Empty;
            viewDataById.Clear();
            treeIdByGuid.Clear();
            List<TreeViewItemData<GameplayTagTreeViewData>> items =
                BuildTreeItems(roots ?? Array.Empty<GameplayTagTreeViewData>());
            tagTree.SetRootItems(items);
            tagTree.Rebuild();
            if (expandedGuids != null)
                foreach (string guid in expandedGuids)
                    if (treeIdByGuid.TryGetValue(guid, out int id))
                        tagTree.ExpandItem(id);
            if (treeIdByGuid.TryGetValue(selectedGuid, out int selectedId)) tagTree.SetSelectionById(selectedId);
            else tagTree.ClearSelection();
            refreshing = false;
        }

        /// <inheritdoc />
        public void RenderDetails(GameplayTagDetailsViewData details)
        {
            refreshing = true;
            nameField.SetEnabled(details.HasSelection);
            pathField.SetEnabled(details.HasSelection);
            descriptionField.SetEnabled(details.HasSelection);
            selectionHelp.style.display = details.HasSelection ? DisplayStyle.None : DisplayStyle.Flex;
            nameField.SetValueWithoutNotify(details.Name);
            pathField.SetValueWithoutNotify(details.Path);
            guidField.SetValueWithoutNotify(details.Guid);
            tagIdField.SetValueWithoutNotify(details.TagId);
            descriptionField.SetValueWithoutNotify(details.Description);
            refreshing = false;
        }

        /// <inheritdoc />
        public void RenderValidation(IReadOnlyList<GameplayTagValidationIssue> issues)
        {
            validationScroll.Clear();
            if (issues == null) return;
            foreach (GameplayTagValidationIssue issue in issues)
            {
                var label = new Label(issue.ToString());
                label.AddToClassList("gas-tag-validation-item");
                label.AddToClassList(issue.Severity == GameplayTagValidationSeverity.Error
                    ? "gas-tag-validation-item--error"
                    : "gas-tag-validation-item--warning");
                validationScroll.Add(label);
            }
        }

        /// <inheritdoc />
        public void RenderBakeState(GameplayTagBakeViewState state)
        {
            bakeStateLabel.text = state switch
            {
                GameplayTagBakeViewState.NoDatabase => "No Database",
                GameplayTagBakeViewState.BakeRequired => "Bake Required",
                _ => "Baked"
            };
            bakeStateLabel.EnableInClassList("gas-tag-bake-state--dirty",
                state == GameplayTagBakeViewState.BakeRequired);
            bakeStateLabel.EnableInClassList("gas-tag-bake-state--clean",
                state == GameplayTagBakeViewState.Baked);
        }

        /// <inheritdoc />
        public IReadOnlyCollection<string> GetExpandedNodeGuids()
        {
            var result = new List<string>();
            foreach (KeyValuePair<string, int> pair in treeIdByGuid)
                if (tagTree.IsExpanded(pair.Value))
                    result.Add(pair.Key);
            return result;
        }

        /// <inheritdoc />
        public void ShowError(string title, string message) =>
            EditorUtility.DisplayDialog(title, message, "OK");

        /// <inheritdoc />
        public bool ConfirmDelete(string message) =>
            EditorUtility.DisplayDialog("Delete Gameplay Tag", message, "Delete", "Cancel");

        /// <inheritdoc />
        public void ShowBakeResult(string message) =>
            EditorUtility.DisplayDialog("Bake Gameplay Tags", message, "OK");
        #endregion

        #region 初始化
        // 查询 UXML 固定控件；缺少控件时尽早抛出，避免窗口进入半初始化状态。
        private void QueryElements()
        {
            databaseField = RequireElement<ObjectField>("DatabaseField");
            searchField = RequireElement<ToolbarSearchField>("SearchField");
            tagTree = RequireElement<TreeView>("TagTree");
            treePanel = RequireElement<VisualElement>("TreePanel");
            dropLevelIndicator = RequireElement<VisualElement>("DropLevelIndicator");
            dropLevelLabel = RequireElement<Label>("DropLevelLabel");
            addRootButton = RequireElement<Button>("AddRootButton");
            addChildButton = RequireElement<Button>("AddChildButton");
            deleteButton = RequireElement<Button>("DeleteButton");
            bakeButton = RequireElement<Button>("BakeButton");
            bakeStateLabel = RequireElement<Label>("BakeStateLabel");
            nameField = RequireElement<TextField>("NameField");
            pathField = RequireElement<TextField>("PathField");
            guidField = RequireElement<TextField>("GuidField");
            tagIdField = RequireElement<IntegerField>("TagIdField");
            descriptionField = RequireElement<TextField>("DescriptionField");
            selectionHelp = RequireElement<HelpBox>("SelectionHelp");
            validationScroll = RequireElement<ScrollView>("ValidationScroll");
        }

        // 配置控件类型、延迟提交和 TreeView 虚拟化。
        private void ConfigureControls()
        {
            databaseField.objectType = typeof(GameplayTagDatabase);
            databaseField.allowSceneObjects = false;

            nameField.isDelayed = true;
            nameField.tooltip = NAME_TOOLTIP;
            pathField.isDelayed = true;
            pathField.tooltip = PATH_TOOLTIP;
            guidField.SetEnabled(false);
            guidField.tooltip = GUID_TOOLTIP;
            tagIdField.SetEnabled(false);
            tagTree.reorderable = false;
            tagTree.makeItem = MakeTreeItem;
            tagTree.bindItem = BindTreeItem;
        }

        // 注册具名事件处理器，确保 Dispose 能够完整反注册。
        private void RegisterEvents()
        {
            databaseField.RegisterValueChangedCallback(OnDatabaseChanged);
            searchField.RegisterValueChangedCallback(OnSearchChanged);
            addRootButton.clicked += OnAddRootClicked;
            addChildButton.clicked += OnAddChildClicked;
            deleteButton.clicked += OnDeleteClicked;
            bakeButton.clicked += OnBakeClicked;
            tagTree.selectionChanged += OnSelectionChanged;
            root.RegisterCallback<KeyDownEvent>(OnRootKeyDown, TrickleDown.TrickleDown);
            nameField.RegisterValueChangedCallback(OnNameChanged);
            pathField.RegisterValueChangedCallback(OnPathChanged);
            descriptionField.RegisterValueChangedCallback(OnDescriptionChanged);
        }
        #endregion

        #region TreeView
        // 把中立树投影转换为 UI Toolkit TreeViewItemData，并为本次渲染分配无冲突整数 ID。
        private List<TreeViewItemData<GameplayTagTreeViewData>> BuildTreeItems(
            IReadOnlyList<GameplayTagTreeViewData> nodes)
        {
            var result = new List<TreeViewItemData<GameplayTagTreeViewData>>(nodes.Count);
            foreach (GameplayTagTreeViewData node in nodes)
            {
                int id = AllocateTreeId(node.Guid);
                viewDataById[id] = node;
                treeIdByGuid[node.Guid] = id;
                List<TreeViewItemData<GameplayTagTreeViewData>> children = BuildTreeItems(node.Children);
                result.Add(children.Count == 0
                    ? new TreeViewItemData<GameplayTagTreeViewData>(id, node)
                    : new TreeViewItemData<GameplayTagTreeViewData>(id, node, children));
            }

            return result;
        }

        // 使用稳定 Guid 哈希并处理极低概率碰撞，保证单次 TreeView 渲染 ID 唯一。
        private int AllocateTreeId(string guid)
        {
            int id = StringComparer.Ordinal.GetHashCode(guid ?? string.Empty);
            while (viewDataById.ContainsKey(id)) id = unchecked(id + 1);
            return id;
        }

        // 从行 UXML 创建可回收行，并让行状态自行管理 Pointer 和重命名交互。
        private VisualElement MakeTreeItem()
        {
            VisualElement row = rowTemplate.CloneTree();
            var state = new RowState(this, row);
            row.userData = state;
            state.Register();
            return row;
        }

        // 把虚拟化行绑定到当前树投影。
        private void BindTreeItem(VisualElement element, int index)
        {
            var state = (RowState)element.userData;
            GameplayTagTreeViewData data = tagTree.GetItemDataForIndex<GameplayTagTreeViewData>(index);
            state.Bind(data, renamingGuid == data.Guid);
        }

        // 右键行先同步当前选择，再复用现有添加子节点和删除意图。
        private void PopulateRowContextMenu(RowState row, ContextualMenuPopulateEvent evt)
        {
            if (row?.Node == null || FindAncestor<TextField>(evt.target as VisualElement) != null) return;
            if (treeIdByGuid.TryGetValue(row.Node.Guid, out int treeId)) tagTree.SetSelectionById(treeId);
            string targetGuid = row.Node.Guid;
            evt.menu.AppendAction("Add Child Tag", _ => AddChildRequested?.Invoke());
            evt.menu.AppendAction("Delete Tag", _ => DeleteRequested?.Invoke());
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Rename Tag", _ => QueueRenameRequest(targetGuid));
            evt.StopPropagation();
        }
        #endregion

        #region 拖放
        // 搜索过滤和重命名期间禁用结构拖放。
        private bool CanStartDrag(GameplayTagTreeViewData node) =>
            node != null && string.IsNullOrWhiteSpace(searchField.value) && string.IsNullOrEmpty(renamingGuid);

        // 记录由 TreeView 创建的可回收行，供缩进区域按纵向位置反查参考行。
        private void RegisterRow(RowState row) => activeRows.Add(row);

        // 进入拖放状态，结构修改只在 PointerUp 后作为意图发送。
        private void BeginDrag(GameplayTagTreeViewData node)
        {
            draggedNode = node;
            ClearDropTarget();
        }

        // 纵向选择参考行，横向决定是否进入任意层级提升模式。
        private void UpdateDrag(Vector2 panelPosition)
        {
            ClearDropTarget();
            RowState referenceRow = FindReferenceRow(panelPosition.y);
            if (referenceRow != null && TryUpdatePromoteTarget(referenceRow, panelPosition)) return;

            if (referenceRow != null && referenceRow.Node != null && referenceRow.Node.Guid != draggedNode?.Guid &&
                referenceRow.Root.worldBound.Contains(panelPosition))
            {
                float normalized = Mathf.InverseLerp(
                    referenceRow.Root.worldBound.yMin, referenceRow.Root.worldBound.yMax, panelPosition.y);
                dropPosition = normalized < 0.25f ? TagDropPosition.Before :
                    normalized > 0.75f ? TagDropPosition.After : TagDropPosition.Inside;
                dropTargetRow = referenceRow;
                referenceRow.SetDropState(dropPosition);
                return;
            }

            if (referenceRow == null && tagTree.worldBound.Contains(panelPosition))
            {
                dropPosition = TagDropPosition.Root;
                ShowLevelIndicator(panelPosition.y, tagTree.worldBound.xMin, 0f, 0);
            }
        }

        // 指针位于行内容左侧时，根据真实缩进宽度计算目标节点深度。
        private bool TryUpdatePromoteTarget(RowState referenceRow, Vector2 panelPosition)
        {
            float indentWidth = GetIndentWidth();
            if (indentWidth <= 0f || panelPosition.x >= referenceRow.Root.worldBound.xMin - indentWidth * 0.25f)
                return false;

            float rootContentX = referenceRow.Root.worldBound.xMin - referenceRow.Node.Depth * indentWidth;
            int desiredDepth = Mathf.Clamp(
                Mathf.FloorToInt((panelPosition.x - rootContentX + indentWidth * 0.5f) / indentWidth),
                0, referenceRow.Node.Depth);
            if (!TryResolveParentGuid(referenceRow.Node, desiredDepth, out string parentGuid)) return false;

            dropPosition = TagDropPosition.Promote;
            dropTargetRow = referenceRow;
            promotedParentGuid = parentGuid;
            ShowLevelIndicator(referenceRow.Root.worldBound.yMax, rootContentX, indentWidth, desiredDepth);
            return true;
        }

        // 从 Unity TreeView 生成的缩进元素读取宽度，并以可见行坐标差作为兼容回退。
        private float GetIndentWidth()
        {
            foreach (RowState row in activeRows)
            {
                if (!IsVisibleRow(row) || row.Node.Depth <= 0) continue;
                VisualElement current = row.Root.parent;
                while (current != null && current != tagTree)
                {
                    VisualElement indent = current.Q<VisualElement>(
                        className: BaseTreeView.itemIndentUssClassName);
                    if (indent != null && indent.resolvedStyle.width > 0f) return indent.resolvedStyle.width;
                    current = current.parent;
                }
            }

            RowState[] rows = activeRows.Where(IsVisibleRow).ToArray();
            for (int left = 0; left < rows.Length; left++)
                for (int right = left + 1; right < rows.Length; right++)
                {
                    int depthDelta = rows[right].Node.Depth - rows[left].Node.Depth;
                    if (depthDelta == 0) continue;
                    float width = (rows[right].Root.worldBound.xMin - rows[left].Root.worldBound.xMin) /
                                  depthDelta;
                    if (width > 0f) return width;
                }
            return 0f;
        }

        // 按纵向范围寻找行，因此命中缩进空白时仍保留正确参考节点。
        private RowState FindReferenceRow(float panelY)
        {
            return activeRows.Where(IsVisibleRow)
                .Where(row => panelY >= row.Root.worldBound.yMin && panelY <= row.Root.worldBound.yMax)
                .OrderBy(row => Mathf.Abs(row.Root.worldBound.center.y - panelY))
                .FirstOrDefault();
        }

        // 过滤尚未绑定、已离开面板或不在 TreeView 可见范围内的虚拟化行。
        private bool IsVisibleRow(RowState row)
        {
            return row?.Node != null && row.Root.panel != null && row.Root.worldBound.height > 0f &&
                   row.Root.resolvedStyle.display != DisplayStyle.None &&
                   row.Root.worldBound.yMax >= tagTree.worldBound.yMin &&
                   row.Root.worldBound.yMin <= tagTree.worldBound.yMax;
        }

        // 把目标节点深度解析为对应祖先 Guid；根层使用空 Guid。
        private bool TryResolveParentGuid(GameplayTagTreeViewData reference, int desiredDepth,
            out string parentGuid)
        {
            if (desiredDepth == 0)
            {
                parentGuid = string.Empty;
                return true;
            }

            int parentDepth = desiredDepth - 1;
            GameplayTagTreeViewData current = reference;
            while (current != null && current.Depth > parentDepth)
                current = TryGetViewData(current.ParentGuid, out GameplayTagTreeViewData parent) ? parent : null;
            if (current == null || current.Depth != parentDepth)
            {
                parentGuid = null;
                return false;
            }

            parentGuid = current.Guid;
            return true;
        }

        // 按 Guid 查询当前完整树投影，不依赖行是否处于可见范围。
        private bool TryGetViewData(string guid, out GameplayTagTreeViewData data)
        {
            if (!string.IsNullOrEmpty(guid) && treeIdByGuid.TryGetValue(guid, out int id) &&
                viewDataById.TryGetValue(id, out data)) return true;
            data = null;
            return false;
        }

        // 显示与目标深度对齐的横向指示器和明确层级文本。
        private void ShowLevelIndicator(float panelY, float rootContentX, float indentWidth, int depth)
        {
            float left = Mathf.Max(0f, rootContentX + depth * indentWidth - treePanel.worldBound.xMin);
            float top = Mathf.Clamp(panelY - treePanel.worldBound.yMin - 1f, 0f,
                Mathf.Max(0f, treePanel.resolvedStyle.height - 20f));
            dropLevelIndicator.style.left = left;
            dropLevelIndicator.style.top = top;
            dropLevelIndicator.style.width = Mathf.Max(100f, treePanel.resolvedStyle.width - left - 4f);
            dropLevelLabel.text = depth == 0 ? "Move to Root Level" : $"Move to Level {depth}";
            dropLevelIndicator.RemoveFromClassList("is-hidden");
        }

        // 提交当前视觉目标；无法解析层级时不会回退到根节点。
        private void EndDrag(Vector2 panelPosition)
        {
            UpdateDrag(panelPosition);
            GameplayTagTreeViewData moved = draggedNode;
            string parentGuid = dropPosition switch
            {
                TagDropPosition.Inside => dropTargetRow?.Node?.Guid,
                TagDropPosition.Before => dropTargetRow?.Node?.ParentGuid,
                TagDropPosition.After => dropTargetRow?.Node?.ParentGuid,
                TagDropPosition.Root => string.Empty,
                TagDropPosition.Promote => promotedParentGuid,
                _ => null
            };
            ClearDropTarget();
            draggedNode = null;
            if (moved != null && parentGuid != null)
                MoveRequested?.Invoke(new GameplayTagMoveRequest(moved.Guid, parentGuid));
        }

        // 取消未提交拖放并清除视觉状态。
        private void CancelDrag()
        {
            ClearDropTarget();
            draggedNode = null;
        }

        // 清除行反馈、层级指示器及已解析父 Guid。
        private void ClearDropTarget()
        {
            dropTargetRow?.SetDropState(TagDropPosition.None);
            dropTargetRow = null;
            promotedParentGuid = null;
            dropPosition = TagDropPosition.None;
            dropLevelIndicator?.AddToClassList("is-hidden");
        }
        #endregion

        #region 事件处理
        // 转发数据库选择。
        private void OnDatabaseChanged(ChangeEvent<UnityEngine.Object> evt) =>
            DatabaseChanged?.Invoke(evt.newValue as GameplayTagDatabase);

        // 转发搜索文本。
        private void OnSearchChanged(ChangeEvent<string> evt) => SearchChanged?.Invoke(evt.newValue);

        // 转发 TreeView 单选 Guid。
        private void OnSelectionChanged(IEnumerable<object> selection)
        {
            if (refreshing) return;
            string guid = selection.OfType<GameplayTagTreeViewData>().FirstOrDefault()?.Guid ?? string.Empty;
            selectedGuid = guid;
            SelectionChanged?.Invoke(guid);
        }

        // 捕获 F2/Delete；文本输入期间保持控件默认行为。
        private void OnRootKeyDown(KeyDownEvent evt)
        {
            VisualElement focused = root.panel?.focusController?.focusedElement as VisualElement;
            if (!IsDescendantOf(focused, tagTree) || FindAncestor<TextField>(focused) != null) return;
            if (evt.keyCode == KeyCode.Delete)
            {
                DeleteRequested?.Invoke();
                evt.StopImmediatePropagation();
            }
            else if (evt.keyCode == KeyCode.F2)
            {
                QueueRenameRequest(selectedGuid);
                evt.StopImmediatePropagation();
            }
        }

        // 转发详情名称的延迟提交。
        private void OnNameChanged(ChangeEvent<string> evt)
        {
            if (!refreshing && !string.IsNullOrEmpty(selectedGuid))
                NameEditRequested?.Invoke(new GameplayTagTextEditRequest(selectedGuid, evt.newValue));
        }

        // 转发完整路径的延迟提交。
        private void OnPathChanged(ChangeEvent<string> evt)
        {
            if (!refreshing && !string.IsNullOrEmpty(selectedGuid))
                PathEditRequested?.Invoke(new GameplayTagTextEditRequest(selectedGuid, evt.newValue));
        }

        // 转发描述编辑。
        private void OnDescriptionChanged(ChangeEvent<string> evt)
        {
            if (!refreshing && !string.IsNullOrEmpty(selectedGuid))
                DescriptionEditRequested?.Invoke(new GameplayTagTextEditRequest(selectedGuid, evt.newValue));
        }

        // 转发工具栏按钮意图。
        private void OnAddRootClicked() => AddRootRequested?.Invoke();
        private void OnAddChildClicked() => AddChildRequested?.Invoke();
        private void OnDeleteClicked() => DeleteRequested?.Invoke();
        private void OnBakeClicked() => BakeRequested?.Invoke();
        #endregion

        #region 内部辅助
        // 把重命名意图延迟到当前 UI 事件结束后；版本号保证连续操作只执行最后一次请求。
        private void QueueRenameRequest(string guid)
        {
            if (disposed || string.IsNullOrEmpty(guid)) return;
            int requestVersion = ++renameRequestVersion;
            root.schedule.Execute(() =>
            {
                if (disposed ||
                    requestVersion != renameRequestVersion ||
                    root.panel == null ||
                    !treeIdByGuid.ContainsKey(guid))
                    return;
                RenameRequested?.Invoke(guid);
            });
        }

        // 获取必需 UXML 元素，并在布局契约损坏时提供明确错误。
        private T RequireElement<T>(string name) where T : VisualElement
        {
            T element = root.Q<T>(name);
            if (element == null) throw new InvalidOperationException($"Gameplay Tag Editor 缺少控件：{name}");
            return element;
        }

        // 判断元素是否位于指定视觉树子树内。
        private static bool IsDescendantOf(VisualElement element, VisualElement ancestor)
        {
            while (element != null)
            {
                if (element == ancestor) return true;
                element = element.parent;
            }

            return false;
        }

        // 沿视觉父级查找文本控件，避免内部 TextElement 被误判。
        private static T FindAncestor<T>(VisualElement element) where T : VisualElement
        {
            while (element != null)
            {
                if (element is T match) return match;
                element = element.parent;
            }

            return null;
        }
        #endregion

        #region 嵌套类型
        /// <summary>描述自定义 TreeView 拖放目标位置。</summary>
        private enum TagDropPosition
        {
            None,
            Root,
            Before,
            Inside,
            After,
            Promote
        }

        /// <summary>保存虚拟化 TreeView 行的绑定、重命名和 Pointer 状态。</summary>
        private sealed class RowState
        {
            private readonly GameplayTagEditorView owner;
            private readonly Label label;
            private bool pointerPressed;
            private bool dragging;
            private bool suppressNextFocusCommit;
            private int pointerId = -1;
            private int bindVersion;
            private int pendingFocusVersion = -1;
            private int pendingRenamePointerId = -1;
            private string pendingFocusGuid = string.Empty;
            private string pendingRenameGuid = string.Empty;
            private Vector2 pointerStart;

            /// <summary>获取行根元素。</summary>
            public VisualElement Root { get; }
            /// <summary>获取当前绑定投影。</summary>
            public GameplayTagTreeViewData Node { get; private set; }
            /// <summary>获取行内重命名输入框。</summary>
            public TextField RenameField { get; }

            /// <summary>创建虚拟化行状态。</summary>
            public RowState(GameplayTagEditorView owner, VisualElement row)
            {
                this.owner = owner;
                Root = row;
                owner.RegisterRow(this);
                label = row.Q<Label>("RowLabel");
                RenameField = row.Q<TextField>("RowRenameField");
            }

            /// <summary>注册仅属于当前可回收行的事件。</summary>
            public void Register()
            {
                RenameField.RegisterCallback<KeyDownEvent>(OnRenameKeyDown);
                RenameField.RegisterCallback<FocusOutEvent>(OnRenameFocusOut);
                RenameField.RegisterCallback<FocusInEvent>(OnRenameFocusIn);
                RenameField.RegisterCallback<GeometryChangedEvent>(OnRenameGeometryChanged);
                label.RegisterCallback<PointerDownEvent>(OnLabelPointerDown);
                label.RegisterCallback<PointerUpEvent>(OnLabelPointerUp);
                label.RegisterCallback<PointerCaptureOutEvent>(OnLabelPointerCaptureOut);
                Root.RegisterCallback<PointerDownEvent>(OnPointerDown);
                Root.RegisterCallback<PointerMoveEvent>(OnPointerMove);
                Root.RegisterCallback<PointerUpEvent>(OnPointerUp);
                Root.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
                Root.AddManipulator(new ContextualMenuManipulator(OnContextMenu));
            }

            /// <summary>绑定节点并切换普通与行内编辑显示。</summary>
            public void Bind(GameplayTagTreeViewData node, bool renaming)
            {
                bool bindingChanged = Node?.Guid != node.Guid;
                if (bindingChanged)
                {
                    ClearPendingRenamePointer();
                    CancelPendingFocus();
                    suppressNextFocusCommit = false;
                }
                bindVersion++;
                Node = node;
                label.text = node.Name;
                RenameField.SetValueWithoutNotify(node.Name);
                label.EnableInClassList("is-hidden", renaming);
                RenameField.EnableInClassList("is-hidden", !renaming);
                if (renaming)
                {
                    if (bindingChanged) suppressNextFocusCommit = false;
                    FocusRename();
                }
                else
                {
                    CancelPendingFocus();
                    suppressNextFocusCommit = false;
                }
            }

            /// <summary>布局完成后聚焦并全选名称；回收行或绑定目标变化时放弃旧任务。</summary>
            public void FocusRename()
            {
                pendingFocusGuid = Node?.Guid ?? string.Empty;
                pendingFocusVersion = bindVersion;
                RenameField.schedule.Execute(TryFocusRename);
            }

            /// <summary>切换拖放目标视觉状态。</summary>
            public void SetDropState(TagDropPosition position)
            {
                Root.EnableInClassList("gas-tag-row--drop-before", position == TagDropPosition.Before);
                Root.EnableInClassList("gas-tag-row--drop-inside", position == TagDropPosition.Inside);
                Root.EnableInClassList("gas-tag-row--drop-after", position == TagDropPosition.After);
            }

            // 由当前虚拟化行填充菜单，确保操作目标与右键行一致。
            private void OnContextMenu(ContextualMenuPopulateEvent evt) =>
                owner.PopulateRowContextMenu(this, evt);
            // Enter 提交，Escape 取消行内重命名。
            private void OnRenameKeyDown(KeyDownEvent evt)
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    suppressNextFocusCommit = true;
                    owner.NameEditRequested?.Invoke(
                        new GameplayTagTextEditRequest(Node?.Guid, RenameField.value));
                    evt.StopImmediatePropagation();
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    owner.RenameCancelled?.Invoke(Node?.Guid ?? string.Empty);
                    evt.StopImmediatePropagation();
                }
            }

            // 失焦时尝试提交仍处于编辑状态的节点。
            private void OnRenameFocusOut(FocusOutEvent evt)
            {
                if (suppressNextFocusCommit)
                {
                    suppressNextFocusCommit = false;
                    return;
                }

                if (Node != null && owner.renamingGuid == Node.Guid)
                    owner.NameEditRequested?.Invoke(new GameplayTagTextEditRequest(Node.Guid, RenameField.value));
            }

            // 输入框确认获得焦点后全选内容，并清除旧提交留下的失焦抑制状态。
            private void OnRenameFocusIn(FocusInEvent evt)
            {
                suppressNextFocusCommit = false;
                CompletePendingFocus();
            }

            // 样式从隐藏切换为显示并完成布局后，再次尝试聚焦。
            private void OnRenameGeometryChanged(GeometryChangedEvent evt) => TryFocusRename();

            // 第二次按下只捕获名称 Label，等待对应 PointerUp 后再请求重建 Tree。
            private void OnLabelPointerDown(PointerDownEvent evt)
            {
                if (evt.button != 0 || evt.clickCount != 2 || Node == null) return;
                if (dragging) owner.CancelDrag();
                if (pointerId >= 0 && Root.HasPointerCapture(pointerId)) Root.ReleasePointer(pointerId);
                ResetPointer();
                ClearPendingRenamePointer();
                pendingRenameGuid = Node.Guid;
                pendingRenamePointerId = evt.pointerId;
                label.CapturePointer(pendingRenamePointerId);
                evt.StopImmediatePropagation();
            }

            // 释放双击 Pointer 后再排队发送意图，避免后续 Pointer 事件夺回焦点。
            private void OnLabelPointerUp(PointerUpEvent evt)
            {
                if (evt.pointerId != pendingRenamePointerId || string.IsNullOrEmpty(pendingRenameGuid)) return;
                string guid = pendingRenameGuid;
                ClearPendingRenamePointer();
                evt.StopImmediatePropagation();
                owner.QueueRenameRequest(guid);
            }

            // Pointer 捕获意外丢失时取消尚未完成的双击重命名。
            private void OnLabelPointerCaptureOut(PointerCaptureOutEvent evt)
            {
                if (evt.pointerId != pendingRenamePointerId) return;
                pendingRenamePointerId = -1;
                pendingRenameGuid = string.Empty;
            }

            // 记录按下位置，超过阈值前保留 TreeView 点击选择行为。
            private void OnPointerDown(PointerDownEvent evt)
            {
                if (IsDescendantOf(evt.target as VisualElement, RenameField)) return;
                if (evt.button != 0 || !owner.CanStartDrag(Node)) return;
                pointerPressed = true;
                dragging = false;
                pointerId = evt.pointerId;
                pointerStart = evt.position;
                Root.CapturePointer(pointerId);
            }

            // 超过移动阈值后进入拖放，并持续刷新落点。
            private void OnPointerMove(PointerMoveEvent evt)
            {
                if (!pointerPressed || evt.pointerId != pointerId) return;
                if (!dragging && ((Vector2)evt.position - pointerStart).sqrMagnitude >= 25f)
                {
                    dragging = true;
                    owner.BeginDrag(Node);
                }

                if (dragging)
                {
                    owner.UpdateDrag(evt.position);
                    evt.StopPropagation();
                }
            }

            // PointerUp 时提交一次移动；普通点击不触发结构变化。
            private void OnPointerUp(PointerUpEvent evt)
            {
                if (!pointerPressed || evt.pointerId != pointerId) return;
                if (dragging)
                {
                    owner.EndDrag(evt.position);
                    evt.StopImmediatePropagation();
                }

                if (Root.HasPointerCapture(pointerId)) Root.ReleasePointer(pointerId);
                ResetPointer();
            }

            // Pointer 捕获意外丢失时取消拖放，防止复用行残留状态。
            private void OnPointerCaptureOut(PointerCaptureOutEvent evt)
            {
                if (dragging) owner.CancelDrag();
                ResetPointer();
            }

            // 清除当前行 Pointer 状态。
            private void ResetPointer()
            {
                pointerPressed = false;
                dragging = false;
                pointerId = -1;
            }

            // View 销毁时释放当前行的 Pointer 捕获和待执行焦点状态。
            internal void CancelTransientState()
            {
                if (dragging) owner.CancelDrag();
                if (pointerId >= 0 && Root.HasPointerCapture(pointerId)) Root.ReleasePointer(pointerId);
                ResetPointer();
                ClearPendingRenamePointer();
                CancelPendingFocus();
                suppressNextFocusCommit = false;
            }

            // 验证虚拟化绑定和显示状态后聚焦；布局未就绪时保留请求等待 GeometryChanged。
            private void TryFocusRename()
            {
                if (pendingFocusVersion != bindVersion ||
                    Node?.Guid != pendingFocusGuid ||
                    owner.renamingGuid != pendingFocusGuid)
                {
                    CancelPendingFocus();
                    return;
                }

                if (RenameField.panel == null || RenameField.ClassListContains("is-hidden")) return;
                VisualElement focused =
                    RenameField.panel.focusController?.focusedElement as VisualElement;
                if (IsDescendantOf(focused, RenameField))
                {
                    CompletePendingFocus();
                    return;
                }

                RenameField.Focus();
                focused = RenameField.panel?.focusController?.focusedElement as VisualElement;
                if (IsDescendantOf(focused, RenameField)) CompletePendingFocus();
            }

            // 输入框获得焦点后全选文本，并使当前焦点任务失效。
            private void CompletePendingFocus()
            {
                if (pendingFocusVersion < 0) return;
                suppressNextFocusCommit = false;
                RenameField.SelectAll();
                CancelPendingFocus();
            }

            // 清除虚拟化行上尚未完成的聚焦任务。
            private void CancelPendingFocus()
            {
                pendingFocusVersion = -1;
                pendingFocusGuid = string.Empty;
            }

            // 清除双击 Pointer 状态；先清空标记再释放捕获，避免 CaptureOut 重入误伤新请求。
            private void ClearPendingRenamePointer()
            {
                int capturedPointerId = pendingRenamePointerId;
                pendingRenamePointerId = -1;
                pendingRenameGuid = string.Empty;
                if (capturedPointerId >= 0 && label.HasPointerCapture(capturedPointerId))
                    label.ReleasePointer(capturedPointerId);
            }
        }
        #endregion
    }
}
#endif