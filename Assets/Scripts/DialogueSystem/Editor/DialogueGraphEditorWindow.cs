#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.UIToolkitExtensions.Editor;
using WS_Modules.UIToolkitExtensions.Editor.GraphView;

namespace RPG.DialogueSystem.Editor
{
    /// <summary>
    /// 以 MVC 组合方式提供 DialogueAsset 的 GraphView 编辑器窗口。
    /// </summary>
    public sealed class DialogueGraphEditorWindow : EditorWindow
    {
        #region 常量与字段

        private const string WindowTitle = "Dialogue Graph Editor";
        private const string WindowUxmlPath = "Assets/Scripts/DialogueSystem/Editor/DialogueGraphEditorWindow.uxml";
        private const string NavigationWidthSessionKey = "RPG.DialogueGraphEditor.NavigationWidth";
        private const string InspectorWidthSessionKey = "RPG.DialogueGraphEditor.InspectorWidth";

        private DialogueAsset currentAsset;
        private DialogueNode selectedNode;
        private SerializedObject selectedNodeSerializedObject;
        private DialogueGraphView graphView;
        private VisualElement nodeListContainer;
        private VisualElement speakerListContainer;
        private VisualElement detailsContainer;
        private Label validationLabel;
        private Label statusLabel;
        private ObjectField assetField;
        private CustomTwoPanelSplitView navigationSplitView;
        private CustomTwoPanelSplitView inspectorSplitView;

        #endregion

        #region 窗口入口

        /// <summary>
        /// 打开或聚焦对话 GraphView 编辑器。
        /// </summary>
        [MenuItem("RPG/Dialogue/Dialogue Graph Editor", priority = 100)]
        private static void ShowWindow()
        {
            DialogueGraphEditorWindow window = GetWindow<DialogueGraphEditorWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(980f, 640f);
            window.Show();
        }

        /// <summary>
        /// 双击 DialogueAsset 时打开对应编辑器窗口。
        /// </summary>
        /// <param name="instanceId">Unity 对象实例 ID。</param>
        /// <param name="line">资产打开请求的行号。</param>
        /// <returns>对象是 DialogueAsset 时返回 true。</returns>
        [OnOpenAsset]
        private static bool OnOpenAsset(int instanceId, int line)
        {
            DialogueAsset asset = EditorUtility.InstanceIDToObject(instanceId) as DialogueAsset;
            if (asset == null) return false;
            Open(asset);
            return true;
        }

        /// <summary>
        /// 打开窗口并载入指定对话资产。
        /// </summary>
        /// <param name="asset">待载入资产。</param>
        internal static void Open(DialogueAsset asset)
        {
            DialogueGraphEditorWindow window = GetWindow<DialogueGraphEditorWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(980f, 640f);
            window.Show();
            window.OpenAsset(asset);
        }

        #endregion

        #region 生命周期与组合

        /// <summary>
        /// 加载 UXML 窗口骨架，组合 GraphView、Inspector 和资产控制器。
        /// </summary>
        private void CreateGUI()
        {
            rootVisualElement.Clear();

            VisualTreeAsset windowAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(WindowUxmlPath);
            if (windowAsset == null)
            {
                rootVisualElement.Add(new HelpBox($"找不到 Dialogue Graph Editor UXML：{WindowUxmlPath}",
                    HelpBoxMessageType.Error));
                return;
            }

            windowAsset.CloneTree(rootVisualElement);
            BindWindowElements();
            ConfigureSplitViews();
            ConfigureGraphView();
            ConfigureToolbar();
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
            RefreshAssetPresentation();
        }

        /// <summary>
        /// 释放 GraphView 事件与 Inspector 绑定，避免窗口重建后保留旧 Model。
        /// </summary>
        private void OnDisable()
        {
            UnsubscribeGraphView();
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            graphView = null;
            selectedNodeSerializedObject = null;
        }

        /// <summary>
        /// 在 Undo/Redo 修改领域资产后重新读取 Model，避免 GraphView 保留旧节点和连线。
        /// </summary>
        private void OnUndoRedoPerformed()
        {
            if (currentAsset == null) return;
            if (selectedNode != null && !currentAsset.Nodes.Contains(selectedNode)) selectedNode = null;
            RefreshAssetPresentation();
        }

        #endregion

