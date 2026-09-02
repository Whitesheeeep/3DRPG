#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.UIModule.Editor;

namespace RPG.ItemSystem.Editor
{
    /// <summary>物品定义左侧列表及筛选区域的独立 View。</summary>
    internal sealed class ItemDefinitionListView : IDisposable
    {
        #region 字段

        private const string DefinitionListRowUxmlPath = UxmlUssPathConstants.Uxml.AssetsScriptsItemSystemEditorStyleItemDefinitionListRow;

        // 依赖左栏视觉树及原生滚动视口，所有命中坐标均使用面板坐标。
        private readonly VisualElement root;
        private readonly ListView listView;
        private readonly ScrollView listScrollView;
        private readonly ToolbarSearchField searchField;
        private readonly DropdownField categoryField;
        private readonly DropdownField kindField;
        private readonly DropdownField sortField;
        private readonly DropdownField sortDirectionField;
        private readonly Label emptyListLabel;
        private readonly VisualTreeAsset rowTemplate;
        private readonly List<ItemDefinition> displayedDefinitions = new();
        private ItemDefinition selectedDefinition;
        private bool suppressSelectionChanged;
        private bool disposed;

        #endregion

        #region 事件

        /// <summary>搜索文本变化事件。</summary>
        internal event Action<string> SearchChanged;
        /// <summary>分类筛选变化事件。</summary>
        internal event Action<string> CategoryChanged;
        /// <summary>定义类型筛选变化事件。</summary>
        internal event Action<string> KindChanged;
        /// <summary>排序字段变化事件。</summary>
        internal event Action<string> SortFieldChanged;
        /// <summary>排序方向变化事件。</summary>
        internal event Action<string> SortDirectionChanged;
        /// <summary>列表选择变化事件。</summary>
        internal event Action<ItemDefinition> DefinitionSelected;
        /// <summary>从列表空白区域请求新建可堆叠物品。</summary>
        internal event Action NewStackableRequested;
        /// <summary>从列表空白区域请求新建武器。</summary>
        internal event Action NewWeaponRequested;
        /// <summary>从列表空白区域请求新建养成道具。</summary>
        internal event Action NewDevelopmentItemRequested;
        /// <summary>从列表空白区域请求新建圣遗物。</summary>
        internal event Action NewArtifactRequested;
        /// <summary>列表双击或失焦重命名提交事件。</summary>
        internal event Action<ItemDefinition, string> RenameSubmitted;
        /// <summary>右键定义操作事件。</summary>
        internal event Action<ItemDefinition, ItemDefinitionCommand> DefinitionCommandRequested;

        #endregion

        #region 生命周期

        /// <summary>查询左栏控件、加载列表行模板并注册回调。</summary>
        /// <param name="root">左栏根节点。</param>
        /// <param name="listView">虚拟化定义列表。</param>
        internal ItemDefinitionListView(VisualElement root, ListView listView)
        {
            this.root = root ?? throw new ArgumentNullException(nameof(root));
            this.listView = listView ?? throw new ArgumentNullException(nameof(listView));
            listScrollView = listView.Q<ScrollView>() ?? throw new InvalidOperationException("物品列表缺少原生 ScrollView。");
            searchField = Require<ToolbarSearchField>("SearchField");
            categoryField = Require<DropdownField>("CategoryField");
            kindField = Require<DropdownField>("KindField");
            sortField = Require<DropdownField>("SortField");
            sortDirectionField = Require<DropdownField>("SortDirection");
            emptyListLabel = Require<Label>("EmptyListLabel");
            rowTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(DefinitionListRowUxmlPath);
            if (rowTemplate == null) throw new InvalidOperationException($"物品配置窗口缺少列表行 UXML：{DefinitionListRowUxmlPath}。");

            ConfigureChoices();
            listView.selectionType = SelectionType.Single;
            listView.fixedItemHeight = 52f;
            listView.itemsSource = displayedDefinitions;
            listView.makeItem = CreateDefinitionRow;
            listView.bindItem = BindDefinitionRow;
            searchField.RegisterValueChangedCallback(OnSearchChanged);
            categoryField.RegisterValueChangedCallback(OnCategoryChanged);
            kindField.RegisterValueChangedCallback(OnKindChanged);
            sortField.RegisterValueChangedCallback(OnSortFieldChanged);
            sortDirectionField.RegisterValueChangedCallback(OnSortDirectionChanged);
            listView.selectionChanged += OnDefinitionSelectionChanged;
            // 在子控件处理右键前统一分发；不再同时安装行菜单与背景菜单。
            listView.RegisterCallback<MouseUpEvent>(OnListViewMouseUp, TrickleDown.TrickleDown);
        }

