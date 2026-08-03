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
    /// <summary>使用 UI Toolkit 渲染 GA 资产列表、原生 SerializedObject 详情和 Validation。</summary>
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
        private readonly Label abilityTitle;
        private readonly VisualElement validationContainer;
        private readonly VisualTreeAsset rowTemplate;
        private readonly List<GameplayAbilityData> displayedAbilities = new();
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
        public event Action CreateRequested;
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
        /// <summary>查询并配置已由 GameplayAbilityWindow 实例化的全部控件。</summary>
        public GameplayAbilityEditorView(VisualElement root)
        {
            this.root = root ?? throw new ArgumentNullException(nameof(root));
            createButton = Require<Button>("CreateAbilityButton");
            duplicateButton = Require<Button>("DuplicateAbilityButton");
            deleteButton = Require<Button>("DeleteAbilityButton");
            searchField = Require<ToolbarSearchField>("SearchField");
            abilityList = Require<ListView>("AbilityList");
            detailsRoot = Require<VisualElement>("AbilityDetailsRoot");
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
            abilityList.itemsSource = null;
            displayedAbilities.Clear();
            boundAbility = null;
        }
        #endregion

        #region 显示操作
        /// <inheritdoc />
        public void SetSearch(string search) => searchField.SetValueWithoutNotify(search ?? string.Empty);

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
            boundAbility = ability;
            bool hasAbility = ability != null;
            detailsRoot.EnableInClassList(HiddenClass, !hasAbility);
            abilityTitle.text = hasAbility ? ability.name : "No Gameplay Ability Selected";
            duplicateButton.SetEnabled(hasAbility);
            deleteButton.SetEnabled(hasAbility);
            if (!hasAbility) return;
            detailsRoot.Bind(new SerializedObject(ability));
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
        // 将工具栏按钮转换为不携带 UI 控件的用户意图。
        private void OnCreateClicked() => CreateRequested?.Invoke();
        // 转发复制当前资产意图。
        private void OnDuplicateClicked() => DuplicateRequested?.Invoke();
        // 转发删除当前资产意图。
        private void OnDeleteClicked() => DeleteRequested?.Invoke();
        // 搜索字段只发送最新字符串，不自行扫描资产。
        private void OnSearchChanged(ChangeEvent<string> evt) => SearchChanged?.Invoke(evt.newValue);

        // ListView 选择变化只发送当前 Model 引用；刷新选择期间不重复通知 Controller。
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

        // 原生绑定写回后通知 Controller 重新校验，不重复修改 SerializedProperty。
        private void OnSerializedPropertyChanged(SerializedPropertyChangeEvent evt)
        {
            if (boundAbility != null) AbilityChanged?.Invoke();
        }
        #endregion

        #region ListView 行
        // ListView 使用稳定 Model 列表，makeItem 仅构造并注册一次动态行回调。
        private void ConfigureList()
        {
            abilityList.itemsSource = displayedAbilities;
            abilityList.selectionType = SelectionType.Single;
            abilityList.fixedItemHeight = 42f;
            abilityList.makeItem = MakeRow;
            abilityList.bindItem = BindRow;
            abilityList.unbindItem = UnbindRow;
        }

        // 克隆独立行 UXML 并把回调生命周期封装到 RowState。
        private VisualElement MakeRow()
        {
            TemplateContainer row = rowTemplate.CloneTree();
            row.userData = new AbilityRowState(row, this);
            return row;
        }

        // 虚拟化绑定只替换当前 Ability 引用和显示文本。
        private void BindRow(VisualElement element, int index)
        {
            var state = (AbilityRowState)element.userData;
            GameplayAbilityData ability = displayedAbilities[index];
            bool hasValidationError =
                validationStates.TryGetValue(
                    ability,
                    out GameplayAbilityValidationSeverity severity) &&
                severity == GameplayAbilityValidationSeverity.Error;
            state.Bind(ability, hasValidationError);
            if (!ReferenceEquals(pendingRenameAbility, ability)) return;
            string text = pendingRenameText;
            pendingRenameAbility = null;
            pendingRenameText = null;
            state.BeginRename(text);
        }

        // 行回收时取消输入状态，避免旧资产在新行上提交。
        private static void UnbindRow(VisualElement element, int index) =>
            ((AbilityRowState)element.userData).Unbind();

        // 行双击时同步选择并进入行内重命名。
        private void BeginRowRename(GameplayAbilityData ability, AbilityRowState state)
        {
            int index = displayedAbilities.IndexOf(ability);
            if (index >= 0) abilityList.selectedIndex = index;
            state.BeginRename(ability.name);
        }

        // 行提交使用明确资产引用，避免虚拟化或排序后重命名错误目标。
        private void SubmitRename(GameplayAbilityData ability, string value) =>
            RenameSubmitted?.Invoke(new GameplayAbilityRenameRequest(ability, value));
        // Ping 只发送目标资产，不修改 ListView 选择。
        private void Ping(GameplayAbilityData ability) => PingRequested?.Invoke(ability);
        #endregion

        #region 内部辅助
        // 所有可编辑文本与数值字段采用延迟提交，避免每次按键触发资产刷新和校验。
        private void ConfigureDelayedInputs()
        {
            detailsRoot.Query<TextField>().ForEach(field => field.isDelayed = true);
            detailsRoot.Query<FloatField>().ForEach(field => field.isDelayed = true);
            detailsRoot.Query<IntegerField>().ForEach(field => field.isDelayed = true);
        }

        // 查询必需 UXML 元素；缺失时立即暴露布局和代码契约不一致。
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
        /// <summary>封装单个虚拟化 GA 资产行的控件、绑定身份和行内重命名状态。</summary>
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

            // 行构造时注册一次回调，后续 bindItem 只更换 Model 引用。
            internal AbilityRowState(VisualElement root, GameplayAbilityEditorView owner)
            {
                this.owner = owner;
                visualRoot = root.Q<VisualElement>(className: "ga-asset-row") ??
                    throw new InvalidOperationException(
                        "Gameplay Ability asset row UXML is missing the 'ga-asset-row' root class.");
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

            // 把当前虚拟化行绑定到真实 GA 资产，不复制业务字段。
            internal void Bind(GameplayAbilityData value, bool hasValidationError)
            {
                ability = value;
                nameLabel.text = value.name;
                pathLabel.text = AssetDatabase.GetAssetPath(value);
                visualRoot.EnableInClassList(ValidationErrorClass, hasValidationError);
                if (!renaming) SetRenameVisible(false);
            }

            // 行回收时取消重命名，防止 FocusOut 提交已经切换的资产。
            internal void Unbind()
            {
                suppressFocusOut = true;
                SetRenameVisible(false);
                suppressFocusOut = false;
                visualRoot.EnableInClassList(ValidationErrorClass, false);
                ability = null;
            }

            // 显示延迟输入框，并在下一次 Panel 更新后聚焦和全选。
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

            // 仅名称 Label 的双击进入重命名，路径和 Ping 保持原行为。
            private void OnNamePointerDown(PointerDownEvent evt)
            {
                if (evt.button != 0 || evt.clickCount != 2 || ability == null) return;
                owner.BeginRowRename(ability, this);
                evt.StopImmediatePropagation();
            }

            // Enter 提交，Escape 取消；两者都抑制随后的 FocusOut 重复提交。
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

            // 正常失焦提交一次；显式 Enter/Escape 和虚拟化回收会抑制该回调。
            private void OnRenameFocusOut(FocusOutEvent evt)
            {
                if (renaming && !suppressFocusOut) CommitRename();
            }

            // 先退出编辑显示再发送请求，避免资产刷新时旧 TextField 仍持有焦点。
            private void CommitRename()
            {
                GameplayAbilityData target = ability;
                string value = renameField.value;
                suppressFocusOut = true;
                SetRenameVisible(false);
                suppressFocusOut = false;
                if (target != null) owner.SubmitRename(target, value);
            }

            // 取消时恢复 Label，不向 Controller 写入任何资产状态。
            private void CancelRename()
            {
                suppressFocusOut = true;
                SetRenameVisible(false);
                suppressFocusOut = false;
            }

            // 切换名称 Label 与输入框的 USS 显隐状态。
            private void SetRenameVisible(bool visible)
            {
                renaming = visible;
                nameLabel.EnableInClassList(HiddenClass, visible);
                renameField.EnableInClassList(HiddenClass, !visible);
            }

            // Ping 按钮直接发送当前绑定资产。
            private void OnPingClicked()
            {
                if (ability != null) owner.Ping(ability);
            }

            // 阻止 Ping PointerDown 冒泡到 ListView 行选择逻辑。
            private static void StopPingPointer(PointerDownEvent evt) => evt.StopPropagation();
        }
        #endregion
    }
}
#endif