        #region 窗口控件绑定

        /// <summary>
        /// 查询 UXML 中的动态容器与状态控件，保留窗口 Controller 对它们的引用。
        /// </summary>
        private void BindWindowElements()
        {
            assetField = rootVisualElement.Q<ObjectField>("AssetField");
            nodeListContainer = rootVisualElement.Q<ScrollView>("NodeListContainer").contentContainer;
            speakerListContainer = rootVisualElement.Q<ScrollView>("SpeakerListContainer").contentContainer;
            detailsContainer = rootVisualElement.Q<ScrollView>("DetailsContainer").contentContainer;
            validationLabel = rootVisualElement.Q<Label>("ValidationLabel");
            statusLabel = rootVisualElement.Q<Label>("StatusLabel");
            navigationSplitView = rootVisualElement.Q<CustomTwoPanelSplitView>("NavigationSplitView");
            inspectorSplitView = rootVisualElement.Q<CustomTwoPanelSplitView>("InspectorSplitView");
        }

        /// <summary>
        /// 配置左右面板的可拖拽范围，并通过 SessionState 恢复当前 Unity 会话宽度。
        /// </summary>
        private void ConfigureSplitViews()
        {
            navigationSplitView.ConfigureFixedPane(170f, 220f, 320f, NavigationWidthSessionKey);
            inspectorSplitView.ConfigureFixedPane(260f, 320f, 480f, InspectorWidthSessionKey);
        }

        /// <summary>
        /// 创建并接入对话 GraphView 到 UXML 预留的画布容器。
        /// </summary>
        private void ConfigureGraphView()
        {
            VisualElement graphContainer = rootVisualElement.Q<VisualElement>("GraphContainer");
            graphView = new DialogueGraphView();
            graphView.GraphChanged += OnGraphChanged;
            graphView.NodeSelected += SelectNodeFromGraph;
            graphView.NodeCreateRequested += CreateNodeFromMenu;
            graphContainer.Add(graphView);
        }

        /// <summary>
        /// 配置资产选择和工具栏命令按钮的 Controller 回调。
        /// </summary>
        private void ConfigureToolbar()
        {
            assetField.objectType = typeof(DialogueAsset);
            assetField.allowSceneObjects = false;
            assetField.RegisterValueChangedCallback(evt => OpenAsset(evt.newValue as DialogueAsset));
            rootVisualElement.Q<Button>("NewGraphButton").clicked += CreateNewAsset;
            rootVisualElement.Q<Button>("SaveButton").clicked += SaveAsset;
            rootVisualElement.Q<Button>("ValidateButton").clicked += RefreshValidation;
        }

        #endregion

        #region 资产载入与保存

        /// <summary>
        /// 切换当前资产并从 Model 重建全部 View。
        /// </summary>
        /// <param name="asset">待载入资产。</param>
        private void OpenAsset(DialogueAsset asset)
        {
            currentAsset = asset;
            selectedNode = null;
            RefreshAssetPresentation();
        }

        /// <summary>
        /// 从当前资产刷新节点树、Speaker 列表、GraphView、详情和校验状态。
        /// </summary>
        private void RefreshAssetPresentation()
        {
            if (assetField != null && assetField.value != currentAsset)
                assetField.SetValueWithoutNotify(currentAsset);
            if (graphView == null) return;

            graphView.Rebuild(currentAsset?.Nodes);
            RefreshNodeList();
            RefreshSpeakerList();
            RefreshDetails();
            RefreshValidation();
            RefreshStatus();
        }

        /// <summary>
        /// 创建一个带 EntryNode 和首个 SpeechNode 的新 DialogueAsset。
        /// </summary>
        private void CreateNewAsset()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "创建 DialogueAsset", "DialogueAsset", "asset", "选择对话资产保存位置");
            if (string.IsNullOrEmpty(path)) return;

            DialogueAsset asset = CreateInstance<DialogueAsset>();
            DialogueEntryNode entry = CreateInstance<DialogueEntryNode>();
            DialogueSpeechNode speech = CreateInstance<DialogueSpeechNode>();
            asset.name = Path.GetFileNameWithoutExtension(path);
            entry.name = "EntryNode";
            speech.name = "SpeechNode_001";
            asset.SetDialogueId(asset.name);
            asset.SetEntryNode(entry);
            asset.AddNode(speech);
            entry.SetFirstSpeechNode(speech);
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.AddObjectToAsset(entry, asset);
            AssetDatabase.AddObjectToAsset(speech, asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            OpenAsset(asset);
        }

