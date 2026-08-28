#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.UIToolkitExtensions.Editor;

namespace RPG.DialogueSystemModule.Editor
{
    /// <summary>
    /// Dialogue Graph Editor 的 UI View，负责控件绑定、用户意图转发和界面刷新。
    /// </summary>
    internal sealed class DialogueGraphEditorView
    {
        #region 字段与事件

        private readonly VisualElement root;
        private readonly ObjectField assetField;
        private readonly VisualElement nodeListContainer;
        private readonly VisualElement speakerListContainer;
        // 详情内容挂载在 UXML 提供的 ScrollView 中，避免刷新 Inspector 时替换滚动容器本身。
        private readonly ScrollView detailsScrollView;
        private readonly VisualElement detailsContainer;
        private readonly VisualElement validationContainer;
        private readonly Label validationLabel;
        private readonly Label statusLabel;
        private SerializedObject detailsSerializedObject;

        /// <summary>资产选择用户意图。</summary>
        internal event Action<DialogueAsset> AssetSelected;
        /// <summary>创建资产用户意图。</summary>
        internal event Action NewAssetRequested;
        /// <summary>保存资产用户意图。</summary>
        internal event Action SaveRequested;
        /// <summary>执行校验用户意图。</summary>
        internal event Action ValidateRequested;
        /// <summary>节点列表选择用户意图。</summary>
        internal event Action<DialogueNode> NodeSelected;
        /// <summary>选择 Speaker 资产用户意图。</summary>
        internal event Action<DialogueSpeaker> SpeakerSelected;
        /// <summary>创建 Speaker 资产用户意图。</summary>
        internal event Action SpeakerCreateRequested;
        /// <summary>重命名 Speaker 资产用户意图。</summary>
        internal event Action<DialogueSpeaker, string> SpeakerRenameRequested;
        /// <summary>SerializedObject 字段发生变化。</summary>
        internal event Action<DialogueNode> PropertiesChanged;

        #endregion

        #region 属性

        /// <summary>获取 GraphView 的挂载容器。</summary>
        internal VisualElement GraphContainer { get; }

        #endregion

        #region 生命周期

        /// <summary>查询 UXML 控件并注册用户输入事件。</summary>
        /// <param name="root">已经克隆完成的窗口根节点。</param>
        internal DialogueGraphEditorView(VisualElement root)
        {
            this.root = root;
            assetField = root.Q<ObjectField>("AssetField");
            nodeListContainer = root.Q<ScrollView>("NodeListContainer").contentContainer;
            speakerListContainer = root.Q<ScrollView>("SpeakerListContainer").contentContainer;
            detailsScrollView = root.Q<ScrollView>("DetailsContainer");
            detailsContainer = detailsScrollView.contentContainer;
            validationContainer = root.Q<ScrollView>("ValidationContainer").contentContainer;
            validationLabel = root.Q<Label>("ValidationLabel");
            statusLabel = root.Q<Label>("StatusLabel");
            GraphContainer = root.Q<VisualElement>("GraphContainer");
            ConfigureSplitViews();
            ConfigureDetailsScrollView();
            RegisterCallbacks();
        }

        /// <summary>解除 View 自身注册的 UI 回调。</summary>
        internal void Dispose()
        {
            detailsContainer.Unbind();
            detailsScrollView.UnregisterCallback<WheelEvent>(OnDetailsWheel, TrickleDown.TrickleDown);
            assetField.UnregisterValueChangedCallback(OnAssetChanged);
            root.Q<Button>("NewGraphButton").clicked -= OnNewAssetRequested;
            root.Q<Button>("SaveButton").clicked -= OnSaveAssetRequested;
            root.Q<Button>("ValidateButton").clicked -= OnValidateRequested;
            detailsContainer.UnregisterCallback<SerializedPropertyChangeEvent>(OnPropertyChanged);
            Button createSpeakerButton = speakerListContainer.Q<Button>("CreateSpeakerButton");
            if (createSpeakerButton != null)
                createSpeakerButton.clicked -= OnCreateSpeakerRequested;
            detailsSerializedObject = null;
        }

        #endregion

        #region 用户输入

        /// <summary>注册工具栏、Inspector 和资产选择事件。</summary>
        private void RegisterCallbacks()
        {
            assetField.objectType = typeof(DialogueAsset);
            assetField.allowSceneObjects = false;
            assetField.RegisterValueChangedCallback(OnAssetChanged);
            root.Q<Button>("NewGraphButton").clicked += OnNewAssetRequested;
            root.Q<Button>("SaveButton").clicked += OnSaveAssetRequested;
            root.Q<Button>("ValidateButton").clicked += OnValidateRequested;
            detailsContainer.RegisterCallback<SerializedPropertyChangeEvent>(OnPropertyChanged);
        }

        /// <summary>转发资产选择。</summary>
        /// <param name="change">资产字段变化。</param>
        private void OnAssetChanged(ChangeEvent<UnityEngine.Object> change) => AssetSelected?.Invoke(change.newValue as DialogueAsset);

