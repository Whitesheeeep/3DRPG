#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.GAS.GameplayEffect;

namespace WS_Modules.GAS.Editor
{
    /// <summary>使用 UI Toolkit 实现 GE 右侧原生绑定、Modifier 编辑和校验显示。</summary>
    internal sealed class GameplayEffectDetailsView : IGameplayEffectDetailsView
    {
        #region 常量与字段

        private const string DetailsUxmlPath =
            "Assets/GAS_Light/GameEffectSystem/Editor/Style/GameplayEffectDetails.uxml";
        private const string ModifierRowUxmlPath =
            "Assets/GAS_Light/GameEffectSystem/Editor/Style/GameplayEffectModifierRow.uxml";
        private const float ModifierDragThresholdSquared = 16f;

        private readonly VisualElement pageRoot;
        private readonly VisualTreeAsset modifierRowAsset;
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
        private readonly Button validateButton;
        private readonly Button bakeCurvePreviewButton;
        private readonly Button viewBakedResultButton;
        private readonly VisualElement validationContainer;
        private readonly List<GameplayEffectModifier> renderedModifiers = new();
        private readonly List<Type> availableModifierTypes = new();

        private PropertyField modifierPropertyField;
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
        /// <inheritdoc />
        public event Action BakeCurvePreviewRequested;
        /// <inheritdoc />
        public event Action ViewBakedResultRequested;

        #endregion

        #region 生命周期

        // 在主 View 提供的宿主中实例化独立详情 UXML，并建立全部右侧 UI 回调。
        internal GameplayEffectDetailsView(VisualElement host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            VisualTreeAsset detailsAsset =
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(DetailsUxmlPath);
            modifierRowAsset =
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ModifierRowUxmlPath);
            if (detailsAsset == null || modifierRowAsset == null)
                throw new InvalidOperationException("Gameplay Effect details UXML resources are missing.");

            pageRoot = new VisualElement { name = "GameplayEffectDetailsPage" };
            pageRoot.AddToClassList("ge-details-page");
            host.Add(pageRoot);
            detailsAsset.CloneTree(pageRoot);

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
            bakeCurvePreviewButton = Require<Button>("BakeCurvePreviewButton");
            viewBakedResultButton = Require<Button>("ViewBakedResultButton");
            validationContainer = Require<VisualElement>("ValidationContainer");

            ConfigureModifierList();
            RegisterCallbacks();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            CancelModifierBindingRestore();
            UnregisterCallbacks();
            UnbindCurrentEffect();
            modifierList.itemsSource = null;
            pageRoot.RemoveFromHierarchy();
            pageRoot.Clear();
        }

        #endregion

        #region 状态与渲染

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
                addModifierButton.SetEnabled(hasEffect && availableModifierTypes.Count > 0);
                bakeCurvePreviewButton.SetEnabled(hasEffect);
                viewBakedResultButton.SetEnabled(hasEffect);
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

        #endregion

        #region Modifier 列表

        // Modifier ListView 只保存 Model 引用；真实排序由 Service 写回资产。
        private void ConfigureModifierList()
        {
            modifierList.selectionType = SelectionType.Single;
            modifierList.fixedItemHeight = 28f;
            modifierList.reorderable = true;
            modifierList.reorderMode = ListViewReorderMode.Simple;
            modifierList.makeItem = () => modifierRowAsset.Instantiate();
            modifierList.bindItem = BindModifierRow;
            modifierList.itemsSource = renderedModifiers;
        }

        // Modifier 行即时格式化类型与 Attribute 名称，不创建 Model 副本。
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

        // Modifier 列表只转发选中索引；冻结期间会延后真正的详情绑定。
        private void OnModifierSelectionChanged(IEnumerable<object> selection) =>
            ModifierSelectionChanged?.Invoke(modifierList.selectedIndex);

