#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.UIToolkitExtensions.Editor;

namespace RPG.TaskSystemNS.Editor
{
    /// <summary>任务配置窗口的 UI Toolkit View，负责列表、筛选、右键菜单和序列化绑定。</summary>
    internal sealed class TaskConfigEditorView : IDisposable
    {
        #region 依赖字段

        // View 读取持久化后缀选项来构建分类菜单，所有设置写入仍通过事件交由 Controller。
        private readonly TaskConfigEditorSettings settings;
        private readonly VisualTreeAsset rowTemplate;
        private readonly VisualTreeAsset stageTemplate;
        private readonly ListView taskListView;
        // 任务列表的原生视口用于排除滚动条与视口外的虚拟化行。
        private readonly ScrollView taskListScrollView;
        private readonly ListView suffixListView;
        private readonly ObjectField databaseField;
        private readonly ObjectField taskIdSourceDatabaseField;
        private readonly DropdownField scopeField;
        private readonly DropdownField categoryFilterField;
        private readonly DropdownField sortField;
        private readonly DropdownField sortDirectionField;
        private readonly DropdownField suffixCategoryField;
        private readonly TextField suffixIdField;
        private readonly TextField suffixDisplayNameField;
        private readonly VisualElement definitionFolderHost;
        private readonly IMGUIContainer definitionFolderContainer;
        private readonly SerializedObject settingsSerializedObject;
        private readonly ToolbarSearchField searchField;
        private readonly ScrollView detailsContainer;
        private readonly Label emptyDetailsLabel;
        private readonly Label listStatusLabel;
        private readonly Label detailStatusLabel;
        private readonly Button createButton;
        private readonly Button duplicateButton;
        private readonly Button removeButton;
        private readonly Button deleteButton;
        private readonly Button validateButton;
        private readonly Button refreshButton;
        private readonly Button addSuffixButton;
        private readonly Button removeSuffixButton;

        #endregion

        #region 视图状态

        private List<TaskDefinitionEditorEntry> visibleEntries = new();
        private TaskDefinition selectedDefinition;
        private TaskIdSuffixSettings selectedSuffix;
        private bool disposed;

        #endregion

        #region 事件

        /// <summary>通知 Controller 用户更换了数据库。</summary>
        internal event Action<TaskDatabase> DatabaseChanged;
        /// <summary>通知 Controller 用户更换了 TaskId 引用下拉框候选数据库。</summary>
        internal event Action<TaskDatabase> TaskIdSourceDatabaseChanged;
        /// <summary>通知 Controller 搜索文本变化。</summary>
        internal event Action<string> SearchChanged;
        /// <summary>通知 Controller 资产范围变化。</summary>
        internal event Action<string> ScopeChanged;
        /// <summary>通知 Controller 任务分类筛选变化。</summary>
        internal event Action<string> CategoryFilterChanged;
        /// <summary>通知 Controller 排序规则变化。</summary>
        internal event Action<string, string> SortChanged;
        /// <summary>通知 Controller 用户选择了任务定义。</summary>
        internal event Action<TaskDefinition> DefinitionSelected;
        /// <summary>通知 Controller 请求创建任务并携带分类和后缀。</summary>
        internal event Action<string, string> CreateRequested;
        /// <summary>通知 Controller 请求执行任务上下文操作。</summary>
        internal event Action<TaskDefinition, string> TaskCommandRequested;
        /// <summary>通知 Controller 请求验证当前数据库。</summary>
        internal event Action ValidateRequested;
        /// <summary>通知 Controller 请求刷新资产列表。</summary>
        internal event Action RefreshRequested;
        /// <summary>通知 Controller 请求新增或更新后缀。</summary>
        internal event Action<string, string, string, string, bool> SuffixSaveRequested;
        /// <summary>通知 Controller 请求删除所选后缀。</summary>
        internal event Action<string, string> SuffixRemoveRequested;
        /// <summary>通知 Controller 请求更新任务资产目录。</summary>
        internal event Action<string> DefinitionFolderChanged;
        /// <summary>通知 Controller 任务序列化属性发生变化。</summary>
        internal event Action<TaskDefinition> PropertiesChanged;

        #endregion

        #region 生命周期

        /// <summary>查询 UXML 控件、配置分栏并连接 UI 输入事件。</summary>
        /// <param name="root">已实例化的窗口 UXML 根节点。</param>
        /// <param name="settings">TaskId 后缀、引用候选数据库与创建路径设置。</param>
        /// <param name="rowTemplate">任务列表项 UXML 模板。</param>
        /// <param name="stageTemplate">任务阶段卡片 UXML 模板。</param>
        internal TaskConfigEditorView(
            VisualElement root,
            TaskConfigEditorSettings settings,
            VisualTreeAsset rowTemplate,
            VisualTreeAsset stageTemplate)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.rowTemplate = rowTemplate ?? throw new ArgumentNullException(nameof(rowTemplate));
            this.stageTemplate = stageTemplate ?? throw new ArgumentNullException(nameof(stageTemplate));
            settingsSerializedObject = new SerializedObject(settings);
            string themeClass = EditorGUIUtility.isProSkin ? "task-editor-theme-dark" : "task-editor-theme-light";
            string otherThemeClass = EditorGUIUtility.isProSkin ? "task-editor-theme-light" : "task-editor-theme-dark";
            root.RemoveFromClassList(otherThemeClass);
            root.AddToClassList(themeClass);
            VisualElement editorRoot = Require<VisualElement>(root, "TaskEditorRoot");
            editorRoot.RemoveFromClassList(otherThemeClass);
            editorRoot.AddToClassList(themeClass);
            searchField = Require<ToolbarSearchField>(root, "SearchField");
            CustomTwoPanelSplitView mainSplit = Require<CustomTwoPanelSplitView>(root, "MainSplitView");
            CustomTwoPanelSplitView leftSplit = Require<CustomTwoPanelSplitView>(root, "LeftSplitView");
            mainSplit.ConfigureFixedPane(260f, 340f, 520f, "RPG.TaskConfig.MainSplit");
            leftSplit.ConfigureFixedPane(250f, 400f, 620f, "RPG.TaskConfig.LeftSplit");

