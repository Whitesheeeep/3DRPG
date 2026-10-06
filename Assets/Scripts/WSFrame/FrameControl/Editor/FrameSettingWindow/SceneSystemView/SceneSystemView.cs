using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.SceneModule;
using Object = UnityEngine.Object;

namespace WS_Modules
{
    /// <summary>构建双栏场景编排界面，并将结构修改交给 SceneSystemController。</summary>
    internal sealed class SceneSystemView
    {
        #region 视图状态

        // 模板定位常量。
        private const string PanelUxmlPath =
            "Assets/Scripts/WSFrame/FrameControl/Editor/FrameSettingWindow/SceneSystemView/SceneSystemPanel.uxml";

        // 依赖与控件：Controller 管理资产结构，本视图持有 UI 元素和当前交互状态。
        private readonly VisualElement host;
        private readonly SceneSystemController controller = new();
        private VisualElement panelRoot;
        private ObjectField databaseField;
        private ObjectField existingConfigField;
        private VisualElement assetFolderFieldHost;
        private IMGUIContainer assetFolderFieldContainer;
        private SerializedObject editorSettingsSerializedObject;
        private ToolbarSearchField searchField;
        private TreeView treeView;
        private Label emptyTreeLabel;
        private VisualElement detailContent;
        private VisualElement validationContent;
        private TextField executionPreviewField;
        private Label detailTitle;
        private Label detailSubtitle;
        private Label selectionBadge;
        private Label statusLabel;
        private VisualElement statusIndicator;
        private Button createDatabaseButton;
        private Button addConfigButton;
        private Button createConfigButton;
        private Button refreshButton;
        private Button expandAllButton;
        private Button collapseAllButton;
        private Button validateAllButton;
        // 缓存当前筛选结果的完整树数据，查询不依赖 TreeView 虚拟化后的可见行数量。
        private readonly List<SceneLoadTreeItem> renderedTreeItems = new();
        private SceneLoadTreeItem selectedItem;
        private SceneLoadTreeItem draggingItem;
        private VisualElement dragSourceRow;
        private VisualElement dropTargetRow;
        private Vector2 dragStartPosition;
        private bool disposed;
        private bool projectRefreshQueued;

        #endregion

        #region 面板生命周期

        /// <summary>创建场景系统视图并记录承载它的模块容器。</summary>
        /// <param name="host">FrameSettingWindow 中的模块内容区。</param>
        public SceneSystemView(VisualElement host)
        {
            this.host = host;
        }

        /// <summary>载入 UXML、恢复面板状态并注册树、菜单、拖拽和撤销事件。</summary>
        public void Bind()
        {
            VisualTreeAsset visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(PanelUxmlPath);
            if (visualTree == null)
            {
                host.Add(new HelpBox($"找不到场景编排面板 UXML：{PanelUxmlPath}", HelpBoxMessageType.Error));
                return;
            }

            visualTree.CloneTree(host);
            panelRoot = host.Q<VisualElement>(className: "scene-system-root");
            FindControls();
            controller.RestoreEditorSettings();
            databaseField.SetValueWithoutNotify(controller.Database);
            BindAssetFolderField();
            ConfigureTree();
            RegisterCallbacks();
            RenderTree();
            SetEditingEnabled(!EditorApplication.isPlaying);
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
            EditorApplication.projectChanged += OnProjectChanged;
            panelRoot.RegisterCallback<DetachFromPanelEvent>(OnPanelDetached);
        }

        // 生命周期释放在模块切换或窗口关闭时执行。
        /// <summary>释放视图回调和临时拖拽状态，避免切换 FrameSetting 模块后仍持有窗口元素。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            EditorApplication.projectChanged -= OnProjectChanged;
            if (panelRoot != null) panelRoot.UnregisterCallback<DetachFromPanelEvent>(OnPanelDetached);
            if (treeView != null)
            {
                treeView.selectionChanged -= OnTreeSelectionChanged;
                treeView.UnregisterCallback<ContextualMenuPopulateEvent>(OnEmptyTreeContextMenu);
                treeView.UnregisterCallback<PointerMoveEvent>(OnTreePointerMove);
                treeView.UnregisterCallback<PointerUpEvent>(OnTreePointerUp);
                treeView.UnregisterCallback<PointerCancelEvent>(OnTreePointerCancel);
                treeView.UnregisterCallback<PointerLeaveEvent>(OnTreePointerLeave);
                treeView.UnregisterCallback<DragUpdatedEvent>(OnProjectDragUpdated);
                treeView.UnregisterCallback<DragPerformEvent>(OnProjectDragPerform);
                treeView.UnregisterCallback<DragExitedEvent>(OnProjectDragExited);
                treeView.Unbind();
            }
            if (detailContent != null) detailContent.Unbind();
            if (assetFolderFieldContainer != null) assetFolderFieldContainer.onGUIHandler = null;
            assetFolderFieldHost?.Clear();
            editorSettingsSerializedObject = null;
            ClearDragVisuals();
        }

        /// <summary>查找面板 UXML 中的控件，并配置资产类型筛选。</summary>
        private void FindControls()
        {
            databaseField = Require<ObjectField>(host, "DatabaseField");
            existingConfigField = Require<ObjectField>(host, "ExistingConfigField");
            assetFolderFieldHost = Require<VisualElement>(host, "AssetFolderFieldHost");
            searchField = Require<ToolbarSearchField>(host, "SearchField");
            treeView = Require<TreeView>(host, "SceneTreeView");
            emptyTreeLabel = Require<Label>(host, "EmptyTreeLabel");
            detailContent = Require<VisualElement>(host, "DetailContent");
            detailTitle = Require<Label>(host, "DetailTitle");
            detailSubtitle = Require<Label>(host, "DetailSubtitle");
            selectionBadge = Require<Label>(host, "SelectionBadge");
            statusLabel = Require<Label>(host, "StatusLabel");
            statusIndicator = Require<VisualElement>(host, "StatusIndicator");
            createDatabaseButton = Require<Button>(host, "CreateDatabaseButton");
            addConfigButton = Require<Button>(host, "AddConfigButton");
            createConfigButton = Require<Button>(host, "CreateConfigButton");
            refreshButton = Require<Button>(host, "RefreshButton");
            expandAllButton = Require<Button>(host, "ExpandAllButton");
            collapseAllButton = Require<Button>(host, "CollapseAllButton");
            validateAllButton = Require<Button>(host, "ValidateAllButton");

            databaseField.objectType = typeof(SceneLoadDatabase);
            databaseField.allowSceneObjects = false;
            existingConfigField.objectType = typeof(SceneLoadConfig);
            existingConfigField.allowSceneObjects = false;
        }

        /// <summary>以 SerializedObject 绘制项目设置，让 Unity 自动调用 WSFolderPath 属性绘制器。</summary>
        private void BindAssetFolderField()
        {
            editorSettingsSerializedObject = new SerializedObject(SceneLoadingEditorSettings.instance);
            assetFolderFieldContainer = new IMGUIContainer(DrawAssetFolderField);
            assetFolderFieldContainer.AddToClassList("scene-folder-imgui");
            assetFolderFieldHost.Add(assetFolderFieldContainer);
        }

        /// <summary>提交目录属性变更后同步 Controller，并将设置保存回项目设置文件。</summary>
        private void DrawAssetFolderField()
        {
            if (disposed || editorSettingsSerializedObject == null) return;

            editorSettingsSerializedObject.UpdateIfRequiredOrScript();
            SerializedProperty folderProperty = editorSettingsSerializedObject.FindProperty("nodeAssetFolder");
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(folderProperty, new GUIContent("新建资产目录"));
            if (!EditorGUI.EndChangeCheck() || !editorSettingsSerializedObject.ApplyModifiedProperties()) return;

            controller.SynchronizeAssetFolderFromEditorSettings();
            editorSettingsSerializedObject.Update();
            SetStatus($"新资产将保存在 {controller.AssetFolder}。", false);
        }

