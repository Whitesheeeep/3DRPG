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
    /// <summary>常驻复用的圣遗物成长配置详情 View。</summary>
    internal sealed class ItemArtifactDetailsView : IDisposable
    {
        #region 字段

        private readonly VisualElement pageRoot;
        private readonly VisualTreeAsset artifactTemplate;
        private readonly VisualElement artifactBaseFields;
        private readonly VisualElement artifactGrowthProfileContent;
        private readonly VisualElement missingGrowthProfileWarning;
        private readonly PropertyField growthProfileMaxLevelField;
        private readonly PropertyField levelEffectsField;
        private readonly PropertyField levelOverridesField;
        private readonly List<(PropertyField field, string label)> fixedPropertyLabels = new();
        private readonly Label bakedSummaryLabel;
        private readonly Button bakeButton;
        private readonly Button viewBakedResultButton;

        private ArtifactDefinition boundArtifact;
        private SerializedObject definitionSerializedObject;
        private ArtifactGrowthProfile boundGrowthProfile;
        private SerializedObject growthProfileSerializedObject;
        private VisualElement growthProfileTracker;
        private int bindingVersion;
        private bool effectListConfigured;
        private bool structureRefreshScheduled;
        private int scheduledStructureVersion = -1;
        private bool revealAfterStructureRefresh;
        private int lastGrowthStructureSignature = int.MinValue;
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

        #region 生命周期

        /// <summary>创建一次圣遗物详情视觉树和烘焙列表。</summary>
        /// <param name="parent">圣遗物页面根节点。</param>
        internal ItemArtifactDetailsView(VisualElement parent)
        {
            pageRoot = parent ?? throw new ArgumentNullException(nameof(parent));
            artifactTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                UxmlUssPathConstants.Uxml.AssetsScriptsItemSystemEditorStyleItemArtifactDetails);
            if (artifactTemplate == null) throw new InvalidOperationException("物品配置窗口缺少圣遗物详情 UXML。");

            artifactTemplate.CloneTree(pageRoot);
            artifactBaseFields = Require<VisualElement>("ArtifactBaseFields");
            artifactGrowthProfileContent = Require<VisualElement>("ArtifactGrowthProfileContent");
            missingGrowthProfileWarning = Require<VisualElement>("MissingArtifactGrowthProfileWarning");
            growthProfileMaxLevelField = Require<PropertyField>("ArtifactGrowthProfileMaxLevelField");
            growthProfileMaxLevelField.SetEnabled(false);
            levelEffectsField = Require<PropertyField>("ArtifactLevelEffectsField");
            levelOverridesField = Require<PropertyField>("ArtifactLevelOverridesField");
            bakedSummaryLabel = Require<VisualElement>("ArtifactBakedSummary").Q<Label>("ArtifactBakedSummaryLabel");
            if (bakedSummaryLabel == null) throw new InvalidOperationException("圣遗物详情 UXML 缺少烘焙摘要标签。");
            bakeButton = Require<Button>("ArtifactBakeButton");
            viewBakedResultButton = Require<Button>("ArtifactViewBakedResultButton");
            CacheFixedPropertyLabels(artifactBaseFields);
            CacheFixedPropertyLabels(artifactGrowthProfileContent);
            bakeButton.clicked += OnBakeButtonClicked;
            viewBakedResultButton.clicked += OnViewBakedResultButtonClicked;
            SetVisible(false);
            UpdateEmptyPresentation();
        }

        /// <summary>释放绑定、Tracker、列表回调和事件。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            bakeButton.clicked -= OnBakeButtonClicked;
            viewBakedResultButton.clicked -= OnViewBakedResultButtonClicked;
            Unbind();
            BakeGrowthRequested = null;
            ViewBakedResultRequested = null;
            PropertiesChanged = null;
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
                if (structureRefreshScheduled) revealAfterStructureRefresh = true;
                else SetVisible(true);
                return;
            }

            Unbind();
            boundArtifact = artifact;
            definitionSerializedObject = definitionObject;
            definitionSerializedObject.UpdateIfRequiredOrScript();
            artifactBaseFields.Bind(definitionSerializedObject);
            BindGrowthProfile(artifact.GrowthProfile);
            ScheduleStructureRefresh(true, true);
        }

        /// <summary>解除圣遗物绑定并清空动态列表，保留常驻控件树。</summary>
        internal void Unbind()
        {
            bindingVersion++;
            structureRefreshScheduled = false;
            scheduledStructureVersion = -1;
            revealAfterStructureRefresh = false;
            artifactBaseFields?.Unbind();
            ReleaseGrowthProfileBinding();
            boundArtifact = null;
            definitionSerializedObject = null;
            lastGrowthStructureSignature = int.MinValue;
            effectListConfigured = false;
            UpdateEmptyPresentation();
            SetVisible(false);
        }

        /// <summary>设置圣遗物页面显隐。</summary>
        /// <param name="visible">是否显示。</param>
        internal void SetVisible(bool visible) => pageRoot.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        #endregion

        #region 状态刷新

        /// <summary>轻量刷新 Profile 引用、标题、烘焙状态和结构文本。</summary>
        internal void RefreshPresentation()
        {
            if (boundArtifact == null) return;
            if (!ReferenceEquals(boundGrowthProfile, boundArtifact.GrowthProfile)) BindGrowthProfile(boundArtifact.GrowthProfile);
            RefreshBakedProgressions(boundGrowthProfile);
            ScheduleStructureRefresh();
        }

        /// <summary>合并当前绑定版本的动态结构中文化任务。</summary>
        /// <param name="revealAfterRefresh">中文化后是否显示页面。</param>
        /// <param name="force">是否强制执行。</param>
        private void ScheduleStructureRefresh(bool revealAfterRefresh = false, bool force = false)
        {
            if (boundArtifact == null) return;
            int signature = ComputeGrowthStructureSignature();
            if (!force && signature == lastGrowthStructureSignature && !revealAfterRefresh) return;
            if (structureRefreshScheduled && scheduledStructureVersion == bindingVersion)
            {
                this.revealAfterStructureRefresh |= revealAfterRefresh;
                return;
            }

            structureRefreshScheduled = true;
            scheduledStructureVersion = bindingVersion;
            this.revealAfterStructureRefresh |= revealAfterRefresh;
            int scheduledVersion = bindingVersion;
            pageRoot.schedule.Execute(() =>
            {
                if (disposed || scheduledVersion != bindingVersion || boundArtifact == null) return;
                ConfigureLevelOverridesList();
                ItemConfigEditorPresentation.ConfigureGameplayEffectList(
                    levelEffectsField,
                    "暂无圣遗物等级效果",
                    "等级效果",
                    !effectListConfigured);
                effectListConfigured = true;
                NormalizeDynamicLabels();
                lastGrowthStructureSignature = ComputeGrowthStructureSignature();
                structureRefreshScheduled = false;
                scheduledStructureVersion = -1;
                bool reveal = this.revealAfterStructureRefresh;
                this.revealAfterStructureRefresh = false;
                if (reveal) SetVisible(true);
            });
        }

        /// <summary>计算特殊等级覆盖数量签名。</summary>
        /// <returns>结构签名。</returns>
        private int ComputeGrowthStructureSignature() => boundGrowthProfile?.LevelOverrides?.Count ?? 0;

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
            ScheduleStructureRefresh(force: true);
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
        /// <param name="serializedObject">发生变化的 Profile。</param>
        private void OnGrowthProfileChanged(SerializedObject serializedObject)
        {
            if (serializedObject == null || serializedObject != growthProfileSerializedObject || boundArtifact == null) return;
            ScheduleStructureRefresh();
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

        #region 烘焙结果与动态结构

        /// <summary>配置特殊等级覆盖的原生 ListView。</summary>
        private void ConfigureLevelOverridesList()
        {
            levelOverridesField.Query<ListView>().ForEach(listView =>
            {
                listView.showBoundCollectionSize = false;
                listView.showFoldoutHeader = true;
                listView.showAddRemoveFooter = true;
                listView.AddToClassList("item-editor-stage-list");
            });
        }

        /// <summary>将动态列表元素标题和字段标签转换为中文。</summary>
        private void NormalizeDynamicLabels()
        {
            RestoreFixedPropertyLabels();
            pageRoot.Query<PropertyField>().ForEach(field =>
            {
                if (field.bindingPath.EndsWith("nextExperience", StringComparison.Ordinal)) field.label = "下一级所需经验";
                else if (field.bindingPath.EndsWith("currencyCost", StringComparison.Ordinal)) field.label = "货币消耗";
                else if (field.bindingPath.EndsWith("level", StringComparison.Ordinal) && field.bindingPath.Contains("levelOverrides", StringComparison.Ordinal)) field.label = "等级";
            });
            pageRoot.Query<Label>().ForEach(label =>
            {
                string text = label.text ?? string.Empty;
                if (text.StartsWith("Element ", StringComparison.Ordinal) && int.TryParse(text.Substring(8), out int index))
                    label.text = $"特殊等级覆盖 {index + 1}";
                else if (text == "List is empty") label.text = "暂无特殊等级覆盖";
            });
        }

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

        /// <summary>缓存固定字段 Label。</summary>
        /// <param name="container">字段容器。</param>
        private void CacheFixedPropertyLabels(VisualElement container) => container.Query<PropertyField>().ForEach(field => fixedPropertyLabels.Add((field, field.label)));

        /// <summary>恢复 UXML 固定字段 Label。</summary>
        private void RestoreFixedPropertyLabels()
        {
            for (int index = 0; index < fixedPropertyLabels.Count; index++)
                if (fixedPropertyLabels[index].field != null) fixedPropertyLabels[index].field.label = fixedPropertyLabels[index].label;
        }

        #endregion

        #region 事件与辅助

        /// <summary>转发烘焙请求。</summary>
        private void OnBakeButtonClicked() => BakeGrowthRequested?.Invoke();

        /// <summary>将查看烘焙结果按钮点击转发给 Controller。</summary>
        private void OnViewBakedResultButtonClicked() => ViewBakedResultRequested?.Invoke();

        /// <summary>查询页面内的必需控件。</summary>
        /// <typeparam name="TElement">控件类型。</typeparam>
        /// <param name="name">UXML 名称。</param>
        /// <returns>找到的控件。</returns>
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