        /// <summary>转发创建资产请求。</summary>
        private void OnNewAssetRequested() => NewAssetRequested?.Invoke();

        /// <summary>转发保存资产请求。</summary>
        private void OnSaveAssetRequested() => SaveRequested?.Invoke();

        /// <summary>转发校验请求。</summary>
        private void OnValidateRequested() => ValidateRequested?.Invoke();

        /// <summary>转发创建 Speaker 资产请求。</summary>
        private void OnCreateSpeakerRequested() => SpeakerCreateRequested?.Invoke();

        /// <summary>转发节点列表选择。</summary>
        /// <param name="node">选中的节点。</param>
        private void SelectNode(DialogueNode node) => NodeSelected?.Invoke(node);

        /// <summary>接收绑定字段变化并转发当前节点。</summary>
        /// <param name="change">序列化字段变化事件。</param>
        private void OnPropertyChanged(SerializedPropertyChangeEvent change)
        {
            if (detailsSerializedObject?.targetObject is DialogueNode node)
                PropertiesChanged?.Invoke(node);
        }

        #endregion

        #region 布局与刷新

        /// <summary>配置左右嵌套面板的会话宽度。</summary>
        private void ConfigureSplitViews()
        {
            root.Q<CustomTwoPanelSplitView>("NavigationSplitView")
                .ConfigureFixedPane(170f, 220f, 320f, "RPG.DialogueGraphEditor.NavigationWidth");
            root.Q<CustomTwoPanelSplitView>("InspectorSplitView")
                .ConfigureFixedPane(260f, 320f, 480f, "RPG.DialogueGraphEditor.InspectorWidth");
        }

        /// <summary>
        /// 配置 Node Details 的纵向滚动，并接管滚轮偏移，避免事件被外层布局吞掉。
        /// </summary>
        private void ConfigureDetailsScrollView()
        {
            detailsScrollView.mode = ScrollViewMode.Vertical;
            detailsScrollView.verticalScrollerVisibility = ScrollerVisibility.Auto;
            detailsScrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            detailsScrollView.RegisterCallback<WheelEvent>(OnDetailsWheel, TrickleDown.TrickleDown);
        }

        /// <summary>
        /// 将 Node Details 的滚轮增量转换为受边界约束的垂直 ScrollView 偏移。
        /// </summary>
        /// <param name="evt">Node Details 收到的滚轮事件。</param>
        private void OnDetailsWheel(WheelEvent evt)
        {
            float minimum = detailsScrollView.verticalScroller.lowValue;
            float maximum = Mathf.Max(minimum, detailsScrollView.verticalScroller.highValue);
            if (maximum <= minimum) return;

            float target = Mathf.Clamp(
                detailsScrollView.scrollOffset.y + evt.delta.y * 20f, minimum, maximum);
            detailsScrollView.scrollOffset = new Vector2(detailsScrollView.scrollOffset.x, target);
            evt.PreventDefault();
            evt.StopPropagation();
        }

        /// <summary>刷新工具栏当前资产显示。</summary>
        /// <param name="asset">当前资产。</param>
        internal void RefreshAssetField(DialogueAsset asset) => assetField.SetValueWithoutNotify(asset);

        /// <summary>重建节点导航列表。</summary>
        /// <param name="asset">当前资产。</param>
        internal void RefreshNodeList(DialogueAsset asset)
        {
            nodeListContainer.Clear();
            if (asset == null)
            {
                nodeListContainer.Add(new Label("请选择 DialogueAsset。"));
                return;
            }

            foreach (DialogueNode node in asset.Nodes)
            {
                if (node == null) continue;
                DialogueNode capturedNode = node;
                Button row = new Button(() => SelectNode(capturedNode))
                {
                    text = $"{GetNodeDisplayName(node)} · {ShortId(node.NodeId)}"
                };
                row.AddToClassList("dialogue-editor-node-row");
                nodeListContainer.Add(row);
            }
        }

        /// <summary>重建项目中的 DialogueSpeaker 资产列表和编辑入口。</summary>
        internal void RefreshSpeakerList()
        {
            speakerListContainer.Clear();
            speakerListContainer.Add(new Label("Dialogue Speakers"));
            speakerListContainer.Add(new Button(OnCreateSpeakerRequested)
            {
                name = "CreateSpeakerButton",
                text = "Create Speaker"
            });

            foreach (DialogueSpeaker speaker in DialogueSpeakerAssetUtility.FindAll())
            {
                DialogueSpeaker capturedSpeaker = speaker;
                VisualElement row = new VisualElement { name = $"DialogueSpeaker_{speaker.name}" };
                row.style.flexDirection = FlexDirection.Row;
                row.AddToClassList("dialogue-editor-speaker-row");
                Button selectButton = new Button(() => SpeakerSelected?.Invoke(capturedSpeaker))
                {
                    text = speaker.SpeakerName
                };
                selectButton.style.flexGrow = 1f;
                row.Add(selectButton);
                TextField renameField = new TextField { value = speaker.SpeakerName };
                renameField.style.flexGrow = 1f;
                renameField.style.width = 100f;
                row.Add(renameField);
                row.Add(new Button(() => SpeakerRenameRequested?.Invoke(capturedSpeaker, renameField.value))
                {
                    text = "Rename"
                });
                speakerListContainer.Add(row);
            }
        }