        // 树控件布局及 TreeView 交互配置。
        /// <summary>设置 TreeView 行渲染、选择和结构拖放回调。</summary>
        private void ConfigureTree()
        {
            treeView.fixedItemHeight = 27;
            treeView.selectionType = SelectionType.Single;
            treeView.autoExpand = true;
            treeView.makeItem = MakeTreeRow;
            treeView.bindItem = BindTreeRow;
            WS_Modules.UIToolkitExtensions.Editor.CustomTwoPanelSplitView splitView =
                host.Q<WS_Modules.UIToolkitExtensions.Editor.CustomTwoPanelSplitView>();
            splitView?.ConfigureFixedPane(250f, 330f, 520f, "WSFrame.SceneSystem.TreePaneWidth");
        }

        /// <summary>注册工具栏、详情和树操作回调。</summary>
        private void RegisterCallbacks()
        {
            databaseField.RegisterValueChangedCallback(OnDatabaseChanged);
            existingConfigField.RegisterValueChangedCallback(OnExistingConfigChanged);
            searchField.RegisterValueChangedCallback(OnSearchChanged);
            createDatabaseButton.clicked += OnCreateDatabaseClicked;
            addConfigButton.clicked += OnAddConfigClicked;
            createConfigButton.clicked += OnCreateConfigClicked;
            refreshButton.clicked += OnRefreshClicked;
            expandAllButton.clicked += treeView.ExpandAll;
            collapseAllButton.clicked += treeView.CollapseAll;
            validateAllButton.clicked += ValidateAll;
            treeView.selectionChanged += OnTreeSelectionChanged;
            treeView.RegisterCallback<ContextualMenuPopulateEvent>(OnEmptyTreeContextMenu);
            treeView.RegisterCallback<PointerMoveEvent>(OnTreePointerMove);
            treeView.RegisterCallback<PointerUpEvent>(OnTreePointerUp);
            treeView.RegisterCallback<PointerCancelEvent>(OnTreePointerCancel);
            treeView.RegisterCallback<PointerLeaveEvent>(OnTreePointerLeave);
            treeView.RegisterCallback<DragUpdatedEvent>(OnProjectDragUpdated);
            treeView.RegisterCallback<DragPerformEvent>(OnProjectDragPerform);
            treeView.RegisterCallback<DragExitedEvent>(OnProjectDragExited);
        }

        #endregion

        #region 树详情渲染

        /// <summary>重建场景与任务树，优先按路径恢复选择，路径变化时按原资产位置回退匹配。</summary>
        private void RenderTree()
        {
            SceneLoadTreeItem previousSelection = selectedItem;
            string selectedPath = selectedItem?.ReferencePath;
            controller.SynchronizeDatabaseSceneNames();
            List<TreeViewItemData<SceneLoadTreeItem>> rootItems = controller.BuildTreeItems(searchField.value);
            renderedTreeItems.Clear();
            CollectTreeItems(rootItems);
            bool hasVisibleItems = rootItems.Count > 0;
            emptyTreeLabel.text = string.IsNullOrWhiteSpace(searchField.value)
                ? controller.Database == null
                    ? "请选择或创建场景配置库。"
                    : "此配置库还没有场景配置。点击“新建配置”或右键添加。"
                : "没有找到匹配的场景或任务。";
            emptyTreeLabel.style.display = hasVisibleItems ? DisplayStyle.None : DisplayStyle.Flex;
            treeView.SetRootItems(rootItems);
            treeView.Rebuild();
            treeView.ExpandAll();
            SceneLoadTreeItem match = string.IsNullOrEmpty(selectedPath)
                ? null
                : FindTreeItemByPath(selectedPath);
            if (match == null && previousSelection != null)
            {
                match = previousSelection.IsConfiguration
                    ? FindConfigItem(previousSelection.Config)
                    : FindTaskItem(previousSelection.Task, previousSelection.ParentTask,
                        previousSelection.Config, previousSelection.SiblingIndex);
            }
            if (match != null)
            {
                selectedItem = match;
                treeView.SetSelectionByIdWithoutNotify(new[] { match.Id });
                BindDetails(match);
                return;
            }
            selectedItem = null;
            BindDetails(null);
        }

        /// <summary>按树结构顺序缓存当前筛选结果中的所有场景和任务引用行。</summary>
        /// <param name="treeItems">TreeView 根节点及其嵌套子节点。</param>
        private void CollectTreeItems(IEnumerable<TreeViewItemData<SceneLoadTreeItem>> treeItems)
        {
            foreach (TreeViewItemData<SceneLoadTreeItem> treeItem in treeItems)
            {
                renderedTreeItems.Add(treeItem.data);
                CollectTreeItems(treeItem.children);
            }
        }

        /// <summary>在当前虚拟树数据中按引用位置路径查找一行。</summary>
        /// <param name="referencePath">完整树内引用路径。</param>
        /// <returns>匹配行；搜索过滤或资产变更后不存在时返回 null。</returns>
        private SceneLoadTreeItem FindTreeItemByPath(string referencePath)
        {
            foreach (SceneLoadTreeItem item in renderedTreeItems)
            {
                if (item != null && string.Equals(item.ReferencePath, referencePath, StringComparison.Ordinal))
                    return item;
            }
            return null;
        }

        /// <summary>创建一个自定义树行，用类型徽标和顺序提示区分任务角色。</summary>
        /// <returns>等待 TreeView 绑定数据的行元素。</returns>
        private VisualElement MakeTreeRow()
        {
            var row = new VisualElement { userData = null };
            row.AddToClassList("scene-tree-row");
            row.Add(new Label { name = "SceneTreeLabel" });
            row.Add(new Label { name = "SceneTreeType" });
            row.Add(new Label { name = "SceneTreeOrder" });
            row.RegisterCallback<PointerDownEvent>(OnTreeRowPointerDown);
            row.AddManipulator(new ContextualMenuManipulator(menuEvent => OnRowContextMenu(menuEvent, row)));
            return row;
        }

        /// <summary>把本次引用位置绑定到可回收复用的虚拟树行。</summary>
        /// <param name="element">待填充行元素。</param>
        /// <param name="index">TreeView 当前可见行索引。</param>
        private void BindTreeRow(VisualElement element, int index)
        {
            ClearRowDropStyle(element);
            SceneLoadTreeItem item = treeView.GetItemDataForIndex<SceneLoadTreeItem>(index);
            element.userData = item;
            element.Q<Label>("SceneTreeLabel").text = item.IsConfiguration
                ? item.Config.DisplayName
                : item.Task.name;
            Label typeLabel = element.Q<Label>("SceneTreeType");
            typeLabel.text = item.TypeLabel;
            typeLabel.EnableInClassList("scene-chip-scene", item.IsConfiguration);
            typeLabel.EnableInClassList("scene-chip-sequence", item.Task is SequenceSceneLoadTask);
            typeLabel.EnableInClassList("scene-chip-parallel", item.Task is ParallelSceneLoadTask);
            typeLabel.EnableInClassList("scene-chip-task", item.Task != null &&
                !(item.Task is SequenceSceneLoadTask) && !(item.Task is ParallelSceneLoadTask));
            typeLabel.EnableInClassList("scene-type-chip--cycle", item.TypeLabel.Contains("CYCLE"));
            element.Q<Label>("SceneTreeOrder").text = (item.SiblingIndex + 1).ToString();
            element.tooltip = item.ReferencePath + (item.Task == null ? string.Empty : "\n" + AssetDatabase.GetAssetPath(item.Task));
        }

