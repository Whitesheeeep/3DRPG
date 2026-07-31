#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.GAS.GameplayEffect;

namespace WS_Modules.GAS.Editor
{
    /// <summary>使用 UI Toolkit 实现 GE 资产列表、原生绑定、Modifier 编辑与校验显示。</summary>
    public sealed class GameplayEffectEditorView : IGameplayEffectEditorView
    {
        #region 常量与字段

        private const string EffectRowUxmlPath =
            "Assets/GAS_Light/GameEffectSystem/Editor/Style/GameplayEffectAssetRow.uxml";
        private const string ModifierRowUxmlPath =
            "Assets/GAS_Light/GameEffectSystem/Editor/Style/GameplayEffectModifierRow.uxml";
        private const float ModifierDragThresholdSquared = 16f;

        private readonly VisualElement root;
        private readonly VisualTreeAsset effectRowAsset;
        private readonly VisualTreeAsset modifierRowAsset;
        private readonly ToolbarSearchField searchField;
        private readonly Button createEffectButton;
        private readonly Button duplicateEffectButton;
        private readonly Button deleteEffectButton;
        private readonly ListView effectList;
        private readonly VisualElement detailsRoot;
        private readonly Label emptySelectionLabel;
        private readonly Label effectTitle;
        private readonly VisualElement durationField;
        private readonly VisualElement periodField;
        private readonly VisualElement executePeriodField;
        private readonly VisualElement grantedTagsField;
        private readonly VisualElement stackingGroup;
        private readonly VisualElement maxStackCountField;
        private readonly VisualElement denyOverflowField;
        private readonly VisualElement durationPolicyField;
        private readonly VisualElement periodPolicyField;
        private readonly VisualElement expirationPolicyField;
        private readonly Button addModifierButton;
        private readonly Button removeModifierButton;
        private readonly ListView modifierList;
        private readonly VisualElement modifierPropertyHost;
        private readonly Label modifierEmptyLabel;
        private PropertyField modifierPropertyField;
        private readonly Button validateButton;
        private readonly VisualElement validationContainer;

        private readonly List<GameplayEffectData> renderedEffects = new();
        private readonly List<GameplayEffectModifier> renderedModifiers = new();
        private readonly List<Type> availableModifierTypes = new();
        private readonly List<Button> effectPingButtons = new();

        private SerializedObject boundObject;
        private GameplayEffectData currentEffect;
        private GameplayAttributeRegistry attributeRegistry;
        private Vector2 modifierPointerDownPosition;
        private string attributeRegistryUnavailableReason = string.Empty;
        private int pendingModifierBindingIndex = -1;
        private bool binding;
        private bool disposed;
        private bool modifierBindingRestoreScheduled;
        private bool modifierInteractionFrozen;
        private bool modifierPointerTracking;

        #endregion

        #region 事件

        /// <inheritdoc />
        public event Action<string> SearchChanged;
        /// <inheritdoc />
        public event Action<GameplayEffectData> EffectSelectionChanged;
        /// <inheritdoc />
        public event Action<GameplayEffectData> PingEffectRequested;
        /// <inheritdoc />
        public event Action<string> CreateEffectRequested;
        /// <inheritdoc />
        public event Action DuplicateEffectRequested;
        /// <inheritdoc />
        public event Action DeleteEffectRequested;
        /// <inheritdoc />
        public event Action EffectSerializedChanged;
        /// <inheritdoc />
        public event Action<int> ModifierSelectionChanged;
        /// <inheritdoc />
        public event Action<Type> AddModifierRequested;
        /// <inheritdoc />
        public event Action RemoveModifierRequested;
        /// <inheritdoc />
        public event Action<GameplayEffectModifierMoveRequest> MoveModifierRequested;
        /// <inheritdoc />
        public event Action ValidateRequested;

        #endregion

        #region 生命周期

        /// <summary>查询必需 UXML 控件、配置两个 ListView 并注册 UI 回调。</summary>
        /// <param name="root">已实例化 GE UXML 的页面根元素。</param>
        /// <exception cref="ArgumentNullException">root 为 null。</exception>
        /// <exception cref="InvalidOperationException">UXML 或行模板缺失必需元素。</exception>
        public GameplayEffectEditorView(VisualElement root)
        {
            this.root = root ?? throw new ArgumentNullException(nameof(root));
            effectRowAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(EffectRowUxmlPath);
            modifierRowAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ModifierRowUxmlPath);
            if (effectRowAsset == null || modifierRowAsset == null)
                throw new InvalidOperationException("Gameplay Effect Editor 行 UXML 资源缺失。");

