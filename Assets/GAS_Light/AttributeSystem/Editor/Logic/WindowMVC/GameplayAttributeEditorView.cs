#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.GAS.AttributeSystem;

namespace WS_Modules.GAS.Editor
{
    /// <summary>使用 UI Toolkit 实现 Attribute Specs 与 Attribute Sets 两个子页面。</summary>
    public sealed class GameplayAttributeEditorView : IGameplayAttributeEditorView
    {
        #region 常量与字段

        private const string ActiveTabClass = "attribute-sub-tab--active";
        private const string SpecRowUxmlPath =
            "Assets/GAS_Light/AttributeSystem/Editor/Style/GameplayAttributeSpecRow.uxml";
        private const string DefinitionRowUxmlPath =
            "Assets/GAS_Light/AttributeSystem/Editor/Style/GameplayAttributeDefinitionRow.uxml";

        private readonly VisualElement root;
        private readonly VisualTreeAsset specRowAsset;
        private readonly VisualTreeAsset definitionRowAsset;
        private readonly Button specsPageButton;
        private readonly Button setsPageButton;
        private readonly ToolbarSearchField searchField;
        private readonly VisualElement specsPage;
        private readonly VisualElement setsPage;
        private readonly ObjectField registryField;
        private readonly ObjectField setField;
        private readonly ListView specList;
        private readonly TreeView definitionTree;
        private readonly TextField specNameField;
        private readonly TextField specGuidField;
        private readonly IntegerField specIdField;
        private readonly TextField specDescriptionField;
        private readonly DropdownField definitionAttributeField;
        private readonly EnumField definitionTypeField;
        private readonly FloatField definitionDefaultField;
        private readonly FloatField definitionMinField;
        private readonly FloatField definitionMaxField;
        private readonly HelpBox statusBox;

        private readonly List<GameplayAttributeEditorNode> renderedSpecs = new();
        private readonly List<GameplayAttributeEditorNode> renderedSelectableNodes = new();

        private GameplayAttributeRegistry currentRegistry;
        private GameplayAttributeEditorPage currentPage;
        private int selectedDefinitionOriginalId = -1;
        private bool renderingDetails;
        private bool disposed;

        #endregion

        #region 事件

        /// <inheritdoc />
        public event Action<GameplayAttributeEditorPage> PageChanged;
        /// <inheritdoc />
        public event Action<GameplayAttributeRegistry> RegistryChanged;
        /// <inheritdoc />
        public event Action<GameplayAttributeSet> SetChanged;
        /// <inheritdoc />
        public event Action<string> SearchChanged;
        /// <inheritdoc />
        public event Action<string> SpecSelectionChanged;
        /// <inheritdoc />
        public event Action<int> DefinitionSelectionChanged;
        /// <inheritdoc />
        public event Action CreateSpecRequested;
        /// <inheritdoc />
        public event Action DeleteSpecRequested;
        /// <inheritdoc />
        public event Action<string> SpecNameSubmitted;
        /// <inheritdoc />
        public event Action<string> SpecDescriptionSubmitted;
        /// <inheritdoc />
        public event Action BakeRequested;
        /// <inheritdoc />
        public event Action CreateSetRequested;
        /// <inheritdoc />
        public event Action<GameplayAttributeType> AddDefinitionRequested;
        /// <inheritdoc />
        public event Action DeleteDefinitionRequested;
        /// <inheritdoc />
        public event Action<GameplayAttributeDefinitionEditRequest> DefinitionSubmitted;

        #endregion

        #region 生命周期