        /// <summary>解除左栏控件回调并清空虚拟化数据源。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            searchField.UnregisterValueChangedCallback(OnSearchChanged);
            categoryField.UnregisterValueChangedCallback(OnCategoryChanged);
            kindField.UnregisterValueChangedCallback(OnKindChanged);
            sortField.UnregisterValueChangedCallback(OnSortFieldChanged);
            sortDirectionField.UnregisterValueChangedCallback(OnSortDirectionChanged);
            listView.selectionChanged -= OnDefinitionSelectionChanged;
            listView.UnregisterCallback<MouseUpEvent>(OnListViewMouseUp, TrickleDown.TrickleDown);
            listView.itemsSource = null;
            listView.makeItem = null;
            listView.bindItem = null;
            displayedDefinitions.Clear();
        }

        #endregion

        #region 状态呈现

        /// <summary>设置搜索文本而不触发筛选请求。</summary>
        /// <param name="value">搜索文本。</param>
        internal void SetSearch(string value) => searchField.SetValueWithoutNotify(value ?? string.Empty);

        /// <summary>设置筛选控件显示值而不触发筛选请求。</summary>
        /// <param name="category">分类筛选。</param>
        /// <param name="kind">定义类型筛选。</param>
        internal void SetFilters(string category, string kind)
        {
            categoryField.SetValueWithoutNotify(category ?? "全部类型");
            kindField.SetValueWithoutNotify(kind ?? "全部定义");
        }

        /// <summary>设置排序控件显示值而不触发排序请求。</summary>
        /// <param name="field">排序字段。</param>
        /// <param name="direction">排序方向。</param>
        internal void SetSorting(string field, string direction)
        {
            sortField.SetValueWithoutNotify(field ?? "默认排序优先级");
            sortDirectionField.SetValueWithoutNotify(direction ?? "降序");
        }

        /// <summary>更新筛选后的数据源并恢复当前选择。</summary>
        /// <param name="definitions">筛选后的定义列表。</param>
        /// <param name="selected">应保持选中的定义。</param>
        /// <param name="locateSelection">是否将选中项滚动到可视区域。</param>
        internal void RenderDefinitions(IReadOnlyList<ItemDefinition> definitions, ItemDefinition selected, bool locateSelection = true)
        {
            displayedDefinitions.Clear();
            if (definitions != null)
                for (int index = 0; index < definitions.Count; index++) displayedDefinitions.Add(definitions[index]);
            selectedDefinition = selected;
            emptyListLabel.style.display = displayedDefinitions.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            suppressSelectionChanged = true;
            listView.RefreshItems();
            int selectedIndex = selected == null ? -1 : displayedDefinitions.IndexOf(selected);
            listView.selectedIndex = selectedIndex;
            if (locateSelection && selectedIndex >= 0) listView.ScrollToItemById(selectedIndex);
            suppressSelectionChanged = false;
        }

        /// <summary>轻量刷新一个已显示定义的列表行。</summary>
        /// <param name="definition">发生变化的定义。</param>
        /// <param name="refreshList">是否刷新虚拟化行。</param>
        internal void RefreshDefinition(ItemDefinition definition, bool refreshList)
        {
            if (definition == null) return;
            if (selectedDefinition == definition) selectedDefinition = definition;
            if (refreshList) listView.RefreshItems();
        }

        #endregion

        #region 列表行与重命名