            searchField = Require<ToolbarSearchField>("SearchField");
            createEffectButton = Require<Button>("CreateEffectButton");
            duplicateEffectButton = Require<Button>("DuplicateEffectButton");
            deleteEffectButton = Require<Button>("DeleteEffectButton");
            effectList = Require<ListView>("EffectList");
            detailsRoot = Require<VisualElement>("DetailsRoot");
            emptySelectionLabel = Require<Label>("EmptySelectionLabel");
            effectTitle = Require<Label>("EffectTitle");
            durationField = Require<VisualElement>("DurationField");
            periodField = Require<VisualElement>("PeriodField");
            executePeriodField = Require<VisualElement>("ExecutePeriodField");
            grantedTagsField = Require<VisualElement>("GrantedTagsField");
            stackingGroup = Require<VisualElement>("StackingGroup");
            maxStackCountField = Require<VisualElement>("MaxStackCountField");
            denyOverflowField = Require<VisualElement>("DenyOverflowField");
            durationPolicyField = Require<VisualElement>("DurationPolicyField");
            periodPolicyField = Require<VisualElement>("PeriodPolicyField");
            expirationPolicyField = Require<VisualElement>("ExpirationPolicyField");
            addModifierButton = Require<Button>("AddModifierButton");
            removeModifierButton = Require<Button>("RemoveModifierButton");
            modifierList = Require<ListView>("ModifierList");
            modifierPropertyHost = Require<VisualElement>("ModifierPropertyHost");
            modifierEmptyLabel = Require<Label>("ModifierEmptyLabel");
            validateButton = Require<Button>("ValidateButton");
            validationContainer = Require<VisualElement>("ValidationContainer");

            ConfigureLists();
            RegisterCallbacks();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            CancelModifierBindingRestore();
            UnregisterCallbacks();
            UnregisterEffectRowCallbacks();
            UnbindCurrentEffect();
            effectList.itemsSource = null;
            modifierList.itemsSource = null;
        }

        #endregion

        #region 状态与渲染

        /// <inheritdoc />
        public void SetSearch(string search) =>
            searchField.SetValueWithoutNotify(search ?? string.Empty);

        /// <inheritdoc />
        public void RenderEffects(
            IReadOnlyList<GameplayEffectData> effects,
            GameplayEffectData selected)
        {
            renderedEffects.Clear();
            if (effects != null) renderedEffects.AddRange(effects);
            effectList.RefreshItems();
            int index = renderedEffects.IndexOf(selected);
            effectList.SetSelectionWithoutNotify(index < 0 ? Array.Empty<int>() : new[] { index });
        }

        /// <inheritdoc />
        public void SetAttributeRegistry(
            GameplayAttributeRegistry registry,
            string unavailableReason)
        {
            string reason = unavailableReason ?? string.Empty;
            bool changed = attributeRegistry != registry ||
                           attributeRegistryUnavailableReason != reason;
            attributeRegistry = registry;
            attributeRegistryUnavailableReason = reason;
            if (changed) modifierList.RefreshItems();
        }

        /// <inheritdoc />
        public void BindEffect(GameplayEffectData effect)
        {
            ResetModifierInteraction();
            binding = true;
            try
            {
                UnbindCurrentEffect();
                currentEffect = effect;
                bool hasEffect = currentEffect != null;
                detailsRoot.style.display = hasEffect ? DisplayStyle.Flex : DisplayStyle.None;
                emptySelectionLabel.style.display = hasEffect ? DisplayStyle.None : DisplayStyle.Flex;
                duplicateEffectButton.SetEnabled(hasEffect);
                deleteEffectButton.SetEnabled(hasEffect);
                addModifierButton.SetEnabled(hasEffect && availableModifierTypes.Count > 0);
                effectTitle.text = hasEffect ? currentEffect.name : string.Empty;
                SetModifierDetailsVisible(false);
                if (!hasEffect) return;

                boundObject = new SerializedObject(currentEffect);
                detailsRoot.Bind(boundObject);
                detailsRoot.schedule.Execute(ConfigureDelayedInputs);
            }
            finally
            {
                binding = false;
            }
        }