        /// <summary>查询控件、配置列表并注册全部 UI 回调。</summary>
        /// <param name="root">已实例化 Attribute UXML 的页面根节点。</param>
        /// <exception cref="ArgumentNullException">root 为 null。</exception>
        /// <exception cref="InvalidOperationException">UXML 缺少必需控件。</exception>
        public GameplayAttributeEditorView(VisualElement root)
        {
            this.root = root ?? throw new ArgumentNullException(nameof(root));
            specRowAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(SpecRowUxmlPath);
            definitionRowAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(DefinitionRowUxmlPath);
            if (specRowAsset == null || definitionRowAsset == null)
                throw new InvalidOperationException("Attribute Editor 行 UXML 资源缺失。");
            specsPageButton = Require<Button>("SpecsPageButton");
            setsPageButton = Require<Button>("SetsPageButton");
            searchField = Require<ToolbarSearchField>("SearchField");
            specsPage = Require<VisualElement>("SpecsPage");
            setsPage = Require<VisualElement>("SetsPage");
            registryField = Require<ObjectField>("RegistryField");
            setField = Require<ObjectField>("SetField");
            specList = Require<ListView>("SpecList");
            definitionTree = Require<TreeView>("DefinitionTree");
            specNameField = Require<TextField>("SpecNameField");
            specGuidField = Require<TextField>("SpecGuidField");
            specIdField = Require<IntegerField>("SpecIdField");
            specDescriptionField = Require<TextField>("SpecDescriptionField");
            definitionAttributeField = Require<DropdownField>("DefinitionAttributeField");
            definitionTypeField = Require<EnumField>("DefinitionTypeField");
            definitionDefaultField = Require<FloatField>("DefinitionDefaultField");
            definitionMinField = Require<FloatField>("DefinitionMinField");
            definitionMaxField = Require<FloatField>("DefinitionMaxField");
            statusBox = Require<HelpBox>("StatusBox");

            specGuidField.SetEnabled(false);
            specIdField.SetEnabled(false);

            registryField.objectType = typeof(GameplayAttributeRegistry);
            registryField.allowSceneObjects = false;
            setField.objectType = typeof(GameplayAttributeSet);
            setField.allowSceneObjects = false;
            definitionTypeField.Init(GameplayAttributeType.Stat);
            ConfigureLists();
            RegisterCallbacks();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            UnregisterCallbacks();
            specList.itemsSource = null;
            definitionTree.SetRootItems(Array.Empty<TreeViewItemData<object>>());
        }

        #endregion

        #region 状态同步

        /// <inheritdoc />
        public void SetPage(GameplayAttributeEditorPage page)
        {
            currentPage = page;
            specsPage.style.display = page == GameplayAttributeEditorPage.Specs
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            setsPage.style.display = page == GameplayAttributeEditorPage.Sets
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            specsPageButton.EnableInClassList(ActiveTabClass, page == GameplayAttributeEditorPage.Specs);
            setsPageButton.EnableInClassList(ActiveTabClass, page == GameplayAttributeEditorPage.Sets);
        }

        /// <inheritdoc />
        public void SetRegistry(GameplayAttributeRegistry registry)
        {
            currentRegistry = registry;
            registryField.SetValueWithoutNotify(registry);
        }

        /// <inheritdoc />
        public void SetAttributeSet(GameplayAttributeSet set) =>
            setField.SetValueWithoutNotify(set);

        /// <inheritdoc />
        public void SetSearch(string search) =>
            searchField.SetValueWithoutNotify(search ?? string.Empty);

        #endregion

        #region 渲染

        /// <inheritdoc />
        public void RenderSpecs(
            IReadOnlyList<GameplayAttributeEditorNode> specs,
            string selectedGuid)
        {
            renderedSpecs.Clear();
            if (specs != null) renderedSpecs.AddRange(specs);
            specList.itemsSource = renderedSpecs;
            specList.Rebuild();
            int index = renderedSpecs.FindIndex(item => item.Guid == selectedGuid);
            specList.SetSelectionWithoutNotify(index < 0 ? Array.Empty<int>() : new[] { index });
        }

        /// <inheritdoc />
        public void RenderSpecDetails(GameplayAttributeEditorNode node)
        {
            renderingDetails = true;
            try
            {
                bool hasSelection = node != null;
                SetSpecDetailsEnabled(hasSelection);
                specNameField.SetValueWithoutNotify(hasSelection ? node.Name : string.Empty);
                specGuidField.SetValueWithoutNotify(hasSelection ? node.Guid : string.Empty);
                int id = hasSelection &&
                         currentRegistry != null &&
                         currentRegistry.TryGetBakedAttribute(
                             node.Guid,
                             out GameplayAttribute attribute)
                    ? attribute.Id
                    : -1;
                specIdField.SetValueWithoutNotify(id);
                specDescriptionField.SetValueWithoutNotify(
                    hasSelection ? node.Description : string.Empty);
            }
            finally
            {
                renderingDetails = false;
            }
        }