        /// <summary>创建可复用列表行并安装一次性输入回调。</summary>
        /// <returns>保留 UXML 样式作用域的模板宿主节点。</returns>
        private VisualElement CreateDefinitionRow()
        {
            var host = new VisualElement { name = "DefinitionRowHost" };
            host.AddToClassList("item-editor-definition-row-host");
            rowTemplate.CloneTree(host);
            // 列表行 UXML 自带 Style 节点，样式会挂在 CloneTree 的宿主上；不能只返回子节点，
            // 否则宿主被丢弃后行会退化为默认纵向布局，导致图标、名称和摘要互相覆盖。
            VisualElement row = host.Q<VisualElement>("DefinitionRow");
            if (row == null) throw new InvalidOperationException("物品列表行 UXML 缺少 DefinitionRow 根节点。");
            Label name = row.Q<Label>("Name");
            TextField inlineName = row.Q<TextField>("InlineName");
            if (name == null || inlineName == null) throw new InvalidOperationException("物品列表行 UXML 缺少名称控件。");
            name.RegisterCallback<MouseDownEvent>(OnNameMouseDown);
            inlineName.RegisterCallback<KeyDownEvent>(OnInlineNameKeyDown);
            inlineName.RegisterCallback<FocusOutEvent>(OnInlineNameFocusOut);
            return host;
        }

        /// <summary>绑定虚拟化行并完整清理上一条数据的视觉状态。</summary>
        /// <param name="element">列表行。</param>
        /// <param name="index">数据索引。</param>
        private void BindDefinitionRow(VisualElement element, int index)
        {
            ItemDefinition definition = index >= 0 && index < displayedDefinitions.Count ? displayedDefinitions[index] : null;
            element.userData = definition;
            VisualElement row = element.Q<VisualElement>("DefinitionRow") ?? element;
            row.userData = definition;
            Image iconImage = row.Q<Image>("IconImage");
            Label iconFallback = row.Q<Label>("IconFallback");
            Label name = row.Q<Label>("Name");
            TextField inlineName = row.Q<TextField>("InlineName");
            Label id = row.Q<Label>("Id");
            Label kind = row.Q<Label>("Kind");
            Label meta = row.Q<Label>("Meta");
            Label rarity = row.Q<Label>("Rarity");
            if (iconImage == null || iconFallback == null || name == null || inlineName == null || id == null || kind == null || meta == null || rarity == null) return;

            InlineRenameState state = inlineName.userData as InlineRenameState ?? new InlineRenameState();
            state.Definition = definition;
            state.NameLabel = name;
            state.OriginalValue = definition?.DisplayName ?? string.Empty;
            state.Completed = true;
            inlineName.userData = state;

            Sprite previewIcon = definition?.EditorPreviewIcon;
            iconImage.scaleMode = ScaleMode.ScaleToFit;
            iconImage.sprite = previewIcon;
            iconImage.style.display = previewIcon == null ? DisplayStyle.None : DisplayStyle.Flex;
            iconFallback.style.display = previewIcon == null ? DisplayStyle.Flex : DisplayStyle.None;
            iconFallback.text = definition switch
            {
                WeaponDefinition => "⚔",
                ArtifactDefinition => "◇",
                DevelopmentItemDefinition => "✚",
                _ => "✦"
            };
            name.text = definition?.DisplayName ?? "空定义";
            name.style.display = DisplayStyle.Flex;
            inlineName.SetValueWithoutNotify(definition?.DisplayName ?? string.Empty);
            inlineName.style.display = DisplayStyle.None;
            id.text = definition == null ? string.Empty : definition.ItemId.ToString();
            kind.text = ItemConfigEditorPresentation.GetDefinitionKindText(definition);
            meta.text = definition switch
            {
                WeaponDefinition weapon => $"武器 · 等级上限 {weapon.MaxLevel}",
                ArtifactDefinition artifact => $"圣遗物 · {ItemConfigEditorPresentation.GetArtifactSlotText(artifact.Slot)} · 等级上限 {artifact.MaxLevel}",
                DevelopmentItemDefinition development => $"养成道具 · {ItemConfigEditorPresentation.GetDevelopmentTypeText(development.DevelopmentType)} · 最大堆叠 {development.MaxQuantity}",
                StackableItemDefinition stackable => $"{ItemConfigEditorPresentation.GetCategoryText(definition.Category)} · 最大堆叠 {stackable.MaxQuantity}",
                _ => "未知类型"
            };
            rarity.text = definition == null ? string.Empty : ItemConfigEditorPresentation.GetRarityStars(definition.Rarity);
            // 视觉状态必须写到 UXML 的真正行节点；宿主只负责保留 Style 和承载 ListView 复用生命周期。
            ItemConfigEditorPresentation.EnableRarityClass(row, "item-editor-definition-row", definition?.Rarity);
            row.EnableInClassList("item-editor-definition-row--weapon", definition is WeaponDefinition);
            row.EnableInClassList("item-editor-definition-row--selected", definition != null && selectedDefinition == definition);
        }

