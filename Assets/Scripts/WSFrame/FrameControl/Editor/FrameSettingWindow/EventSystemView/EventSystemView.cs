using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.UIToolkitExtensions.Editor;

namespace WS_Modules
{
    /// <summary>
    /// EventSystem 源码调用搜索及事件日志 Console 的组合视图。
    /// </summary>
    internal sealed class EventSystemView
    {
        #region 依赖字段

        private readonly Dictionary<string, EventSystemInfo> _eventSystemInfoCache = new Dictionary<string, EventSystemInfo>();
        private readonly IEventSearchService _eventSearchService = new EventSearchService();
        private readonly EventLogCaptureService _eventLogCaptureService = new EventLogCaptureService();
        private EventLogConsoleView _eventLogConsoleView;

        #endregion

        #region 状态字段

        private EventDisplayMode _displayMode = EventDisplayMode.All;
        private VisualTreeAsset _eventInfoTemplate;
        private VisualTreeAsset _eventPanelTemplate;
        private VisualElement _viewRoot;
        private VisualElement _toolbarContainer;
        private VisualElement _resultContainer;
        private Label _summaryLabel;
        private Button _foldAllButton;
        private Button _expandAllButton;
        private string _searchKeyword = string.Empty;

        #endregion

        private enum EventDisplayMode
        {
            All,
            SubscribersOnly,
            PublishersOnly,
        }

        /// <summary>创建事件搜索面板并绑定其交互控件。</summary>
        /// <param name="container">面板宿主元素。</param><param name="eventInfoTemplate">事件卡片模板。</param><param name="eventPanelTemplate">面板模板。</param>
        public void Draw(VisualElement container, VisualTreeAsset eventInfoTemplate, VisualTreeAsset eventPanelTemplate)
        {
            _eventLogConsoleView?.Dispose();
            _eventSystemInfoCache.Clear();
            _eventInfoTemplate = eventInfoTemplate;
            _eventPanelTemplate = eventPanelTemplate ?? LoadPanelTemplate();
            _searchKeyword = string.Empty;

            if (_eventPanelTemplate == null)
            {
                container.Add(new HelpBox(
                    "EventSystemPanel.uxml not found. Create or assign the template to render Event System.",
                    HelpBoxMessageType.Error));
                return;
            }

            _viewRoot = _eventPanelTemplate.Instantiate();
            _toolbarContainer = _viewRoot.Q<VisualElement>("EventSystemToolbar");
            _resultContainer = _viewRoot.Q<VisualElement>("EventSearchResults");
            container.Add(_viewRoot);

            _toolbarContainer?.Add(CreateToolbar());
            var logRoot = _viewRoot.Q<VisualElement>("EventLogConsole");
            if (logRoot != null)
            {
                var logView = new EventLogConsoleView(logRoot, _eventLogCaptureService);
                _eventLogConsoleView = logView;
                _viewRoot.RegisterCallback<DetachFromPanelEvent>(_ => logView.Dispose());
            }
            SetResultActionsEnabled(false);
            ShowEmptyState("Click Search to scan EventSystem and BusinessArchitecture event calls.");
        }

        /// <summary>创建搜索、筛选和折叠操作栏。</summary>
        /// <returns>操作栏元素。</returns>
        private VisualElement CreateToolbar()
        {
            var toolbar = new VisualElement();
            toolbar.AddToClassList("event-toolbar");

            var actionGroup = new VisualElement();
            actionGroup.AddToClassList("event-action-row");
            actionGroup.Add(CreatePrimaryButton("Search Event Calls", RefreshSearchResults));
            _expandAllButton = CreateActionButton("Expand All", () => SetAllFoldouts(true));
            _foldAllButton = CreateActionButton("Collapse All", () => SetAllFoldouts(false));
            actionGroup.Add(_expandAllButton);
            actionGroup.Add(_foldAllButton);

            var filterGroup = new VisualElement();
            filterGroup.AddToClassList("event-filter-row");
            filterGroup.Add(CreateDisplayModeField());
            filterGroup.Add(CreateSearchField());

            _summaryLabel = new Label();
            _summaryLabel.AddToClassList("event-summary");
            filterGroup.Add(_summaryLabel);

            toolbar.Add(actionGroup);
            toolbar.Add(filterGroup);
            return toolbar;
        }

