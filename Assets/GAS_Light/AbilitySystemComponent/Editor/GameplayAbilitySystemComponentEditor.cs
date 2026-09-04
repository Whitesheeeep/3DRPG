#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.Editor;
using WS_Modules.GAS.GameplayAbilitySystem;
using WS_Modules.GAS.GameplayCue;
using WS_Modules.GAS.GameplayEffect;
using WS_Modules.GAS.TAG;
using WS_Modules.UIModule.Editor;

namespace WS_Modules.GAS.AbilitySystemComponent
{
    /// <summary>
    /// 在 ASC Inspector 中以只读方式展示当前 Gameplay Ability System 的运行时状态。
    /// </summary>
    [CustomEditor(typeof(GameplayAbilitySystemComponent))]
    [CanEditMultipleObjects]
    internal sealed class GameplayAbilitySystemComponentEditor : UnityEditor.Editor
    {
        #region 常量
        // Inspector 刷新间隔，避免每帧刷新导致的性能问题。
        private const int RefreshIntervalMilliseconds = 200;
        private const string StylePath = UxmlUssPathConstants.Uss.AssetsGASLightAbilitySystemComponentEditorStyleGameplayAbilitySystemComponentInspector;

        #endregion

        #region 依赖字段

        private GameplayAbilitySystemComponent asc;
        private VisualElement root;
        private IVisualElementScheduledItem refreshSchedule;

        #endregion

        #region 状态字段

        // 分组展开状态跨越每次运行时快照重建，避免实时刷新打断排查流程。
        private bool tagsExpanded = true;
        private bool grantedAbilitiesExpanded = true;
        private bool activeAbilitiesExpanded = true;
        private bool activeEffectsExpanded = true;
        private bool attributesExpanded = true;
        private bool activeCuesExpanded = true;
        private readonly HashSet<string> expandedDetails = new();

        #endregion

        #region 生命周期

        /// <summary>
        /// 创建 ASC 的 UI Toolkit Inspector，并启动低频实时刷新。
        /// </summary>
        /// <returns>用于绘制当前 ASC 状态的根视觉元素。</returns>
        public override VisualElement CreateInspectorGUI()
        {
            asc = target as GameplayAbilitySystemComponent;
            root = new VisualElement { name = "gas-asc-inspector" };
            root.AddToClassList("gas-asc-inspector");

            StyleSheet style = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath);
            if (style != null) root.styleSheets.Add(style);

            root.RegisterCallback<DetachFromPanelEvent>(OnDetached);
            refreshSchedule = root.schedule.Execute(Refresh).Every(RefreshIntervalMilliseconds);
            Refresh();
            return root;
        }

        /// <summary>
        /// Inspector 从面板移除时停止调度并释放视觉树引用，避免迟到回调访问已销毁对象。
        /// </summary>
        /// <param name="_">Unity UI Toolkit 的脱离面板事件。</param>
        private void OnDetached(DetachFromPanelEvent _)
        {
            refreshSchedule?.Pause();
            refreshSchedule = null;
            root = null;
            asc = null;
        }

        #endregion

        #region 界面构建与状态刷新

        /// <summary>
        /// 重建当前快照对应的 Inspector 内容；所有列表均从公开只读 Runtime 查询取得。
        /// </summary>
        private void Refresh()
        {
            if (root == null || asc == null) return;

            root.Clear();
            root.Add(CreateHeader());

            if (!Application.isPlaying)
            {
                root.Add(new HelpBox("进入 Play Mode 后显示 ASC 运行时状态。", HelpBoxMessageType.Info));
                return;
            }

            if (targets != null && targets.Length > 1)
            {
                root.Add(new HelpBox(
                    "同时选中了多个 ASC；请单独选择一个对象查看运行时明细。",
                    HelpBoxMessageType.Info));
                return;
            }

            if (asc.Owner == null || asc.Attributes == null || asc.Abilities == null ||
                asc.GameEffectCtrl == null || asc.Cues == null)
            {
                root.Add(new HelpBox(
                    "ASC 尚未完成 Awake 初始化，或正在被 Unity 销毁；等待下一次刷新。",
                    HelpBoxMessageType.Info));
                return;
            }

            root.Add(CreateSummary());
            root.Add(CreateTagsSection());
            root.Add(CreateGrantedAbilitiesSection());
            root.Add(CreateActiveAbilitiesSection());
            root.Add(CreateActiveEffectsSection());
            root.Add(CreateAttributesSection());
            root.Add(CreateActiveCuesSection());
        }