        /// <summary>解绑旧资产字段，按当前行创建基本信息、参数、引用、操作和校验分组。</summary>
        /// <param name="item">当前选中的配置或任务行。</param>
        private void BindDetails(SceneLoadTreeItem item)
        {
            detailContent.Unbind();
            detailContent.Clear();
            validationContent = null;
            executionPreviewField = null;
            selectedItem = item;
            bool canEdit = !EditorApplication.isPlaying;
            detailContent.SetEnabled(canEdit);
            if (item == null)
            {
                detailTitle.text = "场景详情";
                detailSubtitle.text = "选择配置或任务节点以查看详情";
                selectionBadge.text = "未选择";
                detailContent.Add(new HelpBox("选中场景配置或某一个任务引用位置，右侧即可查看配置、任务参数和共享范围。", HelpBoxMessageType.Info));
                return;
            }

            selectionBadge.text = item.IsConfiguration ? "SCENE" : item.TypeLabel;
            if (item.IsConfiguration) BindConfigDetails(item);
            else BindTaskDetails(item);
        }

        /// <summary>绘制场景配置属性、执行顺序、节点操作和校验结果。</summary>
        /// <param name="item">配置树行。</param>
        private void BindConfigDetails(SceneLoadTreeItem item)
        {
            SceneLoadConfig config = item.Config;
            detailTitle.text = config.DisplayName;
            detailSubtitle.text = $"场景配置  ·  {config.SceneId}";
            AddBoundProperties(config, "基本信息", "sceneId", "displayName", "sceneReference", "sceneName", "loadMode", "rootTask");

            VisualElement assets = AddSection("资产与引用", string.Empty);
            assets.Add(CreateCaption(
                $"资产路径：{AssetDatabase.GetAssetPath(config)}\n当前数据库引用：{controller.GetConfigReferenceCount(config)} 处"));

            VisualElement operations = AddSection("节点操作", "scene-detail-section--accent");
            var createRootButton = new Button(() => ShowCreateTaskMenu(item)) { text = "创建或替换根任务" };
            operations.Add(createRootButton);
            var copyButton = new Button(() => CopyConfig(item)) { text = "复制配置与任务树" };
            operations.Add(copyButton);
            var existingTaskField = new ObjectField("使用已有根任务")
            {
                objectType = typeof(SceneLoadTask),
                allowSceneObjects = false
            };
            operations.Add(existingTaskField);
            var setRootButton = new Button(() =>
            {
                if (existingTaskField.value is SceneLoadTask task)
                {
                    controller.SetRootTask(config, task);
                    RefreshAfterMutation("已设置根任务引用。");
                }
            }) { text = "设置已有任务为根节点" };
            operations.Add(setRootButton);
            operations.Add(CreateActionRow(
                ("定位资产", () => SceneSystemController.PingAsset(item), false),
                ("移除配置引用", () => RemoveConfigReference(item), false),
                ("删除配置资产…", () => DeleteConfigAsset(item), true)));

            AddExecutionPreview(config);
            AddValidation(config);
        }

        /// <summary>绘制任务资产名称、具体字段、项目引用位置及节点操作。</summary>
        /// <param name="item">任务引用树行。</param>
        private void BindTaskDetails(SceneLoadTreeItem item)
        {
            SceneLoadTask task = item.Task;
            detailTitle.text = task.name;
            detailSubtitle.text = $"{task.GetType().Name}  ·  {item.Config.SceneId}";

            VisualElement basics = AddSection("基本信息", string.Empty);
            var taskNameField = new TextField("节点名称") { value = task.name, isDelayed = true };
            taskNameField.RegisterValueChangedCallback(evt =>
            {
                controller.RenameTask(task, evt.newValue);
                RefreshAfterMutation("任务资产名称已更新；共享引用位置同步显示。",
                    task, item.ParentTask, item.Config, item.SiblingIndex);
            });
            basics.Add(taskNameField);
            basics.Add(CreateCaption($"类型标识：{task.GetType().Name}\n引用路径：{item.ReferencePath}"));
            AddTaskParameters(task);

            VisualElement assetSection = AddSection("资产与引用", string.Empty);
            string assetPath = AssetDatabase.GetAssetPath(task);
            assetSection.Add(CreateCaption($"资产路径：{assetPath}\n{controller.GetReferenceSummary(task)}"));

            VisualElement operations = AddSection("节点操作", "scene-detail-section--accent");
            if (IsComposite(task))
            {
                operations.Add(new Button(() => ShowCreateTaskMenu(item)) { text = "新建子任务" });
                var existingTaskField = new ObjectField("引用已有子任务")
                {
                    objectType = typeof(SceneLoadTask),
                    allowSceneObjects = false
                };
                operations.Add(existingTaskField);
                operations.Add(new Button(() =>
                {
                    if (existingTaskField.value is SceneLoadTask existingTask)
                    {
                        controller.AddExistingTask(item, existingTask);
                        RefreshAfterMutation("已追加现有任务引用。");
                    }
                }) { text = "追加已有任务" });
            }

            operations.Add(CreateActionRow(
                ("定位资产", () => SceneSystemController.PingAsset(item), false),
                ("上移", () => MoveSelected(-1), false),
                ("下移", () => MoveSelected(1), false)));
            operations.Add(CreateActionRow(
                ("复制子树", () => CopyTask(item), false),
                ("移除引用", () => RemoveTaskReference(item), false),
                ("删除任务资产…", () => DeleteTaskAsset(item), true)));
            if (item.ParentTask == null)
                operations.Add(CreateCaption("根任务只能通过配置操作替换；复制配置会同时复制整棵任务树。"));

            AddExecutionPreview(item.Config);
            AddValidation(item.Config);
        }

        // 详情卡片及原生序列化字段创建。
        /// <summary>按配置属性路径创建 Unity 原生 PropertyField 并绑定唯一 SerializedObject。</summary>
        /// <param name="target">需要原生序列化绑定的配置资产。</param>
        /// <param name="sectionTitle">详情分组标题。</param>
        /// <param name="propertyPaths">要显示的序列化字段路径。</param>
        private void AddBoundProperties(Object target, string sectionTitle, params string[] propertyPaths)
        {
            VisualElement section = AddSection(sectionTitle, string.Empty);
            if (target is SceneLoadConfig)
                section.Add(CreateCaption("场景引用 Reference 是实际加载依据；SceneName 从引用自动生成，只用于场景名称校验。"));
            var serializedTarget = new SerializedObject(target);
            serializedTarget.Update();
            foreach (string propertyPath in propertyPaths)
            {
                SerializedProperty property = serializedTarget.FindProperty(propertyPath);
                if (property != null)
                {
                    var propertyField = new PropertyField(property);
                    if (target is SceneLoadConfig && string.Equals(propertyPath, "sceneName", StringComparison.Ordinal))
                        propertyField.SetEnabled(false);
                    propertyField.TrackPropertyValue(property, changedProperty =>
                        QueueSerializedRefresh(changedProperty.propertyPath));
                    section.Add(propertyField);
                }
            }
            detailContent.Bind(serializedTarget);
        }

        /// <summary>显示任务类型除 children 列表外的原生可序列化参数。</summary>
        /// <param name="task">当前任务资产。</param>
        private void AddTaskParameters(SceneLoadTask task)
        {
            VisualElement section = AddSection("任务参数", string.Empty);
            var serializedTask = new SerializedObject(task);
            serializedTask.Update();
            SerializedProperty iterator = serializedTask.GetIterator();
            bool hasProperty = iterator.NextVisible(true);
            while (hasProperty)
            {
                if (iterator.depth == 1 && iterator.name != "m_Script" && iterator.name != "m_Name" && iterator.name != "children")
                {
                    SerializedProperty property = iterator.Copy();
                    var propertyField = new PropertyField(property);
                    propertyField.TrackPropertyValue(property, changedProperty =>
                        QueueSerializedRefresh(changedProperty.propertyPath));
                    section.Add(propertyField);
                }
                hasProperty = iterator.NextVisible(false);
            }
            if (section.childCount == 1) section.Add(CreateCaption("此任务没有可编辑的额外参数。"));
            detailContent.Bind(serializedTask);
        }

