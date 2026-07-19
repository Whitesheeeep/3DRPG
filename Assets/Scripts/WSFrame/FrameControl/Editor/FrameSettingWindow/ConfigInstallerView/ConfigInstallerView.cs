using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.ConfigInstaller;
using Object = UnityEngine.Object;

namespace WS_Modules
{
    internal sealed class ConfigInstallerView
    {
        private const string PanelUxmlPath =
            "Assets/Scripts/WSFrame/FrameControl/Editor/FrameSettingWindow/ConfigInstallerView/ConfigInstallerPanel.uxml";

        private readonly ConfigInstallerViewModel viewModel;
        private readonly VisualElement root;

        private ObjectField rootNodeField;
        private TreeView treeView;
        private Label selectedNodeTitle;
        private Button pingNodeButton;
        private VisualElement detailContainer;
        private VisualElement childrenControls;
        private ObjectField childNodeField;
        private Editor cachedEditor;
        private ConfigRegisterNodeBase pendingChild;
        private bool treeRefreshQueued;
        private bool disposed;

        public ConfigInstallerView(VisualElement root, ConfigInstallerViewModel viewModel)
        {
            this.root = root;
            this.viewModel = viewModel;
        }

        public void Bind()
        {
            BuildLayout();
            RegisterEvents();
            viewModel.Refresh();
        }

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
        }

        private void RegisterEvents()
        {
            rootNodeField.RegisterValueChangedCallback(evt =>
                viewModel.SetRootNode(evt.newValue as ConfigRegisterNodeBase));
            childNodeField.RegisterValueChangedCallback(evt => pendingChild = evt.newValue as ConfigRegisterNodeBase);

            root.Q<Button>("CreateRootButton").clicked += () => viewModel.CreateOrFindRootNode();
            root.Q<Button>("RegisterAllButton").clicked += viewModel.RegisterAll;
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
            Undo.undoRedoPerformed += QueueTreeRefreshFromModel;

            // FrameSettingWindow rebuilds this panel when switching modules, so release cached Editor references on detach.
            root.RegisterCallback<DetachFromPanelEvent>(_ => Dispose());
        }

        private static VisualElement MakeTreeItem()
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("config-tree-row");
            row.Add(new Label { name = "NodeName" });
            row.Add(new Label { name = "NodeType" });
            return row;
        }

        private void BindTreeItem(VisualElement element, int index)
        {
            ConfigTreeNodeViewData data = treeView.GetItemDataForIndex<ConfigTreeNodeViewData>(index);
            Label nameLabel = element.Q<Label>("NodeName");
            Label typeLabel = element.Q<Label>("NodeType");
            nameLabel.text = data.DisplayName;
            nameLabel.style.paddingLeft = data.Depth * 12;
            typeLabel.text = data.IsComposite ? $"{data.TypeName} / Composite" : data.TypeName;
            element.EnableInClassList("config-tree-row-missing", data.IsNull);
        }

        private void RefreshState()
        {
            rootNodeField.SetValueWithoutNotify(viewModel.RootNode);
        }

        private void RefreshTree()
        {
            treeView.SetRootItems<ConfigTreeNodeViewData>(viewModel.RootItems);
            treeView.Rebuild();
            treeView.ExpandAll();
        }

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

            // Concrete node fields are drawn by Unity Inspector; View/ViewModel stay focused on the tree.
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

        private void QueueTreeRefreshFromModel()
        {
            if (treeRefreshQueued)
            {
                return;
            }

            treeRefreshQueued = true;
            EditorApplication.delayCall += RefreshTreeFromModelDelayed;
        }

        private void RefreshTreeFromModelDelayed()
        {
            treeRefreshQueued = false;
            if (disposed)
            {
                return;
            }

            viewModel.RefreshTreeFromModel();
        }

        private void ClearCachedEditor()
        {
            if (cachedEditor == null)
            {
                return;
            }

            Object.DestroyImmediate(cachedEditor);
            cachedEditor = null;
        }

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
            Undo.undoRedoPerformed -= QueueTreeRefreshFromModel;
            EditorApplication.delayCall -= RefreshTreeFromModelDelayed;
            ClearCachedEditor();
        }
    }
}

