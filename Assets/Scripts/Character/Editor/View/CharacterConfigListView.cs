#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.EditorExtensions;

namespace RPG.Character.Editor
{
    /// <summary>角色配置编辑器左侧的搜索、虚拟化列表和选中状态 View。</summary>
    internal sealed class CharacterConfigListView : IDisposable
    {
        #region 依赖字段

        private const string RowUxmlPath = "Assets/Scripts/Character/Editor/Style/CharacterConfigListRow.uxml";
        private readonly ListView listView;
        private readonly ScrollView listScrollView;
        private readonly ToolbarSearchField searchField;
        private readonly Label emptyListLabel;
        private readonly VisualTreeAsset rowTemplate;
        private readonly List<CharacterConfig> displayedCharacters = new();
        private CharacterConfig selectedConfig;
        private bool suppressSelectionChanged;
        private bool disposed;

        #endregion

        #region 事件

        /// <summary>搜索文本变化事件。</summary>
        internal event Action<string> SearchChanged;
        /// <summary>列表选择变化事件。</summary>
        internal event Action<CharacterConfig> CharacterSelected;
        /// <summary>角色配置右键操作事件。</summary>
        internal event Action<CharacterConfig, CharacterConfigCommand> CharacterCommandRequested;
        /// <summary>列表空白区域请求新建角色事件。</summary>
        internal event Action NewCharacterRequested;

        #endregion

        #region 生命周期

        /// <summary>加载列表行模板并连接搜索和选择回调。</summary>
        /// <param name="root">角色编辑器根节点。</param>
        public CharacterConfigListView(VisualElement root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            listView = Require<ListView>(root, "CharacterList");
            listScrollView = listView.Q<ScrollView>() ?? throw new InvalidOperationException("角色列表缺少原生 ScrollView。");
            searchField = Require<ToolbarSearchField>(root, "SearchField");
            emptyListLabel = Require<Label>(root, "EmptyListLabel");
            rowTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(RowUxmlPath);
            if (rowTemplate == null) throw new InvalidOperationException($"角色配置窗口缺少列表行 UXML：{RowUxmlPath}。");

            listView.selectionType = SelectionType.Single;
            listView.fixedItemHeight = 58f;
            listView.itemsSource = displayedCharacters;
            listView.makeItem = MakeRow;
            listView.bindItem = BindRow;
            listView.selectionChanged += OnSelectionChanged;
            searchField.RegisterValueChangedCallback(OnSearchChanged);
            // 在 TrickleDown 阶段统一捕获右键，避免虚拟化行中的子控件重复弹出菜单。
            listView.RegisterCallback<MouseUpEvent>(OnListViewMouseUp, TrickleDown.TrickleDown);
        }

        /// <summary>解除列表回调并释放虚拟化数据源。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            listView.selectionChanged -= OnSelectionChanged;
            searchField.UnregisterValueChangedCallback(OnSearchChanged);
            listView.UnregisterCallback<MouseUpEvent>(OnListViewMouseUp, TrickleDown.TrickleDown);
            listView.itemsSource = null;
            listView.makeItem = null;
            listView.bindItem = null;
            displayedCharacters.Clear();
        }

        #endregion

        #region 状态呈现

        /// <summary>设置搜索框文本而不触发筛选事件。</summary>
        /// <param name="search">搜索文本。</param>
        internal void SetSearch(string search) => searchField.SetValueWithoutNotify(search ?? string.Empty);

        /// <summary>刷新筛选后的角色列表并恢复当前选中项。</summary>
        /// <param name="characters">待呈现角色。</param>
        /// <param name="selected">当前选中角色。</param>
        internal void RenderCharacters(IReadOnlyList<CharacterConfig> characters, CharacterConfig selected)
        {
            displayedCharacters.Clear();
            if (characters != null)
                for (int index = 0; index < characters.Count; index++)
                    if (characters[index] != null) displayedCharacters.Add(characters[index]);

            selectedConfig = selected;
            emptyListLabel.style.display = displayedCharacters.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            suppressSelectionChanged = true;
            listView.RefreshItems();
            listView.selectedIndex = selected == null ? -1 : displayedCharacters.IndexOf(selected);
            suppressSelectionChanged = false;
        }