        /// <inheritdoc />
        public void RenderDefinitions(
            IReadOnlyList<GameplayAttributeDefinition> definitions,
            int selectedAttributeId)
        {
            const int statGroupId = int.MinValue;
            const int resourceGroupId = int.MinValue + 1;
            var stats = new List<TreeViewItemData<object>>();
            var resources = new List<TreeViewItemData<object>>();
            if (definitions != null)
                for (int i = 0; i < definitions.Count; i++)
                {
                    GameplayAttributeDefinition definition = definitions[i];
                    var item = new TreeViewItemData<object>(
                        definition.Attribute.Id,
                        definition);
                    if (definition.Type == GameplayAttributeType.Stat) stats.Add(item);
                    else resources.Add(item);
                }

            var roots = new List<TreeViewItemData<object>>
            {
                new(statGroupId, GameplayAttributeType.Stat, stats),
                new(resourceGroupId, GameplayAttributeType.Resource, resources)
            };
            definitionTree.SetRootItems(roots);
            definitionTree.Rebuild();
            definitionTree.ExpandItem(statGroupId);
            definitionTree.ExpandItem(resourceGroupId);
            if (selectedAttributeId >= 0)
                definitionTree.SetSelectionById(selectedAttributeId);
            else
                definitionTree.ClearSelection();
        }

        /// <inheritdoc />
        public void RenderDefinitionDetails(
            GameplayAttributeDefinition definition,
            IReadOnlyList<GameplayAttributeEditorNode> selectableNodes)
        {
            renderingDetails = true;
            try
            {
                renderedSelectableNodes.Clear();
                if (selectableNodes != null) renderedSelectableNodes.AddRange(selectableNodes);
                definitionAttributeField.choices =
                    renderedSelectableNodes.Select(node => node.Name).ToList();

                bool hasSelection = definition != null;
                GameplayAttribute selectedAttribute = hasSelection
                    ? definition.Attribute
                    : GameplayAttribute.Empty;
                selectedDefinitionOriginalId = selectedAttribute.Id;
                SetDefinitionDetailsEnabled(hasSelection);
                int choiceIndex = renderedSelectableNodes.FindIndex(node =>
                    currentRegistry != null &&
                    currentRegistry.TryGetBakedAttribute(node.Guid, out GameplayAttribute candidate) &&
                    candidate == selectedAttribute);
                definitionAttributeField.SetValueWithoutNotify(
                    choiceIndex >= 0 ? renderedSelectableNodes[choiceIndex].Name : string.Empty);
                definitionTypeField.SetValueWithoutNotify(
                    hasSelection ? definition.Type : GameplayAttributeType.Stat);
                definitionDefaultField.SetValueWithoutNotify(
                    hasSelection ? definition.DefaultValue : 0f);
                definitionMinField.SetValueWithoutNotify(
                    hasSelection ? definition.MinValue : float.NegativeInfinity);
                definitionMaxField.SetValueWithoutNotify(
                    hasSelection ? definition.MaxValue : float.PositiveInfinity);
            }
            finally
            {
                renderingDetails = false;
            }
        }

        /// <inheritdoc />
        public void RenderStatus(string message, bool isError)
        {
            statusBox.text = message ?? string.Empty;
            statusBox.messageType = isError ? HelpBoxMessageType.Error : HelpBoxMessageType.Info;
            statusBox.style.display = string.IsNullOrEmpty(message)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
        }

        /// <inheritdoc />
        public bool Confirm(string title, string message) =>
            EditorUtility.DisplayDialog(title, message, "Confirm", "Cancel");

        /// <inheritdoc />
        public void ShowError(string title, string message) =>
            EditorUtility.DisplayDialog(title, message, "OK");

        /// <inheritdoc />
        public void ShowResult(string title, string message) =>
            EditorUtility.DisplayDialog(title, message, "OK");

        #endregion

        #region 控件配置

        // 配置虚拟化列表和 Tree 的行创建/绑定函数。
        private void ConfigureLists()
        {
            specList.selectionType = SelectionType.Single;
            specList.fixedItemHeight = 22f;
            specList.makeItem = MakeSpecItem;
            specList.bindItem = BindSpecItem;

            definitionTree.selectionType = SelectionType.Single;
            definitionTree.fixedItemHeight = 22f;
            definitionTree.makeItem = MakeDefinitionItem;
            definitionTree.bindItem = BindDefinitionItem;
        }

