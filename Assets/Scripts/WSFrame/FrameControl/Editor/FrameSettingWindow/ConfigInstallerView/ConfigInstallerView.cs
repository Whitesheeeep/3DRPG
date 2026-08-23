using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.ConfigInstaller;
using Object = UnityEngine.Object;

namespace WS_Modules
{
    /// <summary>
    /// ConfigInstaller 面板视图，负责构建 UI Toolkit 控件并转发用户操作。
    /// </summary>
    internal sealed class ConfigInstallerView
    {
        #region 常量与字段

        private const string PanelUxmlPath =
            "Assets/Scripts/WSFrame/FrameControl/Editor/FrameSettingWindow/ConfigInstallerView/ConfigInstallerPanel.uxml";

        private readonly ConfigInstallerViewModel viewModel;
        private readonly VisualElement root;

        private ObjectField rootNodeField;
        private TreeView treeView;
        private ListView availableNodeList;
        private Label availableNodeScanStatus;
        private Button applyAvailableNodesButton;
        private Label selectedNodeTitle;
        private Button pingNodeButton;
        private VisualElement detailContainer;
        private VisualElement childrenControls;
        private ObjectField childNodeField;
        private Editor cachedEditor;
        private ConfigRegisterNodeBase pendingChild;
        private bool treeRefreshQueued;
        private bool disposed;

        #endregion

        #region 生命周期

        /// <summary>
        /// 创建 ConfigInstaller 面板视图。
        /// </summary>
        /// <param name="root">承载面板内容的根元素。</param>
        /// <param name="viewModel">面板状态和配置树操作模型。</param>
        public ConfigInstallerView(VisualElement root, ConfigInstallerViewModel viewModel)
        {
            this.root = root;
            this.viewModel = viewModel;
        }

        /// <summary>
        /// 构建并绑定面板；已有配置树先立即显示，节点发现随后延迟执行。
        /// </summary>
        public void Bind()
        {
            BuildLayout();
            RegisterEvents();
            viewModel.Refresh();
            viewModel.BeginAvailableNodeDiscovery();
        }

        /// <summary>
        /// 面板分离时释放缓存的 Unity Editor 和延迟扫描回调。
        /// </summary>
        private void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            viewModel.StateChanged -= RefreshState;
            viewModel.TreeChanged -= RefreshTree;
            viewModel.SelectionChanged -= RefreshSelection;
            viewModel.AvailableNodesChanged -= RefreshAvailableNodeList;
            viewModel.AvailableNodeScanStateChanged -= RefreshAvailableNodeScanState;
            Undo.undoRedoPerformed -= QueueTreeRefreshFromModel;
            EditorApplication.delayCall -= RefreshTreeFromModelDelayed;
            viewModel.Dispose();
            ClearCachedEditor();
        }

        #endregion

        #region 布局与事件

        /// <summary>
        /// 加载 UXML 并配置树、列表和对象字段控件。
        /// </summary>
        private void BuildLayout()
        {
            VisualTreeAsset visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(PanelUxmlPath);
            if (visualTree == null)
            {
                root.Add(new HelpBox($"Missing UXML: {PanelUxmlPath}", HelpBoxMessageType.Error));
                return;
            }

            visualTree.CloneTree(root);

            rootNodeField = root.Q<ObjectField>("RootNodeField");
            treeView = root.Q<TreeView>("ConfigTreeView");
            availableNodeList = root.Q<ListView>("AvailableNodeList");
            availableNodeScanStatus = root.Q<Label>("AvailableNodeScanStatus");
            applyAvailableNodesButton = root.Q<Button>("ApplyAvailableNodesButton");
            selectedNodeTitle = root.Q<Label>("SelectedNodeTitle");
            pingNodeButton = root.Q<Button>("PingNodeButton");
            detailContainer = root.Q<VisualElement>("DetailContainer");
            childrenControls = root.Q<VisualElement>("ChildrenControls");
            childNodeField = root.Q<ObjectField>("ChildNodeField");

            rootNodeField.objectType = typeof(ConfigRegisterNodeBase);
            rootNodeField.allowSceneObjects = false;
            childNodeField.objectType = typeof(ConfigRegisterNodeBase);
            childNodeField.allowSceneObjects = false;

            treeView.fixedItemHeight = 28;
            treeView.selectionType = SelectionType.Single;
            treeView.makeItem = MakeTreeItem;
            treeView.bindItem = BindTreeItem;
            treeView.autoExpand = true;

            availableNodeList.itemsSource = viewModel.AvailableNodeOptions;
            // 每行包含类型、资产和 SelectedNode 状态三行文本，固定高度必须覆盖完整内容。
            availableNodeList.fixedItemHeight = 62;
            availableNodeList.selectionType = SelectionType.None;
            availableNodeList.makeItem = MakeAvailableNodeItem;
            availableNodeList.bindItem = BindAvailableNodeItem;
            availableNodeList.unbindItem = UnbindAvailableNodeItem;
        }