        /// <summary>保存当前 DialogueAsset、节点子资产和 Editor-only SpeakerId 设置。</summary>
        private void SaveAsset()
        {
            if (currentAsset == null) return;
            currentAsset.EnsureStableIds();
            EditorUtility.SetDirty(currentAsset);
            DialogueSpeakerIdSettings.instance.SaveSettings();
            AssetDatabase.SaveAssets();
            RefreshValidation();
            RefreshStatus("已保存");
        }

        #endregion

        #region 节点树与 SpeakerId

        /// <summary>刷新左侧节点树，并把行点击转换成 GraphView 选择。</summary>
        private void RefreshNodeList()
        {
            if (nodeListContainer == null) return;
            nodeListContainer.Clear();
            if (currentAsset == null)
            {
                nodeListContainer.Add(new Label("请选择 DialogueAsset。"));
                return;
            }

            for (int index = 0; index < currentAsset.Nodes.Count; index++)
            {
                DialogueNode node = currentAsset.Nodes[index];
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

        /// <summary>刷新左侧预定义 SpeakerId 列表和添加入口。</summary>
        private void RefreshSpeakerList()
        {
            if (speakerListContainer == null) return;
            speakerListContainer.Clear();
            speakerListContainer.Add(new Label("Speaker IDs"));

            TextField newSpeakerField = new TextField { name = "NewSpeakerIdField" };
            newSpeakerField.style.marginTop = 4f;
            speakerListContainer.Add(newSpeakerField);
            speakerListContainer.Add(new Button(() =>
            {
                if (DialogueSpeakerIdSettings.instance.AddSpeakerId(newSpeakerField.value))
                    RefreshSpeakerList();
            }) { text = "Add SpeakerId" });

            IReadOnlyList<string> speakerIds = DialogueSpeakerIdSettings.instance.SpeakerIds;
            for (int index = 0; index < speakerIds.Count; index++)
            {
                string speakerId = speakerIds[index];
                VisualElement row = new VisualElement { name = $"SpeakerId_{speakerId}" };
                row.style.flexDirection = FlexDirection.Row;
                row.AddToClassList("dialogue-editor-speaker-row");
                Label label = new Label(speakerId);
                label.style.flexGrow = 1f;
                row.Add(label);
                string capturedSpeakerId = speakerId;
                row.Add(new Button(() =>
                {
                    if (DialogueSpeakerIdSettings.instance.RemoveSpeakerId(capturedSpeakerId))
                    {
                        RefreshSpeakerList();
                        RefreshValidation();
                    }
                }) { text = "×" });
                speakerListContainer.Add(row);
            }
        }

        /// <summary>选中节点并同步 GraphView 与右侧 Inspector。</summary>
        /// <param name="node">待选中的领域节点。</param>
        private void SelectNode(DialogueNode node)
        {
            selectedNode = node;
            DialogueGraphNodeView view = graphView.FindView(node);
            if (view != null) graphView.SelectGraphNode(view);
            RefreshDetails();
            RefreshStatus();
        }

        /// <summary>接收 GraphView 的选择变化。</summary>
        /// <param name="node">当前选中的领域节点。</param>
        private void SelectNodeFromGraph(DialogueNode node)
        {
            selectedNode = node;
            RefreshDetails();
            RefreshStatus();
        }

        #endregion

        #region Details Inspector

        /// <summary>
        /// 解除旧 SerializedObject 绑定并为新节点创建 PropertyField。
        /// </summary>
        private void RefreshDetails()
        {
            if (detailsContainer == null) return;
            detailsContainer.Unbind();
            detailsContainer.Clear();
            if (selectedNode == null)
            {
                detailsContainer.Add(new HelpBox("请选择 Graph 节点。", HelpBoxMessageType.Info));
                return;
            }

            Label nodeLabel = new Label($"{GetNodeDisplayName(selectedNode)}\nNodeId: {selectedNode.NodeId}");
            nodeLabel.style.whiteSpace = WhiteSpace.Normal;
            detailsContainer.Add(nodeLabel);
            selectedNodeSerializedObject = new SerializedObject(selectedNode);

            if (selectedNode is DialogueSpeechNode speech)
            {
                AddSpeakerDropdown(speech);
                AddBoundProperty("text", "Text", true);
                AddBoundProperty("animationClip", "AnimationClip", false);
                AddBoundProperty("animationFadeDuration", "Animation Fade Duration", false);
                AddBoundProperty("nextNode", "NextNode", false);
                AddBoundProperty("choices", "Choices", true);
            }
            else if (selectedNode is DialogueChoiceNode)
            {
                AddBoundProperty("choiceId", "ChoiceId", false);
                AddBoundProperty("text", "Text", true);
                AddBoundProperty("conditions", "Conditions", true);
                AddBoundProperty("actions", "Actions", true);
                AddBoundProperty("targetNode", "TargetNode", false);
            }
            else if (selectedNode is DialogueEntryNode)
            {
                AddBoundProperty("firstSpeechNode", "First SpeechNode", false);
            }
            else if (selectedNode is DialogueEndNode)
            {
                AddBoundProperty("endType", "End Type", false);
            }

            detailsContainer.Bind(selectedNodeSerializedObject);
        }

        /// <summary>添加从 Editor-only SpeakerId 设置生成的下拉字段。</summary>
        /// <param name="speech">当前 SpeechNode。</param>
        private void AddSpeakerDropdown(DialogueSpeechNode speech)
        {
            List<string> choices = new List<string>(DialogueSpeakerIdSettings.instance.SpeakerIds);
            string currentSpeakerId = speech.SpeakerId ?? string.Empty;
            if (!choices.Contains(currentSpeakerId))
                choices.Insert(0, currentSpeakerId);

            DropdownField dropdown = new DropdownField("SpeakerId", choices, currentSpeakerId);
            dropdown.RegisterValueChangedCallback(evt =>
            {
                if (selectedNodeSerializedObject == null || selectedNode != speech) return;
                Undo.RecordObject(speech, "Change Dialogue SpeakerId");
                selectedNodeSerializedObject.Update();
                SerializedProperty property = selectedNodeSerializedObject.FindProperty("speakerId");
                property.stringValue = evt.newValue ?? string.Empty;
                selectedNodeSerializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(speech);
                RefreshNodeList();
                RefreshValidation();
            });
            detailsContainer.Add(dropdown);
        }

        /// <summary>添加一个绑定当前节点 SerializedObject 的 PropertyField。</summary>
        /// <param name="propertyName">序列化字段名。</param>
        /// <param name="label">显示标签。</param>
        /// <param name="includeChildren">是否展开子字段。</param>
        private void AddBoundProperty(string propertyName, string label, bool includeChildren)
        {
            SerializedProperty property = selectedNodeSerializedObject.FindProperty(propertyName);
            if (property == null) return;
            detailsContainer.Add(new PropertyField(property, label) { name = $"Property_{propertyName}" });
        }

        #endregion

        #region Graph Controller

        /// <summary>
        /// 将 GraphView 交互结果写回 DialogueAsset，并在结构变化后从 Model 重建视图。
        /// </summary>
        /// <param name="change">已经生效的 GraphView 变更。</param>
        private void OnGraphChanged(GraphChangeEvent change)
        {
            if (currentAsset == null) return;
            switch (change.Type)
            {
                case GraphChangeType.NodesMoved:
                    ApplyMovedNodes(change.Nodes, change.MoveDelta);
                    break;
                case GraphChangeType.ConnectionsCreated:
                    ApplyConnections(change.Connections, true);
                    break;
                case GraphChangeType.ConnectionsRemoved:
                    ApplyConnections(change.Connections, false);
                    break;
                case GraphChangeType.NodesRemoved:
                    RemoveNodes(change.Nodes);
                    break;
            }

            EditorUtility.SetDirty(currentAsset);
            RefreshNodeList();
            RefreshValidation();
            RefreshStatus();
        }

        /// <summary>使用 GraphView 提供的内容坐标增量写回节点子资产。</summary>
        /// <param name="nodes">已经移动的 GraphView 节点。</param>
        /// <param name="moveDelta">本次拖动在 GraphView 内容坐标中的增量。</param>
        private void ApplyMovedNodes(IReadOnlyList<WSGraphNode> nodes, Vector2 moveDelta)
        {
            for (int index = 0; index < nodes.Count; index++)
            {
                if (!(nodes[index] is DialogueGraphNodeView view)) continue;
                Undo.RecordObject(view.Model, "Move Dialogue Node");
                // Model 与 GraphView 始终使用同一内容坐标；用事件快照累加可避免延迟回调读取到重建后的旧层级位置。
                view.Model.EditorPosition += moveDelta;
                EditorUtility.SetDirty(view.Model);
            }
        }

        /// <summary>把 GraphView 连接创建或删除转换为领域直接引用变化。</summary>
        /// <param name="connections">已生效连接。</param>
        /// <param name="created">为 true 表示创建，否则表示删除。</param>
        private void ApplyConnections(IReadOnlyList<GraphConnectionContext> connections, bool created)
        {
            for (int index = 0; index < connections.Count; index++)
            {
                GraphConnectionContext context = connections[index];
                if (!(context.OutputNode is DialogueGraphNodeView output) ||
                    !(context.InputNode is DialogueGraphNodeView input)) continue;
                Undo.RecordObject(currentAsset, created ? "Connect Dialogue Nodes" : "Disconnect Dialogue Nodes");
                Undo.RecordObject(output.Model, created ? "Connect Dialogue Nodes" : "Disconnect Dialogue Nodes");
                ApplyConnection(output.Model, context.OutputDescriptor.Id, input.Model, created);
                EditorUtility.SetDirty(output.Model);
            }
        }

        /// <summary>按端口语义修改 Entry、Speech 和 Choice 的直接引用。</summary>
        /// <param name="output">输出领域节点。</param>
        /// <param name="outputPortId">输出端口 ID。</param>
        /// <param name="input">输入领域节点。</param>
        /// <param name="created">为 true 表示建立引用，否则清理引用。</param>
        private static void ApplyConnection(DialogueNode output, string outputPortId, DialogueNode input, bool created)
        {
            if (output is DialogueEntryNode entry && outputPortId == "entry-output")
                entry.SetFirstSpeechNode(created ? input as DialogueSpeechNode : null);
            else if (output is DialogueSpeechNode speech && outputPortId == "speech-output" &&
                     input is DialogueChoiceNode choice)
            {
                if (created) speech.AddChoice(choice);
                else speech.RemoveChoice(choice);
            }
            else if (output is DialogueSpeechNode speechLinear && outputPortId == "speech-output" &&
                     (input is DialogueSpeechNode || input is DialogueEndNode))
                speechLinear.SetNextNode(created ? input : null);
            else if (output is DialogueChoiceNode choiceOutput && outputPortId == "choice-target")
                choiceOutput.SetTargetNode(created ? input : null);
        }

        /// <summary>移除节点领域引用、清理子资产并刷新 GraphView。</summary>
        /// <param name="nodes">已经从 GraphView 删除的节点。</param>
        private void RemoveNodes(IReadOnlyList<WSGraphNode> nodes)
        {
            Undo.RecordObject(currentAsset, "Delete Dialogue Nodes");
            for (int index = 0; index < nodes.Count; index++)
            {
                if (!(nodes[index] is DialogueGraphNodeView view)) continue;
                DialogueNode model = view.Model;
                currentAsset.RemoveNode(model);
                Undo.DestroyObjectImmediate(model);
                if (ReferenceEquals(selectedNode, model)) selectedNode = null;
            }

            EditorUtility.SetDirty(currentAsset);
            RefreshAssetPresentation();
        }

        /// <summary>响应画布右键创建请求，创建节点子资产并重建 View。</summary>
        /// <param name="kind">待创建节点类型。</param>
        /// <param name="position">GraphView 内容坐标。</param>
        private void CreateNodeFromMenu(DialogueNodeKind kind, Vector2 position)
        {
            if (currentAsset == null) return;
            DialogueNode node = CreateNode(kind);
            node.EditorPosition = position;
            node.name = GetNodeDisplayName(node);
            Undo.RecordObject(currentAsset, "Create Dialogue Node");
            AssetDatabase.AddObjectToAsset(node, currentAsset);
            currentAsset.AddNode(node);
            Undo.RegisterCreatedObjectUndo(node, "Create Dialogue Node");
            if (node is DialogueEntryNode entry && currentAsset.EntryNode == null) currentAsset.SetEntryNode(entry);
            EditorUtility.SetDirty(currentAsset);
            AssetDatabase.SaveAssets();
            RefreshAssetPresentation();
            SelectNode(node);
        }

        /// <summary>按节点种类创建对应 ScriptableObject 子资产对象。</summary>
        /// <param name="kind">节点类型。</param>
        /// <returns>新建的领域节点。</returns>
        private static DialogueNode CreateNode(DialogueNodeKind kind)
        {
            return kind switch
            {
                DialogueNodeKind.Entry => CreateInstance<DialogueEntryNode>(),
                DialogueNodeKind.Speech => CreateInstance<DialogueSpeechNode>(),
                DialogueNodeKind.Choice => CreateInstance<DialogueChoiceNode>(),
                DialogueNodeKind.End => CreateInstance<DialogueEndNode>(),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知对话节点类型。")
            };
        }

        /// <summary>解除 GraphView 事件订阅。</summary>
        private void UnsubscribeGraphView()
        {
            if (graphView == null) return;
            graphView.GraphChanged -= OnGraphChanged;
            graphView.NodeSelected -= SelectNodeFromGraph;
            graphView.NodeCreateRequested -= CreateNodeFromMenu;
        }

        #endregion

        #region 校验与状态

        /// <summary>运行图校验并附加 SpeakerId 设置资产的一致性检查。</summary>
        private void RefreshValidation()
        {
            if (validationLabel == null) return;
            if (currentAsset == null)
            {
                validationLabel.text = "尚未选择 DialogueAsset。";
                validationLabel.EnableInClassList("dialogue-editor-validation--ok", true);
                return;
            }

            List<DialogueValidationMessage> messages = DialogueGraphValidator
                .Validate(currentAsset).ToList();
            HashSet<string> speakerIds = new HashSet<string>(DialogueSpeakerIdSettings.instance.SpeakerIds,
                StringComparer.Ordinal);
            for (int index = 0; index < currentAsset.Nodes.Count; index++)
            {
                if (!(currentAsset.Nodes[index] is DialogueSpeechNode speech) ||
                    string.IsNullOrWhiteSpace(speech.SpeakerId) || speakerIds.Contains(speech.SpeakerId)) continue;
                messages.Add(new DialogueValidationMessage(
                    DialogueValidationSeverity.Warning,
                    $"SpeakerId 未在 Editor 设置中定义：{speech.SpeakerId}。", speech.NodeId));
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

        /// <summary>刷新底部状态栏，显示当前资产、选择和保存状态。</summary>
        /// <param name="message">可选的操作状态。</param>
        private void RefreshStatus(string message = null)
        {
            if (statusLabel == null) return;
            string assetName = currentAsset == null ? "None" : currentAsset.name;
            string selectedName = selectedNode == null ? "None" : GetNodeDisplayName(selectedNode);
            statusLabel.text = $"Asset: {assetName}\nSelected: {selectedName}\n{message ?? "MVC · SerializedObject binding"}";
        }

        #endregion

        #region 显示辅助

        /// <summary>获取节点树和详情标题使用的显示名称。</summary>
        /// <param name="node">领域节点。</param>
        /// <returns>节点显示名称。</returns>
        private static string GetNodeDisplayName(DialogueNode node)
        {
            if (node is DialogueEntryNode) return "EntryNode";
            if (node is DialogueSpeechNode speech)
                return string.IsNullOrWhiteSpace(speech.SpeakerId) ? "SpeechNode" : $"SpeechNode · {speech.SpeakerId}";
            if (node is DialogueChoiceNode choice)
                return string.IsNullOrWhiteSpace(choice.ChoiceId) ? "ChoiceNode" : $"ChoiceNode · {choice.ChoiceId}";
            if (node is DialogueEndNode) return "EndNode";
            return "DialogueNode";
        }

        /// <summary>获取节点 GUID 的短显示文本。</summary>
        /// <param name="value">完整稳定标识。</param>
        /// <returns>适合左侧列表的短标识。</returns>
        private static string ShortId(string value)
        {
            if (string.IsNullOrEmpty(value)) return "<empty>";
            return value.Length <= 8 ? value : value.Substring(0, 8);
        }

        #endregion
    }
}
#endif