        /// <summary>创建突出显示的主操作按钮。</summary>
        /// <param name="text">按钮文字。</param><param name="clicked">点击回调。</param><returns>按钮元素。</returns>
        private Button CreatePrimaryButton(string text, Action clicked)
        {
            var button = CreateActionButton(text, clicked);
            button.RemoveFromClassList("event-button-neutral");
            button.AddToClassList("event-button-primary");
            return button;
        }

        /// <summary>创建普通操作按钮并绑定点击回调。</summary>
        /// <param name="text">按钮文字。</param><param name="clicked">点击回调。</param><returns>按钮元素。</returns>
        private static Button CreateActionButton(string text, Action clicked)
        {
            var button = new Button(clicked)
            {
                text = text,
            };
            button.AddToClassList("event-button");
            button.AddToClassList("event-button-neutral");
            return button;
        }

        /// <summary>创建监听者／发布者显示模式选择器。</summary>
        /// <returns>显示模式字段。</returns>
        private EnumField CreateDisplayModeField()
        {
            var displayModeField = new EnumField("Display:", _displayMode);
            displayModeField.AddToClassList("event-field");
            displayModeField.labelElement.AddToClassList("event-field-label");
            displayModeField.RegisterValueChangedCallback(evt =>
            {
                _displayMode = (EventDisplayMode)evt.newValue;
                ApplyFilters();
            });
            return displayModeField;
        }

        /// <summary>创建事件名称过滤输入框。</summary>
        /// <returns>搜索字段。</returns>
        private TextField CreateSearchField()
        {
            var searchField = new TextField("Event Search:")
            {
                isDelayed = true,
            };
            searchField.AddToClassList("event-field");
            searchField.AddToClassList("event-search-field");
            searchField.labelElement.AddToClassList("event-field-label");
            searchField.RegisterValueChangedCallback(evt =>
            {
                _searchKeyword = evt.newValue?.Trim() ?? string.Empty;
                ApplyFilters();
            });
            return searchField;
        }

        /// <summary>执行源码扫描并重建归并后的卡片数据。</summary>
        private void RefreshSearchResults()
        {
            _resultContainer.Clear();
            _eventSystemInfoCache.Clear();

            var result = _eventSearchService.SearchEventSystems();
            foreach (var kvp in result.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                _eventSystemInfoCache[kvp.Key] = kvp.Value;
            }

            DrawSearchResults();
            SetResultActionsEnabled(_eventSystemInfoCache.Count > 0);
            ApplyFilters();
        }

        /// <summary>把当前扫描结果绘制到滚动容器。</summary>
        private void DrawSearchResults()
        {
            if (_eventSystemInfoCache.Count == 0)
            {
                ShowEmptyState("No EventSystem or BusinessArchitecture event calls found.");
                return;
            }

            var title = new Label("Event call search results (source locations)");
            title.AddToClassList("event-results-title");
            _resultContainer.Add(title);

            if (_eventInfoTemplate == null)
            {
                _resultContainer.Add(new HelpBox(
                    "Event item template is not assigned. Drag EventInfoItem.uxml into FrameSettingWindow.",
                    HelpBoxMessageType.Error));
                return;
            }

            foreach (var kvp in _eventSystemInfoCache)
            {
                _resultContainer.Add(CreateEventInfoElement(_eventInfoTemplate, kvp.Key, kvp.Value));
            }
        }