        /// <summary>刷新右侧节点详情并重新绑定 SerializedObject。</summary>
        /// <param name="node">当前选中节点。</param>
        internal void RefreshDetails(DialogueNode node)
        {
            detailsContainer.Unbind();
            detailsContainer.Clear();
            detailsSerializedObject = null;
            if (node == null)
            {
                detailsContainer.Add(new HelpBox("请选择 Graph 节点。", HelpBoxMessageType.Info));
                return;
            }

            Label nodeLabel = new Label($"{GetNodeDisplayName(node)}\nNodeId: {node.NodeId}");
            nodeLabel.style.whiteSpace = WhiteSpace.Normal;
            detailsContainer.Add(nodeLabel);
            detailsSerializedObject = new SerializedObject(node);
            if (node is DialogueSpeechNode speech)
            {
                AddBoundProperty("nodeName", "节点名称", false);
                AddBoundProperty("speaker", "Speaker", false);
                AddBoundProperty("text", "Text", true);
                AddBoundProperty("animationClip", "AnimationClip", false);
                AddBoundProperty("voiceClip", "VoiceClip", false);
                AddBoundProperty("animationFadeDuration", "Animation Fade Duration", false);
                AddBoundProperty("nextNode", "NextNode", false);
                AddBoundProperty("choices", "Choices", true);
            }
            else if (node is DialogueChoiceNode)
            {
                AddBoundProperty("nodeName", "节点名称", false);
                AddBoundProperty("text", "Text", true);
                AddBoundProperty("conditions", "Conditions", true);
                AddBoundProperty("actions", "Actions", true);
                AddBoundProperty("targetNode", "TargetNode", false);
            }
            else if (node is DialogueEntryNode)
                AddBoundProperty("firstSpeechNode", "First SpeechNode", false);
            detailsContainer.Bind(detailsSerializedObject);
        }

        /// <summary>刷新底部校验文本及状态样式。</summary>
        /// <param name="messages">校验消息。</param>
        internal void RefreshValidation(IReadOnlyList<DialogueValidationMessage> messages, bool hasAsset)
        {
            validationContainer.Clear();
            validationContainer.Add(validationLabel);
            if (!hasAsset)
            {
                validationLabel.text = "尚未选择 DialogueAsset。";
                validationLabel.EnableInClassList("dialogue-editor-validation--ok", true);
                validationLabel.EnableInClassList("dialogue-editor-validation--warning", false);
                validationLabel.EnableInClassList("dialogue-editor-validation--error", false);
                return;
            }

            validationLabel.text = messages.Count == 0
                ? "✓ Graph is valid"
                : string.Join("\n", messages.Select(message => $"[{message.Severity}] {message.Message}"));
            validationLabel.EnableInClassList("dialogue-editor-validation--ok", messages.Count == 0);
            validationLabel.EnableInClassList("dialogue-editor-validation--warning",
                messages.Any(message => message.Severity == DialogueValidationSeverity.Warning));
            validationLabel.EnableInClassList("dialogue-editor-validation--error",
                messages.Any(message => message.Severity == DialogueValidationSeverity.Error));
        }

        /// <summary>刷新底部状态栏。</summary>
        /// <param name="message">状态文本。</param>
        internal void RefreshStatus(string message) => statusLabel.text = message;

        #endregion

        #region Inspector 辅助

        /// <summary>添加一个 SerializedProperty 绑定字段。</summary>
        /// <param name="propertyName">字段名。</param>
        /// <param name="label">显示标签。</param>
        /// <param name="includeChildren">是否展开子字段。</param>
        private void AddBoundProperty(string propertyName, string label, bool includeChildren)
        {
            SerializedProperty property = detailsSerializedObject.FindProperty(propertyName);
            if (property != null)
                detailsContainer.Add(new PropertyField(property, label) { name = $"Property_{propertyName}" });
        }

        /// <summary>获取节点显示名称。</summary>
        /// <param name="node">领域节点。</param>
        /// <returns>显示名称。</returns>
        private static string GetNodeDisplayName(DialogueNode node)
        {
            if (node is DialogueEntryNode) return "EntryNode";
            if (node is DialogueSpeechNode speech)
                return string.IsNullOrWhiteSpace(speech.NodeName) ? "SpeechNode" : speech.NodeName;
            if (node is DialogueChoiceNode choice)
                return string.IsNullOrWhiteSpace(choice.NodeName) ? "ChoiceNode" : choice.NodeName;
            if (node is DialogueEndNode) return "EndNode";
            return "DialogueNode";
        }

        /// <summary>截取稳定 ID 的短显示文本。</summary>
        /// <param name="value">完整 ID。</param>
        /// <returns>短 ID。</returns>
        private static string ShortId(string value) => string.IsNullOrEmpty(value)
            ? "<empty>"
            : value.Length <= 8 ? value : value.Substring(0, 8);

        #endregion
    }
}
#endif
