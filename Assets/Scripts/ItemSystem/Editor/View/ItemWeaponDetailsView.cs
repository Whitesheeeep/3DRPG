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
    /// <summary>常驻复用的武器配置和成长 Profile 详情 View。</summary>
    internal sealed class ItemWeaponDetailsView : IDisposable
    {
        #region 依赖字段

        // 固定视觉树和序列化绑定容器由窗口初始化一次，切换武器时只更换绑定目标。
        private readonly VisualElement pageRoot;
        private readonly VisualTreeAsset weaponTemplate;
        private readonly VisualElement weaponBaseFields;
        private readonly VisualElement ascensionStageHost;
        private readonly VisualElement refinementStageHost;
        private readonly VisualElement growthProfileContent;
        private readonly PropertyField levelEffectsField;
        private readonly PropertyField refinementEffectsField;
        private readonly string levelEffectsHeader;
        private readonly string refinementEffectsHeader;
        private readonly HashSet<ListView> expandedEffectListViewSet = new();
        private readonly Label ascensionStageTitle;
        private readonly Label refinementStageTitle;
        private readonly PropertyField growthProfileMaxLevelField;
        private readonly Label bakedSummaryLabel;
        private readonly Button bakeButton;
        private readonly Button viewBakedResultButton;
        private readonly VisualElement missingGrowthProfileWarning;

        #endregion

        #region 绑定状态

        private WeaponDefinition boundWeapon;
        private SerializedObject definitionSerializedObject;
        private WeaponGrowthProfile boundGrowthProfile;
        private SerializedObject growthProfileSerializedObject;
        private VisualElement growthProfileTracker;
        private int bindingVersion;
        private bool labelRefreshScheduled;
        private bool disposed;

        #endregion

        #region 事件

        /// <summary>请求 Controller 烘焙当前武器成长表。</summary>
        internal event Action BakeGrowthRequested;

        /// <summary>请求打开当前武器的独立烘焙结果窗口。</summary>
        internal event Action ViewBakedResultRequested;

        /// <summary>成长 Profile 字段发生变化。</summary>
        internal event Action<ItemDefinition> PropertiesChanged;

        #endregion

        #region 生命周期与初始化

        /// <summary>创建一次武器详情视觉树和全部可复用控件。</summary>
        /// <param name="parent">武器详情页面根节点。</param>
        internal ItemWeaponDetailsView(VisualElement parent)
        {
            pageRoot = parent ?? throw new ArgumentNullException(nameof(parent));
            weaponTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                UxmlUssPathConstants.Uxml.AssetsScriptsItemSystemEditorStyleItemWeaponDetails);
            if (weaponTemplate == null)
                throw new InvalidOperationException("物品配置窗口缺少武器详情 UXML。");

            weaponTemplate.CloneTree(pageRoot);
            weaponBaseFields = Require<VisualElement>("WeaponBaseFields");
            ascensionStageHost = Require<VisualElement>("AscensionStageHost");
            refinementStageHost = Require<VisualElement>("RefinementStageHost");
            growthProfileContent = Require<VisualElement>("GrowthProfileContent");
            levelEffectsField = Require<PropertyField>("LevelEffectsField");
            refinementEffectsField = Require<PropertyField>("RefinementEffectsField");
            levelEffectsHeader = levelEffectsField.label;
            refinementEffectsHeader = refinementEffectsField.label;
            ascensionStageTitle = Require<Label>("AscensionStageTitle");
            refinementStageTitle = Require<Label>("RefinementStageTitle");
            missingGrowthProfileWarning = Require<VisualElement>("MissingGrowthProfileWarning");
            growthProfileMaxLevelField = Require<PropertyField>("GrowthProfileMaxLevelField");
            growthProfileMaxLevelField.SetEnabled(false);
            bakedSummaryLabel = Require<VisualElement>("BakedSummary").Q<Label>("BakedSummaryLabel");
            if (bakedSummaryLabel == null)
                throw new InvalidOperationException("武器详情 UXML 缺少烘焙摘要标签：BakedSummaryLabel。");

            bakeButton = Require<Button>("BakeButton");
            viewBakedResultButton = Require<Button>("ViewBakedResultButton");
            bakeButton.clicked += OnBakeButtonClicked;
            viewBakedResultButton.clicked += OnViewBakedResultButtonClicked;
            pageRoot.RegisterCallback<GeometryChangedEvent>(OnPageGeometryChanged);
            SetVisible(false);
            UpdateEmptyPresentation();
        }

        /// <summary>释放绑定、Tracker、展示刷新回调和按钮事件。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            pageRoot.UnregisterCallback<GeometryChangedEvent>(OnPageGeometryChanged);
            bakeButton.clicked -= OnBakeButtonClicked;
            viewBakedResultButton.clicked -= OnViewBakedResultButtonClicked;
            Unbind();
            expandedEffectListViewSet.Clear();
            BakeGrowthRequested = null;
            ViewBakedResultRequested = null;
            PropertiesChanged = null;
            Debug.Log("[ItemWeaponDetailsView] 释放武器详情绑定和事件。");
        }

        #endregion

        #region 绑定与显隐

        /// <summary>绑定一个武器定义；视觉树和字段控件不会重建。</summary>
        /// <param name="weapon">当前武器。</param>
        /// <param name="definitionObject">父详情持有的武器 SerializedObject。</param>
        internal void Bind(WeaponDefinition weapon, SerializedObject definitionObject)
        {
            if (disposed) throw new ObjectDisposedException(nameof(ItemWeaponDetailsView));
            if (weapon == null) throw new ArgumentNullException(nameof(weapon));
            if (definitionObject == null) throw new ArgumentNullException(nameof(definitionObject));
            if (ReferenceEquals(boundWeapon, weapon) && ReferenceEquals(definitionSerializedObject, definitionObject))
            {
                RefreshPresentation();
                SetVisible(true);
                return;
            }

            Unbind();
            boundWeapon = weapon;
            definitionSerializedObject = definitionObject;
            definitionSerializedObject.UpdateIfRequiredOrScript();
            weaponBaseFields.Bind(definitionSerializedObject);
            ascensionStageHost.Bind(definitionSerializedObject);
            refinementStageHost.Bind(definitionSerializedObject);
            BindGrowthProfile(weapon.GrowthProfile);
            UpdateStageTitles();
            RefreshBakedProgressions(weapon.GrowthProfile);
            ScheduleVisibleFieldLabelRefresh();
            SetVisible(true);
            Debug.Log($"[ItemWeaponDetailsView] 绑定武器详情：{weapon.ItemId}。");
        }

        /// <summary>解除当前武器和成长 Profile 的数据绑定，但保留控件树。</summary>
        internal void Unbind()
        {
            bindingVersion++;
            labelRefreshScheduled = false;
            growthProfileContent?.Unbind();
            weaponBaseFields?.Unbind();
            ascensionStageHost?.Unbind();
            refinementStageHost?.Unbind();
            ReleaseGrowthProfileBinding();
            boundWeapon = null;
            definitionSerializedObject = null;
            UpdateEmptyPresentation();
            SetVisible(false);
        }

        /// <summary>设置武器详情页面显隐。</summary>
        /// <param name="visible">是否显示。</param>
        internal void SetVisible(bool visible) => pageRoot.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        #endregion

        #region 状态刷新

        /// <summary>轻量刷新阶段标题、Profile 引用、字段标签和烘焙结果。</summary>
        internal void RefreshPresentation()
        {
            if (boundWeapon == null) return;
            if (!ReferenceEquals(boundGrowthProfile, boundWeapon.GrowthProfile))
                BindGrowthProfile(boundWeapon.GrowthProfile);
            UpdateStageTitles();
            RefreshBakedProgressions(boundGrowthProfile);
            ScheduleVisibleFieldLabelRefresh();
        }

        /// <summary>合并当前绑定版本的可见标签与原生集合标题刷新。</summary>
        private void ScheduleVisibleFieldLabelRefresh()
        {
            if (disposed || boundWeapon == null || labelRefreshScheduled) return;
            labelRefreshScheduled = true;
            int scheduledVersion = bindingVersion;
            pageRoot.schedule.Execute(() =>
            {
                labelRefreshScheduled = false;
                if (disposed || scheduledVersion != bindingVersion || boundWeapon == null) return;
                // 列表绑定完成后只设置原生展示属性和 Foldout 标题，不替换 Unity 的 bindItem。
                ItemConfigEditorPresentation.ConfigureNativeCollectionHeader(
                    levelEffectsField,
                    levelEffectsHeader,
                    expandedEffectListViewSet);
                ItemConfigEditorPresentation.ConfigureNativeCollectionHeader(
                    refinementEffectsField,
                    refinementEffectsHeader,
                    expandedEffectListViewSet);
                // 动态字段标签只更新当前已生成节点，数组元素标题与空状态保留 Unity 默认值。
                VisibleSerializedFieldLabelLocalizer.Apply(pageRoot);
            });
        }

        /// <summary>布局生成或虚拟化行进入视觉树后请求一次可见字段标签刷新。</summary>
        /// <param name="eventData">布局变化事件。</param>
        private void OnPageGeometryChanged(GeometryChangedEvent eventData)
        {
            if (disposed || boundWeapon == null) return;
            ScheduleVisibleFieldLabelRefresh();
        }

        /// <summary>更新没有成长 Profile 时的固定视觉状态。</summary>
        private void UpdateEmptyPresentation()
        {
            missingGrowthProfileWarning.style.display = DisplayStyle.None;
            growthProfileContent.style.display = DisplayStyle.None;
            bakedSummaryLabel.text = "烘焙结果：未配置成长配置。";
            bakeButton.SetEnabled(false);
            viewBakedResultButton.SetEnabled(false);
        }

        /// <summary>更新突破和精炼配置标题。</summary>
        private void UpdateStageTitles()
        {
            if (boundWeapon == null) return;
            ascensionStageTitle.text =
                $"突破配置（已配置 {boundWeapon.AscensionStages?.Count ?? 0} 项，最大突破阶数 {boundWeapon.MaxAscensionRank}）";
            refinementStageTitle.text =
                $"精炼配置（已配置 {boundWeapon.RefinementStages?.Count ?? 0} 项，最大精炼阶数 {boundWeapon.MaxRefinementRank}）";
        }

        #endregion

        #region 成长 Profile 绑定

        /// <summary>绑定新的成长 Profile，同时释放旧 Profile 的原生对象。</summary>
        /// <param name="profile">新的成长 Profile；为空时显示警告。</param>
        private void BindGrowthProfile(WeaponGrowthProfile profile)
        {
            growthProfileContent.Unbind();
            ReleaseGrowthProfileBinding();
            boundGrowthProfile = profile;
            if (profile == null)
            {
                missingGrowthProfileWarning.style.display = DisplayStyle.Flex;
                growthProfileContent.style.display = DisplayStyle.None;
                bakedSummaryLabel.text = "烘焙结果：未配置成长配置。";
                bakeButton.SetEnabled(false);
                viewBakedResultButton.SetEnabled(false);
                return;
            }

            missingGrowthProfileWarning.style.display = DisplayStyle.None;
            growthProfileContent.style.display = DisplayStyle.Flex;
            growthProfileSerializedObject = new SerializedObject(profile);
            growthProfileSerializedObject.UpdateIfRequiredOrScript();
            growthProfileContent.Bind(growthProfileSerializedObject);
            growthProfileTracker = CreateGrowthProfileTracker(growthProfileSerializedObject);
            RefreshBakedProgressions(profile);
            ScheduleVisibleFieldLabelRefresh();
        }

        /// <summary>创建成长 Profile 的隐藏变化 Tracker。</summary>
        /// <param name="serializedObject">Profile SerializedObject。</param>
        /// <returns>隐藏 Tracker 节点。</returns>
        private VisualElement CreateGrowthProfileTracker(SerializedObject serializedObject)
        {
            var tracker = new VisualElement { name = "GrowthProfileTracker" };
            tracker.style.display = DisplayStyle.None;
            growthProfileContent.Add(tracker);
            tracker.TrackSerializedObjectValue(serializedObject, OnGrowthProfileChanged);
            return tracker;
        }

        /// <summary>处理 Profile 字段变化并通知父详情。</summary>
        /// <param name="serializedObject">发生变化的 Profile 对象。</param>
        private void OnGrowthProfileChanged(SerializedObject serializedObject)
        {
            if (serializedObject == null || serializedObject != growthProfileSerializedObject || boundWeapon == null)
                return;
            UpdateStageTitles();
            RefreshBakedProgressions(boundGrowthProfile);
            ScheduleVisibleFieldLabelRefresh();
            PropertiesChanged?.Invoke(boundWeapon);
        }

        /// <summary>释放 Profile Tracker 和 SerializedObject。</summary>
        private void ReleaseGrowthProfileBinding()
        {
            growthProfileTracker?.RemoveFromHierarchy();
            growthProfileTracker = null;
            if (growthProfileSerializedObject != null)
            {
                growthProfileContent.Unbind();
                growthProfileSerializedObject.Dispose();
                growthProfileSerializedObject = null;
            }
            boundGrowthProfile = null;
        }

        #endregion

        #region 烘焙结果与事件辅助

        /// <summary>刷新烘焙数据源和只读摘要。</summary>
        /// <param name="profile">成长 Profile。</param>
        private void RefreshBakedProgressions(WeaponGrowthProfile profile)
        {
            bakedSummaryLabel.text = profile == null
                ? "烘焙结果：未配置成长配置。"
                : profile.BakedProgressions.Count == 0
                    ? "烘焙结果：尚未生成，请先编辑曲线后烘焙。"
                    : $"烘焙结果：已生成 {profile.BakedProgressions.Count} 个等级条目，等级 1 至 {profile.MaxLevel}。";
            bakeButton.SetEnabled(profile != null);
            viewBakedResultButton.SetEnabled(profile != null);
        }

        /// <summary>将 UXML 中的 Bake 按钮点击转发给 Controller。</summary>
        private void OnBakeButtonClicked() => BakeGrowthRequested?.Invoke();

        /// <summary>将查看烘焙结果按钮点击转发给 Controller。</summary>
        private void OnViewBakedResultButtonClicked() => ViewBakedResultRequested?.Invoke();

        /// <summary>查询页面内的必需控件。</summary>
        /// <typeparam name="TElement">控件类型。</typeparam>
        /// <param name="name">UXML 名称。</param>
        /// <returns>对应控件。</returns>
        private TElement Require<TElement>(string name) where TElement : VisualElement
        {
            TElement element = pageRoot.Q<TElement>(name);
            if (element == null) throw new InvalidOperationException($"武器详情 UXML 缺少控件：{name}。");
            return element;
        }

        #endregion
    }
}
#endif