        /// <summary>创建 Inspector 顶部标题和只读用途说明。</summary>
        /// <returns>Inspector 标题元素。</returns>
        private VisualElement CreateHeader()
        {
            var header = new VisualElement();
            header.AddToClassList("gas-header");
            header.Add(new Label("Gameplay Ability System Component") { name = "gas-title" });
            header.Add(new Label("Runtime snapshot · Read Only") { name = "gas-subtitle" });
            return header;
        }

        /// <summary>创建 ASC 初始化状态、Owner 和各类 Runtime 数量概览。</summary>
        /// <returns>ASC 概览元素。</returns>
        private VisualElement CreateSummary()
        {
            var summary = new VisualElement();
            summary.AddToClassList("gas-summary");
            summary.Add(new Label($"Initialized: {asc.IsInitialized}") { name = "gas-summary-state" });
            summary.Add(CreateSummaryValue("ASC", asc.gameObject.name));
            summary.Add(CreateSummaryValue("Owner", GetObjectName(asc.Owner as UnityEngine.Object)));
            summary.Add(CreateSummaryValue("Tags", $"{asc.Tags?.Count ?? 0} explicit / {asc.Tags?.ParentTags.Count ?? 0} parent"));
            summary.Add(CreateSummaryValue("Abilities", $"{asc.GrantedAbilities.Count} granted / {asc.ActiveAbilities.Count} active"));
            summary.Add(CreateSummaryValue("Effects", asc.ActiveEffects.Count.ToString()));
            summary.Add(CreateSummaryValue("Attributes", asc.Attributes.Count.ToString()));
            summary.Add(CreateSummaryValue("Cues", asc.Cues.ActiveCues.Count.ToString()));
            return summary;
        }

        /// <summary>创建概览中的单个键值行。</summary>
        /// <param name="label">键名称。</param>
        /// <param name="value">显示值。</param>
        /// <returns>键值行元素。</returns>
        private static VisualElement CreateSummaryValue(string label, string value)
        {
            var row = new VisualElement();
            row.AddToClassList("gas-summary-value");
            row.Add(new Label(label) { name = "gas-summary-label" });
            row.Add(new Label(value ?? "<null>") { name = "gas-summary-content" });
            return row;
        }

        /// <summary>创建显式 Tag 与层级 Parent Tag 两张表。</summary>
        /// <returns>Gameplay Tag 折叠分组。</returns>
        private VisualElement CreateTagsSection()
        {
            var content = new VisualElement();
            IReadOnlyGameplayTagContainer tags = asc.Tags;
            GameplayTagCountContainer countContainer = tags as GameplayTagCountContainer;

            content.Add(new Label("Explicit Tags") { name = "gas-subsection-title" });
            content.Add(CreateTableHeader("Tag", "Id", "Explicit", "Hierarchical"));
            List<GameplayTag> explicitTags = SortTags(tags?.Tags);
            if (explicitTags.Count == 0)
                content.Add(CreateEmptyRow("当前无显式 Tag。"));
            else
                for (int i = 0; i < explicitTags.Count; i++)
                {
                    GameplayTag tag = explicitTags[i];
                    content.Add(CreateTableRow(
                        CreateCell(ResolveTagPath(tag)),
                        CreateCell(tag.Id.ToString()),
                        CreateCell((countContainer?.GetExplicitTagCount(tag) ?? 1).ToString()),
                        CreateCell((countContainer?.GetTagCount(tag) ?? 1).ToString())));
                }

            content.Add(new Label("Derived Parent Tags") { name = "gas-subsection-title" });
            content.Add(CreateTableHeader("Tag", "Id", "Explicit", "Hierarchical"));
            List<GameplayTag> parentTags = SortTags(tags?.ParentTags);
            if (parentTags.Count == 0)
                content.Add(CreateEmptyRow("当前无派生 Parent Tag。"));
            else
                for (int i = 0; i < parentTags.Count; i++)
                {
                    GameplayTag tag = parentTags[i];
                    content.Add(CreateTableRow(
                        CreateCell(ResolveTagPath(tag)),
                        CreateCell(tag.Id.ToString()),
                        CreateCell((countContainer?.GetExplicitTagCount(tag) ?? 0).ToString()),
                        CreateCell((countContainer?.GetTagCount(tag) ?? 1).ToString())));
                }

            return CreateSection("Gameplay Tags", tagsExpanded, value => tagsExpanded = value, content);
        }