        // 从专用 UXML 创建 Spec 行；双击名称时把焦点移到详情名称字段。
        private VisualElement MakeSpecItem()
        {
            TemplateContainer row = specRowAsset.Instantiate();
            row.RegisterCallback<PointerDownEvent>(OnSpecRowPointerDown);
            return row;
        }

        // 绑定虚拟化 Spec 行；Bake ID 直接通过当前 Registry 解析。
        private void BindSpecItem(VisualElement element, int index)
        {
            if (index < 0 || index >= renderedSpecs.Count) return;
            GameplayAttributeEditorNode node = renderedSpecs[index];
            element.Q<Label>("NameLabel").text = node.Name;
            GameplayAttribute attribute = GameplayAttribute.Empty;
            bool baked = currentRegistry != null &&
                         currentRegistry.TryGetBakedAttribute(node.Guid, out attribute);
            element.Q<Label>("IdLabel").text = baked ? $"#{attribute.Id}" : "Unbaked";
        }

        // 从专用 UXML 创建 Definition Tree 行。
        private VisualElement MakeDefinitionItem() => definitionRowAsset.Instantiate();

        // 虚拟分组直接使用 Type，业务行直接使用 Definition Model。
        private void BindDefinitionItem(VisualElement element, int index)
        {
            object item = definitionTree.GetItemDataForIndex<object>(index);
            if (item is GameplayAttributeDefinition definition)
            {
                element.Q<Label>("NameLabel").text =
                    ResolveAttributeName(definition.Attribute.Id);
                element.Q<Label>("TypeLabel").text = definition.Type.ToString();
                return;
            }

            element.Q<Label>("NameLabel").text = item is GameplayAttributeType type
                ? type.ToString()
                : string.Empty;
            element.Q<Label>("TypeLabel").text = string.Empty;
        }

        #endregion

        #region 回调注册

        // 对称注册 View 的全部 UI 回调。
        private void RegisterCallbacks()
        {
            specsPageButton.clicked += OnSpecsPageClicked;
            setsPageButton.clicked += OnSetsPageClicked;
            searchField.RegisterValueChangedCallback(OnSearchChanged);
            registryField.RegisterValueChangedCallback(OnRegistryChanged);
            setField.RegisterValueChangedCallback(OnSetChanged);
            specList.selectionChanged += OnSpecSelectionChanged;
            definitionTree.selectionChanged += OnDefinitionSelectionChanged;
            Require<Button>("CreateSpecButton").clicked += OnCreateSpecClicked;
            Require<Button>("DeleteSpecButton").clicked += OnDeleteSpecClicked;
            Require<Button>("BakeButton").clicked += OnBakeClicked;
            specNameField.RegisterValueChangedCallback(OnSpecNameChanged);
            specDescriptionField.RegisterValueChangedCallback(OnSpecDescriptionChanged);
            Require<Button>("CreateSetButton").clicked += OnCreateSetClicked;
            Require<Button>("AddStatButton").clicked += OnAddStatClicked;
            Require<Button>("AddResourceButton").clicked += OnAddResourceClicked;
            Require<Button>("DeleteDefinitionButton").clicked += OnDeleteDefinitionClicked;
            definitionAttributeField.RegisterValueChangedCallback(OnDefinitionAttributeChanged);
            definitionTypeField.RegisterValueChangedCallback(OnDefinitionTypeChanged);
            definitionDefaultField.RegisterValueChangedCallback(OnDefinitionValueChanged);
            definitionMinField.RegisterValueChangedCallback(OnDefinitionValueChanged);
            definitionMaxField.RegisterValueChangedCallback(OnDefinitionValueChanged);
            root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
        }

