#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.GAS.GameplayAbilitySystem;

namespace WS_Modules.GAS.Editor
{
    /// <summary>使用 UI Toolkit 渲染 GA 资产列表、原生序列化详情和 Validation。</summary>
    public sealed class GameplayAbilityEditorView : IGameplayAbilityEditorView
    {
        #region 常量与字段
        private const string RowUxmlPath =
            "Assets/GAS_Light/AbilitySystem/Editor/Style/GameplayAbilityAssetRow.uxml";
        private const string HiddenClass = "is-hidden";
        private const string ValidationErrorClass = "has-validation-error";

        private readonly VisualElement root;
        private readonly Button createButton;
        private readonly Button duplicateButton;
        private readonly Button deleteButton;
        private readonly ToolbarSearchField searchField;
        private readonly ListView abilityList;
        private readonly VisualElement detailsRoot;
        private readonly VisualElement subclassFieldsContainer;
        private readonly Label abilityTitle;
        private readonly VisualElement validationContainer;
        private readonly VisualTreeAsset rowTemplate;
        private readonly List<GameplayAbilityData> displayedAbilities = new();
        private readonly List<Type> creatableAbilityTypes = new();
        private IReadOnlyDictionary<GameplayAbilityData, GameplayAbilityValidationSeverity>
            validationStates = new Dictionary<GameplayAbilityData, GameplayAbilityValidationSeverity>();
        private GameplayAbilityData boundAbility;
        private GameplayAbilityData pendingRenameAbility;
        private string pendingRenameText;
        private bool suppressSelection;
        private bool disposed;
        #endregion

        #region 事件
        /// <inheritdoc />
        public event Action<string> SearchChanged;
        /// <inheritdoc />
        public event Action<GameplayAbilityData> AbilitySelected;
        /// <inheritdoc />
        public event Action<Type> CreateRequested;
        /// <inheritdoc />
        public event Action DuplicateRequested;
        /// <inheritdoc />
        public event Action DeleteRequested;
        /// <inheritdoc />
        public event Action<GameplayAbilityData> PingRequested;
        /// <inheritdoc />
        public event Action<GameplayAbilityRenameRequest> RenameSubmitted;
        /// <inheritdoc />
        public event Action AbilityChanged;
        #endregion

        #region 生命周期
        /// <summary>查询并配置 GameplayAbilityWindow 已实例化的全部控件。</summary>
        public GameplayAbilityEditorView(VisualElement root)
        {
            this.root = root ?? throw new ArgumentNullException(nameof(root));
            createButton = Require<Button>("CreateAbilityButton");
            duplicateButton = Require<Button>("DuplicateAbilityButton");
            deleteButton = Require<Button>("DeleteAbilityButton");
            searchField = Require<ToolbarSearchField>("SearchField");
            abilityList = Require<ListView>("AbilityList");
            detailsRoot = Require<VisualElement>("AbilityDetailsRoot");
            subclassFieldsContainer = Require<VisualElement>("SubclassFieldsContainer");
            abilityTitle = Require<Label>("AbilityTitle");
            validationContainer = Require<VisualElement>("ValidationContainer");
            rowTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(RowUxmlPath);
            if (rowTemplate == null)
                throw new InvalidOperationException("Gameplay Ability asset row UXML is missing.");

            ConfigureList();
            createButton.clicked += OnCreateClicked;
            duplicateButton.clicked += OnDuplicateClicked;
            deleteButton.clicked += OnDeleteClicked;
            searchField.RegisterValueChangedCallback(OnSearchChanged);
            abilityList.selectionChanged += OnSelectionChanged;
            detailsRoot.RegisterCallback<SerializedPropertyChangeEvent>(OnSerializedPropertyChanged);
            BindAbility(null);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            createButton.clicked -= OnCreateClicked;
            duplicateButton.clicked -= OnDuplicateClicked;
            deleteButton.clicked -= OnDeleteClicked;
            searchField.UnregisterValueChangedCallback(OnSearchChanged);
            abilityList.selectionChanged -= OnSelectionChanged;
            detailsRoot.UnregisterCallback<SerializedPropertyChangeEvent>(OnSerializedPropertyChanged);
            detailsRoot.Unbind();
            subclassFieldsContainer.Clear();
            abilityList.itemsSource = null;
            displayedAbilities.Clear();
            boundAbility = null;
        }
        #endregion