        /// <inheritdoc />
        public void BindModifier(int selectedModifierIndex)
        {
            if (modifierInteractionFrozen)
            {
                if (selectedModifierIndex >= 0)
                    pendingModifierBindingIndex = selectedModifierIndex;
                return;
            }

            binding = true;
            try
            {
                ClearModifierBinding();
                bool hasModifier = currentEffect != null &&
                                   boundObject != null &&
                                   selectedModifierIndex >= 0 &&
                                   selectedModifierIndex < currentEffect.Modifiers.Count;
                SetModifierDetailsVisible(hasModifier);
                if (!hasModifier) return;

                modifierPropertyField = new PropertyField
                {
                    name = "ModifierPropertyField",
                    bindingPath = $"modifiers.Array.data[{selectedModifierIndex}]"
                };
                modifierPropertyHost.Add(modifierPropertyField);
                modifierPropertyField.Bind(boundObject);
                modifierPropertyField.schedule.Execute(ConfigureDelayedInputs);
            }
            finally
            {
                binding = false;
            }
        }

        /// <inheritdoc />
        public void RenderModifiers(
            IReadOnlyList<GameplayEffectModifier> modifiers,
            int selectedIndex,
            IReadOnlyList<Type> availableTypes)
        {
            renderedModifiers.Clear();
            if (modifiers != null) renderedModifiers.AddRange(modifiers);
            availableModifierTypes.Clear();
            if (availableTypes != null) availableModifierTypes.AddRange(availableTypes);
            modifierList.RefreshItems();
            modifierList.SetSelectionWithoutNotify(
                selectedIndex < 0 ? Array.Empty<int>() : new[] { selectedIndex });
            addModifierButton.SetEnabled(currentEffect != null && availableModifierTypes.Count > 0);
            removeModifierButton.SetEnabled(
                currentEffect != null && selectedIndex >= 0 && selectedIndex < renderedModifiers.Count);
        }

        /// <inheritdoc />
        public void RefreshPolicyVisibility(GameplayEffectData effect)
        {
            bool hasEffect = effect != null;
            bool instant = hasEffect && effect.DurationType == E_GameEffectDurationType.Instant;
            bool duration = hasEffect && effect.DurationType == E_GameEffectDurationType.Duration;
            bool periodic = hasEffect && effect.IsPeriodic;
            bool stacking = hasEffect && !instant &&
                            effect.StackingType != E_GameEffectStackingType.None;

            SetVisible(durationField, duration);
            SetVisible(periodField, hasEffect && !instant);
            SetVisible(executePeriodField, hasEffect && !instant && periodic);
            SetVisible(grantedTagsField, hasEffect && !instant);
            SetVisible(stackingGroup, hasEffect && !instant);
            SetVisible(maxStackCountField, stacking);
            SetVisible(denyOverflowField, stacking);
            SetVisible(durationPolicyField, stacking && duration);
            SetVisible(periodPolicyField, stacking && periodic);
            SetVisible(expirationPolicyField, stacking && duration);
        }

        /// <inheritdoc />
        public void RenderValidation(IReadOnlyList<GameplayEffectValidationIssue> issues)
        {
            validationContainer.Clear();
            if (currentEffect == null) return;
            if (issues == null || issues.Count == 0)
            {
                validationContainer.Add(new HelpBox(
                    "Gameplay Effect validation passed.",
                    HelpBoxMessageType.Info));
                return;
            }

            for (int i = 0; i < issues.Count; i++)
            {
                GameplayEffectValidationIssue issue = issues[i];
                validationContainer.Add(new HelpBox(
                    issue.Message,
                    ToHelpBoxType(issue.Severity)));
            }
        }

        /// <inheritdoc />
        public bool Confirm(string title, string message) =>
            EditorUtility.DisplayDialog(title, message, "Confirm", "Cancel");

        /// <inheritdoc />
        public void ShowError(string title, string message) =>
            EditorUtility.DisplayDialog(title, message, "OK");

        #endregion

        #region ListView 配置与绑定