        /// <summary>按当前选中配置添加执行顺序预览。</summary>
        private void AddExecutionPreview(SceneLoadConfig config)
        {
            VisualElement section = AddSection("执行顺序预览", string.Empty);
            executionPreviewField = new TextField
            {
                name = "ExecutionPreviewField",
                value = controller.GetExecutionPreview(config),
                multiline = true,
                isReadOnly = true
            };
            executionPreviewField.AddToClassList("scene-preview-field");
            section.Add(executionPreviewField);
        }

        /// <summary>在详情面板中显示当前配置的结构和 Addressables 场景校验结果。</summary>
        private void AddValidation(SceneLoadConfig config)
        {
            VisualElement section = AddSection("校验结果", string.Empty);
            validationContent = new VisualElement();
            section.Add(validationContent);
            RefreshValidationContents(config);
        }

        /// <summary>更新详情中的路径化结构错误，保持已经聚焦的原生序列化字段控件不变。</summary>
        /// <param name="config">当前显示的场景配置。</param>
        private void RefreshValidationContents(SceneLoadConfig config)
        {
            if (validationContent == null) return;
            validationContent.Clear();
            SceneLoadValidationResult result = controller.Validate(config);
            if (result.IsValid)
            {
                validationContent.Add(CreateCaption("✓ 配置结构可执行。"));
                return;
            }
            foreach (string issue in result.Issues)
            {
                var label = new Label("• " + issue);
                label.AddToClassList("scene-validation-message");
                validationContent.Add(label);
            }
        }

        /// <summary>创建带统一卡片样式的详情分组。</summary>
        /// <param name="title">分组标题。</param>
        /// <param name="additionalClass">可选样式。</param>
        /// <returns>已经加入详情区的分组。</returns>
        private VisualElement AddSection(string title, string additionalClass)
        {
            var section = new VisualElement();
            section.AddToClassList("scene-detail-section");
            if (!string.IsNullOrEmpty(additionalClass)) section.AddToClassList(additionalClass);
            section.Add(new Label(title) { name = "SectionTitle" });
            section.Q<Label>("SectionTitle").AddToClassList("scene-detail-section-title");
            detailContent.Add(section);
            return section;
        }

        /// <summary>创建详情区说明文本。</summary>
        /// <param name="text">说明内容。</param>
        /// <returns>带换行样式的说明 Label。</returns>
        private static Label CreateCaption(string text)
        {
            var caption = new Label(text);
            caption.AddToClassList("scene-detail-caption");
            return caption;
        }

        /// <summary>创建一行上下文操作按钮并在 Play Mode 禁用修改类操作。</summary>
        /// <param name="actions">按钮标题、操作和危险样式三元组。</param>
        /// <returns>操作按钮容器。</returns>
        private VisualElement CreateActionRow(params (string label, Action action, bool isDanger)[] actions)
        {
            var row = new VisualElement();
            row.AddToClassList("scene-action-row");
            foreach ((string label, Action action, bool isDanger) in actions)
            {
                var button = new Button(action) { text = label };
                if (isDanger) button.AddToClassList("scene-danger-button");
                button.SetEnabled(!EditorApplication.isPlaying);
                row.Add(button);
            }
            return row;
        }

        #endregion

        #region 结构操作

        /// <summary>右键行时先同步选择，再根据配置、组合或叶子上下文填充菜单。</summary>
        /// <param name="eventData">当前行菜单事件。</param>
        /// <param name="row">触发菜单的虚拟化行。</param>
        private void OnRowContextMenu(ContextualMenuPopulateEvent eventData, VisualElement row)
        {
            if (row.userData is not SceneLoadTreeItem item) return;
            SelectItem(item);
            bool editable = !EditorApplication.isPlaying;
            if (item.IsConfiguration)
            {
                AddMenuAction(eventData, "编辑配置详情", _ => BindDetails(item), true);
                AddMenuAction(eventData, "在详情中引用已有根任务…", _ => FocusExistingTaskField(item), editable);
                AddTaskCreationMenuItems(eventData, item, editable);
                AddMenuAction(eventData, "复制配置与任务树", _ => CopyConfig(item), editable);
                AddMenuAction(eventData, "定位配置资产", _ => SceneSystemController.PingAsset(item), true);
                AddMenuAction(eventData, "移除配置引用", _ => RemoveConfigReference(item), editable);
                AddMenuAction(eventData, "删除配置资产…", _ => DeleteConfigAsset(item), editable, true);
                return;
            }

            AddMenuAction(eventData, "编辑任务参数", _ => BindDetails(item), true);
            AddMenuAction(eventData, "在详情中引用已有子任务…", _ => FocusExistingTaskField(item), editable && IsComposite(item.Task));
            AddTaskCreationMenuItems(eventData, item, editable && IsComposite(item.Task));
            AddMenuAction(eventData, "复制子树", _ => CopyTask(item), editable && item.ParentTask != null);
            AddMenuAction(eventData, "上移", _ => MoveSelected(-1), editable && controller.CanMoveTaskByOffset(item, -1));
            AddMenuAction(eventData, "下移", _ => MoveSelected(1), editable && controller.CanMoveTaskByOffset(item, 1));
            AddMenuAction(eventData, "定位任务资产", _ => SceneSystemController.PingAsset(item), true);
            AddMenuAction(eventData, "移除当前引用", _ => RemoveTaskReference(item), editable);
            AddMenuAction(eventData, "删除任务资产…", _ => DeleteTaskAsset(item), editable, true);
            AddMenuAction(eventData, "校验所属场景配置", _ => ValidateConfig(item.Config), true);
        }

        // 空白区域与节点菜单使用统一的上下文使能规则。
        /// <summary>为配置空白区提供新建、加入、刷新、校验和展开操作。</summary>
        /// <param name="eventData">TreeView 菜单事件。</param>
        private void OnEmptyTreeContextMenu(ContextualMenuPopulateEvent eventData)
        {
            if (FindTreeRow(eventData.target as VisualElement) != null) return;
            bool hasDatabase = controller.Database != null;
            bool editable = !EditorApplication.isPlaying;
            AddMenuAction(eventData, "新建场景配置", _ => CreateConfig(), editable && hasDatabase);
            AddMenuAction(eventData, "加入已有场景配置…", _ => FocusExistingConfigField(), editable && hasDatabase);
            AddMenuAction(eventData, "刷新数据库", _ => RefreshFromProject(), true);
            AddMenuAction(eventData, "展开全部", _ => treeView.ExpandAll(), true);
            AddMenuAction(eventData, "折叠全部", _ => treeView.CollapseAll(), true);
            AddMenuAction(eventData, "校验数据库内全部配置", _ => ValidateAll(), true);
        }

        /// <summary>把所有可实例化任务类型加入当前上下文菜单，并按组合与叶子分组。</summary>
        /// <param name="eventData">目标菜单。</param>
        /// <param name="item">新任务的目标配置或组合。</param>
        /// <param name="enabled">当前上下文是否允许添加子任务。</param>
        private void AddTaskCreationMenuItems(ContextualMenuPopulateEvent eventData, SceneLoadTreeItem item, bool enabled)
        {
            foreach (Type taskType in controller.GetCreatableTaskTypes())
            {
                string prefix = item.IsConfiguration ? "新建根 " : "新建子 ";
                string category = taskType == typeof(SequenceSceneLoadTask) ? prefix + "Sequence" :
                    taskType == typeof(ParallelSceneLoadTask) ? prefix + "Parallel" :
                    (item.IsConfiguration ? "新建根任务/" : "新建叶子任务/") + taskType.Name;
                AddMenuAction(eventData, category, _ => CreateTask(item, taskType), enabled);
            }
        }

        /// <summary>从空白区菜单定位顶部已有配置选择器。</summary>
        private void FocusExistingConfigField()
        {
            existingConfigField.Focus();
            SetStatus("在顶部“已有配置”中选择资产，再点击“加入”建立数据库引用。", false);
        }