        /// <summary>创建单个事件卡片并填充监听、发布位置。</summary>
        /// <param name="eventInfoTemplate">卡片模板。</param><param name="eventName">事件索引名称。</param><param name="info">归并信息。</param><returns>卡片元素。</returns>
        private VisualElement CreateEventInfoElement(VisualTreeAsset eventInfoTemplate, string eventKey, EventSystemInfo info)
        {
            var eventInfoVE = eventInfoTemplate.Instantiate();
            eventInfoVE.userData = eventKey;
            var eventNameLabel = eventInfoVE.Q<Label>("EventName");
            if (eventNameLabel != null)
            {
                eventNameLabel.text = info.DisplayName;
                eventNameLabel.tooltip = info.Tooltip;
            }

            Label centerLabel = eventInfoVE.Q<Label>("EventCenter");
            if (centerLabel != null)
            {
                centerLabel.text = info.Center.ToString();
                centerLabel.tooltip = info.IsGenericForwarding ? info.Tooltip : $"事件中心：{info.Center}";
            }

            Label forwardingLabel = eventInfoVE.Q<Label>("GenericForwarding");
            if (forwardingLabel != null)
            {
                forwardingLabel.EnableInClassList("event-generic-tag-hidden", !info.IsGenericForwarding);
                forwardingLabel.tooltip = info.Tooltip;
            }

            var listenerCount = eventInfoVE.Q<Label>("ListenerCount");
            if (listenerCount != null)
            {
                listenerCount.text = $"Listeners: {info.RegisterCalls.Count}";
            }

            var publisherCount = eventInfoVE.Q<Label>("PublisherCount");
            if (publisherCount != null)
            {
                publisherCount.text = $"Publishers: {info.TriggerCalls.Count}";
            }

            PopulateScriptContainer(eventInfoVE.Q<CustomScrollView>("ListenerScrollContainer"), info.RegisterCalls);
            PopulateScriptContainer(eventInfoVE.Q<CustomScrollView>("PublisherScrollContainer"), info.TriggerCalls);

            ApplyEventVisibility(eventInfoVE, info, eventInfoVE.Q<VisualElement>("Listener"),
                eventInfoVE.Q<VisualElement>("Publisher"));
            return eventInfoVE;
        }

        /// <summary>将源码调用位置渲染为可双击打开的脚本字段。</summary>
        /// <param name="container">列表容器。</param><param name="calls">调用位置。</param>
        private static void PopulateScriptContainer(VisualElement container, IReadOnlyList<EventCallInfo> calls)
        {
            if (container == null)
            {
                return;
            }

            for (var i = 0; i < calls.Count; i++)
            {
                EventCallInfo call = calls[i];
                string source = call.Source == EventCallSource.BusinessArchitecture ? "BusinessArchitecture" : "EventSystem";
                var scObjectField = new ObjectField($"{source} · line {call.Line}")
                {
                    objectType = typeof(MonoScript),
                    value = call.Script,
                    tooltip = BuildCallTooltip(call)
                };
                scObjectField.AddToClassList("event-script-field");
                scObjectField.RegisterCallback<MouseDownEvent>(evt =>
                    HandleScriptDoubleClick(evt, call.Script, call.Line));
                container.Add(scObjectField);
            }
        }

        /// <summary>根据当前角色和关键字筛选事件卡片。</summary>
        private void ApplyFilters()
        {
            if (_resultContainer == null)
            {
                return;
            }

            int visibleCount = 0;
            foreach (var item in _resultContainer.Query<TemplateContainer>().ToList())
            {
                string eventKey = item.userData as string;
                if (string.IsNullOrEmpty(eventKey) || !_eventSystemInfoCache.TryGetValue(eventKey, out var info))
                {
                    continue;
                }

                if (ApplyEventVisibility(item, info, item.Q<VisualElement>("Listener"),
                        item.Q<VisualElement>("Publisher")))
                {
                    visibleCount++;
                }
            }

            UpdateSummary(visibleCount);
        }

        /// <summary>组合源码路径、表达式和泛型转发说明。</summary>
        /// <param name="call">调用位置。</param><returns>Tooltip 文本。</returns>
        private static string BuildCallTooltip(EventCallInfo call)
        {
            string path = call.Script == null ? string.Empty : AssetDatabase.GetAssetPath(call.Script);
            string tooltip = string.IsNullOrEmpty(path) ? call.Expression : path + "\n" + call.Expression;
            if (call.IsGenericForwarding)
                tooltip += "\n泛型转发：具体事件类型由调用方决定，此处仅记录转发位置。";
            return tooltip;
        }