        #region 显示操作
        /// <inheritdoc />
        public void SetCreatableAbilityTypes(IReadOnlyList<Type> types)
        {
            creatableAbilityTypes.Clear();
            for (int i = 0; i < types.Count; i++) creatableAbilityTypes.Add(types[i]);
            createButton.SetEnabled(creatableAbilityTypes.Count > 0);
        }

        /// <inheritdoc />
        public void SetSearch(string search) =>
            searchField.SetValueWithoutNotify(search ?? string.Empty);

        /// <inheritdoc />
        public void RenderAbilities(
            IReadOnlyList<GameplayAbilityData> abilities,
            GameplayAbilityData selected)
        {
            displayedAbilities.Clear();
            for (int i = 0; i < abilities.Count; i++) displayedAbilities.Add(abilities[i]);
            abilityList.RefreshItems();
            suppressSelection = true;
            abilityList.selectedIndex = selected == null ? -1 : displayedAbilities.IndexOf(selected);
            suppressSelection = false;
            duplicateButton.SetEnabled(selected != null);
            deleteButton.SetEnabled(selected != null);
        }

        /// <inheritdoc />
        public void BindAbility(GameplayAbilityData ability)
        {
            detailsRoot.Unbind();
            subclassFieldsContainer.Clear();
            boundAbility = ability;
            bool hasAbility = ability != null;
            detailsRoot.EnableInClassList(HiddenClass, !hasAbility);
            abilityTitle.text = hasAbility ? ability.name : "No Gameplay Ability Selected";
            duplicateButton.SetEnabled(hasAbility);
            deleteButton.SetEnabled(hasAbility);
            if (!hasAbility) return;

            var serializedObject = new SerializedObject(ability);
            BuildSubclassFields(serializedObject);
            detailsRoot.Bind(serializedObject);
            detailsRoot.schedule.Execute(ConfigureDelayedInputs);
        }

        /// <inheritdoc />
        public void RenderValidation(IReadOnlyList<GameplayAbilityValidationIssue> issues)
        {
            validationContainer.Clear();
            if (issues.Count == 0)
            {
                validationContainer.Add(new HelpBox(
                    "Gameplay Ability validation passed.",
                    HelpBoxMessageType.Info));
                return;
            }

            for (int i = 0; i < issues.Count; i++)
            {
                GameplayAbilityValidationIssue issue = issues[i];
                HelpBoxMessageType type = issue.Severity == GameplayAbilityValidationSeverity.Error
                    ? HelpBoxMessageType.Error
                    : HelpBoxMessageType.Info;
                validationContainer.Add(new HelpBox(issue.Message, type));
            }
        }

        /// <inheritdoc />
        public void RenderAbilityValidationStates(
            IReadOnlyDictionary<GameplayAbilityData, GameplayAbilityValidationSeverity> states)
        {
            validationStates = states ?? throw new ArgumentNullException(nameof(states));
            abilityList.RefreshItems();
        }

        /// <inheritdoc />
        public void ShowError(string message) =>
            EditorUtility.DisplayDialog("Gameplay Ability Editor", message, "OK");

        /// <inheritdoc />
        public bool ConfirmDelete(GameplayAbilityData ability) =>
            EditorUtility.DisplayDialog(
                "Delete Gameplay Ability",
                $"Move '{ability.name}' to the recycle bin?",
                "Delete",
                "Cancel");

        /// <inheritdoc />
        public void RestoreRename(GameplayAbilityData ability, string attemptedName)
        {
            pendingRenameAbility = ability;
            pendingRenameText = attemptedName;
            abilityList.RefreshItems();
        }
        #endregion

        #region 事件处理
        // 创建菜单只包含 Service 发现的具体 Data 类型。
        private void OnCreateClicked()
        {
            var menu = new GenericMenu();
            for (int i = 0; i < creatableAbilityTypes.Count; i++)
            {
                Type abilityType = creatableAbilityTypes[i];
                string label = ObjectNames.NicifyVariableName(
                    abilityType.Name.Replace("GameplayAbilityData", string.Empty));
                menu.AddItem(
                    new GUIContent(label),
                    false,
                    () => CreateRequested?.Invoke(abilityType));
            }
            menu.ShowAsContext();
        }

        // 转发复制当前资产意图。
        private void OnDuplicateClicked() => DuplicateRequested?.Invoke();

        // 转发删除当前资产意图。
        private void OnDeleteClicked() => DeleteRequested?.Invoke();