        // 对称注销 View 的全部 UI 回调，避免模块切换后重复订阅。
        private void UnregisterCallbacks()
        {
            specsPageButton.clicked -= OnSpecsPageClicked;
            setsPageButton.clicked -= OnSetsPageClicked;
            searchField.UnregisterValueChangedCallback(OnSearchChanged);
            registryField.UnregisterValueChangedCallback(OnRegistryChanged);
            setField.UnregisterValueChangedCallback(OnSetChanged);
            specList.selectionChanged -= OnSpecSelectionChanged;
            definitionTree.selectionChanged -= OnDefinitionSelectionChanged;
            Require<Button>("CreateSpecButton").clicked -= OnCreateSpecClicked;
            Require<Button>("DeleteSpecButton").clicked -= OnDeleteSpecClicked;
            Require<Button>("BakeButton").clicked -= OnBakeClicked;
            specNameField.UnregisterValueChangedCallback(OnSpecNameChanged);
            specDescriptionField.UnregisterValueChangedCallback(OnSpecDescriptionChanged);
            Require<Button>("CreateSetButton").clicked -= OnCreateSetClicked;
            Require<Button>("AddStatButton").clicked -= OnAddStatClicked;
            Require<Button>("AddResourceButton").clicked -= OnAddResourceClicked;
            Require<Button>("DeleteDefinitionButton").clicked -= OnDeleteDefinitionClicked;
            definitionAttributeField.UnregisterValueChangedCallback(OnDefinitionAttributeChanged);
            definitionTypeField.UnregisterValueChangedCallback(OnDefinitionTypeChanged);
            definitionDefaultField.UnregisterValueChangedCallback(OnDefinitionValueChanged);
            definitionMinField.UnregisterValueChangedCallback(OnDefinitionValueChanged);
            definitionMaxField.UnregisterValueChangedCallback(OnDefinitionValueChanged);
            root.UnregisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
        }

        #endregion

        #region 事件处理

        // 切换到 Specs 子页面。
        private void OnSpecsPageClicked() => PageChanged?.Invoke(GameplayAttributeEditorPage.Specs);

        // 切换到 Sets 子页面。
        private void OnSetsPageClicked() => PageChanged?.Invoke(GameplayAttributeEditorPage.Sets);

        // 转发搜索意图。
        private void OnSearchChanged(ChangeEvent<string> evt) => SearchChanged?.Invoke(evt.newValue);

        // 转发 Registry 选择。
        private void OnRegistryChanged(ChangeEvent<UnityEngine.Object> evt) =>
            RegistryChanged?.Invoke(evt.newValue as GameplayAttributeRegistry);

        // 转发 Set 选择。
        private void OnSetChanged(ChangeEvent<UnityEngine.Object> evt) =>
            SetChanged?.Invoke(evt.newValue as GameplayAttributeSet);

        // 转发当前 Spec Model 的持久 Guid。
        private void OnSpecSelectionChanged(IEnumerable<object> selection)
        {
            GameplayAttributeEditorNode selected =
                selection.OfType<GameplayAttributeEditorNode>().FirstOrDefault();
            SpecSelectionChanged?.Invoke(selected?.Guid ?? string.Empty);
        }

        // 仅 Definition Model 行可成为业务选择，虚拟 Type 分组返回 -1。
        private void OnDefinitionSelectionChanged(IEnumerable<object> selection)
        {
            GameplayAttributeDefinition selected =
                selection.OfType<GameplayAttributeDefinition>().FirstOrDefault();
            DefinitionSelectionChanged?.Invoke(selected?.Attribute.Id ?? -1);
        }