        /// <summary>计算一个事件卡片及其角色区块的可见性。</summary>
        /// <param name="eventInfoVE">事件卡片。</param><param name="info">归并信息。</param><param name="listenerSection">监听区块。</param><param name="publisherSection">发布区块。</param><returns>卡片是否可见。</returns>
        private bool ApplyEventVisibility(VisualElement eventInfoVE, EventSystemInfo info, VisualElement listenerSection,
            VisualElement publisherSection)
        {
            bool hasListeners = info.RegisterCalls.Count > 0;
            bool hasPublishers = info.TriggerCalls.Count > 0;
            string eventName = eventInfoVE.Q<Label>("EventName")?.text ?? string.Empty;
            bool keywordMatch = string.IsNullOrEmpty(_searchKeyword) ||
                                eventName.IndexOf(_searchKeyword, StringComparison.OrdinalIgnoreCase) >= 0;

            if (listenerSection != null)
            {
                listenerSection.style.display = _displayMode != EventDisplayMode.PublishersOnly && hasListeners
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            if (publisherSection != null)
            {
                publisherSection.style.display = _displayMode != EventDisplayMode.SubscribersOnly && hasPublishers
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            bool hasVisibleSection = _displayMode switch
            {
                EventDisplayMode.SubscribersOnly => hasListeners,
                EventDisplayMode.PublishersOnly => hasPublishers,
                _ => hasListeners || hasPublishers
            };
            bool visible = keywordMatch && hasVisibleSection;
            eventInfoVE.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            return visible;
        }

        /// <summary>统一设置当前结果中所有折叠面板的展开状态。</summary>
        /// <param name="expanded">是否展开。</param>
        private void SetAllFoldouts(bool expanded)
        {
            if (_resultContainer == null)
            {
                return;
            }

            foreach (var foldout in _resultContainer.Query<Foldout>().ToList())
            {
                foldout.value = expanded;
            }
        }

        /// <summary>根据是否存在结果启用或禁用批量操作。</summary>
        /// <param name="enabled">是否启用。</param>
        private void SetResultActionsEnabled(bool enabled)
        {
            _foldAllButton?.SetEnabled(enabled);
            _expandAllButton?.SetEnabled(enabled);
            UpdateSummary(_eventSystemInfoCache.Count);
        }

        /// <summary>清空结果并显示提示状态。</summary>
        /// <param name="message">提示文本。</param>
        private void ShowEmptyState(string message)
        {
            _resultContainer?.Clear();
            _resultContainer?.Add(new HelpBox(message, HelpBoxMessageType.Info));
            UpdateSummary(0);
        }

        /// <summary>刷新当前筛选结果数量摘要。</summary>
        /// <param name="visibleCount">可见卡片数量。</param>
        private void UpdateSummary(int visibleCount)
        {
            if (_summaryLabel == null)
            {
                return;
            }

            _summaryLabel.text = _eventSystemInfoCache.Count == 0
                ? "Events: 0"
                : $"Events: {visibleCount}/{_eventSystemInfoCache.Count}";
        }

        /// <summary>处理脚本字段双击并跳转到调用起始行。</summary>
        /// <param name="evt">鼠标事件。</param><param name="script">脚本资源。</param><param name="sourceLine">调用起始行。</param>
        private static void HandleScriptDoubleClick(MouseDownEvent evt, MonoScript script, int sourceLine)
        {
            if (evt.clickCount != 2 || evt.button != 0)
            {
                return;
            }

            evt.StopImmediatePropagation();
            if (script == null)
            {
                return;
            }

            int openLine = Math.Max(1, sourceLine);
            var assetPath = AssetDatabase.GetAssetPath(script);
            var fullPath = string.IsNullOrEmpty(assetPath)
                ? string.Empty
                : System.IO.Path.GetFullPath(assetPath);
            Debug.Log($"[FrameSetting] Open request: file={fullPath} openLine={openLine} assetPath={assetPath}");

            try
            {
                if (!string.IsNullOrEmpty(fullPath))
                {
                    InternalEditorUtility.OpenFileAtLineExternal(fullPath, openLine);
                }
                else
                {
                    AssetDatabase.OpenAsset(script, openLine);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FrameSetting] Open with line failed: {ex.Message}. Trying fallback to OpenAsset.");
                AssetDatabase.OpenAsset(script, openLine);
            }
        }

        /// <summary>从项目资源中查找默认事件面板模板。</summary>
        /// <returns>找到的模板，找不到时返回 null。</returns>
        private static VisualTreeAsset LoadPanelTemplate()
        {
            var guids = AssetDatabase.FindAssets("EventSystemPanel t:VisualTreeAsset");
            if (guids.Length == 0) return null;

            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
        }
    }
}