        // 两个 ListView 只保存 Model 引用；Modifier 的真实排序由 Service 写回资产。
        private void ConfigureLists()
        {
            effectList.selectionType = SelectionType.Single;
            effectList.fixedItemHeight = 38f;
            effectList.makeItem = CreateEffectRow;
            effectList.bindItem = BindEffectRow;
            effectList.itemsSource = renderedEffects;

            modifierList.selectionType = SelectionType.Single;
            modifierList.fixedItemHeight = 28f;
            modifierList.reorderable = true;
            modifierList.reorderMode = ListViewReorderMode.Simple;
            modifierList.makeItem = () => modifierRowAsset.Instantiate();
            modifierList.bindItem = BindModifierRow;
            modifierList.itemsSource = renderedModifiers;
        }

        // 创建虚拟化资产行并只注册一次 Ping 回调，后续 bindItem 仅替换对应 Model。
        private VisualElement CreateEffectRow()
        {
            VisualElement row = effectRowAsset.Instantiate();
            Button pingButton = row.Q<Button>("PingButton");
            if (pingButton == null)
                throw new InvalidOperationException(
                    "Gameplay Effect asset row UXML is missing required element 'PingButton'.");

            pingButton.RegisterCallback<PointerDownEvent>(OnEffectPingPointerDown);
            pingButton.RegisterCallback<ClickEvent>(OnEffectPingClicked);
            effectPingButtons.Add(pingButton);
            return row;
        }

        // 资产行显示名称和路径，同名资产仍可明确区分。
        private void BindEffectRow(VisualElement element, int index)
        {
            GameplayEffectData item = renderedEffects[index];
            element.Q<Label>("NameLabel").text = item.name;
            element.Q<Label>("PathLabel").text = AssetDatabase.GetAssetPath(item);
            element.Q<Button>("PingButton").userData = item;
        }

        // Modifier 行只格式化现有 Model 引用，不创建字段副本。
        private void BindModifierRow(VisualElement element, int index)
        {
            GameplayEffectModifier modifier = renderedModifiers[index];
            element.Q<Label>("IndexLabel").text = index.ToString();
            element.Q<Label>("NameLabel").text = modifier == null
                ? "Missing Modifier Type"
                : BuildModifierTypeName(modifier.GetType());
            Label attributeLabel = element.Q<Label>("AttributeLabel");
            BindModifierAttribute(attributeLabel, modifier);
        }

        #endregion

        #region UI 事件

        // 搜索框直接转发用户输入。
        private void OnSearchChanged(ChangeEvent<string> evt) => SearchChanged?.Invoke(evt.newValue);

