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
    internal sealed class EventSystemView
    {
        private readonly Dictionary<string, EventSystemInfo> _eventSystemInfoCache = new Dictionary<string, EventSystemInfo>();
        private readonly IEventSearchService _eventSearchService = new EventSearchService();

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

        private enum EventDisplayMode
        {
            All,
            SubscribersOnly,
            PublishersOnly,
        }

        public void Draw(VisualElement container, VisualTreeAsset eventInfoTemplate, VisualTreeAsset eventPanelTemplate)
        {
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
            SetResultActionsEnabled(false);
            ShowEmptyState("Click Search to scan Register and EventTrigger calls.");
        }

        private VisualElement CreateToolbar()
        {
            var toolbar = new VisualElement();
            toolbar.AddToClassList("event-toolbar");

            var actionGroup = new VisualElement();
            actionGroup.AddToClassList("event-action-row");
            actionGroup.Add(CreatePrimaryButton("Search Register/EventTrigger", RefreshSearchResults));
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

        private Button CreatePrimaryButton(string text, Action clicked)
        {
            var button = CreateActionButton(text, clicked);
            button.RemoveFromClassList("event-button-neutral");
            button.AddToClassList("event-button-primary");
            return button;
        }

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

        private void DrawSearchResults()
        {
            if (_eventSystemInfoCache.Count == 0)
            {
                ShowEmptyState("No Register or EventTrigger calls found.");
                return;
            }

            var title = new Label("Event call search results");
            title.AddToClassList("event-results-title");
            _resultContainer.Add(title);

            var scrollView = new ScrollView();
            scrollView.AddToClassList("event-results");
            _resultContainer.Add(scrollView);

            if (_eventInfoTemplate == null)
            {
                scrollView.Add(new HelpBox(
                    "Event item template is not assigned. Drag EventInfoItem.uxml into FrameSettingWindow.",
                    HelpBoxMessageType.Error));
                return;
            }

            foreach (var kvp in _eventSystemInfoCache)
            {
                scrollView.Add(CreateEventInfoElement(_eventInfoTemplate, kvp.Key, kvp.Value));
            }
        }

        private VisualElement CreateEventInfoElement(VisualTreeAsset eventInfoTemplate, string eventName, EventSystemInfo info)
        {
            var eventInfoVE = eventInfoTemplate.Instantiate();
            var eventNameLabel = eventInfoVE.Q<Label>("EventName");
            if (eventNameLabel != null)
            {
                eventNameLabel.text = eventName;
                eventNameLabel.tooltip = eventName;
            }

            var listenerCount = eventInfoVE.Q<Label>("ListenerCount");
            if (listenerCount != null)
            {
                listenerCount.text = $"Listeners: {info.registerScripts.Count}";
            }

            var publisherCount = eventInfoVE.Q<Label>("PublisherCount");
            if (publisherCount != null)
            {
                publisherCount.text = $"Publishers: {info.triggerScripts.Count}";
            }

            PopulateScriptContainer(eventInfoVE.Q<CustomScrollView>("ListenerScrollContainer"), info.registerScripts,
                info.registerLine);
            PopulateScriptContainer(eventInfoVE.Q<CustomScrollView>("PublisherScrollContainer"), info.triggerScripts,
                info.triggerLine);

            ApplyEventVisibility(eventInfoVE, info, eventInfoVE.Q<VisualElement>("Listener"),
                eventInfoVE.Q<VisualElement>("Publisher"));
            return eventInfoVE;
        }

        private static void PopulateScriptContainer(VisualElement container, IReadOnlyList<MonoScript> scripts,
            IReadOnlyList<int> lines)
        {
            if (container == null)
            {
                return;
            }

            int count = Math.Min(scripts.Count, lines.Count);
            for (var i = 0; i < count; i++)
            {
                var script = scripts[i];
                int sourceLine = lines[i];
                var scObjectField = new ObjectField($"line {sourceLine}")
                {
                    objectType = typeof(MonoScript),
                    value = script,
                    tooltip = script == null ? string.Empty : AssetDatabase.GetAssetPath(script)
                };
                scObjectField.AddToClassList("event-script-field");
                scObjectField.RegisterCallback<MouseDownEvent>(evt =>
                    HandleScriptDoubleClick(evt, script, sourceLine));
                container.Add(scObjectField);
            }
        }

        private void ApplyFilters()
        {
            if (_resultContainer == null)
            {
                return;
            }

            int visibleCount = 0;
            foreach (var item in _resultContainer.Query<TemplateContainer>().ToList())
            {
                var eventNameLabel = item.Q<Label>("EventName");
                if (eventNameLabel == null || !_eventSystemInfoCache.TryGetValue(eventNameLabel.text, out var info))
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

        private bool ApplyEventVisibility(VisualElement eventInfoVE, EventSystemInfo info, VisualElement listenerSection,
            VisualElement publisherSection)
        {
            bool hasListeners = info.registerScripts.Count > 0;
            bool hasPublishers = info.triggerScripts.Count > 0;
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

        private void SetResultActionsEnabled(bool enabled)
        {
            _foldAllButton?.SetEnabled(enabled);
            _expandAllButton?.SetEnabled(enabled);
            UpdateSummary(_eventSystemInfoCache.Count);
        }

        private void ShowEmptyState(string message)
        {
            _resultContainer?.Clear();
            _resultContainer?.Add(new HelpBox(message, HelpBoxMessageType.Info));
            UpdateSummary(0);
        }

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

            int openLine = Math.Max(1, sourceLine - 4);
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

        private static VisualTreeAsset LoadPanelTemplate()
        {
            var guids = AssetDatabase.FindAssets("EventSystemPanel t:VisualTreeAsset");
            if (guids.Length == 0) return null;

            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
        }
    }
}