        /// <summary>刷新虚拟化行，确保名称、图片和星级在 Undo 后同步。</summary>
        internal void RefreshItems() => listView.RefreshItems();

        #endregion

        #region 列表行

        /// <summary>克隆带本地样式作用域的角色列表行。</summary>
        /// <returns>ListView 虚拟化宿主节点。</returns>
        private VisualElement MakeRow()
        {
            var host = new VisualElement { name = "CharacterRowHost" };
            host.AddToClassList("config-editor-list-row-host");
            rowTemplate.CloneTree(host);
            if (host.Q<VisualElement>("CharacterRow") == null)
                throw new InvalidOperationException("角色配置列表行 UXML 缺少 CharacterRow 根节点。");
            return host;
        }

        /// <summary>绑定角色列表行并清理上一条数据的稀有度和选中状态。</summary>
        /// <param name="element">虚拟化行宿主。</param>
        /// <param name="index">显示列表索引。</param>
        private void BindRow(VisualElement element, int index)
        {
            CharacterConfig config = index >= 0 && index < displayedCharacters.Count ? displayedCharacters[index] : null;
            element.userData = config;
            VisualElement row = element.Q<VisualElement>("CharacterRow") ?? element;
            Image icon = row.Q<Image>("SideIcon");
            Label fallback = row.Q<Label>("IconFallback");
            Label name = row.Q<Label>("Name");
            Label id = row.Q<Label>("Id");
            Label meta = row.Q<Label>("Meta");
            Label rarity = row.Q<Label>("Rarity");
            if (icon == null || fallback == null || name == null || id == null || meta == null || rarity == null) return;

            row.userData = config;
            Sprite sideIcon = config?.EditorSideIcon;
            icon.scaleMode = ScaleMode.ScaleToFit;
            icon.sprite = sideIcon;
            icon.style.display = sideIcon == null ? DisplayStyle.None : DisplayStyle.Flex;
            fallback.style.display = sideIcon == null ? DisplayStyle.Flex : DisplayStyle.None;
            fallback.text = "♟";
            name.text = string.IsNullOrWhiteSpace(config?.Name) ? "未命名角色" : config.Name;
            id.text = config == null ? string.Empty : config.CharacterId.ToString();
            meta.text = config == null
                ? string.Empty
                : $"Prefab · 属性集 {config.InitialAttributeSets.Count} · 输入 {config.AbilityInputBindings.Count}";
            rarity.text = config == null ? string.Empty : ConfigEditorRarityPresentation.GetRarityStars((int)config.Rarity);
            ConfigEditorRarityPresentation.EnableRarityClass(row, "character-config-list-row", config == null ? null : (int?)config.Rarity);
            row.EnableInClassList("character-config-list-row--selected", config != null && config == selectedConfig);
        }

        #endregion

        #region 右键菜单

        /// <summary>按面板坐标命中角色行，空白区域显示新建菜单。</summary>
        /// <param name="eventData">鼠标释放事件。</param>
        private void OnListViewMouseUp(MouseUpEvent eventData)
        {
            if (eventData.button != 1 || !IsListContentPosition(eventData.mousePosition)) return;

            CharacterConfig config = FindCharacterAtPosition(eventData.mousePosition);
            var menu = new GenericMenu();
            if (config != null)
            {
                int index = displayedCharacters.IndexOf(config);
                if (listView.selectedIndex != index) listView.selectedIndex = index;
                PopulateCharacterContextMenu(menu, config);
            }
            else
            {
                menu.AddItem(new GUIContent("新建/角色"), false, () => NewCharacterRequested?.Invoke());
            }

            // 消费原始右键事件，避免原生控件或其他父节点再次处理同一个菜单请求。
            eventData.StopImmediatePropagation();
            eventData.PreventDefault();
            menu.ShowAsContext();
        }

        /// <summary>限制右键菜单只出现在列表内容区域，排除滚动条。</summary>
        /// <param name="position">鼠标的面板坐标。</param>
        /// <returns>位于可交互列表内容中时返回 true。</returns>
        private bool IsListContentPosition(Vector2 position)
        {
            return listView.worldBound.Contains(position) &&
                   listScrollView.contentViewport.worldBound.Contains(position) &&
                   !ContainsVisiblePosition(listScrollView.verticalScroller, position) &&
                   !ContainsVisiblePosition(listScrollView.horizontalScroller, position);
        }

