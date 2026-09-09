#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace RPG.Character.Editor
{
    /// <summary>角色等级、Attribute BaseValue 曲线、突破阶段和烘焙结果详情 View。</summary>
    internal sealed class CharacterGrowthDetailsView : IDisposable
    {
        #region 依赖字段

        private const string GrowthTemplatePath = "Assets/Scripts/Character/Editor/Style/CharacterGrowthDetails.uxml";
        private readonly VisualElement pageRoot;
        private readonly VisualTreeAsset growthTemplate;
        private readonly VisualElement baseFields;
        private readonly VisualElement ascensionStageHost;
        private readonly VisualElement growthProfileContent;
        private readonly VisualElement missingGrowthProfileWarning;
        private readonly PropertyField growthProfileMaxLevelField;
        private readonly PropertyField ascensionStagesField;
        private readonly PropertyField attributeGrowthCurvesField;
        private readonly PropertyField levelOverridesField;
        private readonly Label ascensionStageTitle;
        private readonly Label bakedSummaryLabel;
        private readonly Button bakeButton;
        private readonly Button viewBakedResultButton;
        private SerializedObject configSerializedObject;
        private SerializedObject growthProfileSerializedObject;
        private VisualElement growthProfileTracker;
        private CharacterConfig boundConfig;
        private CharacterGrowthProfile boundGrowthProfile;
        private bool suppressCallbacks;
        private bool disposed;

        #endregion

        #region 事件

        /// <summary>角色序列化字段发生变化。</summary>
        internal event Action<CharacterConfig, string> PropertiesChanged;

        /// <summary>请求烘焙当前角色成长 Profile。</summary>
        internal event Action BakeGrowthRequested;

        /// <summary>请求打开当前角色成长烘焙结果。</summary>
        internal event Action ViewBakedResultRequested;

        #endregion

        #region 生命周期

        /// <summary>加载角色成长模板并取得固定控件。</summary>
        /// <param name="parent">角色详情父容器。</param>
        internal CharacterGrowthDetailsView(VisualElement parent)
        {
            pageRoot = parent ?? throw new ArgumentNullException(nameof(parent));
            growthTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(GrowthTemplatePath);
            if (growthTemplate == null) throw new InvalidOperationException($"角色配置窗口缺少成长详情 UXML：{GrowthTemplatePath}。");
            growthTemplate.CloneTree(pageRoot);
            baseFields = Require<VisualElement>("CharacterGrowthBaseFields");
            ascensionStageHost = Require<VisualElement>("CharacterAscensionStageHost");
            growthProfileContent = Require<VisualElement>("CharacterGrowthProfileContent");
            missingGrowthProfileWarning = Require<VisualElement>("MissingCharacterGrowthProfileWarning");
            growthProfileMaxLevelField = Require<PropertyField>("CharacterGrowthProfileMaxLevelField");
            ascensionStagesField = Require<PropertyField>("CharacterAscensionStagesField");
            attributeGrowthCurvesField = Require<PropertyField>("CharacterAttributeGrowthCurvesField");
            levelOverridesField = Require<PropertyField>("CharacterLevelOverridesField");
            ascensionStageTitle = Require<Label>("CharacterAscensionStageTitle");
            bakedSummaryLabel = Require<Label>("CharacterBakedSummaryLabel");
            bakeButton = Require<Button>("CharacterBakeButton");
            viewBakedResultButton = Require<Button>("CharacterViewBakedResultButton");
            growthProfileMaxLevelField.SetEnabled(false);
            bakeButton.clicked += OnBakeButtonClicked;
            viewBakedResultButton.clicked += OnViewBakedResultButtonClicked;
            pageRoot.RegisterCallback<SerializedPropertyChangeEvent>(OnSerializedPropertyChanged);
            SetVisible(false);
        }

        /// <summary>解除角色和 GrowthProfile 的 SerializedObject 绑定。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            bakeButton.clicked -= OnBakeButtonClicked;
            viewBakedResultButton.clicked -= OnViewBakedResultButtonClicked;
            pageRoot.UnregisterCallback<SerializedPropertyChangeEvent>(OnSerializedPropertyChanged);
            Unbind();
            PropertiesChanged = null;
            BakeGrowthRequested = null;
            ViewBakedResultRequested = null;
        }

        #endregion

        #region 绑定与刷新

        /// <summary>绑定角色配置及其独立 GrowthProfile。</summary>
        /// <param name="config">当前角色配置。</param>
        /// <param name="serializedObject">角色配置序列化对象。</param>
        internal void Bind(CharacterConfig config, SerializedObject serializedObject)
        {
            if (disposed) throw new ObjectDisposedException(nameof(CharacterGrowthDetailsView));
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (serializedObject == null) throw new ArgumentNullException(nameof(serializedObject));
            Unbind();
            boundConfig = config;
            configSerializedObject = serializedObject;
            configSerializedObject.UpdateIfRequiredOrScript();
            baseFields.Bind(configSerializedObject);
            ascensionStageHost.Bind(configSerializedObject);
            ConfigureCollection(ascensionStagesField, "暂无突破阶段");
            BindGrowthProfile(config.GrowthProfile);
            RefreshPresentation();
        }

        /// <summary>刷新当前角色成长绑定和烘焙摘要。</summary>
        internal void Refresh()
        {
            if (boundConfig == null || configSerializedObject == null) return;
            suppressCallbacks = true;
            try
            {
                configSerializedObject.UpdateIfRequiredOrScript();
                if (!ReferenceEquals(boundGrowthProfile, boundConfig.GrowthProfile))
                    BindGrowthProfile(boundConfig.GrowthProfile);
                growthProfileSerializedObject?.UpdateIfRequiredOrScript();
                RefreshPresentation();
            }
            finally
            {
                suppressCallbacks = false;
            }
        }

        /// <summary>解除所有当前角色和 Profile 的绑定，但保留视觉树。</summary>
        internal void Unbind()
        {
            growthProfileContent?.Unbind();
            baseFields?.Unbind();
            ascensionStageHost?.Unbind();
            growthProfileTracker?.RemoveFromHierarchy();
            growthProfileTracker = null;
            growthProfileSerializedObject?.Dispose();
            growthProfileSerializedObject = null;
            boundGrowthProfile = null;
            configSerializedObject = null;
            boundConfig = null;
            SetVisible(false);
        }

        /// <summary>设置成长详情页显隐。</summary>
        /// <param name="visible">是否显示页面。</param>
        internal void SetVisible(bool visible) => pageRoot.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        #endregion

        #region 状态刷新

        /// <summary>刷新阶段标题、Profile 警告、按钮状态和烘焙摘要。</summary>
        private void RefreshPresentation()
        {
            if (boundConfig == null) return;
            ascensionStageTitle.text = $"突破配置（已配置 {boundConfig.AscensionStages?.Count ?? 0} 项，最大突破阶数 {boundConfig.MaxAscensionRank}）";
            bool hasProfile = boundGrowthProfile != null && growthProfileSerializedObject != null;
            missingGrowthProfileWarning.style.display = hasProfile ? DisplayStyle.None : DisplayStyle.Flex;
            growthProfileContent.style.display = hasProfile ? DisplayStyle.Flex : DisplayStyle.None;
            bakeButton.SetEnabled(hasProfile);
            viewBakedResultButton.SetEnabled(hasProfile && boundGrowthProfile.BakedLevelProgressions.Count > 0);
            if (!hasProfile)
            {
                bakedSummaryLabel.text = "烘焙结果：未配置成长 Profile。";
                SetVisible(true);
                return;
            }

            growthProfileMaxLevelField.SetEnabled(false);
            ConfigureCollection(attributeGrowthCurvesField, "暂无 Attribute 成长曲线");
            ConfigureCollection(levelOverridesField, "暂无特殊等级覆盖");
            int bakedCount = boundGrowthProfile.BakedLevelProgressions.Count;
            bakedSummaryLabel.text = bakedCount == 0
                ? "烘焙结果：尚未生成，请先烘焙成长表。"
                : boundGrowthProfile.NeedsRebake
                    ? $"烘焙结果：输入已变化，请重新烘焙（当前仍保留 {bakedCount} 个旧等级条目）。"
                    : $"烘焙结果：已生成 {bakedCount} 个等级条目，等级 1 至 {boundGrowthProfile.MaxLevel}；Attribute 曲线 {boundGrowthProfile.BakedAttributeProgressions.Count} 条。";
            SetVisible(true);
        }

        /// <summary>绑定新的 GrowthProfile SerializedObject 和变化 Tracker。</summary>
        /// <param name="profile">新的 GrowthProfile，可为空。</param>
        private void BindGrowthProfile(CharacterGrowthProfile profile)
        {
            growthProfileContent.Unbind();
            growthProfileTracker?.RemoveFromHierarchy();
            growthProfileTracker = null;
            growthProfileSerializedObject?.Dispose();
            growthProfileSerializedObject = null;
            boundGrowthProfile = profile;
            if (profile == null) return;
            growthProfileSerializedObject = new SerializedObject(profile);
            growthProfileSerializedObject.UpdateIfRequiredOrScript();
            growthProfileContent.Bind(growthProfileSerializedObject);
            growthProfileTracker = new VisualElement { name = "CharacterGrowthProfileTracker" };
            growthProfileTracker.style.display = DisplayStyle.None;
            pageRoot.Add(growthProfileTracker);
            growthProfileTracker.TrackSerializedObjectValue(growthProfileSerializedObject, OnGrowthProfileChanged);
        }

        /// <summary>配置原生集合字段的增删、重排和中文空状态。</summary>
        /// <param name="propertyField">目标集合字段。</param>
        /// <param name="emptyText">空集合提示。</param>
        private static void ConfigureCollection(PropertyField propertyField, string emptyText)
        {
            if (propertyField == null) return;
            propertyField.Query<ListView>().ForEach(listView =>
            {
                listView.showBoundCollectionSize = false;
                listView.showFoldoutHeader = true;
                listView.showAddRemoveFooter = true;
                listView.reorderable = true;
                listView.reorderMode = ListViewReorderMode.Simple;
                listView.Query<Label>().ForEach(label =>
                {
                    if (label.text == "List is empty") label.text = emptyText;
                });
            });
            NormalizeDynamicPresentation(propertyField);
        }

        /// <summary>把成长详情中原生生成的集合元素和字段标签统一为中文。</summary>
        /// <param name="scope">需要重新配置的集合字段范围。</param>
        private static void NormalizeDynamicPresentation(VisualElement scope)
        {
            scope.Query<PropertyField>().ForEach(field =>
            {
                string localizedLabel = GetNestedPropertyLabel(field.bindingPath);
                if (!string.IsNullOrEmpty(localizedLabel) && !string.Equals(field.label, localizedLabel, StringComparison.Ordinal))
                    field.label = localizedLabel;
            });

            scope.Query<Label>().ForEach(label =>
            {
                string bindingPath = FindBindingPath(label);
                if (TryGetArrayElementInfo(bindingPath, out string collectionName, out int index))
                    label.text = GetLocalizedElementLabel(collectionName, index);
                else if (IsEmptyListText(label.text))
                    label.text = GetLocalizedEmptyListLabel(bindingPath);
            });
        }

        /// <summary>按动态绑定路径取得嵌套字段的中文标题。</summary>
        /// <param name="bindingPath">PropertyField 的绑定路径。</param>
        /// <returns>匹配到的中文标题；未知路径返回空文本。</returns>
        private static string GetNestedPropertyLabel(string bindingPath)
        {
            if (string.IsNullOrEmpty(bindingPath)) return string.Empty;
            if (bindingPath.EndsWith("requiredLevel", StringComparison.Ordinal)) return "所需等级";
            if (bindingPath.EndsWith("maxLevelAfter", StringComparison.Ordinal)) return "突破后等级上限";
            if (bindingPath.EndsWith("itemCosts", StringComparison.Ordinal)) return "物品消耗";
            if (bindingPath.EndsWith("currencyCosts", StringComparison.Ordinal)) return "货币消耗";
            if (bindingPath.EndsWith("itemId", StringComparison.Ordinal)) return "物品标识";
            if (bindingPath.EndsWith("quantity", StringComparison.Ordinal)) return "数量";
            if (bindingPath.EndsWith("currencyId", StringComparison.Ordinal)) return "货币标识";
            if (bindingPath.EndsWith("amount", StringComparison.Ordinal)) return "金额";
            if (bindingPath.EndsWith("nextExperience", StringComparison.Ordinal)) return "下一级所需经验";
            if (bindingPath.EndsWith("currencyCost", StringComparison.Ordinal)) return "货币消耗";
            if (bindingPath.EndsWith("baseValueCurve", StringComparison.Ordinal)) return "BaseValue 曲线";
            if (bindingPath.EndsWith("attribute", StringComparison.Ordinal)) return "Attribute";
            if (bindingPath.EndsWith("level", StringComparison.Ordinal) && bindingPath.Contains("levelOverrides", StringComparison.Ordinal)) return "等级";
            return string.Empty;
        }

        /// <summary>判断原生 ListView 是否展示空集合英文文本。</summary>
        /// <param name="text">当前 Label 文本。</param>
        /// <returns>需要替换为空状态时返回 true。</returns>
        private static bool IsEmptyListText(string text) =>
            text == "List is empty" || (text != null && text.StartsWith("暂无", StringComparison.Ordinal));

        /// <summary>根据集合名称和索引生成中文元素标题。</summary>
        /// <param name="collectionName">最内层集合名称。</param>
        /// <param name="index">零基元素索引。</param>
        /// <returns>中文元素标题。</returns>
        private static string GetLocalizedElementLabel(string collectionName, int index)
        {
            if (string.Equals(collectionName, "ascensionStages", StringComparison.Ordinal)) return $"突破阶段 {index + 1}";
            if (string.Equals(collectionName, "attributeGrowthCurves", StringComparison.Ordinal)) return $"Attribute 成长曲线 {index + 1}";
            if (string.Equals(collectionName, "levelOverrides", StringComparison.Ordinal)) return $"特殊等级覆盖 {index + 1}";
            if (string.Equals(collectionName, "itemCosts", StringComparison.Ordinal)) return $"物品消耗 {index + 1}";
            if (string.Equals(collectionName, "currencyCosts", StringComparison.Ordinal)) return $"货币消耗 {index + 1}";
            return $"配置项 {index + 1}";
        }

        /// <summary>根据集合名称生成中文空状态。</summary>
        /// <param name="bindingPath">空 Label 最近祖先的绑定路径。</param>
        /// <returns>对应集合的空状态文本。</returns>
        private static string GetLocalizedEmptyListLabel(string bindingPath)
        {
            if (bindingPath.Contains("ascensionStages", StringComparison.Ordinal)) return "暂无突破阶段";
            if (bindingPath.Contains("attributeGrowthCurves", StringComparison.Ordinal)) return "暂无 Attribute 成长曲线";
            if (bindingPath.Contains("levelOverrides", StringComparison.Ordinal)) return "暂无特殊等级覆盖";
            if (bindingPath.Contains("itemCosts", StringComparison.Ordinal)) return "暂无物品消耗";
            if (bindingPath.Contains("currencyCosts", StringComparison.Ordinal)) return "暂无货币消耗";
            return "暂无配置项";
        }

        /// <summary>从绑定路径解析最内层集合名称和数组索引。</summary>
        /// <param name="bindingPath">Label 最近祖先的绑定路径。</param>
        /// <param name="collectionName">解析出的集合名称。</param>
        /// <param name="index">解析出的零基索引。</param>
        /// <returns>路径指向集合元素时返回 true。</returns>
        private static bool TryGetArrayElementInfo(string bindingPath, out string collectionName, out int index)
        {
            collectionName = string.Empty;
            index = -1;
            if (string.IsNullOrEmpty(bindingPath)) return false;
            const string marker = ".Array.data[";
            int markerIndex = bindingPath.LastIndexOf(marker, StringComparison.Ordinal);
            if (markerIndex < 0 || !bindingPath.EndsWith("]", StringComparison.Ordinal)) return false;
            int indexStart = markerIndex + marker.Length;
            int indexEnd = bindingPath.Length - 1;
            if (indexStart >= indexEnd || !int.TryParse(bindingPath.Substring(indexStart, indexEnd - indexStart), out index)) return false;
            int collectionEnd = markerIndex;
            int collectionStart = bindingPath.LastIndexOf('.', collectionEnd - 1);
            collectionStart = collectionStart < 0 ? 0 : collectionStart + 1;
            collectionName = bindingPath.Substring(collectionStart, collectionEnd - collectionStart);
            return !string.IsNullOrEmpty(collectionName);
        }

        /// <summary>查找 Label 最近祖先的绑定路径。</summary>
        /// <param name="element">待查找控件。</param>
        /// <returns>祖先绑定路径；不存在时返回空文本。</returns>
        private static string FindBindingPath(VisualElement element)
        {
            for (VisualElement current = element; current != null; current = current.parent)
                if (current is IBindable bindable && !string.IsNullOrEmpty(bindable.bindingPath)) return bindable.bindingPath;
            return string.Empty;
        }

        #endregion

        #region 事件处理

        /// <summary>转发烘焙按钮请求。</summary>
        private void OnBakeButtonClicked() => BakeGrowthRequested?.Invoke();

        /// <summary>转发查看烘焙结果请求。</summary>
        private void OnViewBakedResultButtonClicked() => ViewBakedResultRequested?.Invoke();

        /// <summary>接收角色配置字段变化并通知父 View。</summary>
        /// <param name="eventData">序列化属性变化事件。</param>
        private void OnSerializedPropertyChanged(SerializedPropertyChangeEvent eventData)
        {
            if (suppressCallbacks || boundConfig == null) return;
            if (eventData?.changedProperty != null && eventData.changedProperty.serializedObject != configSerializedObject) return;
            configSerializedObject?.UpdateIfRequiredOrScript();
            if (!ReferenceEquals(boundGrowthProfile, boundConfig.GrowthProfile))
                BindGrowthProfile(boundConfig.GrowthProfile);
            RefreshPresentation();
        }

        /// <summary>接收 Profile 变化并通知父 View。</summary>
        /// <param name="serializedObject">变化的 Profile 序列化对象。</param>
        private void OnGrowthProfileChanged(SerializedObject serializedObject)
        {
            if (suppressCallbacks || serializedObject == null || serializedObject != growthProfileSerializedObject || boundConfig == null) return;
            serializedObject.UpdateIfRequiredOrScript();
            RefreshPresentation();
            PropertiesChanged?.Invoke(boundConfig, string.Empty);
        }

        #endregion

        #region 内部辅助

        /// <summary>从已克隆模板取得指定控件。</summary>
        /// <typeparam name="T">控件类型。</typeparam>
        /// <param name="name">控件名称。</param>
        /// <returns>找到的控件。</returns>
        private T Require<T>(string name) where T : VisualElement
        {
            T element = pageRoot.Q<T>(name);
            if (element == null) throw new InvalidOperationException($"角色成长详情缺少控件：{name}。");
            return element;
        }

        #endregion
    }
}
#endif