        /// <summary>将菜单项设为可执行或禁用，并提供统一危险项视觉分组。</summary>
        private static void AddMenuAction(
            ContextualMenuPopulateEvent eventData,
            string label,
            Action<DropdownMenuAction> action,
            bool enabled,
            bool danger = false)
        {
            string menuLabel = danger ? "删除/" + label : label;
            eventData.menu.AppendAction(menuLabel, action,
                _ => enabled ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
        }

        /// <summary>为工具栏“创建或替换根任务”列出框架和业务层所有任务类型。</summary>
        private void ShowCreateTaskMenu(SceneLoadTreeItem item)
        {
            var menu = new GenericMenu();
            foreach (Type taskType in controller.GetCreatableTaskTypes())
            {
                Type capturedType = taskType;
                string label = capturedType == typeof(SequenceSceneLoadTask) ? "组合/Sequence" :
                    capturedType == typeof(ParallelSceneLoadTask) ? "组合/Parallel" : "叶子任务/" + capturedType.Name;
                menu.AddItem(new GUIContent(label), false, () => CreateTask(item, capturedType));
            }
            if (EditorApplication.isPlaying) menu = DisabledMenu("Play Mode 中不能编辑任务树。");
            menu.ShowAsContext();
        }

        /// <summary>打开详情区已有任务引用控件并引导用户完成对象引用选择。</summary>
        /// <param name="item">配置或组合节点目标。</param>
        private void FocusExistingTaskField(SceneLoadTreeItem item)
        {
            SelectItem(item);
            SetStatus("请在右侧“节点操作”选择已有 SceneLoadTask，再点击设置或追加按钮。", false);
        }

        /// <summary>新建任务资产、连接到当前配置或组合，再刷新已选引用位置。</summary>
        /// <param name="item">配置或组合目标。</param>
        /// <param name="taskType">所选新任务类型。</param>
        private void CreateTask(SceneLoadTreeItem item, Type taskType)
        {
            try
            {
                SceneLoadTask task = controller.CreateTask(item, taskType);
                RenderTree();
                SceneLoadTreeItem createdItem = item.IsConfiguration
                    ? FindTaskItem(task, null)
                    : FindTaskItem(task, item.Task);
                if (createdItem != null) SelectItem(createdItem);
                SetStatus($"已创建 {taskType.Name} 并加入任务树。", false);
            }
            catch (Exception exception) { SetStatus(exception.Message, true); }
        }

        /// <summary>创建一个只用于展示禁用原因的临时空菜单。</summary>
        private static GenericMenu DisabledMenu(string reason)
        {
            var menu = new GenericMenu();
            menu.AddDisabledItem(new GUIContent(reason));
            return menu;
        }

        /// <summary>尝试依据当前同级顺序上移或下移选中任务引用。</summary>
        /// <param name="offset">相对当前顺序的 -1 或 +1。</param>
        private void MoveSelected(int offset)
        {
            if (selectedItem?.ParentTask == null) return;
            SceneLoadTreeItem item = selectedItem;
            int targetIndex = item.SiblingIndex + offset;
            if (controller.MoveTaskByOffset(item, offset))
                RefreshAfterMutation("任务已按真实同级顺序移动。",
                    item.Task, item.ParentTask, item.Config, targetIndex);
        }

        /// <summary>判断任务节点是否为 Sequence 或 Parallel 组合。</summary>
        private static bool IsComposite(SceneLoadTask task) =>
            task is SequenceSceneLoadTask || task is ParallelSceneLoadTask;

        #endregion

        #region 拖拽交互

        /// <summary>开始记录可编辑的任务引用行及其指针起点；配置根节点不可作为移动源。</summary>
        /// <param name="eventData">行指针按下事件。</param>
        private void OnTreeRowPointerDown(PointerDownEvent eventData)
        {
            if (eventData.button != 0 || EditorApplication.isPlaying || !string.IsNullOrEmpty(searchField.value) ||
                !(eventData.currentTarget is VisualElement row) || !(row.userData is SceneLoadTreeItem item)) return;
            SelectItem(item);
            if (item.Task == null || item.ParentTask == null) return;
            draggingItem = item;
            dragSourceRow = row;
            dragStartPosition = eventData.position;
        }

        /// <summary>按与 RedDot 编辑器一致的阈值展示淡化拖拽源和插入位置反馈。</summary>
        /// <param name="eventData">场景树指针移动事件。</param>
        private void OnTreePointerMove(PointerMoveEvent eventData)
        {
            if (draggingItem == null || ((Vector2)eventData.position - dragStartPosition).sqrMagnitude < 36f) return;
            dragSourceRow?.AddToClassList("scene-tree-row--drag-source");
            ClearDropTargetStyle();
            if (!TryResolveTaskDrop(eventData.position,
                    draggingItem, out VisualElement targetRow, out _, out E_SceneLoadDropPlacement placement)) return;

            dropTargetRow = targetRow;
            targetRow.AddToClassList(placement switch
            {
                E_SceneLoadDropPlacement.Before => "scene-tree-row--drop-before",
                E_SceneLoadDropPlacement.After => "scene-tree-row--drop-after",
                _ => "scene-tree-row--drop-inside"
            });
        }

        /// <summary>依据与预览相同的落点解析器移动任务引用。</summary>
        /// <param name="eventData">释放位置事件。</param>
        private void OnTreePointerUp(PointerUpEvent eventData)
        {
            if (draggingItem == null) return;
            SceneLoadTreeItem source = draggingItem;
            bool movedEnough = ((Vector2)eventData.position - dragStartPosition).sqrMagnitude >= 36f;
            SceneLoadTreeItem target = null;
            E_SceneLoadDropPlacement placement = E_SceneLoadDropPlacement.Before;
            bool hasValidTarget = movedEnough && TryResolveTaskDrop(
                eventData.position, source,
                out _, out target, out placement);
            ClearDragVisuals();
            if (!hasValidTarget) return;

            if (controller.MoveTaskReference(source, target, placement,
                    out SceneLoadTask newParentTask, out int newSiblingIndex))
                RefreshAfterMutation("任务引用已移动；资产本身未复制。",
                    source.Task, newParentTask, target.Config, newSiblingIndex);
        }

        /// <summary>在 Unity 拖拽被取消时移除临时源行和落点样式。</summary>
        /// <param name="eventData">取消事件。</param>
        private void OnTreePointerCancel(PointerCancelEvent eventData) => ClearDragVisuals();

        /// <summary>指针离开树区域后清除拖动预览。</summary>
        /// <param name="eventData">离开事件。</param>
        private void OnTreePointerLeave(PointerLeaveEvent eventData)
        {
            if (draggingItem != null) ClearDragVisuals();
        }

        /// <summary>解析一个有效插入位置，令预览和松开操作使用同一结构校验。</summary>
        /// <param name="eventTarget">当前命中的视觉元素。</param>
        /// <param name="pointerPosition">面板坐标中的指针位置。</param>
        /// <param name="source">被拖动的任务引用行。</param>
        /// <param name="targetRow">合法目标视觉行。</param>
        /// <param name="targetItem">目标任务引用行。</param>
        /// <param name="placement">前置、后置或组合内部位置。</param>
        /// <returns>当前位置可以合法改变任务引用关系时返回 true。</returns>
        private bool TryResolveTaskDrop(
            Vector2 pointerPosition,
            SceneLoadTreeItem source,
            out VisualElement targetRow,
            out SceneLoadTreeItem targetItem,
            out E_SceneLoadDropPlacement placement)
        {
            // Pointer capture 会让 event.target 继续指向拖动源；必须按当前位置命中目标行。
            targetRow = FindTreeRowAt(pointerPosition);
            targetItem = targetRow?.userData as SceneLoadTreeItem;
            placement = E_SceneLoadDropPlacement.Before;
            if (targetItem?.Task == null) return false;

            Vector2 localPosition = targetRow.WorldToLocal(pointerPosition);
            float relativeY = Mathf.Clamp01(localPosition.y / Mathf.Max(1f, targetRow.layout.height));
            if (relativeY <= 0.25f)
                placement = E_SceneLoadDropPlacement.Before;
            else if (relativeY >= 0.75f)
                placement = E_SceneLoadDropPlacement.After;
            else if (IsComposite(targetItem.Task))
                placement = E_SceneLoadDropPlacement.Inside;
            else
                placement = relativeY < 0.5f
                    ? E_SceneLoadDropPlacement.Before
                    : E_SceneLoadDropPlacement.After;

            if (controller.CanMoveTaskReference(source, targetItem, placement)) return true;
            targetRow = null;
            targetItem = null;
            return false;
        }
        // Project DragAndDrop 使用 Unity 的资源引用操作，不复制项目资产。
        /// <summary>接受 Project 面板拖入的 SceneLoadTask 资产，并只在可接收节点上显示 Link 提示。</summary>
        /// <param name="eventData">Unity 资源拖动更新事件。</param>
        private void OnProjectDragUpdated(DragUpdatedEvent eventData)
        {
            SceneLoadTask task = DragAndDrop.objectReferences.OfType<SceneLoadTask>().FirstOrDefault();
            SceneLoadTreeItem target = FindTreeRowAt(eventData.mousePosition)?.userData as SceneLoadTreeItem;
            bool canReceive = !EditorApplication.isPlaying && string.IsNullOrEmpty(searchField.value) &&
                              task != null && target != null && (target.IsConfiguration
                                  ? target.Config.RootTask == null
                                  : IsComposite(target.Task));
            ClearDropTargetStyle();
            DragAndDrop.visualMode = canReceive ? DragAndDropVisualMode.Link : DragAndDropVisualMode.Rejected;
            treeView.EnableInClassList("scene-tree--drop-project", canReceive);
            if (canReceive)
            {
                dropTargetRow = FindTreeRowAt(eventData.mousePosition);
                dropTargetRow?.AddToClassList("scene-tree-row--drop-project-target");
            }
            eventData.StopPropagation();
        }

        /// <summary>建立 Project 任务资产到组合节点的引用，并刷新树视图。</summary>
        /// <param name="eventData">Unity 资源拖动完成事件。</param>
        private void OnProjectDragPerform(DragPerformEvent eventData)
        {
            SceneLoadTreeItem target = FindTreeRowAt(eventData.mousePosition)?.userData as SceneLoadTreeItem;
            IEnumerable<SceneLoadTask> tasks = DragAndDrop.objectReferences.OfType<SceneLoadTask>();
            treeView.RemoveFromClassList("scene-tree--drop-project");
            ClearDropTargetStyle();
            if (target == null || !tasks.Any())
            {
                eventData.StopPropagation();
                return;
            }
            DragAndDrop.AcceptDrag();
            int addedCount = 0;
            foreach (SceneLoadTask task in tasks)
                if (controller.DropExistingTask(target, task)) addedCount++;
            if (addedCount > 0) RefreshAfterMutation($"已从 Project 建立 {addedCount} 个任务引用。");
            eventData.StopPropagation();
        }

        /// <summary>Unity Project 拖拽退出树视图时清理落点边框。</summary>
        /// <param name="eventData">拖拽退出事件。</param>
        private void OnProjectDragExited(DragExitedEvent eventData)
        {
            treeView.RemoveFromClassList("scene-tree--drop-project");
            ClearDropTargetStyle();
        }

        /// <summary>按面板坐标定位鼠标下的虚拟化任务行。</summary>
        /// <param name="worldPosition">鼠标世界坐标。</param>
        /// <returns>命中的行元素。</returns>
        private VisualElement FindTreeRowAt(Vector2 worldPosition)
        {
            VisualElement found = null;
            treeView.Query<VisualElement>().ForEach(element =>
            {
                if (found == null && element.ClassListContains("scene-tree-row") &&
                    element.userData is SceneLoadTreeItem && element.worldBound.Contains(worldPosition))
                    found = element;
            });
            return found;
        }

        /// <summary>从事件目标向上查找包含任务引用数据的树行。</summary>
        /// <param name="element">当前事件目标。</param>
        /// <returns>对应任务行或 null。</returns>
        private VisualElement FindTreeRow(VisualElement element)
        {
            while (element != null && !ReferenceEquals(element, treeView))
            {
                if (element.ClassListContains("scene-tree-row") && element.userData is SceneLoadTreeItem)
                    return element;
                element = element.parent;
            }
            return null;
        }

        /// <summary>清除源行、目标行和 TreeView 的拖放视觉状态。</summary>
        private void ClearDragVisuals()
        {
            ClearDropTargetStyle();
            dragSourceRow?.RemoveFromClassList("scene-tree-row--drag-source");
            dragSourceRow = null;
            draggingItem = null;
            treeView?.RemoveFromClassList("scene-tree--drop-project");
        }

        /// <summary>清除目标行的插入或组合内部高亮状态。</summary>
        private void ClearDropTargetStyle()
        {
            if (dropTargetRow != null) ClearRowDropStyle(dropTargetRow);
            dropTargetRow = null;
        }

        /// <summary>清除因 TreeView 虚拟化复用而可能残留的全部落点样式。</summary>
        private static void ClearRowDropStyle(VisualElement row)
        {
            row.RemoveFromClassList("scene-tree-row--drag-source");
            row.RemoveFromClassList("scene-tree-row--drop-before");
            row.RemoveFromClassList("scene-tree-row--drop-after");
            row.RemoveFromClassList("scene-tree-row--drop-inside");
            row.RemoveFromClassList("scene-tree-row--drop-project-target");
        }

        #endregion

        #region 工具栏和配置操作

        /// <summary>同步用户在数据库字段中的资产选择，并立即显示对应配置。</summary>
        /// <param name="eventData">数据库字段变化事件。</param>
        private void OnDatabaseChanged(ChangeEvent<Object> eventData)
        {
            controller.SetDatabase(eventData.newValue as SceneLoadDatabase);
            selectedItem = null;
            RenderTree();
            SetStatus(controller.Database == null ? "请选择或创建场景配置库。" : "数据库已切换。", false);
        }

        /// <summary>缓存要加入当前数据库的已有配置资产。</summary>
        /// <param name="eventData">场景配置字段变化事件。</param>
        private void OnExistingConfigChanged(ChangeEvent<Object> eventData) =>
            SetStatus(eventData.newValue == null ? "选择一个已有 SceneLoadConfig 后可加入数据库。" : "已有配置已就绪，点击“加入”建立数据库引用。", false);

        /// <summary>搜索场景 ID、友好名称、任务资产名称和任务类型。</summary>
        /// <param name="eventData">搜索词变化事件。</param>
        private void OnSearchChanged(ChangeEvent<string> eventData)
        {
            ClearDragVisuals();
            RenderTree();
            SetStatus(string.IsNullOrWhiteSpace(eventData.newValue)
                ? "已显示完整任务树。"
                : "搜索期间已关闭结构拖拽；清空搜索后可继续拖动。", false);
        }

        /// <summary>创建空场景数据库并同步到顶部选择器。</summary>
        private void OnCreateDatabaseClicked()
        {
            try
            {
                SceneLoadDatabase database = controller.CreateDatabase();
                databaseField.SetValueWithoutNotify(database);
                RenderTree();
                SetStatus("场景配置数据库已创建。", false);
            }
            catch (Exception exception) { SetStatus(exception.Message, true); }
        }

        /// <summary>将已有配置加入数据库并清空临时选择框。</summary>
        private void OnAddConfigClicked()
        {
            try
            {
                if (existingConfigField.value is not SceneLoadConfig config)
                    throw new InvalidOperationException("请先在“已有配置”字段中选择 SceneLoadConfig。");
                controller.AddExistingConfig(config);
                existingConfigField.SetValueWithoutNotify(null);
                RenderTree();
                SetStatus($"已加入配置：{config.SceneId}。", false);
            }
            catch (Exception exception) { SetStatus(exception.Message, true); }
        }

        /// <summary>创建新的场景配置和 Sequence 根任务，并立即定位新配置。</summary>
        private void OnCreateConfigClicked() => CreateConfig();

        /// <summary>执行新建配置入口並在完成后刷新树视图。</summary>
        private void CreateConfig()
        {
            try
            {
                SceneLoadConfig config = controller.CreateConfig();
                searchField.SetValueWithoutNotify(string.Empty);
                selectedItem = null;
                RenderTree();
                SceneLoadTreeItem createdItem = FindConfigItem(config);
                if (createdItem != null) SelectItem(createdItem);
                SetStatus($"已创建 {config.SceneId}；请填写场景名称和 Addressable Scene 引用。", false);
            }
            catch (Exception exception) { SetStatus(exception.Message, true); }
        }

        /// <summary>刷新配置数据库、资产索引和树快照，不写入任何配置内容。</summary>
        private void OnRefreshClicked() => RefreshFromProject();

        /// <summary>等待 AssetDatabase 导入完成后重绘树和已选详情。</summary>
        private void RefreshFromProject()
        {
            AssetDatabase.Refresh();
            RenderTree();
            SetStatus("场景配置树已刷新；刷新操作没有改写配置。", false);
        }

        /// <summary>确认后只删除选中的配置资产，并在成功时异步合并树刷新。</summary>
        /// <param name="item">要删除资产的配置行。</param>
        private void DeleteConfigAsset(SceneLoadTreeItem item)
        {
            string preview = controller.GetConfigDeletionSummary(item.Config);
            if (!EditorUtility.DisplayDialog("删除场景配置资产", preview + "\n回收站删除不由 Ctrl+Z 恢复。继续吗？", "移入回收站", "取消")) return;

            // 先解除 SerializedObject 绑定，避免回收站移动时仍有 PropertyField 引用资产。
            detailContent.Unbind();
            selectedItem = null;
            if (!controller.DeleteConfigAsset(item))
            {
                BindDetails(item);
                SetStatus("配置资产未能移入回收站；数据库引用保持不变。", true);
                return;
            }

            BindDetails(null);
            QueueProjectTreeRefresh();
            SetStatus("配置资产已移入回收站；任务资产保留。", false);
        }

        /// <summary>只从当前库移除场景配置引用。</summary>
        /// <param name="item">目标配置树行。</param>
        private void RemoveConfigReference(SceneLoadTreeItem item)
        {
            controller.RemoveConfigReference(item.Config);
            selectedItem = null;
            RenderTree();
            SetStatus("已移除配置引用；配置和任务资产仍保留在项目中。", false);
        }

        /// <summary>复制场景配置及其任务树并把副本加入当前库。</summary>
        /// <param name="item">复制来源配置行。</param>
        private void CopyConfig(SceneLoadTreeItem item)
        {
            SceneLoadConfig copy = controller.CopyConfig(item.Config);
            RenderTree();
            SceneLoadTreeItem copyItem = FindConfigItem(copy);
            if (copyItem != null) SelectItem(copyItem);
            SetStatus($"已复制为独立配置 {copy.SceneId}。", false);
        }

        /// <summary>确认后只删除选中的任务资产，并在成功时异步合并树刷新。</summary>
        /// <param name="item">任务树行。</param>
        private void DeleteTaskAsset(SceneLoadTreeItem item)
        {
            string preview = controller.GetTaskDeletionSummary(item);
            if (!EditorUtility.DisplayDialog("删除场景任务资产", preview + "\n回收站删除不由 Ctrl+Z 恢复。继续吗？", "移入回收站", "取消")) return;

            // 任务详情绑定序列化字段；删除前解除绑定，失败时再绑定回当前行。
            detailContent.Unbind();
            selectedItem = null;
            if (!controller.DeleteTaskAsset(item))
            {
                BindDetails(item);
                SetStatus("任务资产未能移入回收站；当前任务引用保持不变。", true);
                return;
            }

            BindDetails(null);
            QueueProjectTreeRefresh();
            SetStatus("任务资产已移入回收站；子任务资产保留。", false);
        }

        /// <summary>只从当前配置或父组合移除任务引用，保留任务资产。</summary>
        /// <param name="item">需要解除的任务引用行。</param>
        private void RemoveTaskReference(SceneLoadTreeItem item)
        {
            controller.RemoveTaskReference(item);
            selectedItem = null;
            RenderTree();
            SetStatus("当前任务引用已移除；资产仍保留，可重新引用或撤销。", false);
        }

        /// <summary>复制任务子树并在当前父组合中新增副本引用。</summary>
        /// <param name="item">复制来源行。</param>
        private void CopyTask(SceneLoadTreeItem item)
        {
            SceneLoadTask clone = controller.CopyTaskSibling(item);
            RenderTree();
            SceneLoadTreeItem cloneItem = FindTaskItem(clone, item.ParentTask);
            if (cloneItem != null) SelectItem(cloneItem);
            SetStatus("任务子树已复制，副本可以独立编辑；原资产未修改。", false);
        }

        /// <summary>按所选配置显示结构校验结果。</summary>
        /// <param name="config">目标配置。</param>
        private void ValidateConfig(SceneLoadConfig config)
        {
            SceneLoadValidationResult result = controller.Validate(config);
            SetStatus(result.IsValid
                ? $"{config.SceneId} 校验通过。"
                : $"{config.SceneId} 有 {result.Issues.Count} 个校验错误。",
                !result.IsValid);
            BindDetails(selectedItem);
        }

        /// <summary>校验数据库中的全部配置并汇总错误数量，不保存或改写资产。</summary>
        private void ValidateAll()
        {
            if (controller.Database == null)
            {
                SetStatus("请先选择场景配置数据库。", true);
                return;
            }
            int invalidCount = 0;
            int issueCount = 0;
            foreach (SceneLoadConfig config in controller.Database.SceneConfigs)
            {
                if (config == null) { invalidCount++; issueCount++; continue; }
                SceneLoadValidationResult result = controller.Validate(config);
                if (!result.IsValid)
                {
                    invalidCount++;
                    issueCount += result.Issues.Count;
                }
            }
            SetStatus(invalidCount == 0
                ? $"全部 {controller.Database.SceneConfigs.Count} 份配置校验通过。"
                : $"有 {invalidCount} 份配置包含 {issueCount} 个错误。", invalidCount > 0);
            BindDetails(selectedItem);
        }

        /// <summary>响应面板外部 Undo/Redo 后重新读取序列化数据并重建树。</summary>
        private void OnUndoRedoPerformed() => EditorApplication.delayCall += RefreshAfterUndo;

        /// <summary>属性绑定检测到共享配置或任务变化后，在当前 UI 回调结束时重建树和详情。</summary>
        private void QueueSerializedRefresh(string propertyPath)
        {
            if (disposed) return;
            if (string.Equals(propertyPath, "rootTask", StringComparison.Ordinal) ||
                string.Equals(propertyPath, "sceneReference", StringComparison.Ordinal) ||
                propertyPath.StartsWith("sceneReference.", StringComparison.Ordinal))
                EditorApplication.delayCall += RenderTree;
            else
                EditorApplication.delayCall += RefreshBoundAssetDisplay;
        }

        /// <summary>刷新树行、执行预览和校验结果，不重建仍在编辑的 PropertyField。</summary>
        private void RefreshBoundAssetDisplay()
        {
            if (disposed) return;
            treeView.RefreshItems();
            if (selectedItem == null) return;
            SceneLoadConfig config = selectedItem.Config;
            if (config == null) return;
            detailTitle.text = selectedItem.IsConfiguration ? config.DisplayName : selectedItem.Task.name;
            detailSubtitle.text = selectedItem.IsConfiguration
                ? $"场景配置  ·  {config.SceneId}"
                : $"{selectedItem.Task.GetType().Name}  ·  {config.SceneId}";
            if (executionPreviewField != null)
                executionPreviewField.SetValueWithoutNotify(controller.GetExecutionPreview(config));
            RefreshValidationContents(config);
        }

        /// <summary>在 Unity Undo 状态恢复后刷新行引用和绑定字段。</summary>
        private void RefreshAfterUndo()
        {
            if (disposed) return;
            string previousAssetFolder = controller.AssetFolder;
            controller.SynchronizeAssetFolderFromEditorSettings();
            editorSettingsSerializedObject?.Update();
            RenderTree();
            bool assetFolderChanged = !string.Equals(previousAssetFolder, controller.AssetFolder,
                StringComparison.Ordinal);
            SetStatus(assetFolderChanged
                ? $"Undo/Redo 已同步场景树、详情和资产目录（{controller.AssetFolder}）。"
                : "Undo/Redo 已重新同步场景树和详情。", false);
        }

        /// <summary>在 Project 资产新增、删除或重命名后刷新树展示。</summary>
        private void OnProjectChanged() => QueueProjectTreeRefresh();

        /// <summary>合并一次资产操作产生的多个 Project 变化通知，避免重复重建树。</summary>
        private void QueueProjectTreeRefresh()
        {
            if (disposed || projectRefreshQueued) return;
            projectRefreshQueued = true;
            EditorApplication.delayCall += RefreshAfterProjectChange;
        }

        /// <summary>Project 变化后的延迟入口，同步场景名并重建树和详情。</summary>
        private void RefreshAfterProjectChange()
        {
            projectRefreshQueued = false;
            if (!disposed) RenderTree();
        }

        /// <summary>切换 Play Mode 时关闭或恢复结构操作，并同步树数据。</summary>
        /// <param name="state">Unity Play Mode 状态。</param>
        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            bool canEdit = !EditorApplication.isPlaying && state != PlayModeStateChange.ExitingEditMode &&
                           state != PlayModeStateChange.EnteredPlayMode;
            SetEditingEnabled(canEdit);
            ClearDragVisuals();
            RenderTree();
            SetStatus(canEdit ? "场景配置结构可编辑。" : "Play Mode 中场景配置结构为只读。", false);
        }

