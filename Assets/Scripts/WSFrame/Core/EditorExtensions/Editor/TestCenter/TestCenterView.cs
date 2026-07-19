using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace WS_Modules.EditorExtensions
{
    internal sealed class TestCenterView
    {
        private readonly VisualElement root;
        private readonly TestCenterViewModel viewModel;

        private TextField searchField;
        private Button refreshButton;
        private Button createTestObjectButton;
        private Button loadButton;
        private Button removeButton;
        private Button pingScriptButton;
        private Button pingInstanceButton;
        private ListView testerListView;
        private Label selectedTitle;
        private Label selectedSubtitle;
        private VisualElement inspectorContainer;
        private VisualElement emptyState;
        private Editor cachedEditor;
        private bool disposed;

        public TestCenterView(VisualElement root, TestCenterViewModel viewModel)
        {
            this.root = root;
            this.viewModel = viewModel;
        }

        public void Bind()
        {
            QueryElements();
            ConfigureListView();
            RegisterEvents();
            viewModel.Refresh();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            viewModel.TestersChanged -= RefreshList;
            viewModel.SelectionChanged -= RefreshSelection;
            Undo.undoRedoPerformed -= RefreshFromUndo;
            ClearCachedEditor();
        }

        private void QueryElements()
        {
            searchField = root.Q<TextField>("SearchField");
            refreshButton = root.Q<Button>("RefreshButton");
            createTestObjectButton = root.Q<Button>("CreateTestObjectButton");
            loadButton = root.Q<Button>("LoadButton");
            removeButton = root.Q<Button>("RemoveButton");
            pingScriptButton = root.Q<Button>("PingScriptButton");
            pingInstanceButton = root.Q<Button>("PingInstanceButton");
            testerListView = root.Q<ListView>("TesterListView");
            selectedTitle = root.Q<Label>("SelectedTitle");
            selectedSubtitle = root.Q<Label>("SelectedSubtitle");
            inspectorContainer = root.Q<VisualElement>("InspectorContainer");
            emptyState = root.Q<VisualElement>("EmptyState");
        }

        private void ConfigureListView()
        {
            testerListView.fixedItemHeight = 56;
            testerListView.selectionType = SelectionType.Single;
            testerListView.itemsSource = viewModel.FilteredTesters.ToList();
            testerListView.makeItem = MakeTesterRow;
            testerListView.bindItem = BindTesterRow;
            testerListView.selectionChanged += selection =>
            {
                viewModel.Select(selection?.OfType<TesterViewData>().FirstOrDefault());
            };
        }

        private void RegisterEvents()
        {
            searchField.RegisterValueChangedCallback(evt => viewModel.SetSearchKeyword(evt.newValue));
            refreshButton.clicked += viewModel.Refresh;
            createTestObjectButton.clicked += () => viewModel.CreateOrFindTestObject();
            loadButton.clicked += viewModel.LoadSelectedTester;
            removeButton.clicked += viewModel.RemoveSelectedTester;
            pingScriptButton.clicked += viewModel.PingSelectedScript;
            pingInstanceButton.clicked += viewModel.PingSelectedInstance;
            viewModel.TestersChanged += RefreshList;
            viewModel.SelectionChanged += RefreshSelection;
            Undo.undoRedoPerformed += RefreshFromUndo;
            root.RegisterCallback<DetachFromPanelEvent>(_ => Dispose());
        }

        private static VisualElement MakeTesterRow()
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("tester-row");

            VisualElement textBlock = new VisualElement();
            textBlock.AddToClassList("tester-row-text");
            Label typeNameLabel = new Label { name = "TypeName" };
            typeNameLabel.AddToClassList("tester-row-title");
            textBlock.Add(typeNameLabel);
            Label pathLabel = new Label { name = "Path" };
            pathLabel.AddToClassList("tester-row-path");
            textBlock.Add(pathLabel);
            row.Add(textBlock);

            Label statusLabel = new Label { name = "Status" };
            statusLabel.AddToClassList("tester-row-status");
            row.Add(statusLabel);
            return row;
        }

        private void BindTesterRow(VisualElement element, int index)
        {
            TesterViewData tester = viewModel.FilteredTesters[index];
            element.Q<Label>("TypeName").text = tester.TypeName;
            element.Q<Label>("Path").text = string.IsNullOrEmpty(tester.ScriptPath)
                ? tester.NamespaceName
                : tester.ScriptPath;
            Label status = element.Q<Label>("Status");
            status.text = tester.StatusText;
            status.EnableInClassList("is-loaded", tester.IsLoaded);
        }

        private void RefreshList()
        {
            testerListView.itemsSource = viewModel.FilteredTesters.ToList();
            testerListView.Rebuild();

            int selectedIndex = viewModel.FilteredTesters
                .Select((tester, index) => tester == viewModel.SelectedTester ? index : -1)
                .FirstOrDefault(index => index >= 0);

            if (selectedIndex >= 0)
            {
                testerListView.SetSelectionWithoutNotify(new[] { selectedIndex });
            }
            else
            {
                testerListView.ClearSelection();
            }
        }

        private void RefreshSelection()
        {
            ClearCachedEditor();
            inspectorContainer.Clear();

            TesterViewData selected = viewModel.SelectedTester;
            bool hasSelection = selected != null;
            bool hasInstance = selected?.Instance != null;

            selectedTitle.text = hasSelection ? selected.TypeName : "未选择 Tester";
            selectedSubtitle.text = hasSelection ? $"{selected.NamespaceName} / {selected.StatusText}" : "请选择左侧 Tester";
            emptyState.EnableInClassList("is-hidden", hasInstance);
            inspectorContainer.EnableInClassList("is-hidden", !hasInstance);

            loadButton.SetEnabled(hasSelection && !hasInstance);
            removeButton.SetEnabled(hasInstance);
            pingScriptButton.SetEnabled(selected?.Script != null);
            pingInstanceButton.SetEnabled(hasInstance);

            if (!hasInstance)
            {
                return;
            }

            DrawInspector(selected.Instance);
        }

        private void DrawInspector(Object target)
        {
            try
            {
                InspectorElement inspectorElement = new InspectorElement(target);
                inspectorElement.AddToClassList("tester-inspector-element");
                inspectorContainer.Add(inspectorElement);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"[总测试窗口] InspectorElement 绘制失败，改用缓存 Editor 绘制。{exception.Message}");
                Editor.CreateCachedEditor(target, null, ref cachedEditor);
                inspectorContainer.Add(new IMGUIContainer(() =>
                {
                    if (cachedEditor == null)
                    {
                        return;
                    }

                    cachedEditor.OnInspectorGUI();
                }));
            }
        }

        private void RefreshFromUndo()
        {
            viewModel.Refresh();
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
    }
}