        /// <summary>开始列表行内重命名。</summary>
        /// <param name="row">列表行。</param>
        /// <param name="definition">当前定义。</param>
        private void BeginInlineRename(VisualElement row, ItemDefinition definition)
        {
            if (row == null || definition == null) return;
            Label nameLabel = row.Q<Label>("Name");
            TextField editor = row.Q<TextField>("InlineName");
            if (nameLabel == null || editor == null || editor.userData is not InlineRenameState state) return;
            state.Definition = definition;
            state.NameLabel = nameLabel;
            state.OriginalValue = definition.DisplayName ?? string.Empty;
            state.Completed = false;
            nameLabel.style.display = DisplayStyle.None;
            editor.style.display = DisplayStyle.Flex;
            editor.SetValueWithoutNotify(state.OriginalValue);
            editor.Focus();
            editor.SelectAll();
        }

        /// <summary>处理名称 Label 双击。</summary>
        /// <param name="eventData">鼠标事件。</param>
        private void OnNameMouseDown(MouseDownEvent eventData)
        {
            if (eventData.button != 0 || eventData.clickCount != 2 || eventData.currentTarget is not Label nameLabel) return;
            VisualElement row = nameLabel;
            while (row != null && row.userData is not ItemDefinition) row = row.parent;
            if (row?.userData is ItemDefinition definition) BeginInlineRename(row, definition);
            eventData.StopPropagation();
        }

        /// <summary>处理列表行内编辑器键盘提交或取消。</summary>
        /// <param name="eventData">键盘事件。</param>
        private void OnInlineNameKeyDown(KeyDownEvent eventData)
        {
            if (eventData.currentTarget is not TextField editor) return;
            if (eventData.keyCode == KeyCode.Return || eventData.keyCode == KeyCode.KeypadEnter)
            {
                CommitInlineName(editor, true);
                eventData.StopPropagation();
            }
            else if (eventData.keyCode == KeyCode.Escape)
            {
                CommitInlineName(editor, false);
                eventData.StopPropagation();
            }
        }

        /// <summary>处理列表行内编辑器失焦提交。</summary>
        /// <param name="eventData">失焦事件。</param>
        private void OnInlineNameFocusOut(FocusOutEvent eventData)
        {
            if (eventData.currentTarget is TextField editor) CommitInlineName(editor, true);
        }

        /// <summary>提交或取消列表行重命名，并先恢复稳定行结构。</summary>
        /// <param name="editor">行内编辑器。</param>
        /// <param name="apply">是否提交输入。</param>
        private void CommitInlineName(TextField editor, bool apply)
        {
            if (editor.userData is not InlineRenameState state || state.Completed) return;
            state.Completed = true;
            string value = editor.value?.Trim() ?? string.Empty;
            bool invalidEmptyValue = apply && string.IsNullOrWhiteSpace(value);
            bool shouldApply = apply && !string.IsNullOrWhiteSpace(value) && !string.Equals(value, state.OriginalValue, StringComparison.Ordinal);
            string restoredValue = shouldApply ? value : state.OriginalValue;
            editor.style.display = DisplayStyle.None;
            state.NameLabel.style.display = DisplayStyle.Flex;
            state.NameLabel.text = restoredValue;
            editor.SetValueWithoutNotify(restoredValue);
            if ((shouldApply || invalidEmptyValue) && state.Definition != null) RenameSubmitted?.Invoke(state.Definition, value);
        }

        #endregion

        #region 右键菜单

        /// <summary>按面板坐标命中可见行，互斥显示物品操作或空白区域新建菜单。</summary>
        /// <param name="eventData">鼠标释放事件。</param>
        private void OnListViewMouseUp(MouseUpEvent eventData)
        {
            if (eventData.button != 1 || !IsListContentPosition(eventData.mousePosition))
                return;

            // 必须在选择事件刷新虚拟行之前捕获定义，不能使用 selectedDefinition 代替命中结果。
            VisualElement row = FindDefinitionRowAtPosition(eventData.mousePosition);
            ItemDefinition definition = row?.userData as ItemDefinition;
            var menu = new GenericMenu();
            if (definition != null)
            {
                int index = displayedDefinitions.IndexOf(definition);
                if (listView.selectedIndex != index) listView.selectedIndex = index;
                PopulateDefinitionContextMenu(menu, definition);
            }
            else
            {
                menu.AddItem(new GUIContent("新建/普通物品"), false, () => NewStackableRequested?.Invoke());
                menu.AddItem(new GUIContent("新建/武器"), false, () => NewWeaponRequested?.Invoke());
                menu.AddItem(new GUIContent("新建/养成道具"), false, () => NewDevelopmentItemRequested?.Invoke());
                menu.AddItem(new GUIContent("新建/圣遗物"), false, () => NewArtifactRequested?.Invoke());
            }

            // 在 ShowAsContext 前消费原始事件，避免子输入框或其他菜单处理器再次弹出菜单。
            eventData.StopImmediatePropagation();
            eventData.PreventDefault();
            menu.ShowAsContext();
        }