        /// <summary>创建已授予 Ability Spec 表格并计算各 Spec 的 Active Runtime 数量。</summary>
        /// <returns>已授予 Ability 折叠分组。</returns>
        private VisualElement CreateGrantedAbilitiesSection()
        {
            var content = new VisualElement();
            content.Add(CreateTableHeader("Ability", "Handle", "Level", "Active Runtimes"));
            IReadOnlyList<GameplayAbilitySpec> specs = asc.GrantedAbilities;
            if (specs == null || specs.Count == 0)
            {
                content.Add(CreateEmptyRow("当前无已授予 Ability。"));
                return CreateSection("Granted Abilities", grantedAbilitiesExpanded, value => grantedAbilitiesExpanded = value, content);
            }

            for (int i = 0; i < specs.Count; i++)
            {
                GameplayAbilitySpec spec = specs[i];
                int activeCount = CountActiveRuntimes(spec);
                content.Add(CreateTableRow(
                    CreateObjectCell(spec?.Data, typeof(GameplayAbilityData)),
                    CreateCell(spec == null ? "<missing spec>" : spec.Handle.ToString()),
                    CreateCell(spec == null ? "-" : spec.Level.ToString()),
                    CreateCell(activeCount.ToString())));
            }

            return CreateSection("Granted Abilities", grantedAbilitiesExpanded, value => grantedAbilitiesExpanded = value, content);
        }

        /// <summary>创建当前 Active Ability Runtime 表格及其 SetByCaller/Owned GE 详情。</summary>
        /// <returns>Active Ability 折叠分组。</returns>
        private VisualElement CreateActiveAbilitiesSection()
        {
            var content = new VisualElement();
            content.Add(CreateTableHeader("Ability", "Runtime", "Activation", "Handle", "Level", "State"));
            IReadOnlyList<GameplayAbilityRuntime> runtimes = asc.ActiveAbilities;
            if (runtimes == null || runtimes.Count == 0)
            {
                content.Add(CreateEmptyRow("当前无 Active Ability。"));
                return CreateSection("Active Abilities", activeAbilitiesExpanded, value => activeAbilitiesExpanded = value, content);
            }

            for (int i = 0; i < runtimes.Count; i++)
            {
                GameplayAbilityRuntime runtime = runtimes[i];
                if (runtime == null)
                {
                    content.Add(CreateEmptyRow("发现已失效的 Ability Runtime 引用。"));
                    continue;
                }

                VisualElement row = CreateTableRow(
                    CreateObjectCell(runtime.Spec?.Data, typeof(GameplayAbilityData)),
                    CreateCell(runtime.GetType().Name),
                    CreateCell(runtime.ActivationId.ToString()),
                    CreateCell(runtime.Spec == null ? "-" : runtime.Spec.Handle.ToString()),
                    CreateCell(runtime.Level.ToString()),
                    CreateCell(runtime.State.ToString()));
                AddDetailFoldout(
                    row,
                    $"Ability Runtime Details · {runtime.ActivationId}",
                    $"ability:{runtime.ActivationId}",
                    () => CreateAbilityRuntimeDetails(runtime));
                content.Add(row);
            }

            return CreateSection("Active Abilities", activeAbilitiesExpanded, value => activeAbilitiesExpanded = value, content);
        }

