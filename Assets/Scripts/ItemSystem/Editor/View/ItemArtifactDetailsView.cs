#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.UIModule.Editor;

namespace RPG.ItemSystem.Editor
{
    /// <summary>常驻复用的圣遗物成长配置详情 View。</summary>
    internal sealed class ItemArtifactDetailsView : IDisposable
    {
        #region 依赖字段

        // 页面控件只创建一次；绑定周期只负责切换 Definition 和 GrowthProfile 的 SerializedObject。
        private readonly VisualElement pageRoot;
        private readonly VisualTreeAsset artifactTemplate;
        private readonly VisualElement artifactBaseFields;
        private readonly VisualElement artifactGrowthProfileContent;
        private readonly VisualElement missingGrowthProfileWarning;
        private readonly PropertyField growthProfileMaxLevelField;
        private readonly Label bakedSummaryLabel;
        private readonly Button bakeButton;
        private readonly Button viewBakedResultButton;

        #endregion

        #region 绑定状态

        private ArtifactDefinition boundArtifact;
        private SerializedObject definitionSerializedObject;
        private ArtifactGrowthProfile boundGrowthProfile;
        private SerializedObject growthProfileSerializedObject;
        private VisualElement growthProfileTracker;
        private int bindingVersion;
        private bool labelRefreshScheduled;
        private bool disposed;

        #endregion

        #region 事件

        /// <summary>请求 Controller 烘焙圣遗物成长表。</summary>
        internal event Action BakeGrowthRequested;

        /// <summary>请求打开当前圣遗物的独立烘焙结果窗口。</summary>
        internal event Action ViewBakedResultRequested;

        /// <summary>圣遗物字段发生变化。</summary>
        internal event Action<ItemDefinition> PropertiesChanged;

        #endregion

        #region 生命周期与初始化

        /// <summary>创建一次圣遗物详情视觉树和烘焙列表。</summary>
        /// <param name="parent">圣遗物页面根节点。</param>
        internal ItemArtifactDetailsView(VisualElement parent)
        {
            pageRoot = parent ?? throw new ArgumentNullException(nameof(parent));
            artifactTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                UxmlUssPathConstants.Uxml.AssetsScriptsItemSystemEditorStyleItemArtifactDetails);
            if (artifactTemplate == null)
                throw new InvalidOperationException("物品配置窗口缺少圣遗物详情 UXML。");

            artifactTemplate.CloneTree(pageRoot);
            artifactBaseFields = Require<VisualElement>("ArtifactBaseFields");
            artifactGrowthProfileContent = Require<VisualElement>("ArtifactGrowthProfileContent");
            missingGrowthProfileWarning = Require<VisualElement>("MissingArtifactGrowthProfileWarning");
            growthProfileMaxLevelField = Require<PropertyField>("ArtifactGrowthProfileMaxLevelField");
            growthProfileMaxLevelField.SetEnabled(false);
            bakedSummaryLabel = Require<VisualElement>("ArtifactBakedSummary").Q<Label>("ArtifactBakedSummaryLabel");
            if (bakedSummaryLabel == null)
                throw new InvalidOperationException("圣遗物详情 UXML 缺少烘焙摘要标签：ArtifactBakedSummaryLabel。");

            bakeButton = Require<Button>("ArtifactBakeButton");
            viewBakedResultButton = Require<Button>("ArtifactViewBakedResultButton");
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
            BakeGrowthRequested = null;
            ViewBakedResultRequested = null;
            PropertiesChanged = null;
            Debug.Log("[ItemArtifactDetailsView] 释放圣遗物详情绑定和事件。");
        }

        #endregion

        #region 绑定与显隐

        /// <summary>绑定圣遗物 Definition 和父级 SerializedObject。</summary>
        /// <param name="artifact">当前圣遗物定义。</param>
        /// <param name="definitionObject">父详情持有的 Definition SerializedObject。</param>
        internal void Bind(ArtifactDefinition artifact, SerializedObject definitionObject)
        {
            if (disposed) throw new ObjectDisposedException(nameof(ItemArtifactDetailsView));
            if (artifact == null) throw new ArgumentNullException(nameof(artifact));
            if (definitionObject == null) throw new ArgumentNullException(nameof(definitionObject));
            if (ReferenceEquals(boundArtifact, artifact) && ReferenceEquals(definitionSerializedObject, definitionObject))
            {
                RefreshPresentation();
                SetVisible(true);
                return;
            }

            Unbind();
            boundArtifact = artifact;
            definitionSerializedObject = definitionObject;
            definitionSerializedObject.UpdateIfRequiredOrScript();
            artifactBaseFields.Bind(definitionSerializedObject);
            BindGrowthProfile(artifact.GrowthProfile);
            ScheduleVisibleFieldLabelRefresh();
            SetVisible(true);
            Debug.Log($"[ItemArtifactDetailsView] 绑定圣遗物详情：{artifact.ItemId}。");
        }

        /// <summary>解除圣遗物绑定并保留常驻控件树。</summary>
        internal void Unbind()
        {
            bindingVersion++;
            labelRefreshScheduled = false;
            artifactBaseFields?.Unbind();
            artifactGrowthProfileContent?.Unbind();
            ReleaseGrowthProfileBinding();
            boundArtifact = null;
            definitionSerializedObject = null;
            UpdateEmptyPresentation();
            SetVisible(false);
        }

        /// <summary>设置圣遗物页面显隐。</summary>
        /// <param name="visible">是否显示。</param>
        internal void SetVisible(bool visible) => pageRoot.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        #endregion

        #region 状态刷新

