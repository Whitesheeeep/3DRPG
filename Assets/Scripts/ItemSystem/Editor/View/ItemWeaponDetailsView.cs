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
    /// <summary>常驻复用的武器配置、成长 Profile 和烘焙结果详情 View。</summary>
    internal sealed class ItemWeaponDetailsView : IDisposable
    {
        #region 字段

        private readonly VisualElement pageRoot;
        private readonly VisualTreeAsset weaponTemplate;
        private readonly VisualTreeAsset bakedRowTemplate;
        private readonly List<BakedWeaponLevelProgression> displayedBakedProgressions = new();
        private readonly List<(PropertyField field, string label)> fixedPropertyLabels = new();

        private readonly VisualElement weaponBaseFields;
        private readonly VisualElement ascensionStageHost;
        private readonly VisualElement refinementStageHost;
        private readonly VisualElement growthProfileContent;
        private readonly Label ascensionStageTitle;
        private readonly Label refinementStageTitle;
        private readonly PropertyField growthProfileMaxLevelField;
        private readonly PropertyField levelEffectsField;
        private readonly PropertyField refinementEffectsField;
        private readonly PropertyField levelOverridesField;
        private Label bakedSummaryLabel;
        private Button bakeButton;
        private VisualElement missingGrowthProfileWarning;
        private ListView bakedProgressionList;

        private WeaponDefinition boundWeapon;
        private SerializedObject definitionSerializedObject;
        private WeaponGrowthProfile boundGrowthProfile;
        private SerializedObject growthProfileSerializedObject;
        private VisualElement growthProfileTracker;
        private int bindingVersion;
        private bool stageListsConfigured;
        private bool effectListsConfigured;
        // 成长 Profile 切换后，LevelOverridesField 可能重新生成内部 ListView，因此单独记录其配置状态。
        private bool levelOverridesListConfigured;
        // 结构签名与调度状态用于合并 Tracker 请求，并让首轮中文化先于页面显示。
        private bool structureRefreshScheduled;
        private int scheduledStructureVersion = -1;
        private bool revealAfterStructureRefresh;
        private int lastAscensionStructureSignature = int.MinValue;
        private int lastRefinementStructureSignature = int.MinValue;
        private int lastGrowthStructureSignature = int.MinValue;
        private bool disposed;

        #endregion

        #region 事件

        /// <summary>请求 Controller 烘焙当前武器成长表。</summary>
        internal event Action BakeGrowthRequested;

        /// <summary>成长 Profile 字段发生变化。</summary>
        internal event Action<ItemDefinition> PropertiesChanged;

        #endregion

        #region 事件处理

        /// <summary>将 UXML 中的 Bake 按钮点击转发给 Controller。</summary>
        private void OnBakeButtonClicked()
        {
            BakeGrowthRequested?.Invoke();
        }

        #endregion

        #region 生命周期与初始化

        /// <summary>创建一次武器详情视觉树和全部可复用控件。</summary>
        /// <param name="parent">武器详情页面根节点。</param>
        internal ItemWeaponDetailsView(VisualElement parent)
        {
            pageRoot = parent ?? throw new ArgumentNullException(nameof(parent));
            weaponTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                UxmlUssPathConstants.Uxml.AssetsScriptsItemSystemEditorStyleItemWeaponDetails);
            bakedRowTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                UxmlUssPathConstants.Uxml.AssetsScriptsItemSystemEditorStyleItemBakedProgressionRow);
            if (weaponTemplate == null)
                throw new InvalidOperationException("物品配置窗口缺少武器详情 UXML。");
            if (bakedRowTemplate == null)
                throw new InvalidOperationException("物品配置窗口缺少烘焙结果行 UXML。");

            weaponTemplate.CloneTree(pageRoot);
            weaponBaseFields = Require<VisualElement>("WeaponBaseFields");
            ascensionStageHost = Require<VisualElement>("AscensionStageHost");
            refinementStageHost = Require<VisualElement>("RefinementStageHost");
            growthProfileContent = Require<VisualElement>("GrowthProfileContent");
            ascensionStageTitle = Require<Label>("AscensionStageTitle");
            refinementStageTitle = Require<Label>("RefinementStageTitle");

            missingGrowthProfileWarning = Require<VisualElement>("MissingGrowthProfileWarning");
            growthProfileMaxLevelField = Require<PropertyField>("GrowthProfileMaxLevelField");
            growthProfileMaxLevelField.SetEnabled(false);
            levelEffectsField = Require<PropertyField>("LevelEffectsField");
            refinementEffectsField = Require<PropertyField>("RefinementEffectsField");
            levelOverridesField = Require<PropertyField>("LevelOverridesField");
            CacheFixedPropertyLabels(weaponBaseFields);
            CacheFixedPropertyLabels(ascensionStageHost);
            CacheFixedPropertyLabels(refinementStageHost);
            CacheFixedPropertyLabels(growthProfileContent);
            bakedSummaryLabel = Require<VisualElement>("BakedSummary").Q<Label>("BakedSummaryLabel");
            if (bakedSummaryLabel == null)
                throw new InvalidOperationException("武器详情 UXML 缺少烘焙摘要标签：BakedSummaryLabel。");
            bakeButton = Require<Button>("BakeButton");
            bakedProgressionList = Require<ListView>("BakedProgressionList");
            bakedProgressionList.itemsSource = displayedBakedProgressions;
            bakedProgressionList.fixedItemHeight = 24f;
            bakedProgressionList.selectionType = SelectionType.None;
            bakedProgressionList.makeItem = CreateBakedRow;
            bakedProgressionList.bindItem = BindBakedRow;
            bakeButton.clicked += OnBakeButtonClicked;
            SetVisible(false);
        }

        /// <summary>绑定一个武器定义；视觉树和字段控件不会重建。</summary>
        /// <param name="weapon">当前武器。</param>
        /// <param name="definitionObject">父详情持有的武器 SerializedObject。</param>
        internal void Bind(WeaponDefinition weapon, SerializedObject definitionObject)
        {
            if (disposed) throw new ObjectDisposedException(nameof(ItemWeaponDetailsView));
            if (weapon == null) throw new ArgumentNullException(nameof(weapon));
            if (definitionObject == null) throw new ArgumentNullException(nameof(definitionObject));
            if (ReferenceEquals(boundWeapon, weapon) &&
                ReferenceEquals(definitionSerializedObject, definitionObject))
            {
                RefreshPresentation();
                // 同一对象可能正等待首轮结构中文化；此时继续等待，避免再次暴露原生 Element N。
                if (structureRefreshScheduled)
                    revealAfterStructureRefresh = true;
                else
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
            // 首帧先保持隐藏，待结构字段生成并完成中文化后再显示，避免 Element 0 短暂闪现。
            ScheduleStructureRefresh(revealAfterRefresh: true, force: true);
        }

        /// <summary>解除当前武器和成长 Profile 的数据绑定，但保留控件树。</summary>
        internal void Unbind()
        {
            bindingVersion++;
            structureRefreshScheduled = false;
            scheduledStructureVersion = -1;
            revealAfterStructureRefresh = false;
            RestoreDynamicRegionVisibility();
            lastAscensionStructureSignature = int.MinValue;
            lastRefinementStructureSignature = int.MinValue;
            lastGrowthStructureSignature = int.MinValue;
            levelOverridesListConfigured = false;
            effectListsConfigured = false;
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
        internal void SetVisible(bool visible)
        {
            pageRoot.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>释放所有绑定、回调和烘焙数据，但不承担页面外资源。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (bakeButton != null) bakeButton.clicked -= OnBakeButtonClicked;
            Unbind();
            BakeGrowthRequested = null;
            PropertiesChanged = null;
            displayedBakedProgressions.Clear();
        }

        #endregion

        #region 状态刷新

        /// <summary>轻量刷新阶段标题、Profile 引用和烘焙结果。</summary>
        internal void RefreshPresentation()
        {
            if (boundWeapon == null) return;
            if (!ReferenceEquals(boundGrowthProfile, boundWeapon.GrowthProfile))
                BindGrowthProfile(boundWeapon.GrowthProfile);
            UpdateStageTitles();
            RefreshBakedProgressions(boundGrowthProfile);
        }

        /// <summary>在当前 UI 事件结束后刷新动态数组文本，并合并同一绑定版本的重复请求。</summary>
        /// <param name="revealAfterRefresh">完成结构中文化后是否显示武器页面。</param>
        /// <param name="force">是否忽略签名缓存，强制执行一次结构中文化。</param>
        internal void ScheduleStructureRefresh(bool revealAfterRefresh = false, bool force = false)
        {
            if (boundWeapon == null) return;

            int currentVersion = bindingVersion;
            int ascensionSignature = ComputeAscensionStructureSignature();
            int refinementSignature = ComputeRefinementStructureSignature();
            int growthSignature = ComputeGrowthStructureSignature();
            bool structureChanged = force ||
                                    ascensionSignature != lastAscensionStructureSignature ||
                                    refinementSignature != lastRefinementStructureSignature ||
                                    growthSignature != lastGrowthStructureSignature;

            // 同一绑定版本只保留一个调度任务。后续 Tracker 回调只合并显示请求，不能再排队整页遍历。
            if (structureRefreshScheduled && scheduledStructureVersion == currentVersion)
            {
                revealAfterStructureRefresh |= revealAfterRefresh;
                if (ascensionSignature != lastAscensionStructureSignature || force)
                    ascensionStageHost.style.visibility = Visibility.Hidden;
                if (refinementSignature != lastRefinementStructureSignature || force)
                    refinementStageHost.style.visibility = Visibility.Hidden;
                if (growthSignature != lastGrowthStructureSignature || force)
                    growthProfileContent.style.visibility = Visibility.Hidden;
                return;
            }

            if (!structureChanged && !revealAfterRefresh) return;

            if (force || ascensionSignature != lastAscensionStructureSignature)
                ascensionStageHost.style.visibility = Visibility.Hidden;
            if (force || refinementSignature != lastRefinementStructureSignature)
                refinementStageHost.style.visibility = Visibility.Hidden;
            if (force || growthSignature != lastGrowthStructureSignature)
                growthProfileContent.style.visibility = Visibility.Hidden;

            structureRefreshScheduled = true;
            scheduledStructureVersion = currentVersion;
            revealAfterStructureRefresh |= revealAfterRefresh;
            int scheduledVersion = currentVersion;
            pageRoot.schedule.Execute(() =>
            {
                // 绑定版本不一致说明页面已经切换到另一把武器；旧任务不得触碰新页面状态。
                if (disposed || scheduledVersion != bindingVersion || boundWeapon == null) return;
                NormalizeWeaponPropertyPresentation();
                lastAscensionStructureSignature = ComputeAscensionStructureSignature();
                lastRefinementStructureSignature = ComputeRefinementStructureSignature();
                lastGrowthStructureSignature = ComputeGrowthStructureSignature();
                RestoreDynamicRegionVisibility();
                bool reveal = revealAfterStructureRefresh;
                revealAfterStructureRefresh = false;
                // 保持“已调度”状态直到中文化完成，防止 Normalize 期间的绑定回调重新排队任务。
                structureRefreshScheduled = false;
                scheduledStructureVersion = -1;
                if (reveal) SetVisible(true);
            });
        }

        /// <summary>计算突破列表及其嵌套消耗列表的结构签名。</summary>
        /// <returns>当前突破结构签名。</returns>
        private int ComputeAscensionStructureSignature()
        {
            unchecked
            {
                int hash = 17;
                IReadOnlyList<WeaponAscensionStage> stages = boundWeapon?.AscensionStages;
                hash = MixStructureHash(hash, stages?.Count ?? 0);
                if (stages == null) return hash;
                for (int index = 0; index < stages.Count; index++)
                {
                    WeaponGrowthCost cost = stages[index]?.Cost;
                    hash = MixStructureHash(hash, cost?.ItemCosts?.Count ?? 0);
                    hash = MixStructureHash(hash, cost?.CurrencyCosts?.Count ?? 0);
                }

                return hash;
            }
        }

        /// <summary>计算精炼列表及其嵌套消耗列表的结构签名。</summary>
        /// <returns>当前精炼结构签名。</returns>
        private int ComputeRefinementStructureSignature()
        {
            unchecked
            {
                int hash = 19;
                IReadOnlyList<WeaponRefinementStage> stages = boundWeapon?.RefinementStages;
                hash = MixStructureHash(hash, stages?.Count ?? 0);
                if (stages == null) return hash;
                for (int index = 0; index < stages.Count; index++)
                {
                    WeaponGrowthCost cost = stages[index]?.Cost;
                    hash = MixStructureHash(hash, cost?.ItemCosts?.Count ?? 0);
                    hash = MixStructureHash(hash, cost?.CurrencyCosts?.Count ?? 0);
                }

                return hash;
            }
        }

        /// <summary>计算成长配置特殊等级覆盖列表的结构签名。</summary>
        /// <returns>当前成长配置结构签名。</returns>
        private int ComputeGrowthStructureSignature()
        {
            unchecked
            {
                int hash = 23;
                hash = MixStructureHash(hash, boundGrowthProfile?.LevelOverrides?.Count ?? 0);
                return hash;
            }
        }

        /// <summary>混合一个结构签名片段，避免依赖运行时版本相关的 HashCode。</summary>
        /// <param name="hash">当前哈希值。</param>
        /// <param name="value">要加入的结构值。</param>
        /// <returns>混合后的哈希值。</returns>
        private static int MixStructureHash(int hash, int value)
        {
            unchecked
            {
                return hash * 31 + value;
            }
        }

        /// <summary>恢复结构区域的可见性，避免切换或旧任务留下隐藏状态。</summary>
        private void RestoreDynamicRegionVisibility()
        {
            if (ascensionStageHost != null) ascensionStageHost.style.visibility = Visibility.Visible;
            if (refinementStageHost != null) refinementStageHost.style.visibility = Visibility.Visible;
            if (growthProfileContent != null) growthProfileContent.style.visibility = Visibility.Visible;
        }

        /// <summary>将动态创建的内部字段标题更新为固定中文。</summary>
        private void NormalizeWeaponPropertyPresentation()
        {
            if (boundWeapon == null) return;
            ConfigureStageList(ascensionStageHost, !stageListsConfigured);
            ConfigureStageList(refinementStageHost, !stageListsConfigured);
            // LevelOverridesField 属于成长 Profile，不能复用突破/精炼列表的状态；Profile 切换时需要重新配置。
            ConfigureStageList(levelOverridesField, !levelOverridesListConfigured);
            stageListsConfigured = true;
            levelOverridesListConfigured = true;
            UpdateStageTitles();
            RestoreFixedPropertyLabels();
            bool expandEffectLists = !effectListsConfigured;
            ItemConfigEditorPresentation.ConfigureGameplayEffectList(
                levelEffectsField,
                "暂无武器等级效果",
                "等级效果",
                expandEffectLists);
            ItemConfigEditorPresentation.ConfigureGameplayEffectList(
                refinementEffectsField,
                "暂无武器精炼效果",
                "精炼效果",
                expandEffectLists);
            effectListsConfigured = true;

            pageRoot.Query<PropertyField>().ForEach(field =>
            {
                string label = GetNestedPropertyLabel(field.bindingPath);
                if (!string.IsNullOrEmpty(label)) field.label = label;
            });

            pageRoot.Query<Label>().ForEach(label =>
            {
                string text = label.text ?? string.Empty;
                string bindingPath = FindBindingPath(label);
                if (TryGetElementIndex(text, out int index))
                    label.text = GetLocalizedElementLabel(bindingPath, index);
                else if (IsEmptyListText(text))
                    label.text = GetLocalizedEmptyListLabel(bindingPath);
            });
        }

        /// <summary>缓存固定 UXML 字段的初始标签，保持 UXML 为中文显示名称的唯一来源。</summary>
        /// <param name="container">包含固定字段的页面节点。</param>
        private void CacheFixedPropertyLabels(VisualElement container)
        {
            container.Query<PropertyField>().ForEach(field => fixedPropertyLabels.Add((field, field.label)));
        }

        /// <summary>恢复固定 UXML 字段标签，避免原生绑定将其替换为英文属性名。</summary>
        private void RestoreFixedPropertyLabels()
        {
            for (int index = 0; index < fixedPropertyLabels.Count; index++)
            {
                PropertyField field = fixedPropertyLabels[index].field;
                if (field != null) field.label = fixedPropertyLabels[index].label;
            }
        }

        /// <summary>更新没有成长 Profile 时的固定视觉状态。</summary>
        private void UpdateEmptyPresentation()
        {
            missingGrowthProfileWarning.style.display = DisplayStyle.None;
            growthProfileContent.style.display = DisplayStyle.None;
            bakedSummaryLabel.text = "烘焙结果：未配置成长配置。";
            bakeButton.SetEnabled(false);
            displayedBakedProgressions.Clear();
            bakedProgressionList.RefreshItems();
        }

        /// <summary>更新突破和精炼标题。</summary>
        private void UpdateStageTitles()
        {
            if (boundWeapon == null) return;
            ascensionStageTitle.text =
                $"突破配置（已配置 {boundWeapon.AscensionStages?.Count ?? 0} 项，最大突破阶数 {boundWeapon.MaxAscensionRank}）";
            refinementStageTitle.text =
                $"精炼配置（已配置 {boundWeapon.RefinementStages?.Count ?? 0} 项，最大精炼阶数 {boundWeapon.MaxRefinementRank}）";
        }

        #endregion

        #region 常驻字段绑定

        /// <summary>绑定新的成长 Profile，同时释放旧 Profile 的原生对象。</summary>
        /// <param name="profile">新的成长 Profile；为空时显示警告。</param>
        private void BindGrowthProfile(WeaponGrowthProfile profile)
        {
            growthProfileContent.Unbind();
            ReleaseGrowthProfileBinding();
            // 原生 PropertyField 在重新绑定 Profile 时可能重新创建 LevelOverrides 的 ListView。
            // 先清除配置标记，待本绑定版本的结构刷新任务中重新设置列表行为。
            levelOverridesListConfigured = false;
            boundGrowthProfile = profile;
            if (profile == null)
            {
                missingGrowthProfileWarning.style.display = DisplayStyle.Flex;
                growthProfileContent.style.display = DisplayStyle.None;
                bakedSummaryLabel.text = "烘焙结果：未配置成长配置。";
                bakeButton.SetEnabled(false);
                displayedBakedProgressions.Clear();
                bakedProgressionList.RefreshItems();
                return;
            }

            missingGrowthProfileWarning.style.display = DisplayStyle.None;
            growthProfileContent.style.display = DisplayStyle.Flex;
            growthProfileSerializedObject = new SerializedObject(profile);
            growthProfileSerializedObject.UpdateIfRequiredOrScript();
            growthProfileContent.Bind(growthProfileSerializedObject);
            growthProfileTracker = CreateGrowthProfileTracker(growthProfileSerializedObject);
            RefreshBakedProgressions(profile);
            // Profile 控件刚解除旧绑定并重新生成，即使列表数量相同也必须重新做一次结构中文化。
            ScheduleStructureRefresh(force: true);
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
            if (serializedObject == null ||
                serializedObject != growthProfileSerializedObject ||
                boundWeapon == null)
                return;
            ScheduleStructureRefresh();
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

        #region 烘焙结果

        /// <summary>创建烘焙结果行。</summary>
        /// <returns>保留 UXML 样式作用域的模板宿主节点。</returns>
        private VisualElement CreateBakedRow()
        {
            var host = new VisualElement { name = "BakedRowHost" };
            host.AddToClassList("item-editor-baked-row-host");
            bakedRowTemplate.CloneTree(host);
            // 烘焙行 UXML 自带 Style 节点，必须保留 CloneTree 宿主，否则动态行会丢失列宽和横向布局。
            if (host.Q<VisualElement>("BakedRow") == null)
                throw new InvalidOperationException("烘焙结果行 UXML 缺少 BakedRow 根节点。");
            return host;
        }

        /// <summary>绑定烘焙结果行的五列文本。</summary>
        /// <param name="element">烘焙行。</param>
        /// <param name="index">数据索引。</param>
        private void BindBakedRow(VisualElement element, int index)
        {
            BakedWeaponLevelProgression progression =
                index >= 0 && index < displayedBakedProgressions.Count
                    ? displayedBakedProgressions[index]
                    : null;
            SetLabel(element, "Level", progression?.Level.ToString("N0") ?? "—");
            SetLabel(element, "CumulativeExperience", progression?.CumulativeExperience.ToString("N0") ?? "—");
            SetLabel(element, "NextExperience", progression?.NextExperience.ToString("N0") ?? "—");
            SetLabel(element, "CurrencyCost", progression?.CurrencyCost.ToString("N0") ?? "—");
            SetLabel(element, "Breakthrough",
                progression == null
                    ? "—"
                    : IsBreakthroughLevel(progression.Level) ? "突破点" : "—");
        }

        /// <summary>刷新烘焙数据源和只读摘要。</summary>
        /// <param name="profile">成长 Profile。</param>
        private void RefreshBakedProgressions(WeaponGrowthProfile profile)
        {
            displayedBakedProgressions.Clear();
            if (profile != null && profile.BakedProgressions != null)
            {
                for (int index = 0; index < profile.BakedProgressions.Count; index++)
                    displayedBakedProgressions.Add(profile.BakedProgressions[index]);
            }

            bakedSummaryLabel.text = profile == null
                ? "烘焙结果：未配置成长配置。"
                : displayedBakedProgressions.Count == 0
                    ? "烘焙结果：尚未生成，请先编辑曲线后烘焙。"
                    : $"烘焙结果：已生成 {displayedBakedProgressions.Count} 个等级条目，等级 1 至 {profile.MaxLevel}。";
            bakeButton.SetEnabled(profile != null);
            bakedProgressionList.RefreshItems();
        }

        /// <summary>判断等级是否命中突破点。</summary>
        /// <param name="level">等级。</param>
        /// <returns>命中时返回 true。</returns>
        private bool IsBreakthroughLevel(int level)
        {
            if (boundWeapon?.AscensionStages == null) return false;
            for (int index = 0; index < boundWeapon.AscensionStages.Count; index++)
                if (boundWeapon.AscensionStages[index].RequiredLevel == level)
                    return true;
            return false;
        }

        /// <summary>设置烘焙表单元格文本。</summary>
        /// <param name="row">行节点。</param>
        /// <param name="name">单元格名称。</param>
        /// <param name="value">文本。</param>
        private static void SetLabel(VisualElement row, string name, string value)
        {
            Label label = row.Q<Label>(name);
            if (label != null) label.text = value;
        }

        #endregion

        #region 嵌套结构中文化

        /// <summary>配置阶段列表的原生折叠、增删和集合长度显示。</summary>
        /// <param name="host">阶段列表容器。</param>
        /// <param name="expandInitially">是否首次展开。</param>
        private static void ConfigureStageList(VisualElement host, bool expandInitially)
        {
            if (host == null) return;
            host.Query<ListView>().ForEach(listView =>
            {
                listView.showBoundCollectionSize = false;
                listView.showFoldoutHeader = true;
                listView.showAddRemoveFooter = true;
                listView.AddToClassList("item-editor-stage-list");
                if (expandInitially)
                {
                    Foldout foldout = listView.Q<Foldout>();
                    if (foldout != null) foldout.SetValueWithoutNotify(true);
                }
            });
        }

        /// <summary>按绑定路径获取动态 PropertyField 的中文标签。</summary>
        /// <param name="bindingPath">动态字段绑定路径。</param>
        /// <returns>中文标签；不匹配时返回空字符串。</returns>
        private static string GetNestedPropertyLabel(string bindingPath)
        {
            if (string.IsNullOrEmpty(bindingPath)) return string.Empty;
            if (bindingPath.EndsWith("requiredLevel", StringComparison.Ordinal)) return "所需等级";
            if (bindingPath.EndsWith("maxLevelAfter", StringComparison.Ordinal)) return "突破后等级上限";
            if (bindingPath.EndsWith("requiredDuplicateCount", StringComparison.Ordinal)) return "所需同名武器数量";
            if (bindingPath.EndsWith("rank", StringComparison.Ordinal)) return "精炼阶数";
            if (bindingPath.EndsWith("itemCosts", StringComparison.Ordinal)) return "物品消耗";
            if (bindingPath.EndsWith("currencyCosts", StringComparison.Ordinal)) return "货币消耗";
            if (bindingPath.EndsWith("itemId", StringComparison.Ordinal)) return "物品标识";
            if (bindingPath.EndsWith("quantity", StringComparison.Ordinal)) return "数量";
            if (bindingPath.EndsWith("currencyId", StringComparison.Ordinal)) return "货币标识";
            if (bindingPath.EndsWith("amount", StringComparison.Ordinal)) return "金额";
            if (bindingPath.EndsWith("nextExperience", StringComparison.Ordinal)) return "下一级所需经验";
            if (bindingPath.EndsWith("currencyCost", StringComparison.Ordinal)) return "货币消耗";
            if (bindingPath.EndsWith("level", StringComparison.Ordinal) &&
                bindingPath.Contains("levelOverrides", StringComparison.Ordinal))
                return "等级";
            if (bindingPath.Contains("growthProfile", StringComparison.Ordinal)) return "成长配置";
            return string.Empty;
        }

        /// <summary>判断标签是否为 Unity 自动生成的集合元素标题。</summary>
        /// <param name="text">原始标签文本。</param>
        /// <returns>是元素标题时返回 true。</returns>
        private static bool TryGetElementIndex(string text, out int index)
        {
            string[] prefixes = { "Element ", "突破阶段 ", "精炼阶段 ", "物品消耗 ", "货币消耗 ", "特殊等级覆盖 ", "配置项 " };
            for (int i = 0; i < prefixes.Length; i++)
            {
                if (!text.StartsWith(prefixes[i], StringComparison.Ordinal)) continue;
                return int.TryParse(text.Substring(prefixes[i].Length), out index);
            }

            index = -1;
            return false;
        }

        /// <summary>判断标签是否为空列表文本。</summary>
        /// <param name="text">标签文本。</param>
        /// <returns>为空列表文本时返回 true。</returns>
        private static bool IsEmptyListText(string text)
        {
            return text == "List is empty" ||
                   text.StartsWith("暂无", StringComparison.Ordinal);
        }

        /// <summary>生成动态集合元素中文标题。</summary>
        /// <param name="bindingPath">元素绑定路径。</param>
        /// <param name="index">零基元素索引。</param>
        /// <returns>中文元素标题。</returns>
        private static string GetLocalizedElementLabel(string bindingPath, int index)
        {
            if (bindingPath.Contains("ascensionStages", StringComparison.Ordinal)) return $"突破阶段 {index + 1}";
            if (bindingPath.Contains("refinementStages", StringComparison.Ordinal)) return $"精炼阶段 {index + 1}";
            if (bindingPath.Contains("levelEffects", StringComparison.Ordinal)) return $"等级效果 {index + 1}";
            if (bindingPath.Contains("refinementEffects", StringComparison.Ordinal)) return $"精炼效果 {index + 1}";
            if (bindingPath.Contains("itemCosts", StringComparison.Ordinal)) return $"物品消耗 {index + 1}";
            if (bindingPath.Contains("currencyCosts", StringComparison.Ordinal)) return $"货币消耗 {index + 1}";
            if (bindingPath.Contains("levelOverrides", StringComparison.Ordinal)) return $"特殊等级覆盖 {index + 1}";
            return $"配置项 {index + 1}";
        }

        /// <summary>生成动态空列表中文提示。</summary>
        /// <param name="bindingPath">列表绑定路径。</param>
        /// <returns>中文空状态。</returns>
        private static string GetLocalizedEmptyListLabel(string bindingPath)
        {
            if (bindingPath.Contains("ascensionStages", StringComparison.Ordinal)) return "暂无突破阶段";
            if (bindingPath.Contains("refinementStages", StringComparison.Ordinal)) return "暂无精炼阶段";
            if (bindingPath.Contains("levelEffects", StringComparison.Ordinal)) return "暂无武器等级效果";
            if (bindingPath.Contains("refinementEffects", StringComparison.Ordinal)) return "暂无武器精炼效果";
            if (bindingPath.Contains("itemCosts", StringComparison.Ordinal)) return "暂无物品消耗";
            if (bindingPath.Contains("currencyCosts", StringComparison.Ordinal)) return "暂无货币消耗";
            if (bindingPath.Contains("levelOverrides", StringComparison.Ordinal)) return "暂无特殊等级覆盖";
            return "暂无配置项";
        }

        /// <summary>查询指定页面范围内的控件。</summary>
        /// <typeparam name="TElement">控件类型。</typeparam>
        /// <param name="name">控件名称。</param>
        /// <returns>对应控件。</returns>
        private TElement Require<TElement>(string name) where TElement : VisualElement
        {
            TElement element = pageRoot.Q<TElement>(name);
            if (element == null) throw new InvalidOperationException($"武器详情 UXML 缺少控件：{name}。");
            return element;
        }

        /// <summary>查找 Label 最近祖先的绑定路径。</summary>
        /// <param name="element">标签节点。</param>
        /// <returns>绑定路径。</returns>
        private static string FindBindingPath(VisualElement element)
        {
            for (VisualElement current = element; current != null; current = current.parent)
                if (current is IBindable bindable && !string.IsNullOrEmpty(bindable.bindingPath))
                    return bindable.bindingPath;
            return string.Empty;
        }

        #endregion

    }
}
#endif