        /// <summary>创建单个 Ability Runtime 的 SetByCaller 与生命周期持有 GE 详情。</summary>
        /// <param name="runtime">待展示的 Ability Runtime。</param>
        /// <returns>Runtime 详情元素。</returns>
        private VisualElement CreateAbilityRuntimeDetails(GameplayAbilityRuntime runtime)
        {
            var details = CreateDetailsContainer();
            details.Add(new Label($"Owned Effects: {runtime.OwnedEffects.Count}") { name = "gas-detail-title" });
            AddSetByCallerTable(details, runtime.SetByCaller);

            if (runtime.OwnedEffects.Count == 0)
            {
                details.Add(new Label("No lifecycle-owned Gameplay Effect.") { name = "gas-muted" });
                return details;
            }

            details.Add(CreateTableHeader("Owned GE", "Level", "Stacks", "Remaining"));
            for (int i = 0; i < runtime.OwnedEffects.Count; i++)
            {
                GameEffectRuntime effect = runtime.OwnedEffects[i];
                details.Add(CreateTableRow(
                    CreateObjectCell(effect?.Data, typeof(GameplayEffectData)),
                    CreateCell(effect == null ? "-" : effect.Level.ToString()),
                    CreateCell(effect == null ? "-" : effect.StackCount.ToString()),
                    CreateCell(effect == null ? "-" : FormatRemaining(effect))));
            }

            return details;
        }

        /// <summary>创建当前 Active GE 表格及其 SetByCaller 与叠层计时详情。</summary>
        /// <returns>Active GE 折叠分组。</returns>
        private VisualElement CreateActiveEffectsSection()
        {
            var content = new VisualElement();
            content.Add(CreateTableHeader("Effect", "Source", "Level", "Stacks", "Duration", "Period", "Active"));
            IReadOnlyList<GameEffectRuntime> effects = asc.ActiveEffects;
            if (effects == null || effects.Count == 0)
            {
                content.Add(CreateEmptyRow("当前无 Active Gameplay Effect。"));
                return CreateSection("Active Gameplay Effects", activeEffectsExpanded, value => activeEffectsExpanded = value, content);
            }

            for (int i = 0; i < effects.Count; i++)
            {
                GameEffectRuntime effect = effects[i];
                if (effect == null)
                {
                    content.Add(CreateEmptyRow("发现已失效的 Gameplay Effect Runtime 引用。"));
                    continue;
                }

                VisualElement row = CreateTableRow(
                    CreateObjectCell(effect.Data, typeof(GameplayEffectData)),
                    CreateCell(GetObjectName(effect.Source)),
                    CreateCell(effect.Level.ToString()),
                    CreateCell(effect.StackCount.ToString()),
                    CreateCell(FormatDuration(effect)),
                    CreateCell(FormatPeriod(effect)),
                    CreateCell(effect.IsActive.ToString()));
                AddDetailFoldout(
                    row,
                    $"Gameplay Effect Details · {GetObjectName(effect.Data)}",
                    $"effect:{GetRuntimeKey(effect)}",
                    () => CreateEffectDetails(effect));
                content.Add(row);
            }

            return CreateSection("Active Gameplay Effects", activeEffectsExpanded, value => activeEffectsExpanded = value, content);
        }

        /// <summary>创建单个 GE Runtime 的 SetByCaller、持续策略和周期策略详情。</summary>
        /// <param name="effect">待展示的 GE Runtime。</param>
        /// <returns>GE 详情元素。</returns>
        private VisualElement CreateEffectDetails(GameEffectRuntime effect)
        {
            var details = CreateDetailsContainer();
            AddSetByCallerTable(details, effect.SetByCaller);
            if (effect.Data == null)
            {
                details.Add(new Label("GameplayEffectData 已失效。") { name = "gas-warning" });
                return details;
            }

            details.Add(CreateDetailLine("Duration Policy", effect.Data.DurationType.ToString()));
            details.Add(CreateDetailLine("Configured Duration", FormatNumber(effect.Data.Duration)));
            details.Add(CreateDetailLine("Configured Period", FormatNumber(effect.Data.Period)));
            details.Add(CreateDetailLine("Stacking", effect.Data.StackingType.ToString()));
            details.Add(CreateDetailLine("Stack Duration", effect.Data.StackingDurationPolicy.ToString()));
            details.Add(CreateDetailLine("Stack Period", effect.Data.StackingPeriodPolicy.ToString()));
            details.Add(CreateDetailLine("Stack Expiration", effect.Data.StackingExpirationPolicy.ToString()));
            return details;
        }