        /// <summary>
        /// 注册对象字段、按钮、列表和模型事件。
        /// </summary>
        private void RegisterEvents()
        {
            rootNodeField.RegisterValueChangedCallback(evt =>
                viewModel.SetRootNode(evt.newValue as ConfigRegisterNodeBase));
            childNodeField.RegisterValueChangedCallback(evt => pendingChild = evt.newValue as ConfigRegisterNodeBase);

            root.Q<Button>("CreateRootButton").clicked += () => viewModel.CreateOrFindRootNode();
            root.Q<Button>("RegisterAllButton").clicked += viewModel.RegisterAll;
            applyAvailableNodesButton.clicked += viewModel.ApplySelectedNodes;
            root.Q<Button>("AddChildButton").clicked += () => viewModel.AddChildToSelectedComposite(pendingChild);
            root.Q<Button>("RemoveSelectedButton").clicked += viewModel.RemoveSelectedNode;
            root.Q<Button>("MoveUpButton").clicked += () => viewModel.MoveSelectedNode(-1);
            root.Q<Button>("MoveDownButton").clicked += () => viewModel.MoveSelectedNode(1);
            pingNodeButton.clicked += () => viewModel.Ping(viewModel.SelectedNode?.Node);

            treeView.selectionChanged += selection =>
            {
                viewModel.Select(selection?.OfType<ConfigTreeNodeViewData>().FirstOrDefault());
            };

            viewModel.StateChanged += RefreshState;
            viewModel.TreeChanged += RefreshTree;
            viewModel.SelectionChanged += RefreshSelection;
            viewModel.AvailableNodesChanged += RefreshAvailableNodeList;
            viewModel.AvailableNodeScanStateChanged += RefreshAvailableNodeScanState;
            Undo.undoRedoPerformed += QueueTreeRefreshFromModel;

            // FrameSettingWindow 切换模块时会重建面板，因此在分离时释放缓存的 Editor 和延迟任务。
            root.RegisterCallback<DetachFromPanelEvent>(_ => Dispose());
        }

        #endregion

        #region 配置树列表