        /// <summary>视图宿主从 FrameSettingWindow 分离时释放 Editor 和 UI 事件。</summary>
        /// <param name="eventData">宿主分离事件。</param>
        private void OnPanelDetached(DetachFromPanelEvent eventData) => Dispose();

        /// <summary>将新建、删除、排序和资产引用编辑按钮统一切换为可编辑状态。</summary>
        /// <param name="enabled">是否允许结构编辑。</param>
        private void SetEditingEnabled(bool enabled)
        {
            createDatabaseButton?.SetEnabled(enabled);
            addConfigButton?.SetEnabled(enabled && controller.Database != null);
            createConfigButton?.SetEnabled(enabled && controller.Database != null);
            assetFolderFieldHost?.SetEnabled(enabled);
            validateAllButton?.SetEnabled(true);
            if (panelRoot != null) panelRoot.Q<HelpBox>("PlayModeHelpBox")?.SetEnabled(!enabled);
            if (detailContent != null) detailContent.SetEnabled(enabled);
        }

        /// <summary>根据状态结果更新面板底部文字和颜色提示。</summary>
        /// <param name="message">用户可读状态。</param>
        /// <param name="isError">是否使用错误提示。</param>
        private void SetStatus(string message, bool isError)
        {
            if (statusLabel == null) return;
            statusLabel.text = message;
            statusIndicator.EnableInClassList("scene-status-indicator--error", isError);
        }