        /// <summary>创建 Attribute 运行时 Definition 表格。</summary>
        /// <returns>Attribute 折叠分组。</returns>
        private VisualElement CreateAttributesSection()
        {
            var content = new VisualElement();
            GameplayAttributeRegistry registry = GameplayAttributeEditorSession.ResolveSingleRegistry(out string registryError);
            if (registry == null)
                content.Add(new HelpBox($"Attribute 名称解析不可用：{registryError}", HelpBoxMessageType.Warning));

            content.Add(CreateTableHeader("Attribute", "Id", "Type", "Base", "Current", "Min", "Max"));
            IReadOnlyList<GameplayAttributeDefinition> definitions = asc.Attributes.Attributes;
            if (definitions == null || definitions.Count == 0)
            {
                content.Add(CreateEmptyRow("当前无已初始化 Attribute。"));
                return CreateSection("Attributes", attributesExpanded, value => attributesExpanded = value, content);
            }

            for (int i = 0; i < definitions.Count; i++)
            {
                GameplayAttributeDefinition definition = definitions[i];
                if (definition == null)
                {
                    content.Add(CreateEmptyRow("发现已失效的 Attribute Definition 引用。"));
                    continue;
                }

                content.Add(CreateTableRow(
                    CreateCell(ResolveAttributeName(registry, definition.Attribute)),
                    CreateCell(definition.Attribute.Id.ToString()),
                    CreateCell(definition.Type.ToString()),
                    CreateCell(FormatNumber(definition.BaseValue)),
                    CreateCell(FormatNumber(definition.CurrentValue)),
                    CreateCell(FormatBound(definition.MinValue)),
                    CreateCell(FormatBound(definition.MaxValue))));
            }

            return CreateSection("Attributes", attributesExpanded, value => attributesExpanded = value, content);
        }

        /// <summary>创建 Active Cue 表格及其来源 Runtime、挂点和变换详情。</summary>
        /// <returns>Active Cue 折叠分组。</returns>
        private VisualElement CreateActiveCuesSection()
        {
            var content = new VisualElement();
            content.Add(CreateTableHeader("Cue", "Tag", "Event", "Source", "Target", "Object"));
            IReadOnlyList<GameplayCueRuntime> cues = asc.Cues.ActiveCues;
            if (cues == null || cues.Count == 0)
            {
                content.Add(CreateEmptyRow("当前无 Active Gameplay Cue。"));
                return CreateSection("Active Gameplay Cues", activeCuesExpanded, value => activeCuesExpanded = value, content);
            }

            for (int i = 0; i < cues.Count; i++)
            {
                GameplayCueRuntime cue = cues[i];
                if (cue == null)
                {
                    content.Add(CreateEmptyRow("发现已失效的 Gameplay Cue Runtime 引用。"));
                    continue;
                }

                VisualElement row = CreateTableRow(
                    CreateObjectCell(cue.CueData, typeof(GameplayCueData)),
                    CreateCell(ResolveTagPath(cue.CueTag)),
                    CreateCell(cue.EventType.ToString()),
                    CreateCell(GetObjectName(cue.Source)),
                    CreateCell(GetObjectName(cue.Target)),
                    CreateObjectCell(cue.CueObject, typeof(GameObject)));
                AddDetailFoldout(
                    row,
                    $"Gameplay Cue Details · {GetObjectName(cue.CueData)}",
                    $"cue:{GetRuntimeKey(cue)}",
                    () => CreateCueDetails(cue));
                content.Add(row);
            }

            return CreateSection("Active Gameplay Cues", activeCuesExpanded, value => activeCuesExpanded = value, content);
        }