            taskListView = Require<ListView>(root, "TaskList");
            taskListScrollView = taskListView.Q<ScrollView>() ?? throw new InvalidOperationException("任务列表缺少原生 ScrollView。");
            suffixListView = Require<ListView>(root, "SuffixList");
            databaseField = Require<ObjectField>(root, "DatabaseField");
            taskIdSourceDatabaseField = Require<ObjectField>(root, "TaskIdSourceDatabaseField");
            scopeField = Require<DropdownField>(root, "ScopeField");
            categoryFilterField = Require<DropdownField>(root, "CategoryFilterField");
            sortField = Require<DropdownField>(root, "SortField");
            sortDirectionField = Require<DropdownField>(root, "SortDirectionField");
            suffixCategoryField = Require<DropdownField>(root, "SuffixCategoryField");
            suffixIdField = Require<TextField>(root, "SuffixIdField");
            suffixDisplayNameField = Require<TextField>(root, "SuffixDisplayNameField");
            definitionFolderHost = Require<VisualElement>(root, "DefinitionFolderField");
            definitionFolderContainer = new IMGUIContainer(DrawDefinitionFolder);
            definitionFolderContainer.AddToClassList("task-editor-folder-field");
            definitionFolderHost.Add(definitionFolderContainer);
            detailsContainer = Require<ScrollView>(root, "DetailsContainer");
            emptyDetailsLabel = Require<Label>(root, "EmptyDetailsLabel");
            listStatusLabel = Require<Label>(root, "ListStatusLabel");
            detailStatusLabel = Require<Label>(root, "DetailStatusLabel");
            createButton = Require<Button>(root, "CreateTaskButton");
            duplicateButton = Require<Button>(root, "DuplicateButton");
            removeButton = Require<Button>(root, "RemoveFromDatabaseButton");
            deleteButton = Require<Button>(root, "DeleteTaskButton");
            validateButton = Require<Button>(root, "ValidateButton");
            refreshButton = Require<Button>(root, "RefreshButton");
            addSuffixButton = Require<Button>(root, "SaveSuffixButton");
            removeSuffixButton = Require<Button>(root, "RemoveSuffixButton");
            ConfigureList();
            ConfigureFields(root);
            ConfigureButtons();
            ConfigureDefinitionFolder();
            detailsContainer.RegisterCallback<SerializedPropertyChangeEvent>(OnSerializedPropertyChanged);
        }