        /// <summary>限制菜单在列表可视内容内，排除滚动条和视口外的缓存行。</summary>
        /// <param name="position">鼠标的面板坐标，不是相对当前事件目标的坐标。</param>
        /// <returns>位于可交互内容区域时返回 true。</returns>
        private bool IsListContentPosition(Vector2 position)
        {
            return listView.worldBound.Contains(position) &&
                   listScrollView.contentViewport.worldBound.Contains(position) &&
                   !ContainsVisiblePosition(listScrollView.verticalScroller, position) &&
                   !ContainsVisiblePosition(listScrollView.horizontalScroller, position);
        }

        /// <summary>检查节点在当前可见状态下是否包含面板坐标。</summary>
        /// <param name="element">待检查的节点。</param>
        /// <param name="position">面板坐标。</param>
        /// <returns>节点显示且包含坐标时返回 true。</returns>
        private static bool ContainsVisiblePosition(VisualElement element, Vector2 position)
        {
            return element.visible && element.resolvedStyle.display != DisplayStyle.None &&
                   element.worldBound.Contains(position);
        }

        /// <summary>从已实例化且已绑定的行中命中物品，包含原生行包装层的空隙。</summary>
        /// <param name="position">已通过滚动视口校验的面板坐标。</param>
        /// <returns>命中的模板宿主；真正空白区域返回 null。</returns>
        private VisualElement FindDefinitionRowAtPosition(Vector2 position)
        {
            foreach (VisualElement host in listView.Query<VisualElement>(className: "item-editor-definition-row-host").ToList())
            {
                if (host.userData is not ItemDefinition definition || definition == null ||
                    !displayedDefinitions.Contains(definition)) continue;

                VisualElement bounds = host;
                bool hidden = false;
                // 原生复用容器可能比 UXML 内容宽，不能把行边缘当成列表背景。
                for (VisualElement current = host; current != listScrollView.contentContainer && current != null; current = current.parent)
                {
                    if (!current.visible || current.resolvedStyle.display == DisplayStyle.None) hidden = true;
                    if (current.ClassListContains("unity-collection-view__item")) bounds = current;
                }

                if (!hidden && bounds.worldBound.Contains(position)) return host;
            }

            return null;
        }