        /// <summary>从树当前选择事件中取得独立引用行身份并绑定详情。</summary>
        /// <param name="selection">TreeView 当前选择数据。</param>
        private void OnTreeSelectionChanged(IEnumerable<object> selection)
        {
            SceneLoadTreeItem item = selection.OfType<SceneLoadTreeItem>().FirstOrDefault();
            SelectItem(item);
        }

        /// <summary>同步 TreeView 选择、当前引用路径和右侧绑定字段。</summary>
        /// <param name="item">要选中的行。</param>
        private void SelectItem(SceneLoadTreeItem item)
        {
            if (item == null) return;
            selectedItem = item;
            treeView.SetSelectionByIdWithoutNotify(new[] { item.Id });
            BindDetails(item);
        }

        /// <summary>处理校验、配置和任务操作后统一重建树与状态栏。</summary>
        /// <param name="message">操作成功说明。</param>
        /// <param name="preferredTask">若有变更后的任务引用，优先重新选中其资产。</param>
        /// <param name="preferredParentTask">优先引用位置的新父组合。</param>
        /// <param name="preferredConfig">优先引用位置所属的配置。</param>
        /// <param name="preferredSiblingIndex">优先引用位置的真实同级顺序。</param>
        private void RefreshAfterMutation(
            string message,
            SceneLoadTask preferredTask = null,
            SceneLoadTask preferredParentTask = null,
            SceneLoadConfig preferredConfig = null,
            int preferredSiblingIndex = -1)
        {
            string selectedPath = selectedItem?.ReferencePath;
            RenderTree();
            SceneLoadTreeItem refreshedItem = preferredTask == null
                ? null
                : FindTaskItem(preferredTask, preferredParentTask, preferredConfig, preferredSiblingIndex);
            if (refreshedItem == null && !string.IsNullOrEmpty(selectedPath))
            {
                refreshedItem = FindTreeItemByPath(selectedPath);
            }
            if (refreshedItem != null) SelectItem(refreshedItem);
            SetStatus(message, false);
        }