        // 搜索字段仅发送最新文本。
        private void OnSearchChanged(ChangeEvent<string> evt) => SearchChanged?.Invoke(evt.newValue);

        // ListView 选择变化只发送真实 Model 引用。
        private void OnSelectionChanged(IEnumerable<object> selection)
        {
            if (suppressSelection) return;
            foreach (object item in selection)
            {
                AbilitySelected?.Invoke(item as GameplayAbilityData);
                return;
            }
            AbilitySelected?.Invoke(null);
        }

        // 原生序列化写回后通知 Controller 重新校验。
        private void OnSerializedPropertyChanged(SerializedPropertyChangeEvent evt)
        {
            if (boundAbility != null) AbilityChanged?.Invoke();
        }
        #endregion

        #region ListView 行
        // ListView 使用稳定 Model 列表，makeItem 只注册一次动态行回调。
        private void ConfigureList()
        {
            abilityList.itemsSource = displayedAbilities;
            abilityList.selectionType = SelectionType.Single;
            abilityList.fixedItemHeight = 42f;
            abilityList.makeItem = MakeRow;
            abilityList.bindItem = BindRow;
            abilityList.unbindItem = UnbindRow;
        }

        // 克隆独立行 UXML 并封装回调生命周期。
        private VisualElement MakeRow()
        {
            TemplateContainer row = rowTemplate.CloneTree();
            row.userData = new AbilityRowState(row, this);
            return row;
        }

        // 虚拟化绑定只替换当前 Ability 引用和显示状态。
        private void BindRow(VisualElement element, int index)
        {
            var state = (AbilityRowState)element.userData;
            GameplayAbilityData ability = displayedAbilities[index];
            bool hasError = validationStates.TryGetValue(
                ability,
                out GameplayAbilityValidationSeverity severity) &&
                severity == GameplayAbilityValidationSeverity.Error;
            state.Bind(ability, hasError);
            if (!ReferenceEquals(pendingRenameAbility, ability)) return;
            string text = pendingRenameText;
            pendingRenameAbility = null;
            pendingRenameText = null;
            state.BeginRename(text);
        }

        // 行回收时取消输入状态，避免向新绑定资产提交旧值。
        private static void UnbindRow(VisualElement element, int index) =>
            ((AbilityRowState)element.userData).Unbind();

        // 双击名称时同步选择并进入行内重命名。
        private void BeginRowRename(GameplayAbilityData ability, AbilityRowState state)
        {
            int index = displayedAbilities.IndexOf(ability);
            if (index >= 0) abilityList.selectedIndex = index;
            state.BeginRename(ability.name);
        }

        // 提交请求携带明确资产引用。
        private void SubmitRename(GameplayAbilityData ability, string value) =>
            RenameSubmitted?.Invoke(new GameplayAbilityRenameRequest(ability, value));

        // Ping 不改变 ListView 选择。
        private void Ping(GameplayAbilityData ability) => PingRequested?.Invoke(ability);
        #endregion

        #region 详情绑定
        // 动态渲染具体子类新增字段；公共字段由固定 UXML 负责。
        private void BuildSubclassFields(SerializedObject serializedObject)
        {
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (IsCommonProperty(iterator.propertyPath)) continue;
                subclassFieldsContainer.Add(new PropertyField(iterator.Copy()));
            }
        }

        // 排除脚本引用和 GameplayAbilityData 的固定公共字段。
        private static bool IsCommonProperty(string propertyPath) =>
            propertyPath == "m_Script" ||
            propertyPath == "description" ||
            propertyPath == "activationTagQuery" ||
            propertyPath == "costEffect" ||
            propertyPath == "cooldownEffect";

        // 文本和数值字段使用延迟提交，减少输入过程中的重复校验。
        private void ConfigureDelayedInputs()
        {
            detailsRoot.Query<TextField>().ForEach(field => field.isDelayed = true);
            detailsRoot.Query<FloatField>().ForEach(field => field.isDelayed = true);
            detailsRoot.Query<IntegerField>().ForEach(field => field.isDelayed = true);
        }

        // 必需 UXML 元素缺失时立即暴露布局契约错误。
        private T Require<T>(string name) where T : VisualElement
        {
            T element = root.Q<T>(name);
            if (element == null)
                throw new InvalidOperationException(
                    $"Gameplay Ability Editor UXML is missing required element '{name}'.");
            return element;
        }
        #endregion