        /// <summary>创建针对捕获定义的右键菜单。</summary>
        /// <param name="menu">当前右键唯一的菜单。</param>
        /// <param name="definition">刷新虚拟行前已捕获的右键目标。</param>
        private void PopulateDefinitionContextMenu(GenericMenu menu, ItemDefinition definition)
        {
            menu.AddItem(new GUIContent("重命名"), false, () => listView.schedule.Execute(() => RenameVisibleDefinition(definition)));
            menu.AddItem(new GUIContent("复制"), false, () => DefinitionCommandRequested?.Invoke(definition, ItemDefinitionCommand.Duplicate));
            menu.AddItem(new GUIContent("应用当前物品类型默认值"), false, () => DefinitionCommandRequested?.Invoke(definition, ItemDefinitionCommand.ApplyTypeDefaults));
            menu.AddItem(new GUIContent("定位资产"), false, () => DefinitionCommandRequested?.Invoke(definition, ItemDefinitionCommand.PingAsset));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("移出数据库"), false, () => DefinitionCommandRequested?.Invoke(definition, ItemDefinitionCommand.RemoveFromDatabase));
            menu.AddItem(new GUIContent("删除资产…"), false, () => DefinitionCommandRequested?.Invoke(definition, ItemDefinitionCommand.DeleteAsset));
        }

        /// <summary>菜单关闭后重新定位定义的可见行，不使用可能已复用的旧节点。</summary>
        /// <param name="definition">打开菜单时捕获的定义。</param>
        private void RenameVisibleDefinition(ItemDefinition definition)
        {
            // Scheduler 可能晚于窗口释放或筛选变化，失效目标不再进入编辑。
            if (disposed || definition == null || !displayedDefinitions.Contains(definition)) return;
            foreach (VisualElement host in listView.Query<VisualElement>(className: "item-editor-definition-row-host").ToList())
            {
                if (host.userData as ItemDefinition != definition) continue;
                Vector2 position = host.worldBound.center;
                if (IsListContentPosition(position) && FindDefinitionRowAtPosition(position) == host)
                    BeginInlineRename(host, definition);
                return;
            }
        }

        #endregion

        #region 事件处理与辅助

        /// <summary>初始化筛选和排序下拉选项。</summary>
        private void ConfigureChoices()
        {
            categoryField.choices = new List<string> { "全部类型", "养成素材", "食材", "料理", "摆设", "武器", "圣遗物", "养成道具" };
            kindField.choices = new List<string> { "全部定义", "可堆叠物品", "养成道具定义", "武器定义", "圣遗物定义" };
            sortField.choices = new List<string> { "默认排序优先级", "显示名称", "稀有度", "物品类型", "定义类型", "养成用途", "稳定物品标识", "最大堆叠数量", "最大等级" };
            sortDirectionField.choices = new List<string> { "升序", "降序" };
        }

        /// <summary>处理搜索变化。</summary>
        /// <param name="change">文本变化。</param>
        private void OnSearchChanged(ChangeEvent<string> change) => SearchChanged?.Invoke(change.newValue);
        /// <summary>处理分类变化。</summary>
        /// <param name="change">下拉变化。</param>
        private void OnCategoryChanged(ChangeEvent<string> change) => CategoryChanged?.Invoke(change.newValue);
        /// <summary>处理定义类型变化。</summary>
        /// <param name="change">下拉变化。</param>
        private void OnKindChanged(ChangeEvent<string> change) => KindChanged?.Invoke(change.newValue);
        /// <summary>处理排序字段变化。</summary>
        /// <param name="change">下拉变化。</param>
        private void OnSortFieldChanged(ChangeEvent<string> change) => SortFieldChanged?.Invoke(change.newValue);
        /// <summary>处理排序方向变化。</summary>
        /// <param name="change">下拉变化。</param>
        private void OnSortDirectionChanged(ChangeEvent<string> change) => SortDirectionChanged?.Invoke(change.newValue);

        /// <summary>转发当前选择对象。</summary>
        /// <param name="selection">ListView 选择集合。</param>
        private void OnDefinitionSelectionChanged(IEnumerable<object> selection)
        {
            if (suppressSelectionChanged) return;
            ItemDefinition nextDefinition = null;
            foreach (object item in selection)
            {
                nextDefinition = item as ItemDefinition;
                break;
            }

            // ListView 的选择事件只会通知详情页，旧行不会自动重新执行 bindItem；
            // 这里显式刷新可见行，才能清掉上一行的自定义外框并点亮新选中行。
            bool selectionChanged = selectedDefinition != nextDefinition;
            selectedDefinition = nextDefinition;
            if (selectionChanged) listView.RefreshItems();
            DefinitionSelected?.Invoke(nextDefinition);
        }

        /// <summary>查询左栏范围内的控件。</summary>
        /// <typeparam name="TElement">控件类型。</typeparam>
        /// <param name="name">UXML 名称。</param>
        /// <returns>找到的控件。</returns>
        private TElement Require<TElement>(string name) where TElement : VisualElement
        {
            TElement element = root.Q<TElement>(name);
            if (element == null) throw new InvalidOperationException($"Item 配置窗口左栏缺少 UXML 控件：{name}。");
            return element;
        }

        /// <summary>保存一个虚拟化行的短生命周期重命名状态。</summary>
        private sealed class InlineRenameState
        {
            /// <summary>当前行定义。</summary>
            public ItemDefinition Definition;
            /// <summary>名称 Label。</summary>
            public Label NameLabel;
            /// <summary>进入编辑时的原名称。</summary>
            public string OriginalValue = string.Empty;
            /// <summary>是否已提交或取消。</summary>
            public bool Completed;
        }

        #endregion
    }
}
#endif