        // 创建路径对话属于 View 交互，取消时不发送意图。
        private void OnCreateEffectClicked()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Gameplay Effect",
                "GameplayEffectData",
                "asset",
                "Choose a project path for the GameplayEffectData asset.");
            if (!string.IsNullOrEmpty(path)) CreateEffectRequested?.Invoke(path);
        }

        // 复制意图不携带 UI 状态。
        private void OnDuplicateEffectClicked() => DuplicateEffectRequested?.Invoke();

        // 删除确认由 Controller 通过 View.Confirm 统一执行。
        private void OnDeleteEffectClicked() => DeleteEffectRequested?.Invoke();

        // 将 ListView 选择转换为稳定 Model 引用。
        private void OnEffectSelectionChanged(IEnumerable<object> selection)
        {
            EffectSelectionChanged?.Invoke(selection.OfType<GameplayEffectData>().FirstOrDefault());
        }

        // Ping 按钮优先消费按下事件，避免 ListView 将定位操作解释为资产选择。
        private void OnEffectPingPointerDown(PointerDownEvent evt) => evt.StopPropagation();

        // 从虚拟化按钮的当前 userData 读取 Model，确保滚动复用后仍定位正确资产。
        private void OnEffectPingClicked(ClickEvent evt)
        {
            evt.StopPropagation();
            if (evt.currentTarget is Button { userData: GameplayEffectData effect })
                PingEffectRequested?.Invoke(effect);
        }

        // 详情中任意 SerializedProperty 提交后通知 Controller 刷新派生状态。
        private void OnSerializedPropertyChanged(SerializedPropertyChangeEvent evt)
        {
            if (evt.changedProperty.propertyPath.StartsWith(
                    "modifiers.Array.data[",
                    StringComparison.Ordinal) &&
                modifierList.selectedIndex >= 0 &&
                modifierList.selectedIndex < renderedModifiers.Count)
                modifierList.RefreshItem(modifierList.selectedIndex);
            if (!binding && !disposed) EffectSerializedChanged?.Invoke();
        }

        // Modifier 列表只转发选中索引；冻结期间 View 会延后真正的详情绑定。
        private void OnModifierSelectionChanged(IEnumerable<object> selection) =>
            ModifierSelectionChanged?.Invoke(modifierList.selectedIndex);

        // 左键按下只记录候选拖拽；普通点击不会冻结详情。
        private void OnModifierPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0) return;
            modifierPointerTracking = true;
            modifierPointerDownPosition = evt.position;
        }

        // 超过拖拽阈值后立即移除详情，确保旧 IMGUIContainer 早于数组移动退出 Panel。
        private void OnModifierPointerMove(PointerMoveEvent evt)
        {
            if (!modifierPointerTracking || modifierInteractionFrozen ||
                (evt.pressedButtons & 1) == 0)
                return;

            Vector2 delta = (Vector2)evt.position - modifierPointerDownPosition;
            if (delta.sqrMagnitude < ModifierDragThresholdSquared) return;
            FreezeModifierInteraction();
        }

        // PointerUp 后跨两次 Editor 更新恢复，保证延迟的数组移动已经提交完成。
        private void OnModifierPointerUp(PointerUpEvent evt)
        {
            if (evt.button != 0) return;
            modifierPointerTracking = false;
            if (modifierInteractionFrozen) ScheduleModifierBindingRestore();
        }

        // Pointer 被系统取消时使用相同恢复路径，避免详情长期保持冻结。
        private void OnModifierPointerCancel(PointerCancelEvent evt)
        {
            modifierPointerTracking = false;
            if (modifierInteractionFrozen) ScheduleModifierBindingRestore();
        }

        // Drop 先让 Controller 排队数组移动，再排队详情恢复，保证两者执行顺序稳定。
        private void OnModifierIndexChanged(int fromIndex, int toIndex)
        {
            MoveModifierRequested?.Invoke(new GameplayEffectModifierMoveRequest(fromIndex, toIndex));
            if (modifierInteractionFrozen) ScheduleModifierBindingRestore();
        }

        // Add 菜单仅显示 Service 已确认可实例化的派生类型。
        private void OnAddModifierClicked()
        {
            var menu = new GenericMenu();
            for (int i = 0; i < availableModifierTypes.Count; i++)
            {
                Type type = availableModifierTypes[i];
                menu.AddItem(
                    new GUIContent(BuildModifierTypeName(type)),
                    false,
                    () => AddModifierRequested?.Invoke(type));
            }

            if (availableModifierTypes.Count == 0)
                menu.AddDisabledItem(new GUIContent("No serializable Modifier types"));
            menu.ShowAsContext();
        }

        // 删除意图由 Controller 使用当前索引执行。
        private void OnRemoveModifierClicked() => RemoveModifierRequested?.Invoke();

        // 手动刷新仅请求重新校验。
        private void OnValidateClicked() => ValidateRequested?.Invoke();

        #endregion

        #region 绑定与显示辅助

        // 切换资产前解除旧 SerializedObject，避免两个资产同时被绑定。
        private void UnbindCurrentEffect()
        {
            ClearModifierBinding();
            detailsRoot.Unbind();
            boundObject = null;
            currentEffect = null;
        }

        // 拖拽期间冻结详情，但保留最终应恢复的 Modifier 索引。
        private void FreezeModifierInteraction()
        {
            modifierInteractionFrozen = true;
            pendingModifierBindingIndex = modifierList.selectedIndex;
            ClearModifierBinding();
            modifierEmptyLabel.text = "Modifier details paused while reordering.";
            SetModifierDetailsVisible(false);
        }

        // PointerUp 的第一阶段只排队下一次恢复，使数组提交始终先于 PropertyField 重建。
        private void ScheduleModifierBindingRestore()
        {
            if (modifierBindingRestoreScheduled) return;
            modifierBindingRestoreScheduled = true;
            EditorApplication.delayCall += QueueModifierBindingRestore;
        }

        // 再跨一次 Editor 更新，避开 Unity 2022.3 当前 Panel 中残留的 IMGUI 绘制回调。
        private void QueueModifierBindingRestore()
        {
            EditorApplication.delayCall += RestoreModifierBinding;
        }

        // 使用最终选择索引创建全新的 PropertyField，不复用拖拽前 SerializedProperty。
        private void RestoreModifierBinding()
        {
            modifierBindingRestoreScheduled = false;
            if (disposed) return;
            modifierInteractionFrozen = false;
            modifierEmptyLabel.text = "Select a Modifier.";
            int index = pendingModifierBindingIndex;
            pendingModifierBindingIndex = -1;
            BindModifier(index);
        }

        // 切换 GE 或释放 View 时取消所有延迟恢复，并清空纯交互状态。
        private void ResetModifierInteraction()
        {
            CancelModifierBindingRestore();
            modifierPointerTracking = false;
            modifierInteractionFrozen = false;
            pendingModifierBindingIndex = -1;
            modifierEmptyLabel.text = "Select a Modifier.";
        }

        // 对称移除两阶段 delayCall；无论当前处于哪一阶段都不会残留回调。
        private void CancelModifierBindingRestore()
        {
            EditorApplication.delayCall -= QueueModifierBindingRestore;
            EditorApplication.delayCall -= RestoreModifierBinding;
            modifierBindingRestoreScheduled = false;
        }

        // 完整移除动态 PropertyField，使其内部 IMGUIContainer 不再持有旧 SerializedProperty。
        private void ClearModifierBinding()
        {
            if (modifierPropertyField != null)
            {
                modifierPropertyField.Unbind();
                modifierPropertyField.RemoveFromHierarchy();
                modifierPropertyField = null;
            }

            modifierPropertyHost.Clear();
        }

        // 使用当前 Registry 即时解析作者名称；名称只用于显示，不写入 GE 资产。
        private void BindModifierAttribute(
            Label label,
            GameplayEffectModifier modifier)
        {
            if (modifier == null)
            {
                label.text = "Invalid";
                label.tooltip = "Modifier type is missing.";
                return;
            }

            int attributeId = modifier.Attribute.Id;
            if (attributeRegistry == null)
            {
                label.text = $"Attribute {attributeId}";
                label.tooltip = string.IsNullOrEmpty(attributeRegistryUnavailableReason)
                    ? $"AttributeId: {attributeId}"
                    : $"{attributeRegistryUnavailableReason}\nAttributeId: {attributeId}";
                return;
            }

            if (attributeRegistry.TryGetNodeById(
                    attributeId,
                    out GameplayAttributeEditorNode node))
            {
                label.text = node.Name;
                label.tooltip = $"AttributeId: {attributeId}";
                return;
            }

            label.text = $"Invalid AttributeId ({attributeId})";
            label.tooltip = $"AttributeId {attributeId} 未在当前 Registry 中烘焙。";
        }

        // 原生 PropertyField 实例化子控件后将文本和数字输入统一设为延迟提交。
        private void ConfigureDelayedInputs()
        {
            if (disposed || currentEffect == null) return;
            detailsRoot.Query<TextField>().ForEach(field => field.isDelayed = true);
            detailsRoot.Query<FloatField>().ForEach(field => field.isDelayed = true);
            detailsRoot.Query<IntegerField>().ForEach(field => field.isDelayed = true);
        }

        // Modifier 详情和空提示始终互斥。
        private void SetModifierDetailsVisible(bool visible)
        {
            modifierPropertyHost.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            modifierEmptyLabel.style.display = visible ? DisplayStyle.None : DisplayStyle.Flex;
            removeModifierButton.SetEnabled(visible);
        }

        // 统一切换策略字段的 DisplayStyle。
        private static void SetVisible(VisualElement element, bool visible) =>
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        // 去掉通用后缀后生成稳定可读的 Add 菜单和行名称。
        private static string BuildModifierTypeName(Type type)
        {
            const string suffix = "GameplayEffectModifier";
            string name = type.Name.EndsWith(suffix, StringComparison.Ordinal)
                ? type.Name.Substring(0, type.Name.Length - suffix.Length)
                : type.Name;
            return ObjectNames.NicifyVariableName(name);
        }

        // 将领域严重程度映射到 Unity HelpBox 样式。
        private static HelpBoxMessageType ToHelpBoxType(GameplayEffectValidationSeverity severity)
        {
            return severity switch
            {
                GameplayEffectValidationSeverity.Error => HelpBoxMessageType.Error,
                GameplayEffectValidationSeverity.Warning => HelpBoxMessageType.Warning,
                _ => HelpBoxMessageType.Info
            };
        }

        // 查询必需 UXML 元素，缺失时立即暴露资源与代码契约不一致。
        private T Require<T>(string name) where T : VisualElement
        {
            T element = root.Q<T>(name);
            if (element == null)
                throw new InvalidOperationException(
                    $"Gameplay Effect Editor UXML is missing required element '{name}'.");
            return element;
        }

        #endregion

        #region 回调注册

        // 注册全部 UI 回调，由 Dispose 对称解除。
        private void RegisterCallbacks()
        {
            searchField.RegisterValueChangedCallback(OnSearchChanged);
            createEffectButton.clicked += OnCreateEffectClicked;
            duplicateEffectButton.clicked += OnDuplicateEffectClicked;
            deleteEffectButton.clicked += OnDeleteEffectClicked;
            effectList.selectionChanged += OnEffectSelectionChanged;
            detailsRoot.RegisterCallback<SerializedPropertyChangeEvent>(OnSerializedPropertyChanged);
            modifierList.selectionChanged += OnModifierSelectionChanged;
            modifierList.itemIndexChanged += OnModifierIndexChanged;
            modifierList.RegisterCallback<PointerDownEvent>(
                OnModifierPointerDown,
                TrickleDown.TrickleDown);
            modifierList.RegisterCallback<PointerMoveEvent>(
                OnModifierPointerMove,
                TrickleDown.TrickleDown);
            modifierList.RegisterCallback<PointerUpEvent>(
                OnModifierPointerUp,
                TrickleDown.TrickleDown);
            modifierList.RegisterCallback<PointerCancelEvent>(
                OnModifierPointerCancel,
                TrickleDown.TrickleDown);
            addModifierButton.clicked += OnAddModifierClicked;
            removeModifierButton.clicked += OnRemoveModifierClicked;
            validateButton.clicked += OnValidateClicked;
        }

        // 释放时注销全部 UI 回调，防止离开 GE 页后残留订阅。
        private void UnregisterCallbacks()
        {
            searchField.UnregisterValueChangedCallback(OnSearchChanged);
            createEffectButton.clicked -= OnCreateEffectClicked;
            duplicateEffectButton.clicked -= OnDuplicateEffectClicked;
            deleteEffectButton.clicked -= OnDeleteEffectClicked;
            effectList.selectionChanged -= OnEffectSelectionChanged;
            detailsRoot.UnregisterCallback<SerializedPropertyChangeEvent>(OnSerializedPropertyChanged);
            modifierList.selectionChanged -= OnModifierSelectionChanged;
            modifierList.itemIndexChanged -= OnModifierIndexChanged;
            modifierList.UnregisterCallback<PointerDownEvent>(
                OnModifierPointerDown,
                TrickleDown.TrickleDown);
            modifierList.UnregisterCallback<PointerMoveEvent>(
                OnModifierPointerMove,
                TrickleDown.TrickleDown);
            modifierList.UnregisterCallback<PointerUpEvent>(
                OnModifierPointerUp,
                TrickleDown.TrickleDown);
            modifierList.UnregisterCallback<PointerCancelEvent>(
                OnModifierPointerCancel,
                TrickleDown.TrickleDown);
            addModifierButton.clicked -= OnAddModifierClicked;
            removeModifierButton.clicked -= OnRemoveModifierClicked;
            validateButton.clicked -= OnValidateClicked;
        }

        // View 释放时解除所有已创建虚拟化行的回调，防止页面重建后残留旧 View 引用。
        private void UnregisterEffectRowCallbacks()
        {
            for (int i = 0; i < effectPingButtons.Count; i++)
            {
                Button button = effectPingButtons[i];
                button.UnregisterCallback<PointerDownEvent>(OnEffectPingPointerDown);
                button.UnregisterCallback<ClickEvent>(OnEffectPingClicked);
                button.userData = null;
            }

            effectPingButtons.Clear();
        }

        #endregion
    }
}
#endif