        /// <summary>
        /// 创建一个配置树列表行。
        /// </summary>
        /// <returns>未绑定数据的配置树行元素。</returns>
        private VisualElement MakeTreeItem()
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("config-tree-row");
            row.Add(new Label { name = "NodeName" });
            row.Add(new Label { name = "NodeType" });
            row.AddManipulator(new ContextualMenuManipulator(evt => PopulateTreeItemContextMenu(row, evt)));
            return row;
        }

        /// <summary>
        /// 将配置树节点数据绑定到可视化行。
        /// </summary>
        /// <param name="element">待绑定的行元素。</param>
        /// <param name="index">数据索引。</param>
        private void BindTreeItem(VisualElement element, int index)
        {
            ConfigTreeNodeViewData data = treeView.GetItemDataForIndex<ConfigTreeNodeViewData>(index);
            element.userData = data;
            Label nameLabel = element.Q<Label>("NodeName");
            Label typeLabel = element.Q<Label>("NodeType");
            nameLabel.text = data.DisplayName;
            nameLabel.style.paddingLeft = data.Depth * 12;
            typeLabel.text = data.IsComposite ? $"{data.TypeName} / Composite" : data.TypeName;
            element.EnableInClassList("config-tree-row-missing", data.IsNull);
        }

        /// <summary>
        /// 为配置树节点行填充右键菜单，并先将右键目标同步为当前选择。
        /// </summary>
        /// <param name="row">触发菜单的虚拟化树行。</param>
        /// <param name="evt">UI Toolkit 上下文菜单事件。</param>
        private void PopulateTreeItemContextMenu(VisualElement row, ContextualMenuPopulateEvent evt)
        {
            ConfigTreeNodeViewData data = row.userData as ConfigTreeNodeViewData;
            if (data == null)
            {
                return;
            }

            // 右键操作必须以当前行作为目标，避免删除之前左键选中的其他节点。
            treeView.SetSelection(new[] { data.Id });
            if (viewModel.SelectedNode != data)
            {
                viewModel.Select(data);
            }

            evt.menu.AppendAction(
                "Delete Node",
                _ => viewModel.RemoveSelectedNode(),
                _ => viewModel.CanRemoveSelectedNode
                    ? DropdownMenuAction.Status.Normal
                    : DropdownMenuAction.Status.Disabled);
            evt.StopPropagation();
        }

        /// <summary>
        /// 刷新配置树列表并保持树节点展开状态。
        /// </summary>
        private void RefreshTree()
        {
            treeView.SetRootItems<ConfigTreeNodeViewData>(viewModel.RootItems);
            treeView.Rebuild();
            treeView.ExpandAll();
        }

        #endregion

        #region 可发现节点列表

        /// <summary>
        /// 创建一个带勾选框、类型名称和资产状态的节点发现行。
        /// </summary>
        /// <returns>未绑定数据的节点发现行元素。</returns>
        private VisualElement MakeAvailableNodeItem()
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("config-node-option-row");

            Toggle toggle = new Toggle { name = "NodeToggle" };
            toggle.RegisterValueChangedCallback(evt =>
            {
                if (row.userData is ConfigRegisterNodeOptionViewData option)
                {
                    viewModel.SetAvailableNodeSelection(option, evt.newValue);
                }
            });
            row.Add(toggle);

            VisualElement textContainer = new VisualElement();
            textContainer.AddToClassList("config-node-option-text");
            textContainer.Add(new Label { name = "NodeType" });
            textContainer.Add(new Label { name = "NodeAsset" });
            textContainer.Add(new Label { name = "NodeState" });
            row.Add(textContainer);
            return row;
        }

        /// <summary>
        /// 将节点发现选项绑定到虚拟化列表行。
        /// </summary>
        /// <param name="element">待绑定的行元素。</param>
        /// <param name="index">数据索引。</param>
        private void BindAvailableNodeItem(VisualElement element, int index)
        {
            ConfigRegisterNodeOptionViewData option = viewModel.AvailableNodeOptions[index];
            Toggle toggle = element.Q<Toggle>("NodeToggle");
            Label typeLabel = element.Q<Label>("NodeType");
            Label assetLabel = element.Q<Label>("NodeAsset");
            Label stateLabel = element.Q<Label>("NodeState");

            element.userData = option;
            toggle.SetValueWithoutNotify(option.IsSelected);
            typeLabel.text = option.TypeDisplayName;
            assetLabel.text = option.AssetDisplayName;
            stateLabel.text = option.InclusionDisplayName;
            element.EnableInClassList("config-node-option-included", option.IsIncluded);
            element.EnableInClassList("config-node-option-not-included", !option.IsIncluded);
        }

        /// <summary>
        /// 清理被列表虚拟化回收的节点发现行状态。
        /// </summary>
        /// <param name="element">待解绑的行元素。</param>
        /// <param name="index">数据索引。</param>
        private static void UnbindAvailableNodeItem(VisualElement element, int index)
        {
            element.userData = null;
        }

        /// <summary>
        /// 刷新节点发现列表的行绑定和应用按钮状态。
        /// </summary>
        private void RefreshAvailableNodeList()
        {
            availableNodeList.Rebuild();
            RefreshAvailableNodeScanState();
        }

        /// <summary>
        /// 刷新节点扫描提示文本和应用按钮可用状态。
        /// </summary>
        private void RefreshAvailableNodeScanState()
        {
            if (viewModel.IsDiscoveringAvailableNodes)
            {
                availableNodeScanStatus.text = "Scanning ConfigRegisterNodeBase types...";
                availableNodeScanStatus.EnableInClassList("config-installer-status-warning", false);
            }
            else if (viewModel.AvailableNodeOptions.Count == 0)
            {
                availableNodeScanStatus.text = "No concrete config register node types found.";
                availableNodeScanStatus.EnableInClassList("config-installer-status-warning", false);
            }
            else if (!viewModel.CanApplyAvailableNodes)
            {
                availableNodeScanStatus.text = viewModel.SelectedNode == null
                    ? "Select a Composite node before applying selections."
                    : "The selected node is not a Composite node.";
                availableNodeScanStatus.EnableInClassList("config-installer-status-warning", true);
            }
            else if (!string.IsNullOrWhiteSpace(viewModel.AvailableNodeActionMessage))
            {
                availableNodeScanStatus.text = viewModel.AvailableNodeActionMessage;
                availableNodeScanStatus.EnableInClassList("config-installer-status-warning", false);
            }
            else
            {
                availableNodeScanStatus.text =
                    $"{viewModel.AvailableNodeOptions.Count} node type(s) found. Target: {viewModel.SelectedNode.DisplayName}.";
                availableNodeScanStatus.EnableInClassList("config-installer-status-warning", false);
            }

            applyAvailableNodesButton.SetEnabled(viewModel.CanApplyAvailableNodes && viewModel.AvailableNodeOptions.Count > 0);
        }

        #endregion

        #region 详情与状态刷新

        /// <summary>
        /// 刷新根节点对象字段和节点发现操作状态。
        /// </summary>
        private void RefreshState()
        {
            rootNodeField.SetValueWithoutNotify(viewModel.RootNode);
            RefreshAvailableNodeScanState();
        }

        /// <summary>
        /// 重绘当前选中节点的 Inspector 详情。
        /// </summary>
        private void RefreshSelection()
        {
            detailContainer.Clear();
            ClearCachedEditor();

            ConfigTreeNodeViewData selected = viewModel.SelectedNode;
            childrenControls.style.display =
                selected?.Node is CompositeConfigRegisterNode ? DisplayStyle.Flex : DisplayStyle.None;

            if (selected?.Node == null)
            {
                selectedNodeTitle.text = "No Node Selected";
                pingNodeButton.SetEnabled(false);
                detailContainer.Add(new HelpBox("No config node selected.", HelpBoxMessageType.Info));
                return;
            }

            selectedNodeTitle.text = $"{selected.DisplayName} ({selected.TypeName})";
            pingNodeButton.SetEnabled(true);

            // 具体节点字段由 Unity Inspector 绘制，View/ViewModel 只负责树和状态。
            Editor.CreateCachedEditor(selected.Node, null, ref cachedEditor);
            detailContainer.Add(new IMGUIContainer(() =>
            {
                if (cachedEditor != null)
                {
                    EditorGUI.BeginChangeCheck();
                    cachedEditor.OnInspectorGUI();
                    if (EditorGUI.EndChangeCheck())
                    {
                        QueueTreeRefreshFromModel();
                    }
                }
            }));
        }

        /// <summary>
        /// 将 Inspector 或 Undo 产生的树变化合并到下一次编辑器更新。
        /// </summary>
        private void QueueTreeRefreshFromModel()
        {
            if (treeRefreshQueued)
            {
                return;
            }

            treeRefreshQueued = true;
            EditorApplication.delayCall += RefreshTreeFromModelDelayed;
        }

        /// <summary>
        /// 执行排队的配置树刷新并忽略已分离面板的回调。
        /// </summary>
        private void RefreshTreeFromModelDelayed()
        {
            treeRefreshQueued = false;
            if (disposed)
            {
                return;
            }

            viewModel.RefreshTreeFromModel();
        }

        /// <summary>
        /// 销毁缓存的节点 Inspector，避免模块切换后持有旧资产编辑器。
        /// </summary>
        private void ClearCachedEditor()
        {
            if (cachedEditor == null)
            {
                return;
            }

            Object.DestroyImmediate(cachedEditor);
            cachedEditor = null;
        }

        #endregion
    }
}
