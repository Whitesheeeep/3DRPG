#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace RPG.RedDotSystemNS.Editor
{
    /// <summary>
    /// 渲染 RedDotConfig 节点设置页，并将 UI 意图转发给 Controller。
    /// </summary>
    internal sealed class RedDotNodeSettingsView : IDisposable
    {
        #region 依赖字段

        private readonly VisualElement callbackRoot;
        private readonly ObjectField configField;
        private readonly TextField newNodeNameField;
        private readonly PropertyField nodeFolderField;
        private readonly SerializedObject editorSettingsSerializedObject;
        private readonly SerializedProperty nodeFolderProperty;
        private readonly TextField segmentNameField;
        private readonly ObjectField parentField;
        private readonly VisualElement detailContent;
        private readonly HelpBox detailEmptyState;
        private readonly Label configDetailLabel;
        private readonly TextField derivedPathField;
        private readonly HelpBox derivedPathErrorHelpBox;
        private readonly Label siblingOrderLabel;
        private readonly Label directChildrenCountLabel;
        private readonly TextField assetPathField;
        private readonly Label configStatusLabel;
        private readonly TreeView treeView;
        private readonly Button createConfigButton;
        private readonly Button createFolderButton;
        private readonly Button refreshConfigButton;
        private readonly Button createRootNodeButton;
        private readonly Button createChildNodeButton;
        private readonly Button moveUpButton;
        private readonly Button moveDownButton;
        private readonly Button pingAssetButton;
        private readonly Button deleteNodeButton;
        private readonly Button deleteSubtreeButton;

        #endregion

        #region 状态字段

        private IReadOnlyDictionary<RedDotKey, RedDotNodeSettingsViewData> nodeByKeyMap =
            new Dictionary<RedDotKey, RedDotNodeSettingsViewData>();
        private RedDotNodeSettingsViewData selectedNode;
        private RedDotNodeSettingsViewData draggingNode;
        private Vector2 dragStartPosition;
        private VisualElement dragSourceRow;
        private VisualElement dropTargetRow;
        private RedDotNodeSettingsViewData dropTargetNode;
        private NodeDropPlacement dropPlacement;
        private RedDotConfig displayedConfig;
        private bool refreshingTree;
        private bool suppressNodeFolderChange;
        private IVisualElementScheduledItem derivedPathErrorHideSchedule;
        private bool disposed;

        #endregion

        #region 事件

        /// <summary>请求切换当前编辑的 Config。</summary>
        internal event Action<RedDotConfig> ConfigChangedRequested;

        /// <summary>请求创建 Config Asset。</summary>
        internal event Action CreateConfigRequested;

        /// <summary>请求保存新建节点目录设置。</summary>
        internal event Action<string> NodeFolderChangedRequested;

        /// <summary>请求创建当前输入目录。</summary>
        internal event Action CreateFolderRequested;

        /// <summary>请求重新读取当前 Config。</summary>
        internal event Action RefreshConfigRequested;

        /// <summary>请求新建一个节点 Asset。</summary>
        internal event Action<string, RedDotKey> CreateNodeRequested;

        /// <summary>请求提交当前节点名称。</summary>
        internal event Action<RedDotKey, string> SegmentNameSubmitted;

        /// <summary>请求迁移节点到新的父级和顺序位置。</summary>
        internal event Action<RedDotKey, RedDotKey, NodeDropPlacement> NodeDropRequested;

        /// <summary>请求通过 Parent 字段迁移节点。</summary>
        internal event Action<RedDotKey, RedDotKey> ParentChangedRequested;

        /// <summary>请求通过派生路径输入迁移节点到目标父节点。</summary>
        internal event Action<RedDotKey, string> DerivedParentPathSubmitted;

        /// <summary>请求将节点在当前父级下移动一个位置。</summary>
        internal event Action<RedDotKey, int> SiblingMoveRequested;

        /// <summary>请求定位当前节点 Asset。</summary>
        internal event Action<RedDotKey> PingAssetRequested;

        /// <summary>请求删除当前叶节点。</summary>
        internal event Action<RedDotKey> DeleteNodeRequested;

        /// <summary>请求删除当前节点及全部后代。</summary>
        internal event Action<RedDotKey> DeleteSubtreeRequested;

        #endregion

        #region 生命周期

        /// <summary>查询节点设置页控件并注册所有界面回调。</summary>
        /// <param name="root">已克隆窗口 UXML 的根节点。</param>
        public RedDotNodeSettingsView(VisualElement root)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            callbackRoot = root;
            configField = Require<ObjectField>(root, "ConfigField");
            newNodeNameField = Require<TextField>(root, "NewNodeNameField");
            nodeFolderField = Require<PropertyField>(root, "NodeFolderField");
            editorSettingsSerializedObject =
                new SerializedObject(RedDotEditorSettings.instance);
            nodeFolderProperty = editorSettingsSerializedObject.FindProperty("newNodeAssetFolder");
            if (nodeFolderProperty == null)
            {
                throw new InvalidOperationException(
                    "[RedDotNodeSettings] RedDotEditorSettings 缺少 newNodeAssetFolder 属性。");
            }

            segmentNameField = Require<TextField>(root, "SegmentNameField");
            parentField = Require<ObjectField>(root, "ParentField");
            detailContent = Require<VisualElement>(root, "DetailContent");
            detailEmptyState = Require<HelpBox>(root, "DetailEmptyState");
            configDetailLabel = Require<Label>(root, "ConfigDetailLabel");
            derivedPathField = Require<TextField>(root, "DerivedPathField");
            derivedPathErrorHelpBox = Require<HelpBox>(root, "DerivedPathErrorHelpBox");
            siblingOrderLabel = Require<Label>(root, "SiblingOrderLabel");
            directChildrenCountLabel = Require<Label>(root, "DirectChildrenCountLabel");
            assetPathField = Require<TextField>(root, "AssetPathField");
            configStatusLabel = Require<Label>(root, "ConfigStatusLabel");
            treeView = Require<TreeView>(root, "ConfigTreeView");
            createConfigButton = Require<Button>(root, "CreateConfigButton");
            createFolderButton = Require<Button>(root, "CreateFolderButton");
            refreshConfigButton = Require<Button>(root, "RefreshConfigButton");
            createRootNodeButton = Require<Button>(root, "CreateRootNodeButton");
            createChildNodeButton = Require<Button>(root, "CreateChildNodeButton");
            moveUpButton = Require<Button>(root, "MoveUpButton");
            moveDownButton = Require<Button>(root, "MoveDownButton");
            pingAssetButton = Require<Button>(root, "PingAssetButton");
            deleteNodeButton = Require<Button>(root, "DeleteNodeButton");
            deleteSubtreeButton = Require<Button>(root, "DeleteSubtreeButton");

            configField.objectType = typeof(RedDotConfig);
            parentField.objectType = typeof(RedDotKey);
            segmentNameField.isDelayed = true;
            // 派生路径只作为“目标父节点路径”输入，提交后由 Controller 统一迁移，禁止原生绑定直接改 Parent。
            derivedPathField.isReadOnly = false;
            derivedPathField.isDelayed = true;
            assetPathField.isReadOnly = true;
            ClearDerivedPathError();
            nodeFolderField.bindingPath = "newNodeAssetFolder";
            nodeFolderField.Bind(editorSettingsSerializedObject);
            nodeFolderField.TrackPropertyValue(nodeFolderProperty, OnNodeFolderPropertyChanged);
            treeView.fixedItemHeight = 24f;
            treeView.selectionType = SelectionType.Single;
            treeView.autoExpand = true;
            treeView.makeItem = MakeTreeRow;
            treeView.bindItem = BindTreeRow;
            treeView.selectionChanged += OnTreeSelectionChanged;
            root.RegisterCallback<PointerUpEvent>(OnPointerUp);
            root.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            root.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
            root.RegisterCallback<PointerLeaveEvent>(OnPointerLeave);
            treeView.RegisterCallback<ContextualMenuPopulateEvent>(OnTreeContextMenuPopulate);

            configField.RegisterValueChangedCallback(OnConfigChanged);
            segmentNameField.RegisterValueChangedCallback(OnSegmentNameChanged);
            parentField.RegisterValueChangedCallback(OnParentChanged);
            derivedPathField.RegisterValueChangedCallback(OnDerivedPathChanged);
            createConfigButton.clicked += OnCreateConfigClicked;
            createFolderButton.clicked += OnCreateFolderClicked;
            refreshConfigButton.clicked += OnRefreshConfigClicked;
            createRootNodeButton.clicked += OnCreateRootNodeClicked;
            createChildNodeButton.clicked += OnCreateChildNodeClicked;
            moveUpButton.clicked += OnMoveUpClicked;
            moveDownButton.clicked += OnMoveDownClicked;
            pingAssetButton.clicked += OnPingAssetClicked;
            deleteNodeButton.clicked += OnDeleteNodeClicked;
            deleteSubtreeButton.clicked += OnDeleteSubtreeClicked;
        }

        /// <summary>解除 UI 回调并清空 TreeView 行数据。</summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            treeView.selectionChanged -= OnTreeSelectionChanged;
            treeView.UnregisterCallback<ContextualMenuPopulateEvent>(OnTreeContextMenuPopulate);
            callbackRoot.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            callbackRoot.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            callbackRoot.UnregisterCallback<PointerCancelEvent>(OnPointerCancel);
            callbackRoot.UnregisterCallback<PointerLeaveEvent>(OnPointerLeave);
            configField.UnregisterValueChangedCallback(OnConfigChanged);
            segmentNameField.UnregisterValueChangedCallback(OnSegmentNameChanged);
            parentField.UnregisterValueChangedCallback(OnParentChanged);
            derivedPathField.UnregisterValueChangedCallback(OnDerivedPathChanged);
            createConfigButton.clicked -= OnCreateConfigClicked;
            createFolderButton.clicked -= OnCreateFolderClicked;
            refreshConfigButton.clicked -= OnRefreshConfigClicked;
            createRootNodeButton.clicked -= OnCreateRootNodeClicked;
            createChildNodeButton.clicked -= OnCreateChildNodeClicked;
            moveUpButton.clicked -= OnMoveUpClicked;
            moveDownButton.clicked -= OnMoveDownClicked;
            pingAssetButton.clicked -= OnPingAssetClicked;
            deleteNodeButton.clicked -= OnDeleteNodeClicked;
            deleteSubtreeButton.clicked -= OnDeleteSubtreeClicked;
            nodeFolderField.Unbind();
            ClearDerivedPathError();
            editorSettingsSerializedObject.Dispose();
            ClearDragState();
            ClearTree();
        }

        #endregion

        #region 状态渲染

        /// <summary>设置当前编辑 Config 和新建目录。</summary>
        /// <param name="config">当前 Config；为空时清空节点树。</param>
        /// <param name="folderPath">新建节点 Asset 默认目录。</param>
        internal void SetEditorState(RedDotConfig config, string folderPath)
        {
            configField.SetValueWithoutNotify(config);
            SetNodeFolder(folderPath);
            Render(config);
        }

        /// <summary>重新渲染指定 Config 的完整节点树。</summary>
        /// <param name="config">待显示 Config；为空时清空节点树。</param>
        internal void Render(RedDotConfig config)
        {
            RedDotKey selectedKey = selectedNode?.Key;
            displayedConfig = config;
            if (config == null)
            {
                ClearTree();
                configDetailLabel.text = "请选择一个 RedDotConfig。";
                configStatusLabel.text = "节点设置需要先选择 RedDotConfig。";
                return;
            }

            IList<TreeViewItemData<RedDotNodeSettingsViewData>> roots =
                RedDotNodeSettingsViewData.BuildTreeItems(config, out nodeByKeyMap);
            // TreeView 重建会产生中间的空选择事件；先屏蔽回调，再按 Asset 身份恢复选择。
            refreshingTree = true;
            treeView.SetRootItems(roots);
            treeView.Rebuild();
            treeView.ExpandAll();
            refreshingTree = false;
            configDetailLabel.text = $"Config：{config.name}\n节点数：{config.NodeKeys.Count}";
            configStatusLabel.text = $"正在编辑：{config.name}";

            if (selectedKey != null && nodeByKeyMap.TryGetValue(selectedKey, out RedDotNodeSettingsViewData restored))
            {
                treeView.SetSelectionByIdWithoutNotify(new[] { restored.Id });
                SetSelectedNode(restored);
            }
            else
            {
                treeView.ClearSelection();
                SetSelectedNode(null);
            }
        }

        /// <summary>设置底部操作状态文本。</summary>
        /// <param name="message">待显示消息。</param>
        /// <param name="isError">是否显示为错误样式。</param>
        internal void ShowStatus(string message, bool isError)
        {
            configStatusLabel.text = message;
            configStatusLabel.EnableInClassList("status-error", isError);
        }

        /// <summary>显示派生路径校验错误，并在三秒后自动隐藏。</summary>
        /// <param name="message">需要展示给用户的具体错误。</param>
        internal void ShowDerivedPathError(string message)
        {
            if (disposed)
            {
                return;
            }

            derivedPathErrorHideSchedule?.Pause();
            derivedPathErrorHelpBox.text = message ?? string.Empty;
            derivedPathErrorHelpBox.style.display = DisplayStyle.Flex;
            derivedPathErrorHideSchedule = derivedPathErrorHelpBox.schedule
                .Execute(HideDerivedPathError)
                .StartingIn(3000);
        }

        /// <summary>立即隐藏派生路径错误提示并取消尚未执行的隐藏任务。</summary>
        internal void ClearDerivedPathError()
        {
            derivedPathErrorHideSchedule?.Pause();
            derivedPathErrorHideSchedule = null;
            if (derivedPathErrorHelpBox != null)
            {
                derivedPathErrorHelpBox.text = string.Empty;
                derivedPathErrorHelpBox.style.display = DisplayStyle.None;
            }
        }

        /// <summary>获取当前选中的节点 Asset。</summary>
        /// <returns>选中节点；没有选择时返回 null。</returns>
        internal RedDotNodeSettingsViewData GetSelectedNode() => selectedNode;

        /// <summary>获取当前输入的新节点名称。</summary>
        /// <returns>新节点名称文本。</returns>
        internal string GetNewNodeName() => newNodeNameField.value;

        /// <summary>设置新建节点名称输入框的值。</summary>
        /// <param name="segmentName">待显示的节点名称。</param>
        internal void SetNewNodeName(string segmentName) =>
            newNodeNameField.SetValueWithoutNotify(segmentName ?? string.Empty);

        /// <summary>按节点 Asset 恢复 TreeView 选中项。</summary>
        /// <param name="key">待选中的节点 Asset。</param>
        internal void SelectNode(RedDotKey key)
        {
            if (key != null && nodeByKeyMap.TryGetValue(key, out RedDotNodeSettingsViewData node))
            {
                treeView.SetSelectionByIdWithoutNotify(new[] { node.Id });
                SetSelectedNode(node);
                return;
            }

            treeView.ClearSelection();
            SetSelectedNode(null);
        }

        /// <summary>
        /// 将焦点移到当前节点名称输入框，便于新建节点后立即确认名称。
        /// </summary>
        internal void FocusSegmentNameField()
        {
            if (selectedNode != null)
            {
                segmentNameField.Focus();
            }
        }

        /// <summary>刷新新建目录的 SerializedObject 绑定并显示规范化值。</summary>
        /// <param name="folderPath">目录路径。</param>
        internal void SetNodeFolder(string folderPath)
        {
            suppressNodeFolderChange = true;
            try
            {
                editorSettingsSerializedObject.Update();
                nodeFolderProperty.stringValue = folderPath ?? string.Empty;
                editorSettingsSerializedObject.ApplyModifiedPropertiesWithoutUndo();
                editorSettingsSerializedObject.Update();
            }
            finally
            {
                suppressNodeFolderChange = false;
            }
        }

        #endregion

        #region TreeView 与拖拽

        /// <summary>创建一行节点设置 TreeView 行。</summary>
        /// <returns>未绑定数据的行元素。</returns>
        private VisualElement MakeTreeRow()
        {
            var row = new VisualElement { userData = null };
            row.AddToClassList("red-dot-tree-row");
            row.Add(new Label { name = "KeyLabel" });
            row.Add(new Label { name = "PathLabel" });
            row.RegisterCallback<PointerDownEvent>(OnRowPointerDown);
            row.AddManipulator(new ContextualMenuManipulator(
                eventData => PopulateNodeContextMenu(row, eventData)));
            return row;
        }

        /// <summary>把节点设置数据绑定到虚拟化行。</summary>
        /// <param name="element">待绑定行。</param>
        /// <param name="index">当前行索引。</param>
        private void BindTreeRow(VisualElement element, int index)
        {
            RemoveDragClasses(element);
            RedDotNodeSettingsViewData data =
                treeView.GetItemDataForIndex<RedDotNodeSettingsViewData>(index);
            element.userData = data;
            element.Q<Label>("KeyLabel").text = data.DisplayName;
            element.Q<Label>("PathLabel").text = data.DerivedPath;
            element.tooltip = data.DerivedPath;
        }

        /// <summary>记录鼠标按下位置和待拖动节点。</summary>
        /// <param name="eventData">指针事件。</param>
        private void OnTreeRowPointerDown(PointerDownEvent eventData)
        {
            if (eventData.button != 0 || !(eventData.currentTarget is VisualElement row))
            {
                return;
            }

            draggingNode = row.userData as RedDotNodeSettingsViewData;
            if (draggingNode == null)
            {
                return;
            }

            ClearDropVisuals();
            dragSourceRow = row;
            dragStartPosition = eventData.position;
        }

        /// <summary>根据指针移动更新拖拽源、目标边框和根区域提示。</summary>
        /// <param name="eventData">指针移动事件。</param>
        private void OnPointerMove(PointerMoveEvent eventData)
        {
            if (draggingNode == null)
            {
                return;
            }

            if (((Vector2)eventData.position - dragStartPosition).sqrMagnitude < 36f)
            {
                return;
            }

            if (TryResolveDropTarget(
                    eventData.target as VisualElement,
                    eventData.position,
                    out VisualElement targetRow,
                    out RedDotNodeSettingsViewData targetNode,
                    out NodeDropPlacement placement))
            {
                ApplyDragVisuals(targetRow, targetNode, placement);
                return;
            }

            ClearDropVisuals();
            dragSourceRow?.AddToClassList("red-dot-tree-row--drag-source");
        }

        /// <summary>处理指针释放，使用与预览完全一致的落点解析结果提交迁移意图。</summary>
        /// <param name="eventData">指针释放事件。</param>
        private void OnPointerUp(PointerUpEvent eventData)
        {
            if (draggingNode == null)
            {
                return;
            }

            RedDotNodeSettingsViewData sourceNode = draggingNode;
            bool movedEnough = ((Vector2)eventData.position - dragStartPosition).sqrMagnitude >= 36f;
            RedDotNodeSettingsViewData targetNode = null;
            NodeDropPlacement placement = NodeDropPlacement.AppendToRoot;
            bool hasDropTarget = movedEnough && TryResolveDropTarget(
                eventData.target as VisualElement,
                eventData.position,
                out _,
                out targetNode,
                out placement);
            ClearDragState();

            if (hasDropTarget)
            {
                NodeDropRequested?.Invoke(sourceNode.Key, targetNode?.Key, placement);
            }
        }

        /// <summary>在指针交互被系统取消时清理所有临时拖拽状态。</summary>
        /// <param name="eventData">指针取消事件。</param>
        private void OnPointerCancel(PointerCancelEvent eventData)
        {
            ClearDragState();
        }

        /// <summary>指针离开编辑树时取消未提交拖拽，避免窗口外残留落点边框。</summary>
        /// <param name="eventData">指针离开事件。</param>
        private void OnPointerLeave(PointerLeaveEvent eventData)
        {
            if (draggingNode != null)
            {
                ClearDragState();
            }
        }

        /// <summary>
        /// 解析当前指针位置对应的节点落点，并拒绝自身或后代目标。
        /// </summary>
        /// <param name="eventTarget">指针事件命中的视觉元素。</param>
        /// <param name="pointerPosition">面板坐标系中的指针位置。</param>
        /// <param name="targetRow">命中的目标行；根区域时为空。</param>
        /// <param name="targetNode">命中的目标节点；根区域时为空。</param>
        /// <param name="placement">解析出的迁移落点。</param>
        /// <returns>落点在当前树内且可迁移时返回 true。</returns>
        private bool TryResolveDropTarget(
            VisualElement eventTarget,
            Vector2 pointerPosition,
            out VisualElement targetRow,
            out RedDotNodeSettingsViewData targetNode,
            out NodeDropPlacement placement)
        {
            targetRow = FindTreeRow(eventTarget);
            targetNode = targetRow?.userData as RedDotNodeSettingsViewData;
            placement = NodeDropPlacement.AppendToRoot;

            if (targetNode != null)
            {
                if (!IsValidDropTarget(draggingNode, targetNode))
                {
                    targetRow = null;
                    targetNode = null;
                    return false;
                }

                Vector2 localPosition = targetRow.WorldToLocal(pointerPosition);
                float rowHeight = Mathf.Max(targetRow.layout.height, 1f);
                placement = localPosition.y < rowHeight * 0.25f
                    ? NodeDropPlacement.Before
                    : localPosition.y > rowHeight * 0.75f
                        ? NodeDropPlacement.After
                        : NodeDropPlacement.AppendAsChild;
                return true;
            }

            if (IsDescendantOf(eventTarget, treeView) && treeView.worldBound.Contains(pointerPosition))
            {
                return true;
            }

            targetRow = null;
            targetNode = null;
            return false;
        }

        /// <summary>判断目标节点是否不是拖拽源本身或其后代。</summary>
        /// <param name="sourceNode">拖拽源节点。</param>
        /// <param name="targetNode">待判断目标节点。</param>
        /// <returns>目标节点可作为落点时返回 true。</returns>
        private static bool IsValidDropTarget(
            RedDotNodeSettingsViewData sourceNode,
            RedDotNodeSettingsViewData targetNode)
        {
            if (sourceNode == null || targetNode == null ||
                ReferenceEquals(sourceNode.Key, targetNode.Key))
            {
                return false;
            }

            RedDotKey ancestor = targetNode.Key.Parent;
            while (ancestor != null)
            {
                if (ReferenceEquals(ancestor, sourceNode.Key))
                {
                    return false;
                }

                ancestor = ancestor.Parent;
            }

            return true;
        }

        /// <summary>把当前拖拽状态映射为源行、目标行或根区域的视觉反馈。</summary>
        /// <param name="targetRow">目标行；根区域时为空。</param>
        /// <param name="targetNode">目标节点；根区域时为空。</param>
        /// <param name="placement">目标落点。</param>
        private void ApplyDragVisuals(
            VisualElement targetRow,
            RedDotNodeSettingsViewData targetNode,
            NodeDropPlacement placement)
        {
            ClearDropVisuals();
            dropTargetRow = targetRow;
            dropTargetNode = targetNode;
            dropPlacement = placement;
            dragSourceRow?.AddToClassList("red-dot-tree-row--drag-source");

            if (targetRow == null)
            {
                treeView.AddToClassList("node-tree--drop-root");
                return;
            }

            string className = placement switch
            {
                NodeDropPlacement.Before => "red-dot-tree-row--drop-before",
                NodeDropPlacement.After => "red-dot-tree-row--drop-after",
                NodeDropPlacement.AppendAsChild => "red-dot-tree-row--drop-inside",
                _ => string.Empty
            };
            if (!string.IsNullOrEmpty(className))
            {
                targetRow.AddToClassList(className);
            }
        }

        /// <summary>清除目标行和根区域的落点反馈，但保留拖拽源引用。</summary>
        private void ClearDropVisuals()
        {
            RemoveDragClasses(dragSourceRow);
            RemoveDragClasses(dropTargetRow);
            treeView.RemoveFromClassList("node-tree--drop-root");
            dropTargetRow = null;
            dropTargetNode = null;
            dropPlacement = NodeDropPlacement.AppendToRoot;
        }

        /// <summary>清除源、目标和落点记录，结束一次拖拽交互。</summary>
        private void ClearDragState()
        {
            ClearDropVisuals();
            RemoveDragClasses(dragSourceRow);
            dragSourceRow = null;
            draggingNode = null;
            dragStartPosition = default;
        }

        /// <summary>移除虚拟化行可能残留的所有拖拽样式。</summary>
        /// <param name="row">待清理的树行。</param>
        private static void RemoveDragClasses(VisualElement row)
        {
            if (row == null)
            {
                return;
            }

            row.RemoveFromClassList("red-dot-tree-row--drag-source");
            row.RemoveFromClassList("red-dot-tree-row--drop-before");
            row.RemoveFromClassList("red-dot-tree-row--drop-inside");
            row.RemoveFromClassList("red-dot-tree-row--drop-after");
        }

        /// <summary>判断元素是否位于指定视觉元素的子树中。</summary>
        /// <param name="element">待判断的视觉元素。</param>
        /// <param name="ancestor">可能的祖先元素。</param>
        /// <returns>元素属于祖先子树时返回 true。</returns>
        private static bool IsDescendantOf(VisualElement element, VisualElement ancestor)
        {
            VisualElement current = element;
            while (current != null)
            {
                if (ReferenceEquals(current, ancestor))
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        /// <summary>构造节点 TreeView 的右键操作菜单。</summary>
        /// <param name="eventData">右键菜单构造事件。</param>
        private void OnTreeContextMenuPopulate(ContextualMenuPopulateEvent eventData)
        {
            if (FindTreeRow(eventData.target as VisualElement) != null)
            {
                return;
            }

            PopulateEmptyTreeContextMenu(eventData);
        }

        /// <summary>为 TreeView 空白区域填充根节点创建和刷新菜单。</summary>
        /// <param name="eventData">右键菜单构造事件。</param>
        private void PopulateEmptyTreeContextMenu(ContextualMenuPopulateEvent eventData)
        {
            eventData.menu.AppendAction(
                "新建根节点",
                _ => OnCreateRootNodeClicked(),
                GetMenuStatus(displayedConfig != null));
            eventData.menu.AppendAction(
                "刷新节点树",
                _ => OnRefreshConfigClicked(),
                GetMenuStatus(displayedConfig != null));
        }

        /// <summary>为节点行填充操作菜单，并把右键目标同步为当前选择。</summary>
        /// <param name="row">触发菜单的虚拟化 TreeView 行。</param>
        /// <param name="eventData">右键菜单构造事件。</param>
        private void PopulateNodeContextMenu(
            VisualElement row,
            ContextualMenuPopulateEvent eventData)
        {
            RedDotNodeSettingsViewData contextNode = row.userData as RedDotNodeSettingsViewData;
            if (contextNode == null)
            {
                return;
            }

            // 右键目标必须成为当前选中节点，随后所有菜单命令都使用该节点 Asset。
            SelectNode(contextNode.Key);
            bool canMoveUp = contextNode.SiblingIndex > 0;
            bool canMoveDown = contextNode.SiblingIndex < contextNode.SiblingCount - 1;
            bool canDelete = contextNode.Children.Count == 0;
            RedDotKey contextKey = contextNode.Key;

            eventData.menu.AppendAction(
                "新建子节点",
                _ => CreateNodeRequested?.Invoke(newNodeNameField.value, contextKey),
                GetMenuStatus(displayedConfig != null));
            eventData.menu.AppendAction(
                "上移",
                _ => SiblingMoveRequested?.Invoke(contextKey, -1),
                GetMenuStatus(canMoveUp));
            eventData.menu.AppendAction(
                "下移",
                _ => SiblingMoveRequested?.Invoke(contextKey, 1),
                GetMenuStatus(canMoveDown));
            eventData.menu.AppendSeparator();
            eventData.menu.AppendAction(
                "Ping Asset",
                _ => PingAssetRequested?.Invoke(contextKey),
                GetMenuStatus(true));
            eventData.menu.AppendSeparator();
            eventData.menu.AppendAction(
                "删除节点",
                _ => DeleteNodeRequested?.Invoke(contextKey),
                GetMenuStatus(canDelete));
            eventData.menu.AppendAction(
                "删除整个子树",
                _ => DeleteSubtreeRequested?.Invoke(contextKey),
                GetMenuStatus(true));
            eventData.StopPropagation();
        }

        /// <summary>将布尔启用状态转换为 UI Toolkit 右键菜单状态回调。</summary>
        /// <param name="enabled">菜单项是否可用。</param>
        /// <returns>菜单状态回调。</returns>
        private static Func<DropdownMenuAction, DropdownMenuAction.Status> GetMenuStatus(bool enabled)
        {
            return _ => enabled
                ? DropdownMenuAction.Status.Normal
                : DropdownMenuAction.Status.Disabled;
        }

        /// <summary>寻找指针事件目标所在的节点行。</summary>
        /// <param name="element">指针事件目标。</param>
        /// <returns>节点行；未落在节点行时返回 null。</returns>
        private static VisualElement FindTreeRow(VisualElement element)
        {
            VisualElement current = element;
            while (current != null)
            {
                if (current.ClassListContains("red-dot-tree-row"))
                {
                    return current;
                }

                current = current.parent;
            }

            return null;
        }

        /// <summary>设置当前选择并刷新右侧详情。</summary>
        /// <param name="node">新的选中节点。</param>
        private void SetSelectedNode(RedDotNodeSettingsViewData node)
        {
            // 节点切换后旧路径错误已经失去上下文，必须先清理，再渲染新节点详情。
            ClearDerivedPathError();
            selectedNode = node;
            bool hasSelection = node != null;
            detailEmptyState.style.display = hasSelection
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            detailContent.style.display = hasSelection
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            UpdateNodeCommandState();
            segmentNameField.SetEnabled(hasSelection);
            parentField.SetEnabled(hasSelection);
            pingAssetButton.SetEnabled(hasSelection);
            deleteNodeButton.SetEnabled(hasSelection && node.Children.Count == 0);
            deleteSubtreeButton.SetEnabled(hasSelection);
            deleteNodeButton.tooltip = hasSelection && node.Children.Count > 0
                ? "当前节点仍有子节点，请先迁移子节点，或删除整个子树。"
                : string.Empty;

            if (!hasSelection)
            {
                segmentNameField.SetValueWithoutNotify(string.Empty);
                parentField.SetValueWithoutNotify(null);
                configDetailLabel.text = displayedConfig == null
                    ? "请选择一个 RedDotConfig。"
                    : $"Config：{displayedConfig.name}\n请选择节点以查看和编辑。";
                derivedPathField.SetValueWithoutNotify(string.Empty);
                siblingOrderLabel.text = "排序：";
                directChildrenCountLabel.text = "直接子节点：0";
                assetPathField.SetValueWithoutNotify(string.Empty);
                return;
            }

            RedDotKey key = node.Key;
            configDetailLabel.text = displayedConfig == null
                ? $"节点：{key.name}"
                : $"Config：{displayedConfig.name}\n节点：{key.name}";
            segmentNameField.SetValueWithoutNotify(key.SegmentName);
            parentField.SetValueWithoutNotify(key.Parent);
            derivedPathField.SetValueWithoutNotify(key.DerivedPath);
            siblingOrderLabel.text = $"排序：{key.SiblingOrder}";
            directChildrenCountLabel.text = $"直接子节点：{node.Children.Count}";
            assetPathField.SetValueWithoutNotify(AssetDatabase.GetAssetPath(key));
        }

        /// <summary>根据 Config、选中节点和同级位置刷新节点工具栏状态。</summary>
        private void UpdateNodeCommandState()
        {
            bool hasConfig = displayedConfig != null;
            bool hasSelection = selectedNode != null;
            bool canMoveUp = hasSelection && selectedNode.SiblingIndex > 0;
            bool canMoveDown = hasSelection &&
                               selectedNode.SiblingIndex < selectedNode.SiblingCount - 1;

            newNodeNameField.SetEnabled(hasConfig);
            createRootNodeButton.SetEnabled(hasConfig);
            createChildNodeButton.SetEnabled(hasConfig && hasSelection);
            moveUpButton.SetEnabled(canMoveUp);
            moveDownButton.SetEnabled(canMoveDown);
            createChildNodeButton.tooltip = hasSelection
                ? $"在 {selectedNode.DerivedPath} 下创建子节点。"
                : "请先选择一个父节点。";
            moveUpButton.tooltip = canMoveUp
                ? "将当前节点移动到上一个同级节点之前。"
                : "当前节点已经是同级第一个节点。";
            moveDownButton.tooltip = canMoveDown
                ? "将当前节点移动到下一个同级节点之后。"
                : "当前节点已经是同级最后一个节点。";
        }

        /// <summary>清空 TreeView 和当前节点详情。</summary>
        private void ClearTree()
        {
            displayedConfig = null;
            ClearDragState();
            refreshingTree = true;
            treeView.SetRootItems(
                new List<TreeViewItemData<RedDotNodeSettingsViewData>>());
            treeView.Rebuild();
            treeView.ClearSelection();
            refreshingTree = false;
            nodeByKeyMap = new Dictionary<RedDotKey, RedDotNodeSettingsViewData>();
            SetSelectedNode(null);
        }

        #endregion

        #region UI 回调

        /// <summary>转发 Config ObjectField 变化。</summary>
        private void OnConfigChanged(ChangeEvent<UnityEngine.Object> eventData) =>
            ConfigChangedRequested?.Invoke(eventData.newValue as RedDotConfig);

        /// <summary>转发 SerializedObject 绑定后的新建目录变化。</summary>
        /// <param name="property">已变化的新建目录属性。</param>
        private void OnNodeFolderPropertyChanged(SerializedProperty property)
        {
            if (!suppressNodeFolderChange)
            {
                NodeFolderChangedRequested?.Invoke(property.stringValue);
            }
        }

        /// <summary>转发节点名称提交。</summary>
        private void OnSegmentNameChanged(ChangeEvent<string> eventData)
        {
            if (selectedNode != null)
            {
                SegmentNameSubmitted?.Invoke(selectedNode.Key, eventData.newValue);
            }
        }

        /// <summary>转发 Parent ObjectField 变化。</summary>
        private void OnParentChanged(ChangeEvent<UnityEngine.Object> eventData)
        {
            if (selectedNode != null)
            {
                ParentChangedRequested?.Invoke(selectedNode.Key, eventData.newValue as RedDotKey);
            }
        }

        /// <summary>转发延迟提交的目标父节点路径。</summary>
        /// <param name="eventData">路径输入变化事件。</param>
        private void OnDerivedPathChanged(ChangeEvent<string> eventData)
        {
            if (selectedNode != null)
            {
                DerivedParentPathSubmitted?.Invoke(selectedNode.Key, eventData.newValue);
            }
        }

        /// <summary>执行派生路径错误提示的定时隐藏。</summary>
        private void HideDerivedPathError()
        {
            derivedPathErrorHideSchedule = null;
            if (derivedPathErrorHelpBox != null)
            {
                derivedPathErrorHelpBox.style.display = DisplayStyle.None;
            }
        }

        /// <summary>处理 TreeView 选择变化。</summary>
        private void OnTreeSelectionChanged(IEnumerable<object> selection)
        {
            if (!refreshingTree)
            {
                SetSelectedNode(selection?.OfType<RedDotNodeSettingsViewData>().FirstOrDefault());
            }
        }

        /// <summary>记录一行的拖拽起点。</summary>
        private void OnRowPointerDown(PointerDownEvent eventData)
        {
            OnTreeRowPointerDown(eventData);
        }

        /// <summary>转发创建 Config 请求。</summary>
        private void OnCreateConfigClicked() => CreateConfigRequested?.Invoke();

        /// <summary>转发创建目录请求。</summary>
        private void OnCreateFolderClicked() => CreateFolderRequested?.Invoke();

        /// <summary>转发刷新 Config 请求。</summary>
        private void OnRefreshConfigClicked() => RefreshConfigRequested?.Invoke();

        /// <summary>转发新建根节点请求。</summary>
        private void OnCreateRootNodeClicked() =>
            CreateNodeRequested?.Invoke(newNodeNameField.value, null);

        /// <summary>转发新建子节点请求。</summary>
        private void OnCreateChildNodeClicked() =>
            CreateNodeRequested?.Invoke(newNodeNameField.value, selectedNode?.Key);

        /// <summary>请求将选中节点上移一个同级位置。</summary>
        private void OnMoveUpClicked() => RequestSiblingMove(-1);

        /// <summary>请求将选中节点下移一个同级位置。</summary>
        private void OnMoveDownClicked() => RequestSiblingMove(1);

        /// <summary>将同级移动意图转发给 Controller。</summary>
        /// <param name="delta">同级位置变化量，-1 表示上移，1 表示下移。</param>
        private void RequestSiblingMove(int delta)
        {
            if (selectedNode != null)
            {
                SiblingMoveRequested?.Invoke(selectedNode.Key, delta);
            }
        }

        /// <summary>转发定位 Asset 请求。</summary>
        private void OnPingAssetClicked() =>
            PingAssetRequested?.Invoke(selectedNode?.Key);

        /// <summary>转发删除节点请求。</summary>
        private void OnDeleteNodeClicked() =>
            DeleteNodeRequested?.Invoke(selectedNode?.Key);

        /// <summary>转发删除子树请求。</summary>
        private void OnDeleteSubtreeClicked() =>
            DeleteSubtreeRequested?.Invoke(selectedNode?.Key);

        #endregion

        #region 控件辅助

        /// <summary>取得 UXML 中的必需控件。</summary>
        /// <typeparam name="T">控件类型。</typeparam>
        /// <param name="root">查找根节点。</param>
        /// <param name="name">控件名称。</param>
        /// <returns>找到的控件。</returns>
        private static T Require<T>(VisualElement root, string name) where T : VisualElement
        {
            T element = root.Q<T>(name);
            if (element == null)
            {
                throw new InvalidOperationException($"[RedDotNodeSettings] UXML 缺少控件：{name}。");
            }

            return element;
        }

        #endregion
    }

    /// <summary>描述节点拖拽落点语义。</summary>
    internal enum NodeDropPlacement
    {
        /// <summary>放入目标节点成为最后一个子节点。</summary>
        AppendAsChild,
        /// <summary>插入目标节点之前。</summary>
        Before,
        /// <summary>插入目标节点之后。</summary>
        After,
        /// <summary>放到根节点列表末尾。</summary>
        AppendToRoot
    }
}
#endif