        /// <summary>解除当前目标的 SerializedObject 绑定并释放 View 事件订阅。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            detailsContainer.Unbind();
            detailsContainer.UnregisterCallback<SerializedPropertyChangeEvent>(OnSerializedPropertyChanged);
            definitionFolderContainer.onGUIHandler = null;
            settingsSerializedObject.Dispose();
            taskListView.selectionChanged -= OnTaskSelectionChanged;
            taskListView.UnregisterCallback<MouseUpEvent>(OnTaskListMouseUp, TrickleDown.TrickleDown);
            suffixListView.selectionChanged -= OnSuffixSelectionChanged;
            taskIdSourceDatabaseField.UnregisterValueChangedCallback(OnTaskIdSourceDatabaseFieldChanged);
            Debug.Log("[TaskConfigEditorView] 已注销任务列表右键事件。");
            DatabaseChanged = null;
            TaskIdSourceDatabaseChanged = null;
            SearchChanged = null;
            ScopeChanged = null;
            CategoryFilterChanged = null;
            SortChanged = null;
            DefinitionSelected = null;
            CreateRequested = null;
            TaskCommandRequested = null;
            ValidateRequested = null;
            RefreshRequested = null;
            SuffixSaveRequested = null;
            SuffixRemoveRequested = null;
            DefinitionFolderChanged = null;
            PropertiesChanged = null;
        }

        #endregion

        #region 数据呈现

        /// <summary>更新数据库选择器和列表范围视图。</summary>
        /// <param name="database">当前数据库。</param>
        internal void SetDatabase(TaskDatabase database)
        {
            databaseField.SetValueWithoutNotify(database);
            UpdateCommandAvailability();
        }

        /// <summary>显示当前明确指定的 TaskId 候选数据库。</summary>
        /// <param name="database">当前候选数据库；空值表示尚未配置。</param>
        internal void SetTaskIdSourceDatabase(TaskDatabase database)
        {
            taskIdSourceDatabaseField.SetValueWithoutNotify(database);
            taskIdSourceDatabaseField.tooltip = database == null
                ? "未设置候选数据库；选择正式 TaskDatabase 后，前置任务 ID 下拉框才可用。"
                : $"TaskId 引用候选来自：{database.name}";
        }

        /// <summary>恢复筛选、排序及后缀面板设置。</summary>
        /// <param name="search">搜索文本。</param>
        /// <param name="scope">资产范围。</param>
        /// <param name="category">分类筛选显示名。</param>
        /// <param name="sort">排序字段。</param>
        /// <param name="direction">排序方向。</param>
        /// <param name="suffixCategory">后缀管理分类显示名。</param>
        internal void RestoreFilters(string search, string scope, string category, string sort, string direction, string suffixCategory)
        {
            suffixCategoryField.choices = TaskCategoryCatalog.Options.Select(option => option.DisplayName).ToList();
            categoryFilterField.choices = new[] { "全部分类" }.Concat(TaskCategoryCatalog.Options.Select(option => option.DisplayName)).ToList();
            scopeField.SetValueWithoutNotify(scopeField.choices.Contains(scope) ? scope : scopeField.choices[0]);
            categoryFilterField.SetValueWithoutNotify(category);
            sortField.SetValueWithoutNotify(sort);
            sortDirectionField.SetValueWithoutNotify(direction);
            suffixCategoryField.SetValueWithoutNotify(suffixCategory);
            if (!suffixCategoryField.choices.Contains(suffixCategory)) suffixCategoryField.SetValueWithoutNotify(suffixCategoryField.choices[0]);
            if (!categoryFilterField.choices.Contains(category)) categoryFilterField.SetValueWithoutNotify(categoryFilterField.choices[0]);
            searchField.SetValueWithoutNotify(search);
            RenderSuffixes(CurrentSuffixCategoryId());
        }

        /// <summary>渲染过滤排序后的任务列表并恢复当前选择。</summary>
        /// <param name="definitions">列表数据和编辑器安全读取的任务字段。</param>
        /// <param name="selected">当前任务选择。</param>
        internal void RenderDefinitions(List<TaskDefinitionEditorEntry> definitions, TaskDefinition selected)
        {
            visibleEntries = definitions;
            taskListView.itemsSource = visibleEntries;
            taskListView.Rebuild();
            int selectedIndex = visibleEntries.FindIndex(entry => entry.Definition == selected);
            if (selectedIndex >= 0) taskListView.SetSelection(selectedIndex);
            else taskListView.ClearSelection();
            selectedDefinition = selected;
            UpdateCommandAvailability();
        }

        /// <summary>把选中的任务资产绑定到详情字段。</summary>
        /// <param name="entry">待编辑任务及原始标识字段。</param>
        /// <param name="validationMessage">当前校验结果。</param>
        internal void BindDefinition(TaskDefinitionEditorEntry entry, string validationMessage)
        {
            detailsContainer.Unbind();
            detailsContainer.Clear();
            TaskDefinition definition = entry?.Definition;
            selectedDefinition = definition;
            bool hasDefinition = definition != null;
            emptyDetailsLabel.style.display = hasDefinition ? DisplayStyle.None : DisplayStyle.Flex;
            detailsContainer.style.display = hasDefinition ? DisplayStyle.Flex : DisplayStyle.None;
            if (!hasDefinition)
            {
                detailStatusLabel.text = "";
                UpdateCommandAvailability();
                return;
            }

            var serializedDefinition = new SerializedObject(definition);
            serializedDefinition.Update();
            VisualElement basicSection = CreateSection("基本信息", entry.HasValidTaskId ? entry.TaskId : entry.ConfigurationIssue);
            VisualElement basicContent = basicSection.Q<VisualElement>("SectionContent");
            detailsContainer.Add(basicSection);
            PropertyField taskIdField = AddProperty(serializedDefinition, basicContent, "taskId", "TaskId（创建后固定）");
            taskIdField.SetEnabled(false);
            AddProperty(serializedDefinition, basicContent, "categoryId", "任务分类");
            AddProperty(serializedDefinition, basicContent, "title", "任务标题");
            AddProperty(serializedDefinition, basicContent, "description", "任务说明");
            VisualElement conditionsSection = CreateSection("接取条件", "所有条件均需满足");
            detailsContainer.Add(conditionsSection);
            AddProperty(serializedDefinition, conditionsSection.Q<VisualElement>("SectionContent"), "unlockConditions", "接取条件");
            VisualElement stagesSection = CreateSection("任务阶段", "阶段顺序执行，阶段内目标同时生效");
            detailsContainer.Add(stagesSection);
            RenderStages(serializedDefinition, definition, stagesSection.Q<VisualElement>("SectionContent"));
            VisualElement rewardsSection = CreateSection("奖励", "由通用 RewardSystem 发放");
            detailsContainer.Add(rewardsSection);
            AddProperty(serializedDefinition, rewardsSection.Q<VisualElement>("SectionContent"), "rewards", "任务奖励");
            detailsContainer.Bind(serializedDefinition);
            detailStatusLabel.text = validationMessage ?? string.Empty;
            UpdateCommandAvailability();
        }

        /// <summary>在阶段编辑后使用当前资产原始字段重新绘制详情。</summary>
        /// <param name="definition">当前任务定义。</param>
        /// <param name="validationMessage">需要显示的校验结果。</param>
        private void BindDefinition(TaskDefinition definition, string validationMessage)
        {
            BindDefinition(definition == null ? null : new TaskDefinitionEditorEntry(definition, false), validationMessage);
        }

        /// <summary>更新筛选后任务数量和数据库状态。</summary>
        /// <param name="message">状态栏消息。</param>
        internal void SetListStatus(string message) => listStatusLabel.text = message ?? string.Empty;

        /// <summary>按新的设置内容刷新后缀列表。</summary>
        /// <param name="categoryId">待显示后缀的分类。</param>
        internal void RefreshSuffixes(string categoryId)
        {
            selectedSuffix = null;
            suffixIdField.SetValueWithoutNotify(string.Empty);
            suffixDisplayNameField.SetValueWithoutNotify(string.Empty);
            RenderSuffixes(categoryId);
        }

        /// <summary>设置校验消息。</summary>
        /// <param name="message">校验消息。</param>
        internal void SetDetailStatus(string message) => detailStatusLabel.text = message ?? string.Empty;

        /// <summary>控制播放模式下的资产编辑权限。</summary>
        /// <param name="isReadOnly">只读时禁用所有修改命令和字段。</param>
        internal void SetReadOnly(bool isReadOnly)
        {
            detailsContainer.SetEnabled(!isReadOnly);
            suffixIdField.SetEnabled(!isReadOnly);
            suffixDisplayNameField.SetEnabled(!isReadOnly);
            definitionFolderHost.SetEnabled(!isReadOnly);
            createButton.SetEnabled(!isReadOnly && databaseField.value != null);
            addSuffixButton.SetEnabled(!isReadOnly);
            removeSuffixButton.SetEnabled(!isReadOnly && selectedSuffix != null);
            UpdateCommandAvailability();
        }

        #endregion

        #region 列表与筛选

        /// <summary>为 ListView 配置行模板、选择回调及统一右键入口。</summary>
        private void ConfigureList()
        {
            taskListView.selectionType = SelectionType.Single;
            taskListView.fixedItemHeight = 56f;
            taskListView.makeItem = CreateTaskRow;
            taskListView.bindItem = BindTaskRow;
            taskListView.selectionChanged += OnTaskSelectionChanged;
            // 在子元素和 ListView 默认菜单处理前分发，确保每次右键只产生一个菜单。
            taskListView.RegisterCallback<MouseUpEvent>(OnTaskListMouseUp, TrickleDown.TrickleDown);
            Debug.Log("[TaskConfigEditorView] 已注册任务列表统一右键事件。");
            suffixListView.selectionType = SelectionType.Single;
            suffixListView.fixedItemHeight = 42f;
            suffixListView.makeItem = CreateSuffixRow;
            suffixListView.bindItem = BindSuffixRow;
            suffixListView.selectionChanged += OnSuffixSelectionChanged;
        }

        /// <summary>查询搜索输入框并配置所有筛选控件。</summary>
        /// <param name="root">UXML 根节点。</param>
        private void ConfigureFields(VisualElement root)
        {
            scopeField.choices = new List<string> { "当前数据库", "项目全部任务", "未加入当前数据库" };
            categoryFilterField.choices = new[] { "全部分类" }.Concat(TaskCategoryCatalog.Options.Select(option => option.DisplayName)).ToList();
            sortField.choices = new List<string> { "TaskId", "分类", "标题" };
            sortDirectionField.choices = new List<string> { "升序", "降序" };
            suffixCategoryField.choices = TaskCategoryCatalog.Options.Select(option => option.DisplayName).ToList();
            databaseField.objectType = typeof(TaskDatabase);
            databaseField.allowSceneObjects = false;
            databaseField.RegisterValueChangedCallback(evt => DatabaseChanged?.Invoke(evt.newValue as TaskDatabase));
            taskIdSourceDatabaseField.objectType = typeof(TaskDatabase);
            taskIdSourceDatabaseField.allowSceneObjects = false;
            taskIdSourceDatabaseField.RegisterValueChangedCallback(OnTaskIdSourceDatabaseFieldChanged);
            searchField.RegisterValueChangedCallback(evt => SearchChanged?.Invoke(evt.newValue));
            scopeField.RegisterValueChangedCallback(evt => ScopeChanged?.Invoke(evt.newValue));
            categoryFilterField.RegisterValueChangedCallback(evt => CategoryFilterChanged?.Invoke(evt.newValue));
            sortField.RegisterValueChangedCallback(evt => SortChanged?.Invoke(evt.newValue, sortDirectionField.value));
            sortDirectionField.RegisterValueChangedCallback(evt => SortChanged?.Invoke(sortField.value, evt.newValue));
            suffixCategoryField.RegisterValueChangedCallback(evt =>
            {
                selectedSuffix = null;
                suffixIdField.value = string.Empty;
                suffixDisplayNameField.value = string.Empty;
                RenderSuffixes(CategoryIdFromDisplayName(evt.newValue));
            });
        }

        /// <summary>将 TaskId 候选数据库选择转发给 Controller 持久化。</summary>
        /// <param name="changeEvent">ObjectField 的新旧引用。</param>
        private void OnTaskIdSourceDatabaseFieldChanged(ChangeEvent<UnityEngine.Object> changeEvent)
        {
            TaskIdSourceDatabaseChanged?.Invoke(changeEvent.newValue as TaskDatabase);
        }

        /// <summary>注册工具栏、列表和后缀配置操作。</summary>
        private void ConfigureButtons()
        {
            createButton.clicked += ShowCreateMenu;
            duplicateButton.clicked += () => TaskCommandRequested?.Invoke(selectedDefinition, "duplicate");
            removeButton.clicked += () => TaskCommandRequested?.Invoke(selectedDefinition, "remove");
            deleteButton.clicked += () => TaskCommandRequested?.Invoke(selectedDefinition, "delete");
            validateButton.clicked += () => ValidateRequested?.Invoke();
            refreshButton.clicked += () => RefreshRequested?.Invoke();
            addSuffixButton.clicked += SaveSuffix;
            removeSuffixButton.clicked += RemoveSelectedSuffix;
        }

        /// <summary>配置带 WSFolderPath Drawer 的项目目录字段并响应其序列化变更。</summary>
        private void ConfigureDefinitionFolder()
        {
            definitionFolderContainer.style.flexGrow = 1f;
            definitionFolderContainer.style.flexShrink = 0f;
            definitionFolderContainer.style.height = EditorGUIUtility.singleLineHeight + 6f;
        }

        /// <summary>通过 WSFolderPath 的 IMGUI Drawer 编辑目录，并由 Controller 持久化变更。</summary>
        private void DrawDefinitionFolder()
        {
            if (disposed) return;

            settingsSerializedObject.UpdateIfRequiredOrScript();
            SerializedProperty folderProperty = settingsSerializedObject.FindProperty("definitionFolder");
            EditorGUILayout.PropertyField(folderProperty, new GUIContent("新任务资产目录"));
            if (!settingsSerializedObject.ApplyModifiedProperties()) return;

            DefinitionFolderChanged?.Invoke(folderProperty.stringValue);
        }

        /// <summary>创建任务列表行；右键菜单由 ListView 统一分发。</summary>
        /// <returns>由 UXML 行模板实例化的列表行。</returns>
        private VisualElement CreateTaskRow()
        {
            TemplateContainer template = rowTemplate.CloneTree();
            VisualElement row = template.Q<VisualElement>("TaskRow");
            row.RemoveFromHierarchy();
            return row;
        }

        /// <summary>把任务基础信息与登记状态填入复用行。</summary>
        /// <param name="element">列表行。</param>
        /// <param name="index">可视数据索引。</param>
        private void BindTaskRow(VisualElement element, int index)
        {
            if (index < 0 || index >= visibleEntries.Count) return;
            TaskDefinitionEditorEntry entry = visibleEntries[index];
            element.userData = entry;
            Label title = element.Q<Label>("TaskTitle");
            title.text = string.IsNullOrWhiteSpace(entry.Definition.Title) ? "（无标题）" : entry.Definition.Title;
            title.tooltip = string.IsNullOrEmpty(entry.ConfigurationIssue)
                ? entry.AssetPath
                : $"{entry.ConfigurationIssue}\n{entry.AssetPath}";
            element.Q<Label>("TaskId").text = string.IsNullOrWhiteSpace(entry.TaskId) ? "（空 TaskId）" : entry.TaskId;
            element.Q<Label>("TaskCategory").text = TaskCategoryCatalog.TryGetDisplayName(entry.CategoryId, out string name)
                ? name
                : $"无效分类：{(string.IsNullOrWhiteSpace(entry.CategoryId) ? "（空）" : entry.CategoryId)}";
            element.Q<Label>("TaskDatabaseState").text = entry.IsRegistered ? "已登记" : "未登记";
            element.EnableInClassList("is-invalid", !string.IsNullOrEmpty(entry.ConfigurationIssue));
            element.EnableInClassList("is-selected", taskListView.selectedIndex == index);
        }

        /// <summary>根据用户选择通知 Controller 切换任务详情。</summary>
        /// <param name="selection">新选中对象集合。</param>
        private void OnTaskSelectionChanged(IEnumerable<object> selection)
        {
            taskListView.RefreshItems();
            TaskDefinition definition = selection.OfType<TaskDefinitionEditorEntry>().Select(entry => entry.Definition).FirstOrDefault();
            DefinitionSelected?.Invoke(definition);
        }

        /// <summary>在列表捕获阶段按点击位置互斥显示任务菜单或空白菜单。</summary>
        /// <param name="eventData">UI Toolkit 鼠标释放事件。</param>
        private void OnTaskListMouseUp(MouseUpEvent eventData)
        {
            if (eventData.button != 1 || !IsTaskListContentPosition(eventData.mousePosition)) return;

            TaskDefinitionEditorEntry entry = FindTaskEntryAtPosition(eventData.mousePosition, out Rect rowBounds);
            var menu = new GenericMenu();
            if (entry != null)
            {
                // 先捕获目标及锚点；选择回调可能刷新并复用虚拟行，菜单不能再读取旧 VisualElement。
                TaskDefinition definition = entry.Definition;
                bool isRegistered = entry.IsRegistered;
                int entryIndex = visibleEntries.IndexOf(entry);
                if (entryIndex >= 0 && taskListView.selectedIndex != entryIndex)
                    taskListView.SetSelection(entryIndex);
                PopulateTaskRowMenu(menu, definition, isRegistered, rowBounds);
            }
            else
            {
                PopulateBlankListMenu(menu);
            }

            // 在显示原生菜单前消费释放事件，防止默认行为或祖先菜单处理器再次响应。
            eventData.StopImmediatePropagation();
            eventData.PreventDefault();
            menu.ShowAsContext();
        }

        /// <summary>检查右键位置是否位于 ListView 的可见内容区域。</summary>
        /// <param name="position">UI 面板坐标。</param>
        /// <returns>位于列表内容且不在滚动条上时返回 true。</returns>
        private bool IsTaskListContentPosition(Vector2 position)
        {
            return taskListView.worldBound.Contains(position) &&
                   taskListScrollView.contentViewport.worldBound.Contains(position) &&
                   !ContainsVisiblePosition(taskListScrollView.verticalScroller, position) &&
                   !ContainsVisiblePosition(taskListScrollView.horizontalScroller, position);
        }

        /// <summary>检查节点在当前可见状态下是否包含给定面板坐标。</summary>
        /// <param name="element">需要检查的 UI 节点。</param>
        /// <param name="position">UI 面板坐标。</param>
        /// <returns>节点可见且坐标落在其边界内时返回 true。</returns>
        private static bool ContainsVisiblePosition(VisualElement element, Vector2 position)
        {
            return element.visible && element.resolvedStyle.display != DisplayStyle.None && element.worldBound.Contains(position);
        }

        /// <summary>命中当前显示的任务行，并把 Unity 原生行包装层纳入点击范围。</summary>
        /// <param name="position">已经通过可视内容区域校验的面板坐标。</param>
        /// <param name="rowBounds">命中行的原生包装边界，用作重命名弹窗锚点。</param>
        /// <returns>命中的任务条目；真正空白区域返回 null。</returns>
        private TaskDefinitionEditorEntry FindTaskEntryAtPosition(Vector2 position, out Rect rowBounds)
        {
            foreach (VisualElement row in taskListView.Query<VisualElement>(className: "task-editor-list-row").ToList())
            {
                if (row.userData is not TaskDefinitionEditorEntry entry || !visibleEntries.Contains(entry)) continue;

                VisualElement bounds = row;
                bool hidden = false;
                // 固定行模板可能窄于原生单元格；将 cell 包装边缘也归属该任务。
                for (VisualElement current = row; current != taskListScrollView.contentContainer && current != null; current = current.parent)
                {
                    if (!current.visible || current.resolvedStyle.display == DisplayStyle.None) hidden = true;
                    if (current.ClassListContains("unity-collection-view__item")) bounds = current;
                }

                if (!hidden && bounds.worldBound.Contains(position))
                {
                    rowBounds = bounds.worldBound;
                    return entry;
                }
            }

            rowBounds = default;
            return null;
        }

        /// <summary>向空白列表菜单添加创建、刷新和校验入口。</summary>
        /// <param name="menu">本次右键创建的唯一原生菜单。</param>
        private void PopulateBlankListMenu(GenericMenu menu)
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && databaseField.value != null)
                AddCreateMenuItems(menu);
            menu.AddItem(new GUIContent("刷新任务列表"), false, () => RefreshRequested?.Invoke());
            menu.AddItem(new GUIContent("验证当前数据库"), false, () => ValidateRequested?.Invoke());
        }

        /// <summary>向任务行菜单添加与数据库登记状态相符的命令。</summary>
        /// <param name="menu">本次右键创建的唯一原生菜单。</param>
        /// <param name="definition">右键时捕获的任务定义。</param>
        /// <param name="isRegistered">右键时任务是否登记在当前数据库。</param>
        /// <param name="rowBounds">用于锚定重命名弹窗的原生行边界。</param>
        private void PopulateTaskRowMenu(GenericMenu menu, TaskDefinition definition, bool isRegistered, Rect rowBounds)
        {
            menu.AddItem(new GUIContent("定位资产"), false, () => TaskCommandRequested?.Invoke(definition, "locate"));
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            menu.AddItem(new GUIContent("重命名…"), false, () => ShowRenamePopup(definition, rowBounds));
            if (databaseField.value == null)
            {
                menu.AddDisabledItem(new GUIContent("复制任务"));
                menu.AddDisabledItem(new GUIContent(isRegistered ? "从当前数据库移除" : "加入当前数据库"));
            }
            else
            {
                menu.AddItem(new GUIContent("复制任务"), false, () => TaskCommandRequested?.Invoke(definition, "duplicate"));
                string command = isRegistered ? "remove" : "add";
                menu.AddItem(new GUIContent(isRegistered ? "从当前数据库移除" : "加入当前数据库"), false,
                    () => TaskCommandRequested?.Invoke(definition, command));
            }

            menu.AddItem(new GUIContent("删除任务资产…"), false, () => TaskCommandRequested?.Invoke(definition, "delete"));
        }

        #endregion

        #region 阶段详情

        /// <summary>按配置顺序建立阶段卡片和阶段顺序控制。</summary>
        /// <param name="serializedDefinition">当前任务序列化对象。</param>
        /// <param name="definition">当前任务配置。</param>
        /// <param name="stageContainer">阶段卡片及排序操作的容器。</param>
        private void RenderStages(SerializedObject serializedDefinition, TaskDefinition definition, VisualElement stageContainer)
        {
            SerializedProperty stages = serializedDefinition.FindProperty("stages");
            for (int index = 0; index < stages.arraySize; index++)
            {
                int stageIndex = index;
                SerializedProperty stage = stages.GetArrayElementAtIndex(index);
                TemplateContainer card = stageTemplate.CloneTree();
                Foldout foldout = card.Q<Foldout>("StageFoldout");
                foldout.text = $"阶段 {index + 1}";
                foldout.value = true;
                VisualElement fields = card.Q<VisualElement>("StageFields");
                PropertyField stageId = new(stage.FindPropertyRelative("stageId"), "阶段 ID");
                stageId.SetEnabled(false);
                fields.Add(stageId);
                fields.Add(new PropertyField(stage.FindPropertyRelative("title"), "阶段标题"));
                fields.Add(new PropertyField(stage.FindPropertyRelative("description"), "阶段说明"));
                fields.Add(new PropertyField(stage.FindPropertyRelative("objectives"), "阶段目标"));
                card.Q<Button>("MoveStageUpButton").clicked += () => MoveStage(definition, stageIndex, -1);
                card.Q<Button>("MoveStageDownButton").clicked += () => MoveStage(definition, stageIndex, 1);
                card.Q<Button>("RemoveStageButton").clicked += () => RemoveStage(definition, stageIndex);
                card.Q<Button>("RemoveStageButton").AddToClassList("task-editor-danger-button");
                card.Q<Button>("MoveStageUpButton").SetEnabled(stageIndex > 0);
                card.Q<Button>("MoveStageDownButton").SetEnabled(stageIndex < stages.arraySize - 1);
                stageContainer.Add(card);
            }

            var addStageButton = new Button(() => AddStage(definition)) { text = "＋ 添加阶段" };
            addStageButton.AddToClassList("task-editor-add-stage");
            stageContainer.Add(addStageButton);
        }

        /// <summary>新增带唯一稳定 ID 的空阶段。</summary>
        /// <param name="definition">当前任务定义。</param>
        private void AddStage(TaskDefinition definition)
        {
            var serializedDefinition = new SerializedObject(definition);
            SerializedProperty stages = serializedDefinition.FindProperty("stages");
            string stageId = FindUniqueNestedId(stages, "stageId", "stage_");
            stages.InsertArrayElementAtIndex(stages.arraySize);
            SerializedProperty stage = stages.GetArrayElementAtIndex(stages.arraySize - 1);
            stage.FindPropertyRelative("stageId").stringValue = stageId;
            stage.FindPropertyRelative("title").stringValue = $"阶段 {stages.arraySize}";
            stage.FindPropertyRelative("description").stringValue = string.Empty;
            stage.FindPropertyRelative("objectives").ClearArray();
            serializedDefinition.ApplyModifiedProperties();
            BindDefinition(definition, "请为阶段配置至少一个目标。");
            PropertiesChanged?.Invoke(definition);
        }

        /// <summary>改变阶段列表顺序，稳定阶段 ID 随配置项保留。</summary>
        /// <param name="definition">当前任务定义。</param>
        /// <param name="index">阶段索引。</param>
        /// <param name="offset">移动方向。</param>
        private void MoveStage(TaskDefinition definition, int index, int offset)
        {
            var serializedDefinition = new SerializedObject(definition);
            SerializedProperty stages = serializedDefinition.FindProperty("stages");
            int destination = index + offset;
            if (destination < 0 || destination >= stages.arraySize) return;
            stages.MoveArrayElement(index, destination);
            serializedDefinition.ApplyModifiedProperties();
            BindDefinition(definition, string.Empty);
            PropertiesChanged?.Invoke(definition);
        }

        /// <summary>删除指定阶段并保留其他阶段和目标数据。</summary>
        /// <param name="definition">当前任务定义。</param>
        /// <param name="index">待删除阶段索引。</param>
        private void RemoveStage(TaskDefinition definition, int index)
        {
            if (!EditorUtility.DisplayDialog("删除任务阶段", "删除此阶段及其目标配置？", "删除", "取消")) return;
            var serializedDefinition = new SerializedObject(definition);
            SerializedProperty stages = serializedDefinition.FindProperty("stages");
            if (index < 0 || index >= stages.arraySize) return;
            stages.DeleteArrayElementAtIndex(index);
            serializedDefinition.ApplyModifiedProperties();
            BindDefinition(definition, "任务至少需要一个阶段。");
            PropertiesChanged?.Invoke(definition);
        }

        /// <summary>从嵌套 ID 字段生成当前集合内唯一的新标识。</summary>
        /// <param name="items">序列化列表。</param>
        /// <param name="fieldName">ID 相对字段名。</param>
        /// <param name="prefix">ID 前缀。</param>
        /// <returns>从一开始递增的唯一标识。</returns>
        private static string FindUniqueNestedId(SerializedProperty items, string fieldName, string prefix)
        {
            int candidate = items.arraySize + 1;
            while (true)
            {
                string id = $"{prefix}{candidate:000}";
                bool exists = false;
                for (int index = 0; index < items.arraySize; index++)
                    if (items.GetArrayElementAtIndex(index).FindPropertyRelative(fieldName).stringValue == id) exists = true;
                if (!exists) return id;
                candidate++;
            }
        }

        /// <summary>创建并绑定任务详情普通序列化字段。</summary>
        /// <param name="serializedDefinition">当前任务序列化对象。</param>
        /// <param name="parent">详情容器。</param>
        /// <param name="propertyName">私有序列化字段路径。</param>
        /// <param name="label">Inspector 显示名称。</param>
        /// <returns>新建字段，用于设置只读状态。</returns>
        private static PropertyField AddProperty(SerializedObject serializedDefinition, VisualElement parent, string propertyName, string label)
        {
            PropertyField field = new(serializedDefinition.FindProperty(propertyName), label);
            field.AddToClassList("task-editor-property");
            parent.Add(field);
            return field;
        }

        /// <summary>创建同时包含标题、说明与序列化字段容器的详情区块。</summary>
        /// <param name="title">区块标题。</param>
        /// <param name="description">标题下方的简短说明。</param>
        /// <returns>准备接收字段内容的区块根节点。</returns>
        private static VisualElement CreateSection(string title, string description)
        {
            var section = new VisualElement();
            section.AddToClassList("task-editor-detail-section");
            var header = new VisualElement();
            header.AddToClassList("task-editor-section-header");
            header.Add(new Label(title));
            header.Add(new Label(description) { name = "SectionDescription" });
            section.Add(header);
            var content = new VisualElement { name = "SectionContent" };
            content.AddToClassList("task-editor-section-content");
            section.Add(content);
            return section;
        }

        /// <summary>接收 UI Toolkit 序列化字段变化并刷新任务列表与校验信息。</summary>
        /// <param name="evt">由 PropertyField 发出的字段变化事件。</param>
        private void OnSerializedPropertyChanged(SerializedPropertyChangeEvent evt)
        {
            if (selectedDefinition != null) PropertiesChanged?.Invoke(selectedDefinition);
        }

        #endregion

        #region 后缀编辑

        /// <summary>按当前分类刷新持久化后缀列表。</summary>
        /// <param name="categoryId">分类稳定标识。</param>
        private void RenderSuffixes(string categoryId)
        {
            suffixListView.itemsSource = settings.GetSuffixes(categoryId).ToList();
            suffixListView.Rebuild();
            removeSuffixButton.SetEnabled(!EditorApplication.isPlayingOrWillChangePlaymode && selectedSuffix != null);
        }

        /// <summary>创建后缀配置行。</summary>
        /// <returns>后缀列表行。</returns>
        private VisualElement CreateSuffixRow()
        {
            var row = new VisualElement();
            row.AddToClassList("task-editor-suffix-row");
            row.Add(new Label { name = "SuffixId" });
            row.Add(new Label { name = "SuffixDescription" });
            return row;
        }

        /// <summary>显示后缀稳定文本与说明。</summary>
        /// <param name="element">复用的后缀行。</param>
        /// <param name="index">后缀索引。</param>
        private void BindSuffixRow(VisualElement element, int index)
        {
            if (suffixListView.itemsSource is not List<TaskIdSuffixSettings> suffixes || index < 0 || index >= suffixes.Count) return;
            TaskIdSuffixSettings suffix = suffixes[index];
            element.Q<Label>("SuffixId").text = $"_{suffix.SuffixId}";
            element.Q<Label>("SuffixDescription").text = suffix.DisplayName;
            element.EnableInClassList("is-selected", ReferenceEquals(suffix, selectedSuffix));
            element.tooltip = $"_{suffix.SuffixId} · {suffix.DisplayName}";
        }

        /// <summary>把所选后缀载入编辑框。</summary>
        /// <param name="selection">所选后缀。</param>
        private void OnSuffixSelectionChanged(IEnumerable<object> selection)
        {
            selectedSuffix = selection.OfType<TaskIdSuffixSettings>().FirstOrDefault();
            suffixIdField.SetValueWithoutNotify(selectedSuffix?.SuffixId ?? string.Empty);
            suffixDisplayNameField.SetValueWithoutNotify(selectedSuffix?.DisplayName ?? string.Empty);
            removeSuffixButton.SetEnabled(!EditorApplication.isPlayingOrWillChangePlaymode && selectedSuffix != null);
            suffixListView.RefreshItems();
        }

        /// <summary>根据当前表单发送新增或更新后缀命令。</summary>
        private void SaveSuffix()
        {
            string categoryId = CurrentSuffixCategoryId();
            string id = suffixIdField.value;
            string displayName = suffixDisplayNameField.value;
            SuffixSaveRequested?.Invoke(categoryId, selectedSuffix?.SuffixId ?? string.Empty, id, displayName, selectedSuffix != null);
            selectedSuffix = null;
        }

        /// <summary>请求删除当前分类的所选后缀。</summary>
        private void RemoveSelectedSuffix()
        {
            if (selectedSuffix == null) return;
            if (!EditorUtility.DisplayDialog("删除 TaskId 后缀", $"删除后缀“{selectedSuffix.SuffixId}”？已有任务 ID 不会改变。", "删除", "取消")) return;
            SuffixRemoveRequested?.Invoke(CurrentSuffixCategoryId(), selectedSuffix.SuffixId);
            selectedSuffix = null;
        }

        #endregion

        #region 菜单与输入

        /// <summary>显示按分类分组的后缀新建菜单。</summary>
        private void ShowCreateMenu()
        {
            var menu = new GenericMenu();
            foreach (TaskCategoryOption category in TaskCategoryCatalog.Options)
            {
                string root = category.DisplayName;
                menu.AddItem(new GUIContent($"{root}/无后缀"), false,
                    () => CreateRequested?.Invoke(category.Id.Value, string.Empty));
                IReadOnlyList<TaskIdSuffixSettings> suffixes = settings.GetSuffixes(category.Id.Value);
                for (int index = 0; index < suffixes.Count; index++)
                {
                    TaskIdSuffixSettings suffix = suffixes[index];
                    menu.AddItem(new GUIContent($"{root}/{suffix.DisplayName} (_{suffix.SuffixId})"), false,
                        () => CreateRequested?.Invoke(category.Id.Value, suffix.SuffixId));
                }
            }

            menu.DropDown(GUIUtility.GUIToScreenRect(createButton.worldBound));
        }

        /// <summary>向空白处 GenericMenu 添加“分类/后缀”层级创建选项。</summary>
        /// <param name="menu">目标原生菜单。</param>
        private void AddCreateMenuItems(GenericMenu menu)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            foreach (TaskCategoryOption category in TaskCategoryCatalog.Options)
            {
                TaskIdSuffixSettings[] suffixes = settings.GetSuffixes(category.Id.Value).ToArray();
                string root = $"新建任务/{category.DisplayName}";
                menu.AddItem(new GUIContent($"{root}/无后缀"), false,
                    () => CreateRequested?.Invoke(category.Id.Value, string.Empty));
                for (int index = 0; index < suffixes.Length; index++)
                {
                    TaskIdSuffixSettings suffix = suffixes[index];
                    menu.AddItem(new GUIContent($"{root}/{suffix.DisplayName} (_{suffix.SuffixId})"), false,
                        () => CreateRequested?.Invoke(category.Id.Value, suffix.SuffixId));
                }
            }
        }

        /// <summary>显示可直接编辑标题并同步资产名的轻量弹出窗口。</summary>
        /// <param name="definition">待重命名任务。</param>
        /// <param name="anchor">弹窗锚定的任务列表行区域。</param>
        private void ShowRenamePopup(TaskDefinition definition, Rect anchor)
        {
            var popup = new TaskRenamePopup(definition.Title, title => TaskCommandRequested?.Invoke(definition, $"rename:{title}"));
            UnityEditor.PopupWindow.Show(GUIUtility.GUIToScreenRect(anchor), popup);
        }

        /// <summary>将分类显示名解析为静态表中的稳定 ID。</summary>
        /// <param name="displayName">分类中文名。</param>
        /// <returns>稳定分类 ID。</returns>
        private static string CategoryIdFromDisplayName(string displayName)
        {
            foreach (TaskCategoryOption option in TaskCategoryCatalog.Options)
                if (option.DisplayName == displayName) return option.Id.Value;
            return TaskCategoryCatalog.MainIdValue;
        }

        /// <summary>获取后缀面板当前分类 ID。</summary>
        /// <returns>分类稳定 ID。</returns>
        private string CurrentSuffixCategoryId() => CategoryIdFromDisplayName(suffixCategoryField.value);

        /// <summary>根据选中资产与数据库状态更新工具栏命令可用性。</summary>
        private void UpdateCommandAvailability()
        {
            bool canEdit = !EditorApplication.isPlayingOrWillChangePlaymode;
            bool hasDatabase = databaseField != null && databaseField.value != null;
            duplicateButton?.SetEnabled(canEdit && hasDatabase && selectedDefinition != null);
            removeButton?.SetEnabled(canEdit && hasDatabase && selectedDefinition != null);
            deleteButton?.SetEnabled(canEdit && selectedDefinition != null);
            validateButton?.SetEnabled(hasDatabase);
            refreshButton?.SetEnabled(true);
            createButton?.SetEnabled(canEdit && hasDatabase);
        }

        /// <summary>从 UI 树中按名称获取必须存在的控件。</summary>
        /// <typeparam name="TElement">目标控件类型。</typeparam>
        /// <param name="root">UXML 根节点。</param>
        /// <param name="name">控件名称。</param>
        /// <returns>找到的控件。</returns>
        /// <exception cref="InvalidOperationException">UXML 缺少指定控件时抛出。</exception>
        private static TElement Require<TElement>(VisualElement root, string name) where TElement : VisualElement
        {
            return root.Q<TElement>(name) ?? throw new InvalidOperationException($"TaskConfigEditorWindow UXML 缺少控件：{name}。");
        }

        #endregion

        #region 嵌套类型

        /// <summary>提供同步修改任务标题和资产名的弹出输入框。</summary>
        private sealed class TaskRenamePopup : PopupWindowContent
        {
            private readonly string currentTitle;
            private readonly Action<string> submit;
            private string editedTitle;

            /// <summary>创建标题重命名弹窗。</summary>
            /// <param name="currentTitle">当前标题。</param>
            /// <param name="submit">用户确认后的提交回调。</param>
            internal TaskRenamePopup(string currentTitle, Action<string> submit)
            {
                this.currentTitle = currentTitle;
                this.submit = submit;
                editedTitle = currentTitle;
            }

            /// <summary>获取弹出窗口的固定输入布局尺寸。</summary>
            /// <returns>弹窗尺寸。</returns>
            public override Vector2 GetWindowSize() => new(320f, 88f);

            /// <summary>绘制输入框并在确认后提交新标题。</summary>
            /// <param name="rect">弹窗绘制区域。</param>
            public override void OnGUI(Rect rect)
            {
                GUILayout.BeginArea(rect);
                editedTitle = EditorGUILayout.TextField("任务标题", editedTitle ?? currentTitle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("应用重命名"))
                {
                    submit?.Invoke(editedTitle);
                    editorWindow.Close();
                }

                GUILayout.EndArea();
            }
        }

        #endregion
    }
}
#endif