        /// <summary>判断滚动条是否在当前位置可见并拦截菜单。</summary>
        /// <param name="element">待检查的滚动条。</param>
        /// <param name="position">鼠标的面板坐标。</param>
        /// <returns>元素可见且包含坐标时返回 true。</returns>
        private static bool ContainsVisiblePosition(VisualElement element, Vector2 position)
        {
            return element != null && element.visible && element.resolvedStyle.display != DisplayStyle.None && element.worldBound.Contains(position);
        }

        /// <summary>从当前已经实例化的虚拟化行中找到右键目标。</summary>
        /// <param name="position">已通过列表视口校验的面板坐标。</param>
        /// <returns>命中的角色配置，空白区域返回 null。</returns>
        private CharacterConfig FindCharacterAtPosition(Vector2 position)
        {
            foreach (VisualElement host in listView.Query<VisualElement>(className: "config-editor-list-row-host").ToList())
            {
                CharacterConfig config = host.userData as CharacterConfig;
                if (config == null || !displayedCharacters.Contains(config)) continue;

                VisualElement bounds = host;
                bool hidden = false;
                // 原生集合项包装器可能比模板行宽；使用集合项边界覆盖其可点击空隙。
                for (VisualElement current = host; current != listScrollView.contentContainer && current != null; current = current.parent)
                {
                    if (!current.visible || current.resolvedStyle.display == DisplayStyle.None) hidden = true;
                    if (current.ClassListContains("unity-collection-view__item")) bounds = current;
                }

                if (!hidden && bounds.worldBound.Contains(position)) return config;
            }

            return null;
        }

        /// <summary>填充针对已捕获角色配置的右键菜单。</summary>
        /// <param name="menu">当前菜单。</param>
        /// <param name="config">打开菜单时捕获的角色配置。</param>
        private void PopulateCharacterContextMenu(GenericMenu menu, CharacterConfig config)
        {
            menu.AddItem(new GUIContent("复制"), false, () => CharacterCommandRequested?.Invoke(config, CharacterConfigCommand.Duplicate));
            menu.AddItem(new GUIContent("验证当前"), false, () => CharacterCommandRequested?.Invoke(config, CharacterConfigCommand.Validate));
            menu.AddItem(new GUIContent("定位资产"), false, () => CharacterCommandRequested?.Invoke(config, CharacterConfigCommand.PingAsset));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("移出数据库"), false, () => CharacterCommandRequested?.Invoke(config, CharacterConfigCommand.RemoveFromDatabase));
            menu.AddItem(new GUIContent("删除资产…"), false, () => CharacterCommandRequested?.Invoke(config, CharacterConfigCommand.DeleteAsset));
        }

        #endregion

        #region 事件处理

        /// <summary>转发搜索框提交的文本。</summary>
        private void OnSearchChanged(ChangeEvent<string> eventData)
        {
            if (!suppressSelectionChanged) SearchChanged?.Invoke(eventData.newValue);
        }

        /// <summary>转发列表选中的角色配置。</summary>
        private void OnSelectionChanged(IEnumerable<object> selection)
        {
            if (suppressSelectionChanged) return;
            CharacterConfig nextConfig = null;
            foreach (object item in selection)
            {
                nextConfig = item as CharacterConfig;
                break;
            }

            // ListView 的选择变化不会自动重新执行已创建行的 bindItem；先刷新所有可见行，
            // 才能清除旧行外框并按当前星级显示新的选中 border，再通知 Controller 绑定右侧详情。
            bool selectionChanged = selectedConfig != nextConfig;
            selectedConfig = nextConfig;
            if (selectionChanged) listView.RefreshItems();
            CharacterSelected?.Invoke(nextConfig);
        }

        #endregion

        #region 内部辅助

        /// <summary>从根节点取得指定类型控件。</summary>
        /// <typeparam name="T">控件类型。</typeparam>
        /// <param name="root">搜索根节点。</param>
        /// <param name="name">控件名称。</param>
        /// <returns>找到的控件。</returns>
        private static T Require<T>(VisualElement root, string name) where T : VisualElement
        {
            T element = root.Q<T>(name);
            if (element == null) throw new InvalidOperationException($"角色配置窗口缺少控件：{name}。");
            return element;
        }

        #endregion
    }
}
#endif