        /// <summary>创建单个 Cue Runtime 的来源和世界空间状态详情。</summary>
        /// <param name="cue">待展示的 Cue Runtime。</param>
        /// <returns>Cue 详情元素。</returns>
        private VisualElement CreateCueDetails(GameplayCueRuntime cue)
        {
            var details = CreateDetailsContainer();
            string origin = cue.AbilityRuntime != null
                ? $"Ability Runtime #{cue.AbilityRuntime.ActivationId}"
                : cue.EffectRuntime != null
                    ? $"Gameplay Effect Runtime ({GetObjectName(cue.EffectRuntime.Data)})"
                    : "Direct request";
            details.Add(CreateDetailLine("Origin", origin));
            details.Add(CreateDetailLine("Event Type", cue.EventType.ToString()));
            details.Add(CreateDetailLine("Is Active", cue.IsActive.ToString()));
            details.Add(CreateDetailLine("Is Released", cue.IsReleased.ToString()));
            details.Add(CreateDetailLine("Position", cue.Position.ToString("F2")));
            details.Add(CreateDetailLine("Rotation", cue.Rotation.eulerAngles.ToString("F1")));
            details.Add(CreateDetailLine("Attach Transform", GetObjectName(cue.AttachTransform)));
            return details;
        }

        #endregion

        #region 表格与折叠分组

        /// <summary>创建带记忆展开状态的 Inspector 折叠分组。</summary>
        /// <param name="title">分组标题。</param>
        /// <param name="expanded">当前展开状态引用。</param>
        /// <param name="content">分组内容。</param>
        /// <returns>折叠分组元素。</returns>
        private static Foldout CreateSection(
            string title,
            bool expanded,
            Action<bool> stateChanged,
            VisualElement content)
        {
            var foldout = new Foldout { text = title, value = expanded };
            foldout.AddToClassList("gas-section");
            foldout.RegisterValueChangedCallback(evt => stateChanged(evt.newValue));
            foldout.Add(content);
            return foldout;
        }

        /// <summary>创建表格标题行。</summary>
        /// <param name="columns">标题列文本。</param>
        /// <returns>表头元素。</returns>
        private static VisualElement CreateTableHeader(params string[] columns)
        {
            var header = new VisualElement();
            header.AddToClassList("gas-table-row");
            header.AddToClassList("gas-table-header");
            for (int i = 0; i < columns.Length; i++) header.Add(CreateCell(columns[i]));
            return header;
        }

        /// <summary>创建通用表格行并加入传入的单元格。</summary>
        /// <param name="cells">当前行单元格。</param>
        /// <returns>表格行元素。</returns>
        private static VisualElement CreateTableRow(params VisualElement[] cells)
        {
            var row = new VisualElement();
            row.AddToClassList("gas-table-row");
            for (int i = 0; i < cells.Length; i++) row.Add(cells[i]);
            return row;
        }

        /// <summary>创建一个可自动换行的文本单元格。</summary>
        /// <param name="text">单元格文本。</param>
        /// <returns>单元格元素。</returns>
        private static VisualElement CreateCell(string text)
        {
            var cell = new VisualElement();
            cell.AddToClassList("gas-cell");
            cell.Add(new Label(text ?? "<null>"));
            return cell;
        }

        /// <summary>创建只读 Unity ObjectField 单元格，保留资产类型和对象定位语义。</summary>
        /// <param name="value">对象值。</param>
        /// <param name="objectType">允许的对象类型。</param>
        /// <returns>只读对象单元格。</returns>
        private static VisualElement CreateObjectCell(UnityEngine.Object value, Type objectType)
        {
            var cell = new VisualElement();
            cell.AddToClassList("gas-cell");
            var field = new ObjectField { objectType = objectType, value = value, allowSceneObjects = true };
            field.SetEnabled(false);
            cell.Add(field);
            return cell;
        }

        /// <summary>创建空列表行。</summary>
        /// <param name="message">空列表说明。</param>
        /// <returns>空列表元素。</returns>
        private static VisualElement CreateEmptyRow(string message)
        {
            var row = new VisualElement();
            row.AddToClassList("gas-empty-row");
            row.Add(new Label(message));
            return row;
        }