        #region 嵌套类型
        /// <summary>封装单个虚拟化 GA 行的控件、绑定身份和行内重命名状态。</summary>
        private sealed class AbilityRowState
        {
            private readonly GameplayAbilityEditorView owner;
            private readonly VisualElement visualRoot;
            private readonly Label nameLabel;
            private readonly TextField renameField;
            private readonly Label pathLabel;
            private readonly Button pingButton;
            private GameplayAbilityData ability;
            private bool renaming;
            private bool suppressFocusOut;

            // 构造时注册一次回调，bindItem 只更换 Model 引用。
            internal AbilityRowState(VisualElement root, GameplayAbilityEditorView owner)
            {
                this.owner = owner;
                visualRoot = root.Q<VisualElement>(className: "ga-asset-row") ??
                    throw new InvalidOperationException(
                        "Gameplay Ability row UXML is missing the 'ga-asset-row' root class.");
                nameLabel = root.Q<Label>("NameLabel");
                renameField = root.Q<TextField>("RenameField");
                pathLabel = root.Q<Label>("PathLabel");
                pingButton = root.Q<Button>("PingButton");
                nameLabel.RegisterCallback<PointerDownEvent>(OnNamePointerDown);
                renameField.RegisterCallback<KeyDownEvent>(OnRenameKeyDown);
                renameField.RegisterCallback<FocusOutEvent>(OnRenameFocusOut);
                pingButton.clicked += OnPingClicked;
                pingButton.RegisterCallback<PointerDownEvent>(StopPingPointer);
            }

            // 绑定真实 GA 资产并重置虚拟化残留样式。
            internal void Bind(GameplayAbilityData value, bool hasValidationError)
            {
                ability = value;
                nameLabel.text = value.name;
                pathLabel.text = AssetDatabase.GetAssetPath(value);
                visualRoot.EnableInClassList(ValidationErrorClass, hasValidationError);
                if (!renaming) SetRenameVisible(false);
            }

            // 回收时取消编辑和错误背景。
            internal void Unbind()
            {
                suppressFocusOut = true;
                SetRenameVisible(false);
                suppressFocusOut = false;
                visualRoot.EnableInClassList(ValidationErrorClass, false);
                ability = null;
            }

            // 下一次 Panel 更新聚焦并全选行内输入。
            internal void BeginRename(string value)
            {
                if (ability == null) return;
                renameField.SetValueWithoutNotify(value ?? ability.name);
                SetRenameVisible(true);
                renameField.schedule.Execute(() =>
                {
                    if (!renaming || renameField.panel == null) return;
                    renameField.Focus();
                    renameField.SelectAll();
                });
            }

            // 仅名称 Label 双击进入重命名。
            private void OnNamePointerDown(PointerDownEvent evt)
            {
                if (evt.button != 0 || evt.clickCount != 2 || ability == null) return;
                owner.BeginRowRename(ability, this);
                evt.StopImmediatePropagation();
            }

            // Enter 提交，Escape 取消，并抑制随后 FocusOut 重复提交。
            private void OnRenameKeyDown(KeyDownEvent evt)
            {
                if (!renaming) return;
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    CommitRename();
                    evt.StopImmediatePropagation();
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    CancelRename();
                    evt.StopImmediatePropagation();
                }
            }

            // 正常失焦提交一次。
            private void OnRenameFocusOut(FocusOutEvent evt)
            {
                if (renaming && !suppressFocusOut) CommitRename();
            }

            // 先退出编辑显示，再通知 Controller 刷新资产。
            private void CommitRename()
            {
                GameplayAbilityData target = ability;
                string value = renameField.value;
                suppressFocusOut = true;
                SetRenameVisible(false);
                suppressFocusOut = false;
                if (target != null) owner.SubmitRename(target, value);
            }

            // 取消只恢复 Label，不写入资产。
            private void CancelRename()
            {
                suppressFocusOut = true;
                SetRenameVisible(false);
                suppressFocusOut = false;
            }

            // 切换名称 Label 与输入框显示。
            private void SetRenameVisible(bool visible)
            {
                renaming = visible;
                nameLabel.EnableInClassList(HiddenClass, visible);
                renameField.EnableInClassList(HiddenClass, !visible);
            }

            // Ping 当前绑定资产。
            private void OnPingClicked()
            {
                if (ability != null) owner.Ping(ability);
            }

            // 阻止 Ping PointerDown 冒泡到列表选择。
            private static void StopPingPointer(PointerDownEvent evt) => evt.StopPropagation();
        }
        #endregion
    }
}
#endif