        // 双击 Spec 行后进入详情名称编辑。
        private void OnSpecRowPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || evt.clickCount != 2) return;
            specNameField.Focus();
            specNameField.SelectAll();
            evt.StopPropagation();
        }

        // 转发 Spec 创建。
        private void OnCreateSpecClicked() => CreateSpecRequested?.Invoke();

        // 转发 Spec 删除。
        private void OnDeleteSpecClicked() => DeleteSpecRequested?.Invoke();

        // 转发 Bake。
        private void OnBakeClicked() => BakeRequested?.Invoke();

        // Name 由 delayed TextField 在 Enter 或失焦后提交，避免按键级 Undo。
        private void OnSpecNameChanged(ChangeEvent<string> evt)
        {
            if (!renderingDetails) SpecNameSubmitted?.Invoke(evt.newValue);
        }

        // Description 在 delayed TextField 结束编辑时提交。
        private void OnSpecDescriptionChanged(ChangeEvent<string> evt)
        {
            if (!renderingDetails) SpecDescriptionSubmitted?.Invoke(evt.newValue);
        }

        // 转发 Set 资产创建请求。
        private void OnCreateSetClicked() => CreateSetRequested?.Invoke();

        // 请求创建 Stat Definition。
        private void OnAddStatClicked() => AddDefinitionRequested?.Invoke(GameplayAttributeType.Stat);

        // 请求创建 Resource Definition。
        private void OnAddResourceClicked() => AddDefinitionRequested?.Invoke(GameplayAttributeType.Resource);

        // 转发 Definition 删除。
        private void OnDeleteDefinitionClicked() => DeleteDefinitionRequested?.Invoke();

        // Attribute 下拉选择完成后立即提交当前完整 Definition。
        private void OnDefinitionAttributeChanged(ChangeEvent<string> evt) => SubmitDefinition();

        // Type 枚举选择完成后立即提交当前完整 Definition。
        private void OnDefinitionTypeChanged(ChangeEvent<Enum> evt) => SubmitDefinition();

        // delayed FloatField 在 Enter 或失焦后提交当前完整 Definition。
        private void OnDefinitionValueChanged(ChangeEvent<float> evt) => SubmitDefinition();

        // 将全部详情控件组合成明确请求，跨字段校验仍由 Service 原子完成。
        private void SubmitDefinition()
        {
            if (renderingDetails || selectedDefinitionOriginalId < 0) return;
            int choiceIndex = definitionAttributeField.index;
            GameplayAttribute attribute = GameplayAttribute.Empty;
            if (choiceIndex >= 0 &&
                choiceIndex < renderedSelectableNodes.Count &&
                currentRegistry != null)
                currentRegistry.TryGetBakedAttribute(
                    renderedSelectableNodes[choiceIndex].Guid,
                    out attribute);
            GameplayAttributeType type = definitionTypeField.value is GameplayAttributeType selectedType
                ? selectedType
                : GameplayAttributeType.Stat;
            DefinitionSubmitted?.Invoke(new GameplayAttributeDefinitionEditRequest(
                selectedDefinitionOriginalId,
                attribute,
                type,
                definitionDefaultField.value,
                definitionMinField.value,
                definitionMaxField.value));
        }

        // Tree/List 聚焦时提供 Delete 与 F2；文本输入期间不拦截。
        private void OnKeyDown(KeyDownEvent evt)
        {
            if (root.panel?.focusController?.focusedElement is TextInputBaseField<string> ||
                root.panel?.focusController?.focusedElement is TextInputBaseField<float>)
                return;

            if (evt.keyCode == KeyCode.Delete)
            {
                if (currentPage == GameplayAttributeEditorPage.Specs) DeleteSpecRequested?.Invoke();
                else DeleteDefinitionRequested?.Invoke();
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.F2 && currentPage == GameplayAttributeEditorPage.Specs)
            {
                specNameField.Focus();
                specNameField.SelectAll();
                evt.StopPropagation();
            }
        }

        #endregion

        #region 内部辅助

        // 查询必需 UXML 控件并在资源契约不一致时立即失败。
        private T Require<T>(string name) where T : VisualElement
        {
            T element = root.Q<T>(name);
            if (element == null)
                throw new InvalidOperationException($"Attribute UXML 缺少必需控件 '{name}'。");
            return element;
        }

        // 使用当前 Registry 解析显示名称；失效 ID 保留明确诊断文本。
        private string ResolveAttributeName(int id) =>
            currentRegistry != null &&
            currentRegistry.TryGetNodeById(id, out GameplayAttributeEditorNode node)
                ? node.Name
                : $"Invalid AttributeId ({id})";

        // 统一设置 Spec 详情可编辑状态。
        private void SetSpecDetailsEnabled(bool enabled)
        {
            specNameField.SetEnabled(enabled);
            specDescriptionField.SetEnabled(enabled);
        }

        // 统一设置 Definition 详情可编辑状态。
        private void SetDefinitionDetailsEnabled(bool enabled)
        {
            definitionAttributeField.SetEnabled(enabled);
            definitionTypeField.SetEnabled(enabled);
            definitionDefaultField.SetEnabled(enabled);
            definitionMinField.SetEnabled(enabled);
            definitionMaxField.SetEnabled(enabled);
        }


        #endregion
    }
}
#endif
