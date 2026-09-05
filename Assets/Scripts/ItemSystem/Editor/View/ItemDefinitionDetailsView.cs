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
    /// <summary>右侧公共物品详情及常驻类型页面的组合 View。</summary>
    internal sealed class ItemDefinitionDetailsView : IDisposable
    {
        #region 字段

        private readonly VisualElement root;
        private readonly ScrollView scrollView;
        private readonly VisualElement summaryHost;
        private readonly VisualElement emptyState;
        private readonly VisualElement commonDetailsPage;
        private readonly VisualElement stackableDetailsPage;
        private readonly VisualElement developmentItemDetailsPage;
        private readonly VisualElement weaponDetailsPage;
        private readonly VisualElement artifactDetailsPage;
        private readonly ItemWeaponDetailsView weaponDetailsView;
        private readonly ItemArtifactDetailsView artifactDetailsView;
        private readonly VisualTreeAsset summaryTemplate;
        private readonly VisualTreeAsset commonDetailsTemplate;
        private readonly VisualTreeAsset stackableDetailsTemplate;
        private readonly VisualTreeAsset developmentDetailsTemplate;
        private readonly VisualElement summaryCard;
        private readonly Image summaryIconImage;
        private readonly Label summaryIconFallback;
        private readonly Label summaryName;
        private readonly Label summaryKind;
        private readonly Label summaryCategory;
        private readonly Label summaryRarity;
        private readonly Label summaryId;
        private readonly PropertyField itemIdField;
        private readonly PropertyField useEffectsField;
        private readonly List<(PropertyField field, string label)> fixedPropertyLabels = new();

        private ItemDefinition boundDefinition;
        private SerializedObject definitionSerializedObject;
        private VisualElement definitionTracker;
        private int bindingVersion;
        private bool useEffectsListConfigured;
        private TextField displayNameField;
        private string displayNameEditingOriginal = string.Empty;
        private bool displayNameCommitCompleted;
        private bool disposed;

        #endregion

        #region 事件

        /// <summary>右侧详情显示名称提交事件。</summary>
        internal event Action<ItemDefinition, string> RenameSubmitted;

        /// <summary>定义序列化字段变化事件。</summary>
        internal event Action<ItemDefinition> PropertiesChanged;

        /// <summary>编辑器预览 Sprite 变化事件；由 Controller 负责反查 Atlas 并同步运行时引用。</summary>
        internal event Action<ItemDefinition, Sprite> PreviewIconChanged;

        /// <summary>武器成长烘焙请求。</summary>
        internal event Action BakeGrowthRequested;

        /// <summary>圣遗物成长烘焙请求。</summary>
        internal event Action BakeArtifactGrowthRequested;

        /// <summary>请求在通用窗口查看当前武器成长结果。</summary>
        internal event Action ViewBakedResultRequested;

        /// <summary>请求在通用窗口查看当前圣遗物成长结果。</summary>
        internal event Action ViewArtifactBakedResultRequested;

        #endregion

        #region 生命周期

        /// <summary>创建常驻详情页面、摘要卡和武器子 View。</summary>
        /// <param name="root">详情面板根节点。</param>
        /// <param name="scrollView">详情滚动容器。</param>
        internal ItemDefinitionDetailsView(VisualElement root, ScrollView scrollView)
        {
            this.root = root ?? throw new ArgumentNullException(nameof(root));
            this.scrollView = scrollView ?? throw new ArgumentNullException(nameof(scrollView));
            summaryHost = Require<VisualElement>("DefinitionSummary");
            emptyState = Require<VisualElement>("EmptyDetailsState");
            commonDetailsPage = Require<VisualElement>("CommonDetailsPage");
            stackableDetailsPage = Require<VisualElement>("StackableDetailsPage");
            developmentItemDetailsPage = Require<VisualElement>("DevelopmentItemDetailsPage");
            weaponDetailsPage = Require<VisualElement>("WeaponDetailsPage");
            artifactDetailsPage = Require<VisualElement>("ArtifactDetailsPage");
            scrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scrollView.verticalScrollerVisibility = ScrollerVisibility.Auto;

            summaryTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                UxmlUssPathConstants.Uxml.AssetsScriptsItemSystemEditorStyleItemDefinitionSummary);
            if (summaryTemplate == null)
                throw new InvalidOperationException("物品配置窗口缺少摘要卡 UXML。");
            summaryTemplate.CloneTree(summaryHost);
            summaryCard = RequireFromSummary<VisualElement>("SummaryCard");
            summaryIconImage = RequireFromSummary<Image>("SummaryIconImage");
            summaryIconFallback = RequireFromSummary<Label>("SummaryIconFallback");
            summaryName = RequireFromSummary<Label>("SummaryName");
            summaryKind = RequireFromSummary<Label>("SummaryKind");
            summaryCategory = RequireFromSummary<Label>("SummaryCategory");
            summaryRarity = RequireFromSummary<Label>("SummaryRarity");
            summaryId = RequireFromSummary<Label>("SummaryId");

            commonDetailsTemplate = LoadDetailsTemplate(
                UxmlUssPathConstants.Uxml.AssetsScriptsItemSystemEditorStyleItemCommonDetails,
                "公共物品详情");
            stackableDetailsTemplate = LoadDetailsTemplate(
                UxmlUssPathConstants.Uxml.AssetsScriptsItemSystemEditorStyleItemStackableDetails,
                "可堆叠物品详情");
            developmentDetailsTemplate = LoadDetailsTemplate(
                UxmlUssPathConstants.Uxml.AssetsScriptsItemSystemEditorStyleItemDevelopmentDetails,
                "养成道具详情");
            commonDetailsTemplate.CloneTree(commonDetailsPage);
            stackableDetailsTemplate.CloneTree(stackableDetailsPage);
            developmentDetailsTemplate.CloneTree(developmentItemDetailsPage);
            displayNameField = RequireFromPage<TextField>(commonDetailsPage, "DisplayNameField", "公共物品详情");
            itemIdField = RequireFromPage<PropertyField>(commonDetailsPage, "ItemIdField", "公共物品详情");
            itemIdField.SetEnabled(false);
            useEffectsField = RequireFromPage<PropertyField>(stackableDetailsPage, "UseEffectsField", "可堆叠物品详情");
            commonDetailsPage.RegisterCallback<SerializedPropertyChangeEvent>(OnCommonPropertyChanged);
            CacheFixedPropertyLabels(commonDetailsPage);
            CacheFixedPropertyLabels(stackableDetailsPage);
            CacheFixedPropertyLabels(developmentItemDetailsPage);
            displayNameField.RegisterCallback<FocusInEvent>(OnDisplayNameFocusIn);
            displayNameField.RegisterCallback<ChangeEvent<string>>(OnDisplayNameChanged);
            displayNameField.RegisterCallback<KeyDownEvent>(OnDisplayNameKeyDown);
            weaponDetailsView = new ItemWeaponDetailsView(weaponDetailsPage);
            weaponDetailsView.BakeGrowthRequested += OnBakeGrowthRequested;
            weaponDetailsView.ViewBakedResultRequested += OnViewBakedResultRequested;
            weaponDetailsView.PropertiesChanged += OnWeaponPropertiesChanged;
            artifactDetailsView = new ItemArtifactDetailsView(artifactDetailsPage);
            artifactDetailsView.BakeGrowthRequested += OnBakeArtifactGrowthRequested;
            artifactDetailsView.ViewBakedResultRequested += OnViewArtifactBakedResultRequested;
            artifactDetailsView.PropertiesChanged += OnArtifactPropertiesChanged;
            SetPageVisibility(false, false, false, false, false);
            SetEmptyState(true);
        }

        /// <summary>释放 Tracker、SerializedObject、武器子 View 和输入回调。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            weaponDetailsView.BakeGrowthRequested -= OnBakeGrowthRequested;
            weaponDetailsView.ViewBakedResultRequested -= OnViewBakedResultRequested;
            weaponDetailsView.PropertiesChanged -= OnWeaponPropertiesChanged;
            artifactDetailsView.BakeGrowthRequested -= OnBakeArtifactGrowthRequested;
            artifactDetailsView.ViewBakedResultRequested -= OnViewArtifactBakedResultRequested;
            artifactDetailsView.PropertiesChanged -= OnArtifactPropertiesChanged;
            commonDetailsPage.UnregisterCallback<SerializedPropertyChangeEvent>(OnCommonPropertyChanged);
            displayNameField.UnregisterCallback<FocusInEvent>(OnDisplayNameFocusIn);
            displayNameField.UnregisterCallback<ChangeEvent<string>>(OnDisplayNameChanged);
            displayNameField.UnregisterCallback<KeyDownEvent>(OnDisplayNameKeyDown);
            ReleaseBinding();
            weaponDetailsView.Dispose();
            artifactDetailsView.Dispose();
        }

        #endregion

        #region 绑定与刷新

        /// <summary>绑定当前定义；详情页面本身只创建一次。</summary>
        /// <param name="definition">当前定义；为空时显示空状态。</param>
        internal void BindDefinition(ItemDefinition definition)
        {
            if (definition != null && definition == boundDefinition && definitionSerializedObject != null)
            {
                RefreshPresentation(definition);
                return;
            }

            ReleaseBinding();
            boundDefinition = definition;
            SetEmptyState(definition == null);
            if (definition == null)
            {
                RefreshSummary(null);
                return;
            }

            definitionSerializedObject = new SerializedObject(definition);
            commonDetailsPage.Bind(definitionSerializedObject);
            if (definition is DevelopmentItemDefinition)
            {
                stackableDetailsPage.Bind(definitionSerializedObject);
                developmentItemDetailsPage.Bind(definitionSerializedObject);
                weaponDetailsView.Unbind();
                artifactDetailsView.Unbind();
                SetPageVisibility(true, true, true, false, false);
            }
            else if (definition is StackableItemDefinition)
            {
                stackableDetailsPage.Bind(definitionSerializedObject);
                developmentItemDetailsPage.Unbind();
                weaponDetailsView.Unbind();
                artifactDetailsPage.Unbind();
                artifactDetailsView.Unbind();
                SetPageVisibility(true, true, false, false, false);
            }
            else if (definition is WeaponDefinition weapon)
            {
                stackableDetailsPage.Unbind();
                developmentItemDetailsPage.Unbind();
                artifactDetailsPage.Unbind();
                // 武器页面先保持隐藏，待子 View 完成原生数组生成和中文化后再显示，避免 Element 0 首帧闪现。
                SetPageVisibility(true, false, false, false, false);
                weaponDetailsView.Bind(weapon, definitionSerializedObject);
            }
            else if (definition is ArtifactDefinition)
            {
                stackableDetailsPage.Unbind();
                developmentItemDetailsPage.Unbind();
                weaponDetailsView.Unbind();
                artifactDetailsView.Bind((ArtifactDefinition)definition, definitionSerializedObject);
                // 圣遗物页与武器页一样，先隐藏动态列表，待 Profile 绑定和中文化完成后再由子 View 显示。
                SetPageVisibility(true, false, false, false, false);
            }
            else
            {
                stackableDetailsPage.Unbind();
                developmentItemDetailsPage.Unbind();
                weaponDetailsView.Unbind();
                artifactDetailsPage.Unbind();
                artifactDetailsView.Unbind();
                SetPageVisibility(true, false, false, false, false);
            }

            RefreshSummary(definition);
            displayNameEditingOriginal = definition.DisplayName ?? string.Empty;
            displayNameCommitCompleted = false;
            displayNameField.SetValueWithoutNotify(displayNameEditingOriginal);
            definitionTracker = CreateSerializedObjectTracker(definitionSerializedObject);
            ScheduleFixedPropertyLabels();
            weaponDetailsView.RefreshPresentation();
            artifactDetailsView.RefreshPresentation();
        }

        /// <summary>轻量刷新摘要、名称和武器烘焙结果，不重建详情树。</summary>
        /// <param name="definition">发生变化的定义。</param>
        internal void RefreshPresentation(ItemDefinition definition)
        {
            if (definition == null || definition != boundDefinition) return;
            RefreshSummary(definition);
            // 提交事件在输入框仍保持焦点时同步回调；完成标记允许这里恢复 Service 失败后的旧值，普通编辑则继续保留用户输入。
            if (!IsDisplayNameFieldFocused() || displayNameCommitCompleted)
            {
                displayNameEditingOriginal = definition.DisplayName ?? string.Empty;
                displayNameField.SetValueWithoutNotify(displayNameEditingOriginal);
                displayNameCommitCompleted = false;
            }
            weaponDetailsView.RefreshPresentation();
            artifactDetailsView.RefreshPresentation();
        }

        /// <summary>同步 Undo/Redo 恢复后的 Definition 序列化视图并清理未提交名称。</summary>
        internal void PrepareForUndoRedoRefresh()
        {
            if (boundDefinition == null) return;

            // Undo 可能由其他 SerializedObject 或 AssetDatabase 操作完成；先更新长期持有的序列化快照，避免旧值覆盖恢复结果。
            definitionSerializedObject?.UpdateIfRequiredOrScript();
            displayNameEditingOriginal = boundDefinition.DisplayName ?? string.Empty;
            displayNameField.SetValueWithoutNotify(displayNameEditingOriginal);
            displayNameCommitCompleted = false;
            weaponDetailsView.RefreshPresentation();
            artifactDetailsView.RefreshPresentation();
        }

        /// <summary>解除当前对象绑定但保留所有详情控件。</summary>
        private void ReleaseBinding()
        {
            // 绑定版本先递增，使尚未执行的标签刷新回调失效，避免它访问即将解除的页面状态。
            bindingVersion++;
            if (displayNameField != null && IsDisplayNameFieldFocused())
                CommitDisplayName(true);

            weaponDetailsView.Unbind();
            artifactDetailsView.Unbind();
            commonDetailsPage.Unbind();
            stackableDetailsPage.Unbind();
            developmentItemDetailsPage.Unbind();
            artifactDetailsPage.Unbind();
            definitionTracker?.RemoveFromHierarchy();
            definitionTracker = null;

            if (definitionSerializedObject != null)
            {
                definitionSerializedObject.Dispose();
                definitionSerializedObject = null;
            }

            boundDefinition = null;
            useEffectsListConfigured = false;
            displayNameEditingOriginal = string.Empty;
            displayNameCommitCompleted = false;
            SetPageVisibility(false, false, false, false, false);
        }

        /// <summary>切换空状态、摘要和常驻详情页面的显示状态。</summary>
        /// <param name="empty">是否显示空状态。</param>
        private void SetEmptyState(bool empty)
        {
            summaryHost.style.display = empty ? DisplayStyle.None : DisplayStyle.Flex;
            emptyState.style.display = empty ? DisplayStyle.Flex : DisplayStyle.None;
            scrollView.style.display = empty ? DisplayStyle.None : DisplayStyle.Flex;
            if (empty) SetPageVisibility(false, false, false, false, false);
        }

        /// <summary>设置公共、堆叠和武器页面的显隐。</summary>
        /// <param name="commonVisible">公共页面是否显示。</param>
        /// <param name="stackableVisible">堆叠页面是否显示。</param>
        /// <param name="weaponVisible">武器页面是否显示。</param>
        private void SetPageVisibility(bool commonVisible, bool stackableVisible, bool developmentVisible, bool weaponVisible, bool artifactVisible)
        {
            commonDetailsPage.style.display = commonVisible ? DisplayStyle.Flex : DisplayStyle.None;
            stackableDetailsPage.style.display = stackableVisible ? DisplayStyle.Flex : DisplayStyle.None;
            developmentItemDetailsPage.style.display = developmentVisible ? DisplayStyle.Flex : DisplayStyle.None;
            weaponDetailsPage.style.display = weaponVisible ? DisplayStyle.Flex : DisplayStyle.None;
            artifactDetailsPage.style.display = artifactVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        #endregion

        #region UXML 字段缓存

        /// <summary>加载一个详情页 UXML，并在资源缺失时报告实际生成路径。</summary>
        /// <param name="path">路径生成器提供的 UXML 路径。</param>
        /// <param name="purpose">页面用途。</param>
        /// <returns>已加载的详情页模板。</returns>
        private static VisualTreeAsset LoadDetailsTemplate(string path, string purpose)
        {
            VisualTreeAsset template = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            if (template == null)
                throw new InvalidOperationException($"物品配置窗口缺少{purpose} UXML，生成路径为：{path}。");
            return template;
        }

        /// <summary>缓存 UXML 中的固定字段标签，避免在 C# 中重复维护中文名称。</summary>
        /// <param name="page">详情页根节点。</param>
        private void CacheFixedPropertyLabels(VisualElement page)
        {
            page.Query<PropertyField>().ForEach(field => fixedPropertyLabels.Add((field, field.label)));
        }

        /// <summary>在原生绑定完成后恢复 UXML 中声明的固定字段标签。</summary>
        private void ScheduleFixedPropertyLabels()
        {
            if (boundDefinition == null) return;
            int scheduledVersion = bindingVersion;
            root.schedule.Execute(() =>
            {
                if (disposed || scheduledVersion != bindingVersion || boundDefinition == null) return;
                for (int index = 0; index < fixedPropertyLabels.Count; index++)
                {
                    PropertyField field = fixedPropertyLabels[index].field;
                    if (field != null) field.label = fixedPropertyLabels[index].label;
                }
                ItemConfigEditorPresentation.ConfigureGameplayEffectList(
                    useEffectsField,
                    "暂无使用效果",
                    "使用效果",
                    !useEffectsListConfigured);
                useEffectsListConfigured = true;
            });
        }

        #endregion

        #region 摘要卡

        /// <summary>刷新摘要卡图标、名称、类型和稀有度。</summary>
        /// <param name="definition">当前定义。</param>
        private void RefreshSummary(ItemDefinition definition)
        {
            if (definition == null)
            {
                ItemConfigEditorPresentation.EnableRarityClass(summaryCard, "item-editor-definition-summary", null);
                summaryIconImage.sprite = null;
                summaryIconImage.style.display = DisplayStyle.None;
                summaryIconFallback.style.display = DisplayStyle.Flex;
                summaryIconFallback.text = "✦";
                summaryName.text = string.Empty;
                summaryKind.text = string.Empty;
                summaryCategory.text = string.Empty;
                summaryRarity.text = string.Empty;
                summaryId.text = string.Empty;
                return;
            }

            Sprite previewIcon = definition.EditorPreviewIcon;
            summaryIconImage.scaleMode = ScaleMode.ScaleToFit;
            summaryIconImage.sprite = previewIcon;
            summaryIconImage.style.display = previewIcon == null ? DisplayStyle.None : DisplayStyle.Flex;
            summaryIconFallback.style.display = previewIcon == null ? DisplayStyle.Flex : DisplayStyle.None;
            summaryIconFallback.text = definition switch
            {
                WeaponDefinition => "🗡",
                ArtifactDefinition => "◇",
                DevelopmentItemDefinition => "✚",
                _ => "✦"
            };
            summaryName.text = string.IsNullOrWhiteSpace(definition.DisplayName) ? "未命名物品" : definition.DisplayName;
            summaryKind.text = ItemConfigEditorPresentation.GetDefinitionKindText(definition);
            summaryCategory.text = ItemConfigEditorPresentation.GetCategoryText(definition.Category);
            summaryRarity.text = ItemConfigEditorPresentation.GetRarityStars(definition.Rarity);
            summaryId.text = $"物品标识：{definition.ItemId}";
            ItemConfigEditorPresentation.EnableRarityClass(summaryCard, "item-editor-definition-summary", definition.Rarity);
        }

        #endregion

        #region SerializedObject 生命周期

        /// <summary>创建当前定义的隐藏变化 Tracker。</summary>
        /// <param name="serializedObject">当前定义 SerializedObject。</param>
        /// <returns>隐藏 Tracker 节点。</returns>
        private VisualElement CreateSerializedObjectTracker(SerializedObject serializedObject)
        {
            var tracker = new VisualElement { name = "DefinitionTracker" };
            tracker.style.display = DisplayStyle.None;
            commonDetailsPage.Add(tracker);
            tracker.TrackSerializedObjectValue(serializedObject, OnSerializedObjectChanged);
            return tracker;
        }

        /// <summary>处理当前定义 SerializedObject 的字段变化。</summary>
        /// <param name="serializedObject">发生变化的对象。</param>
        private void OnSerializedObjectChanged(SerializedObject serializedObject)
        {
            if (serializedObject == null || serializedObject != definitionSerializedObject || boundDefinition == null) return;
            weaponDetailsView.ScheduleStructureRefresh();
            PropertiesChanged?.Invoke(boundDefinition);
        }

        /// <summary>捕获公共页面预览 Sprite 的绑定变化，避免普通字段修改触发 Atlas 反查。</summary>
        /// <param name="eventData">序列化属性变化事件。</param>
        private void OnCommonPropertyChanged(SerializedPropertyChangeEvent eventData)
        {
            SerializedProperty changedProperty = eventData?.changedProperty;
            if (changedProperty == null || changedProperty.propertyPath != "editorPreviewIcon" || boundDefinition == null) return;
            PreviewIconChanged?.Invoke(boundDefinition, boundDefinition.EditorPreviewIcon);
        }

        #endregion

        #region 显示名称编辑

        /// <summary>记录显示名称编辑的原始值。</summary>
        /// <param name="eventData">焦点事件。</param>
        private void OnDisplayNameFocusIn(FocusInEvent eventData)
        {
            displayNameEditingOriginal = displayNameField.value ?? string.Empty;
            displayNameCommitCompleted = false;
        }

        /// <summary>处理显示名称 Escape 取消；Enter 交由 delayed TextField 触发 ChangeEvent。</summary>
        /// <param name="eventData">键盘事件。</param>
        private void OnDisplayNameKeyDown(KeyDownEvent eventData)
        {
            if (eventData.keyCode == KeyCode.Escape)
            {
                CommitDisplayName(false);
                eventData.StopPropagation();
            }
        }

        /// <summary>处理 delayed TextField 完成提交后的显示名称变化。</summary>
        /// <param name="eventData">包含控件提交后新值的变化事件。</param>
        private void OnDisplayNameChanged(ChangeEvent<string> eventData)
        {
            CommitDisplayNameValue(eventData?.newValue, true);
        }

        /// <summary>提交或放弃当前显示名称编辑。</summary>
        /// <param name="apply">是否提交。</param>
        private void CommitDisplayName(bool apply)
        {
            if (displayNameField == null || boundDefinition == null) return;
            CommitDisplayNameValue(apply ? displayNameField.value : displayNameEditingOriginal, apply);
        }

        /// <summary>按控件已提交的值执行一次幂等显示名称提交。</summary>
        /// <param name="rawValue">TextField 提交的原始值。</param>
        /// <param name="apply">是否应用新值；false 表示取消并恢复原值。</param>
        private void CommitDisplayNameValue(string rawValue, bool apply)
        {
            if (displayNameField == null || boundDefinition == null) return;
            string value = rawValue?.Trim() ?? string.Empty;
            bool invalidEmptyValue = apply && string.IsNullOrWhiteSpace(value);
            bool shouldApply = apply &&
                               !string.IsNullOrWhiteSpace(value) &&
                               !string.Equals(value, displayNameEditingOriginal, StringComparison.Ordinal);
            displayNameCommitCompleted = true;
            displayNameField.SetValueWithoutNotify(shouldApply ? value : displayNameEditingOriginal);

            // 成功提交后把新值作为下一次编辑的比较基准；Controller 若失败会通过 RefreshPresentation 恢复 Definition 中的旧值。
            if (shouldApply)
                displayNameEditingOriginal = value;
            if (shouldApply || invalidEmptyValue)
                RenameSubmitted?.Invoke(boundDefinition, value);
        }

        /// <summary>判断右侧显示名称输入框是否拥有焦点。</summary>
        /// <returns>拥有焦点时返回 true。</returns>
        private bool IsDisplayNameFieldFocused() =>
            displayNameField?.panel?.focusController?.focusedElement == displayNameField;

        #endregion

        #region 事件转发与查询

        /// <summary>转发武器成长烘焙请求。</summary>
        private void OnBakeGrowthRequested() => BakeGrowthRequested?.Invoke();

        /// <summary>转发圣遗物成长烘焙请求。</summary>
        private void OnBakeArtifactGrowthRequested() => BakeArtifactGrowthRequested?.Invoke();

        /// <summary>转发武器成长结果查看请求。</summary>
        private void OnViewBakedResultRequested() => ViewBakedResultRequested?.Invoke();

        /// <summary>转发圣遗物成长结果查看请求。</summary>
        private void OnViewArtifactBakedResultRequested() => ViewArtifactBakedResultRequested?.Invoke();

        /// <summary>转发武器字段变化。</summary>
        /// <param name="definition">发生变化的武器。</param>
        private void OnWeaponPropertiesChanged(ItemDefinition definition) => PropertiesChanged?.Invoke(definition);

        /// <summary>转发圣遗物字段变化。</summary>
        /// <param name="definition">发生变化的圣遗物。</param>
        private void OnArtifactPropertiesChanged(ItemDefinition definition) => PropertiesChanged?.Invoke(definition);

        /// <summary>在详情面板范围内查询必需控件。</summary>
        /// <typeparam name="TElement">控件类型。</typeparam>
        /// <param name="name">控件名称。</param>
        /// <returns>找到的控件。</returns>
        private TElement Require<TElement>(string name) where TElement : VisualElement
        {
            TElement element = root.Q<TElement>(name);
            if (element == null) throw new InvalidOperationException($"物品配置窗口详情缺少 UXML 控件：{name}。");
            return element;
        }

        /// <summary>在摘要卡范围内查询必需控件。</summary>
        /// <typeparam name="TElement">控件类型。</typeparam>
        /// <param name="name">控件名称。</param>
        /// <returns>找到的控件。</returns>
        private TElement RequireFromSummary<TElement>(string name) where TElement : VisualElement
        {
            TElement element = summaryHost.Q<TElement>(name);
            if (element == null) throw new InvalidOperationException($"物品摘要卡 UXML 缺少控件：{name}。");
            return element;
        }

        /// <summary>在指定详情页内查询必需控件，防止子页面之间发生全局查询串线。</summary>
        /// <typeparam name="TElement">控件类型。</typeparam>
        /// <param name="page">详情页根节点。</param>
        /// <param name="name">控件名称。</param>
        /// <param name="purpose">页面用途。</param>
        /// <returns>找到的控件。</returns>
        private static TElement RequireFromPage<TElement>(VisualElement page, string name, string purpose)
            where TElement : VisualElement
        {
            TElement element = page.Q<TElement>(name);
            if (element == null) throw new InvalidOperationException($"{purpose} UXML 缺少控件：{name}。");
            return element;
        }

        #endregion
    }
}
#endif