        /// <summary>轻量刷新 Profile 引用、烘焙状态和可见字段标签。</summary>
        internal void RefreshPresentation()
        {
            if (boundArtifact == null) return;
            if (!ReferenceEquals(boundGrowthProfile, boundArtifact.GrowthProfile))
                BindGrowthProfile(boundArtifact.GrowthProfile);
            RefreshBakedProgressions(boundGrowthProfile);
            ScheduleVisibleFieldLabelRefresh();
        }

        /// <summary>合并当前绑定版本的可见字段标签刷新，不介入 ListView 原生绑定。</summary>
        private void ScheduleVisibleFieldLabelRefresh()
        {
            if (disposed || boundArtifact == null || labelRefreshScheduled) return;
            labelRefreshScheduled = true;
            int scheduledVersion = bindingVersion;
            pageRoot.schedule.Execute(() =>
            {
                labelRefreshScheduled = false;
                if (disposed || scheduledVersion != bindingVersion || boundArtifact == null) return;
                VisibleSerializedFieldLabelLocalizer.Apply(pageRoot);
            });
        }

        /// <summary>布局生成或虚拟化行进入视觉树后请求一次可见字段标签刷新。</summary>
        /// <param name="eventData">布局变化事件。</param>
        private void OnPageGeometryChanged(GeometryChangedEvent eventData)
        {
            if (disposed || boundArtifact == null) return;
            ScheduleVisibleFieldLabelRefresh();
        }

        /// <summary>更新没有 Profile 时的固定空状态。</summary>
        private void UpdateEmptyPresentation()
        {
            missingGrowthProfileWarning.style.display = DisplayStyle.None;
            artifactGrowthProfileContent.style.display = DisplayStyle.None;
            bakedSummaryLabel.text = "烘焙结果：未配置成长配置。";
            bakeButton.SetEnabled(false);
            viewBakedResultButton.SetEnabled(false);
        }

        #endregion

        #region 成长 Profile 绑定

        /// <summary>绑定 Profile 或显示缺失配置警告。</summary>
        /// <param name="profile">成长配置。</param>
        private void BindGrowthProfile(ArtifactGrowthProfile profile)
        {
            artifactGrowthProfileContent.Unbind();
            ReleaseGrowthProfileBinding();
            boundGrowthProfile = profile;
            if (profile == null)
            {
                missingGrowthProfileWarning.style.display = DisplayStyle.Flex;
                artifactGrowthProfileContent.style.display = DisplayStyle.None;
                bakedSummaryLabel.text = "烘焙结果：未配置成长配置。";
                bakeButton.SetEnabled(false);
                viewBakedResultButton.SetEnabled(false);
                return;
            }

            missingGrowthProfileWarning.style.display = DisplayStyle.None;
            artifactGrowthProfileContent.style.display = DisplayStyle.Flex;
            growthProfileSerializedObject = new SerializedObject(profile);
            growthProfileSerializedObject.UpdateIfRequiredOrScript();
            artifactGrowthProfileContent.Bind(growthProfileSerializedObject);
            growthProfileTracker = CreateGrowthProfileTracker(growthProfileSerializedObject);
            RefreshBakedProgressions(profile);
            ScheduleVisibleFieldLabelRefresh();
        }

        /// <summary>创建 Profile 字段变化 Tracker。</summary>
        /// <param name="serializedObject">Profile SerializedObject。</param>
        /// <returns>隐藏 Tracker。</returns>
        private VisualElement CreateGrowthProfileTracker(SerializedObject serializedObject)
        {
            var tracker = new VisualElement { name = "ArtifactGrowthProfileTracker" };
            tracker.style.display = DisplayStyle.None;
            artifactGrowthProfileContent.Add(tracker);
            tracker.TrackSerializedObjectValue(serializedObject, OnGrowthProfileChanged);
            return tracker;
        }

        /// <summary>响应成长 Profile 字段变化。</summary>
        /// <param name="serializedObject">发生变化的对象。</param>
        private void OnGrowthProfileChanged(SerializedObject serializedObject)
        {
            if (serializedObject == null || serializedObject != growthProfileSerializedObject || boundArtifact == null)
                return;
            RefreshBakedProgressions(boundGrowthProfile);
            ScheduleVisibleFieldLabelRefresh();
            PropertiesChanged?.Invoke(boundArtifact);
        }

        /// <summary>释放 Profile Tracker 和 SerializedObject。</summary>
        private void ReleaseGrowthProfileBinding()
        {
            growthProfileTracker?.RemoveFromHierarchy();
            growthProfileTracker = null;
            if (growthProfileSerializedObject != null)
            {
                artifactGrowthProfileContent.Unbind();
                growthProfileSerializedObject.Dispose();
                growthProfileSerializedObject = null;
            }
            boundGrowthProfile = null;
        }

        #endregion

        #region 烘焙结果与事件辅助

        /// <summary>刷新圣遗物烘焙结果。</summary>
        /// <param name="profile">成长配置。</param>
        private void RefreshBakedProgressions(ArtifactGrowthProfile profile)
        {
            bakedSummaryLabel.text = profile == null
                ? "烘焙结果：未配置成长配置。"
                : profile.BakedProgressions.Count == 0
                    ? "烘焙结果：尚未生成，请先编辑曲线后烘焙。"
                    : $"烘焙结果：已生成 {profile.BakedProgressions.Count} 个等级条目，等级 0 至 {profile.MaxLevel}。";
            bakeButton.SetEnabled(profile != null);
            viewBakedResultButton.SetEnabled(profile != null);
        }

        /// <summary>转发烘焙请求。</summary>
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
            if (element == null) throw new InvalidOperationException($"圣遗物详情 UXML 缺少控件：{name}。");
            return element;
        }

        #endregion
    }
}
#endif