        /// <summary>在表格行中加入可记忆展开状态的详情折叠项。</summary>
        /// <param name="row">所属表格行。</param>
        /// <param name="title">详情标题。</param>
        /// <param name="key">用于跨刷新保持状态的稳定键。</param>
        /// <param name="contentFactory">详情内容工厂。</param>
        private void AddDetailFoldout(
            VisualElement row,
            string title,
            string key,
            Func<VisualElement> contentFactory)
        {
            var foldout = new Foldout { text = title, value = expandedDetails.Contains(key) };
            foldout.AddToClassList("gas-detail-foldout");
            bool built = false;
            foldout.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue)
                {
                    expandedDetails.Add(key);
                    if (!built)
                    {
                        foldout.Add(contentFactory());
                        built = true;
                    }
                }
                else
                {
                    expandedDetails.Remove(key);
                }
            });
            if (foldout.value)
            {
                foldout.Add(contentFactory());
                built = true;
            }

            row.Add(foldout);
        }

        /// <summary>创建详情区域容器。</summary>
        /// <returns>详情容器。</returns>
        private static VisualElement CreateDetailsContainer()
        {
            var details = new VisualElement();
            details.AddToClassList("gas-details");
            return details;
        }

        /// <summary>创建详情中的单行键值说明。</summary>
        /// <param name="label">键名称。</param>
        /// <param name="value">值文本。</param>
        /// <returns>详情行。</returns>
        private static VisualElement CreateDetailLine(string label, string value)
        {
            var line = new VisualElement();
            line.AddToClassList("gas-detail-line");
            line.Add(new Label(label) { name = "gas-detail-label" });
            line.Add(new Label(value ?? "<null>") { name = "gas-detail-value" });
            return line;
        }

        /// <summary>将 SetByCaller 字典渲染为稳定 Tag 顺序的详情表。</summary>
        /// <param name="parent">详情父元素。</param>
        /// <param name="values">SetByCaller 只读字典。</param>
        private void AddSetByCallerTable(
            VisualElement parent,
            IReadOnlyDictionary<GameplayTag, float> values)
        {
            parent.Add(new Label("SetByCaller") { name = "gas-detail-title" });
            if (values == null || values.Count == 0)
            {
                parent.Add(new Label("No SetByCaller values.") { name = "gas-muted" });
                return;
            }

            parent.Add(CreateTableHeader("Tag", "Value"));
            var entries = new List<KeyValuePair<GameplayTag, float>>(values);
            entries.Sort((left, right) => left.Key.Id.CompareTo(right.Key.Id));
            for (int i = 0; i < entries.Count; i++)
                parent.Add(CreateTableRow(
                    CreateCell(ResolveTagPath(entries[i].Key)),
                    CreateCell(FormatNumber(entries[i].Value))));
        }

        #endregion

        #region 名称解析与格式化

        /// <summary>按稳定 ID 排序 Tag，避免 HashSet 枚举顺序造成 Inspector 抖动。</summary>
        /// <param name="tags">待排序的 Tag 集合。</param>
        /// <returns>稳定顺序的 Tag 列表。</returns>
        private static List<GameplayTag> SortTags(IReadOnlyCollection<GameplayTag> tags)
        {
            var result = tags == null ? new List<GameplayTag>() : new List<GameplayTag>(tags);
            result.Sort((left, right) => left.Id.CompareTo(right.Id));
            return result;
        }

        /// <summary>使用运行时 Tag Database 解析完整路径；失败时保留稳定 ID。</summary>
        /// <param name="tag">待解析 Tag。</param>
        /// <returns>Tag 路径或诊断文本。</returns>
        private static string ResolveTagPath(GameplayTag tag)
        {
            if (!tag.IsValid) return $"Invalid Tag ({tag.Id})";
            GameplayTagManager manager = GameplayTagManager.Instance;
            if (manager.IsInitialized && manager.Database.TryGetBakedPath(tag, out string path) &&
                !string.IsNullOrEmpty(path))
                return path;
            return $"GameplayTag({tag.Id})";
        }

        /// <summary>使用 Attribute Registry 解析名称，Registry 不可用时保留 AttributeId。</summary>
        /// <param name="registry">当前唯一或 Session 选中的 Registry。</param>
        /// <param name="attribute">待解析 Attribute。</param>
        /// <returns>Attribute 名称或诊断文本。</returns>
        private static string ResolveAttributeName(
            GameplayAttributeRegistry registry,
            GameplayAttribute attribute)
        {
            if (registry != null && registry.TryGetNodeById(attribute.Id, out GameplayAttributeEditorNode node))
                return node.Name;
            return $"Invalid AttributeId ({attribute.Id})";
        }

        /// <summary>计算一个已授予 Spec 当前关联的 Active Runtime 数量。</summary>
        /// <param name="spec">目标 Ability Spec。</param>
        /// <returns>Active Runtime 数量。</returns>
        private int CountActiveRuntimes(GameplayAbilitySpec spec)
        {
            if (spec == null) return 0;
            int count = 0;
            IReadOnlyList<GameplayAbilityRuntime> runtimes = asc.ActiveAbilities;
            for (int i = 0; i < runtimes.Count; i++)
                if (runtimes[i] != null && ReferenceEquals(runtimes[i].Spec, spec)) count++;
            return count;
        }

        /// <summary>格式化 GE 剩余 Duration；Infinite 与 Instant 使用语义化文本。</summary>
        /// <param name="effect">目标 GE Runtime。</param>
        /// <returns>剩余 Duration 文本。</returns>
        private static string FormatDuration(GameEffectRuntime effect)
        {
            if (effect.Data == null) return "<missing data>";
            return effect.Data.DurationType switch
            {
                E_GameEffectDurationType.Infinite => "Infinite",
                E_GameEffectDurationType.Instant => "Instant",
                _ => FormatNumber(effect.RemainingDuration)
            };
        }

        /// <summary>格式化 GE 剩余 Period；非周期效果显示无周期文本。</summary>
        /// <param name="effect">目标 GE Runtime。</param>
        /// <returns>剩余 Period 文本。</returns>
        private static string FormatPeriod(GameEffectRuntime effect) =>
            effect.Data == null || !effect.Data.IsPeriodic ? "No Period" : FormatNumber(effect.RemainingPeriod);

        /// <summary>格式化 GE 详情中的剩余时间摘要。</summary>
        /// <param name="effect">目标 GE Runtime。</param>
        /// <returns>剩余时间文本。</returns>
        private static string FormatRemaining(GameEffectRuntime effect) =>
            effect.Data == null ? "<missing data>" : $"{FormatDuration(effect)} / {FormatPeriod(effect)}";

        /// <summary>格式化有限浮点值；非法值使用可诊断文本。</summary>
        /// <param name="value">待格式化数值。</param>
        /// <returns>数值文本。</returns>
        private static string FormatNumber(float value) =>
            float.IsNaN(value) ? "NaN" : float.IsInfinity(value) ? (value > 0f ? "+Infinity" : "-Infinity") : value.ToString("0.###");

        /// <summary>格式化 Attribute 边界值，保留无限边界的语义。</summary>
        /// <param name="value">边界值。</param>
        /// <returns>边界文本。</returns>
        private static string FormatBound(float value) =>
            float.IsPositiveInfinity(value) ? "+Infinity" :
            float.IsNegativeInfinity(value) ? "-Infinity" : FormatNumber(value);

        /// <summary>取得 Unity Object 的稳定显示名称；对象已销毁时返回明确文本。</summary>
        /// <param name="value">待显示对象。</param>
        /// <returns>对象名称。</returns>
        private static string GetObjectName(UnityEngine.Object value) => value == null ? "<none>" : value.name;

        /// <summary>取得 Runtime 引用在本次 Inspector 生命周期内的稳定键。</summary>
        /// <param name="value">任意 Runtime 对象。</param>
        /// <returns>Runtime 键。</returns>
        private static string GetRuntimeKey(object value) =>
            value == null ? "null" : $"{value.GetType().FullName}:{value.GetHashCode()}";

        #endregion
    }
}
#endif