        // 左键按下只记录候选拖拽；普通点击不会冻结详情。
        private void OnModifierPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0) return;
            modifierPointerTracking = true;
            modifierPointerDownPosition = evt.position;
        }

        // 超过拖拽阈值后立即移除详情，确保旧 IMGUIContainer 先退出 Panel。
        private void OnModifierPointerMove(PointerMoveEvent evt)
        {
            if (!modifierPointerTracking || modifierInteractionFrozen ||
                (evt.pressedButtons & 1) == 0)
                return;

            Vector2 delta = (Vector2)evt.position - modifierPointerDownPosition;
            if (delta.sqrMagnitude < ModifierDragThresholdSquared) return;
            FreezeModifierInteraction();
        }

        // PointerUp 后跨两次 Editor 更新恢复，保证延迟数组移动已经提交。
        private void OnModifierPointerUp(PointerUpEvent evt)
        {
            if (evt.button != 0) return;
            modifierPointerTracking = false;
            if (modifierInteractionFrozen) ScheduleModifierBindingRestore();
        }

        // Pointer 被系统取消时使用相同恢复路径，避免详情长期冻结。
        private void OnModifierPointerCancel(PointerCancelEvent evt)
        {
            modifierPointerTracking = false;
            if (modifierInteractionFrozen) ScheduleModifierBindingRestore();
        }

        // Drop 先让 Controller 排队数组移动，再排队详情恢复。
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

        // 手动刷新只请求重新校验。
        private void OnValidateClicked() => ValidateRequested?.Invoke();

        // Curve 预览烘焙交给通用 Editor Service，Details View 不直接写资产。
        private void OnBakeCurvePreviewClicked() => BakeCurvePreviewRequested?.Invoke();

        // 结果查看只表达用户意图，窗口创建由 Controller 负责。
        private void OnViewBakedResultClicked() => ViewBakedResultRequested?.Invoke();

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

        // PointerUp 第一阶段排队下一次恢复，使数组提交先于 PropertyField 重建。
        private void ScheduleModifierBindingRestore()
        {
            if (modifierBindingRestoreScheduled) return;
            modifierBindingRestoreScheduled = true;
            EditorApplication.delayCall += QueueModifierBindingRestore;
        }

        // 再跨一次 Editor 更新，避开 Unity 2022.3 当前 Panel 中残留的 IMGUI 回调。
        private void QueueModifierBindingRestore()
        {
            EditorApplication.delayCall += RestoreModifierBinding;
        }

        // 使用最终选择索引创建全新 PropertyField，不复用拖拽前 SerializedProperty。
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

        // 切换 GE 或释放 View 时取消延迟恢复并清空纯交互状态。
        private void ResetModifierInteraction()
        {
            CancelModifierBindingRestore();
            modifierPointerTracking = false;
            modifierInteractionFrozen = false;
            pendingModifierBindingIndex = -1;
            modifierEmptyLabel.text = "Select a Modifier.";
        }

        // 对称移除两阶段 delayCall；任一阶段都不会残留回调。
        private void CancelModifierBindingRestore()
        {
            EditorApplication.delayCall -= QueueModifierBindingRestore;
            EditorApplication.delayCall -= RestoreModifierBinding;
            modifierBindingRestoreScheduled = false;
        }

        // 完整移除动态 PropertyField，避免其 IMGUIContainer 持有旧 SerializedProperty。
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

        // 使用当前 Registry 即时解析作者名称；名称只用于显示。
        private void BindModifierAttribute(Label label, GameplayEffectModifier modifier)
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

        // 原生 PropertyField 创建子控件后将文本和数字输入统一设为延迟提交。
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

        // 去掉通用后缀后生成 Add 菜单和行名称。
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

        // 查询右侧 UXML 必需控件，缺失时立即暴露资源与代码契约不一致。
        private T Require<T>(string name) where T : VisualElement
        {
            T element = pageRoot.Q<T>(name);
            if (element == null)
                throw new InvalidOperationException(
                    $"Gameplay Effect details UXML is missing required element '{name}'.");
            return element;
        }

        #endregion

        #region 回调注册

        // 注册右侧绑定、Modifier 和校验回调，由 Dispose 对称解除。
        private void RegisterCallbacks()
        {
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
            bakeCurvePreviewButton.clicked += OnBakeCurvePreviewClicked;
            viewBakedResultButton.clicked += OnViewBakedResultClicked;
        }

        // 释放时对称注销全部右侧 UI 回调。
        private void UnregisterCallbacks()
        {
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
            bakeCurvePreviewButton.clicked -= OnBakeCurvePreviewClicked;
            viewBakedResultButton.clicked -= OnViewBakedResultClicked;
        }

        #endregion
    }
}
#endif