        /// <summary>按配置资产查找其当前根行。</summary>
        private SceneLoadTreeItem FindConfigItem(SceneLoadConfig config)
        {
            foreach (SceneLoadTreeItem item in renderedTreeItems)
            {
                if (item.IsConfiguration && ReferenceEquals(item.Config, config)) return item;
            }
            return null;
        }

        /// <summary>按资产、父组合以及可选配置和顺序查找某一引用位置。</summary>
        /// <param name="task">需要定位的任务资产。</param>
        /// <param name="parentTask">该引用位置的父组合；根任务使用空值。</param>
        /// <param name="config">可选的场景配置筛选。</param>
        /// <param name="siblingIndex">可选的真实同级顺序筛选。</param>
        /// <returns>匹配引用行；当前搜索结果中不存在时返回 null。</returns>
        private SceneLoadTreeItem FindTaskItem(
            SceneLoadTask task,
            SceneLoadTask parentTask,
            SceneLoadConfig config = null,
            int siblingIndex = -1)
        {
            foreach (SceneLoadTreeItem item in renderedTreeItems)
            {
                if (ReferenceEquals(item.Task, task) && ReferenceEquals(item.ParentTask, parentTask) &&
                    (config == null || ReferenceEquals(item.Config, config)) &&
                    (siblingIndex < 0 || item.SiblingIndex == siblingIndex)) return item;
            }
            return null;
        }

        /// <summary>需要 UXML 命名元素时尽早报告模板错误。</summary>
        /// <typeparam name="TElement">期望元素类型。</typeparam>
        /// <param name="root">元素搜索根节点。</param>
        /// <param name="elementName">UXML name。</param>
        /// <returns>匹配的必需元素。</returns>
        private static TElement Require<TElement>(VisualElement root, string elementName)
            where TElement : VisualElement => root.Q<TElement>(elementName) ??
            throw new InvalidOperationException($"[SceneSystemView] UXML 缺少必需控件 '{elementName}'。");

        #endregion
    }
}
