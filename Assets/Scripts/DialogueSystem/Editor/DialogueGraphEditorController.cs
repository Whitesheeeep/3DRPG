#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using WS_Modules.UIToolkitExtensions.Editor.GraphView;

namespace RPG.DialogueSystemModule.Editor
{
    /// <summary>
    /// 协调 Dialogue Graph Editor 的资产 Model、GraphView 和面板 View。
    /// </summary>
    internal sealed class DialogueGraphEditorController : IDisposable
    {
        #region 字段

        private readonly DialogueGraphEditorView editorView;
        private readonly DialogueGraphView graphView;
        private DialogueAsset currentAsset;
        private DialogueNode selectedNode;
        private bool bound;

        #endregion

        #region 生命周期

        /// <summary>
        /// 创建编辑器 Controller。
        /// </summary>
        /// <param name="editorView">窗口 UI View。</param>
        /// <param name="graphView">对话 GraphView。</param>
        internal DialogueGraphEditorController(DialogueGraphEditorView editorView, DialogueGraphView graphView)
        {
            this.editorView = editorView ?? throw new ArgumentNullException(nameof(editorView));
            this.graphView = graphView ?? throw new ArgumentNullException(nameof(graphView));
        }

        /// <summary>
        /// 建立所有 View、GraphView 和 Undo 事件订阅，并执行首次刷新。
        /// </summary>
        internal void Bind()
        {
            if (bound) return;
            bound = true;
            editorView.AssetSelected += OpenAsset;
            editorView.NewAssetRequested += CreateNewAsset;
            editorView.SaveRequested += SaveAsset;
            editorView.ValidateRequested += RefreshValidation;
            editorView.NodeSelected += SelectNode;
            editorView.SpeakerAddRequested += AddSpeakerId;
            editorView.SpeakerRemoveRequested += RemoveSpeakerId;
            editorView.PropertiesChanged += OnPropertiesChanged;
            graphView.GraphChanged += OnGraphChanged;
            graphView.LayoutChanged += OnLayoutChanged;
            graphView.NodeSelected += SelectNodeFromGraph;
            graphView.NodeCreateRequested += CreateNodeFromMenu;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
            RefreshAllPresentation();
        }

        /// <summary>
        /// 解除所有事件订阅，释放 Controller 持有的编辑器生命周期资源。
        /// </summary>
        public void Dispose()
        {
            if (!bound)
            {
                editorView.Dispose();
                return;
            }

            editorView.AssetSelected -= OpenAsset;
            editorView.NewAssetRequested -= CreateNewAsset;
            editorView.SaveRequested -= SaveAsset;
            editorView.ValidateRequested -= RefreshValidation;
            editorView.NodeSelected -= SelectNode;
            editorView.SpeakerAddRequested -= AddSpeakerId;
            editorView.SpeakerRemoveRequested -= RemoveSpeakerId;
            editorView.PropertiesChanged -= OnPropertiesChanged;
            graphView.GraphChanged -= OnGraphChanged;
            graphView.LayoutChanged -= OnLayoutChanged;
            graphView.NodeSelected -= SelectNodeFromGraph;
            graphView.NodeCreateRequested -= CreateNodeFromMenu;
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            editorView.Dispose();
            bound = false;
        }

        #endregion

        #region 资产操作

        /// <summary>
        /// 切换当前资产并从 Model 重建视觉图。
        /// </summary>
        /// <param name="asset">待载入资产。</param>
        internal void OpenAsset(DialogueAsset asset)
        {
            currentAsset = asset;
            selectedNode = null;
            RefreshAllPresentation();
        }

        /// <summary>
        /// 创建一个包含 EntryNode 和首个 SpeechNode 的新资产。
        /// </summary>
        private void CreateNewAsset()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "创建 DialogueAsset", "DialogueAsset", "asset", "选择对话资产保存位置");
            if (string.IsNullOrEmpty(path)) return;

            DialogueAsset asset = UnityEngine.ScriptableObject.CreateInstance<DialogueAsset>();
            DialogueEntryNode entry = UnityEngine.ScriptableObject.CreateInstance<DialogueEntryNode>();
            DialogueSpeechNode speech = UnityEngine.ScriptableObject.CreateInstance<DialogueSpeechNode>();
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

        /// <summary>
        /// 保存当前资产和 Editor-only SpeakerId 设置。
        /// </summary>
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

        /// <summary>
        /// 在资产切换、首次载入和 Undo/Redo 后执行一次完整 View 刷新。
        /// </summary>
        private void RefreshAllPresentation()
        {
            editorView.RefreshAssetField(currentAsset);
            graphView.Rebuild(currentAsset?.Nodes);
            editorView.RefreshNodeList(currentAsset);
            editorView.RefreshSpeakerList();
            editorView.RefreshDetails(selectedNode);
            RefreshValidation();
            RefreshStatus(null);
        }

        /// <summary>
        /// 处理 Undo/Redo 后的整体资产状态变化。
        /// </summary>
        private void OnUndoRedoPerformed()
        {
            if (currentAsset == null) return;
            if (selectedNode != null && !currentAsset.Nodes.Contains(selectedNode)) selectedNode = null;
            RefreshAllPresentation();
        }

        #endregion

        #region 节点选择与 Speaker

        /// <summary>
        /// 从导航列表选择节点并同步 GraphView 选择状态。
        /// </summary>
        /// <param name="node">待选中的节点。</param>
        private void SelectNode(DialogueNode node)
        {
            selectedNode = node;
            DialogueGraphNodeView view = graphView.FindView(node);
            if (view != null) graphView.SelectGraphNode(view);
            RefreshSelectedPresentation();
        }

        /// <summary>
        /// 接收 GraphView 的节点选择变化。
        /// </summary>
        /// <param name="node">当前选择的节点。</param>
        private void SelectNodeFromGraph(DialogueNode node)
        {
            selectedNode = node;
            RefreshSelectedPresentation();
        }

        /// <summary>刷新选择相关面板，不重建整个画布。</summary>
        private void RefreshSelectedPresentation()
        {
            editorView.RefreshDetails(selectedNode);
            RefreshStatus(null);
        }

        /// <summary>
        /// 添加全局 SpeakerId，并通过 Controller 触发相关面板刷新。
        /// </summary>
        /// <param name="speakerId">待添加的 SpeakerId。</param>
        private void AddSpeakerId(string speakerId)
        {
            if (!DialogueSpeakerIdSettings.instance.AddSpeakerId(speakerId)) return;
            editorView.RefreshSpeakerList();
            RefreshValidation();
        }

        /// <summary>
        /// 删除全局 SpeakerId，并保留资产中历史值以便 Validation 报告。
        /// </summary>
        /// <param name="speakerId">待删除的 SpeakerId。</param>
        private void RemoveSpeakerId(string speakerId)
        {
            if (!DialogueSpeakerIdSettings.instance.RemoveSpeakerId(speakerId)) return;
            editorView.RefreshSpeakerList();
            RefreshValidation();
        }

        /// <summary>
        /// 响应 SerializedObject 字段变化，刷新节点摘要和相关面板。
        /// </summary>
        /// <param name="node">字段变化所属节点。</param>
        private void OnPropertiesChanged(DialogueNode node)
        {
            if (node == null) return;
            RefreshNodeAndPanels(node, false);
        }

        /// <summary>刷新单个节点及其列表、校验和状态。</summary>
        /// <param name="node">需要刷新的节点。</param>
        /// <param name="refreshDetails">是否重新绑定右侧 Inspector。</param>
        private void RefreshNodeAndPanels(DialogueNode node, bool refreshDetails = true)
        {
            graphView.RefreshNodeView(node);
            graphView.RefreshEdgesForNode(node);
            editorView.RefreshNodeList(currentAsset);
            if (refreshDetails) editorView.RefreshDetails(selectedNode);
            RefreshValidation();
            RefreshStatus(null);
        }

        #endregion

        #region Graph 变更

        /// <summary>
        /// 将 GraphView 用户变更写回 Model，并按变化类型执行局部刷新。
        /// </summary>
        /// <param name="change">已经在 GraphView 生效的变更。</param>
        private void OnGraphChanged(GraphChangeEvent change)
        {
            if (currentAsset == null) return;
            switch (change.Type)
            {
                case GraphChangeType.NodesMoved:
                    ApplyMovedNodes(change.Nodes, change.MoveDelta);
                    editorView.RefreshStatus("节点位置已更新");
                    break;
                case GraphChangeType.ConnectionsCreated:
                    ApplyConnections(change.Connections, true);
                    RefreshConnectionPanels(change.Connections);
                    break;
                case GraphChangeType.ConnectionsRemoved:
                    ApplyConnections(change.Connections, false);
                    RefreshConnectionPanels(change.Connections);
                    break;
                case GraphChangeType.NodesRemoved:
                    RemoveNodes(change.Nodes);
                    break;
            }

            EditorUtility.SetDirty(currentAsset);
            editorView.RefreshNodeList(currentAsset);
            RefreshValidation();
            if (change.Type != GraphChangeType.NodesMoved) RefreshStatus(null);
        }

        /// <summary>使用 GraphView 内容坐标增量更新节点位置。</summary>
        /// <param name="nodes">已经移动的 GraphView 节点。</param>
        /// <param name="moveDelta">本次移动的内容坐标增量。</param>
        private static void ApplyMovedNodes(IReadOnlyList<WSGraphNode> nodes, Vector2 moveDelta)
        {
            foreach (WSGraphNode node in nodes)
            {
                if (!(node is DialogueGraphNodeView view)) continue;
                Undo.RecordObject(view.Model, "Move Dialogue Node");
                view.Model.EditorPosition += moveDelta;
                EditorUtility.SetDirty(view.Model);
            }
        }

        /// <summary>
        /// 将通用 GraphView 布局结果写回各节点的编辑器位置，不重建画布。
        /// </summary>
        /// <param name="change">已经应用到视觉节点的位置变化。</param>
        private void OnLayoutChanged(GraphLayoutChange change)
        {
            if (currentAsset == null || change.Nodes.Count == 0) return;

            foreach (WSGraphNode graphNode in change.Nodes)
            {
                if (graphNode is not DialogueGraphNodeView view) continue;
                Undo.RecordObject(view.Model, "Layout Dialogue Nodes");
                view.Model.EditorPosition = view.GetPosition().position;
                EditorUtility.SetDirty(view.Model);
            }

            EditorUtility.SetDirty(currentAsset);
            editorView.RefreshNodeList(currentAsset);
            editorView.RefreshStatus("节点布局已更新");
            RefreshValidation();
        }

        /// <summary>将 GraphView 连接变化转换为领域直接引用变化。</summary>
        /// <param name="connections">连接上下文。</param>
        /// <param name="created">是否为建立连接。</param>
        private void ApplyConnections(IReadOnlyList<GraphConnectionContext> connections, bool created)
        {
            foreach (GraphConnectionContext context in connections)
            {
                if (!(context.OutputNode is DialogueGraphNodeView output) ||
                    !(context.InputNode is DialogueGraphNodeView input)) continue;
                Undo.RecordObject(currentAsset, created ? "Connect Dialogue Nodes" : "Disconnect Dialogue Nodes");
                Undo.RecordObject(output.Model, created ? "Connect Dialogue Nodes" : "Disconnect Dialogue Nodes");
                ApplyConnection(output.Model, context.OutputDescriptor.Id, input.Model, created);
                EditorUtility.SetDirty(output.Model);
            }
        }

        /// <summary>按端口语义修改领域节点直接引用。</summary>
        /// <param name="output">输出节点。</param>
        /// <param name="outputPortId">输出端口标识。</param>
        /// <param name="input">输入节点。</param>
        /// <param name="created">是否建立引用。</param>
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

        /// <summary>刷新连接两端的节点摘要。</summary>
        /// <param name="connections">已变化的连接。</param>
        private void RefreshConnectionPanels(IReadOnlyList<GraphConnectionContext> connections)
        {
            HashSet<DialogueNode> affected = new HashSet<DialogueNode>();
            foreach (GraphConnectionContext context in connections)
            {
                if (context.OutputNode is DialogueGraphNodeView output) affected.Add(output.Model);
                if (context.InputNode is DialogueGraphNodeView input) affected.Add(input.Model);
            }
            graphView.RefreshNodeViews(affected);
        }

        /// <summary>删除节点领域对象并仅移除对应视觉节点。</summary>
        /// <param name="nodes">已经从 GraphView 删除的节点。</param>
        private void RemoveNodes(IReadOnlyList<WSGraphNode> nodes)
        {
            Undo.RecordObject(currentAsset, "Delete Dialogue Nodes");
            foreach (WSGraphNode graphNode in nodes)
            {
                if (!(graphNode is DialogueGraphNodeView view)) continue;
                DialogueNode model = view.Model;
                graphView.RemoveNodeView(model);
                currentAsset.RemoveNode(model);
                Undo.DestroyObjectImmediate(model);
                if (ReferenceEquals(selectedNode, model)) selectedNode = null;
            }
            EditorUtility.SetDirty(currentAsset);
            editorView.RefreshDetails(selectedNode);
        }

        /// <summary>响应画布菜单创建节点，只追加新节点 View。</summary>
        /// <param name="kind">节点类型。</param>
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
            graphView.AddNodeView(node);
            SelectNode(node);
            editorView.RefreshNodeList(currentAsset);
            RefreshValidation();
        }

        /// <summary>根据节点类型创建对应的 ScriptableObject。</summary>
        /// <param name="kind">节点类型。</param>
        /// <returns>新建节点。</returns>
        private static DialogueNode CreateNode(DialogueNodeKind kind)
        {
            return kind switch
            {
                DialogueNodeKind.Entry => UnityEngine.ScriptableObject.CreateInstance<DialogueEntryNode>(),
                DialogueNodeKind.Speech => UnityEngine.ScriptableObject.CreateInstance<DialogueSpeechNode>(),
                DialogueNodeKind.Choice => UnityEngine.ScriptableObject.CreateInstance<DialogueChoiceNode>(),
                DialogueNodeKind.End => UnityEngine.ScriptableObject.CreateInstance<DialogueEndNode>(),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知对话节点类型。")
            };
        }

        #endregion

        #region 校验与状态

        /// <summary>运行图校验并附加 SpeakerId 设置一致性检查。</summary>
        private void RefreshValidation()
        {
            if (currentAsset == null)
            {
                editorView.RefreshValidation(Array.Empty<DialogueValidationMessage>(), false);
                return;
            }

            List<DialogueValidationMessage> messages = DialogueGraphValidator.Validate(currentAsset).ToList();
            HashSet<string> speakerIds = new HashSet<string>(DialogueSpeakerIdSettings.instance.SpeakerIds,
                StringComparer.Ordinal);
            foreach (DialogueNode node in currentAsset.Nodes)
            {
                if (!(node is DialogueSpeechNode speech) || string.IsNullOrWhiteSpace(speech.SpeakerId) ||
                    speakerIds.Contains(speech.SpeakerId)) continue;
                messages.Add(new DialogueValidationMessage(DialogueValidationSeverity.Warning,
                    $"SpeakerId 未在 Editor 设置中定义：{speech.SpeakerId}。", speech.NodeId));
            }
            editorView.RefreshValidation(messages, true);
        }

        /// <summary>刷新状态栏，显示当前资产、选择和操作状态。</summary>
        /// <param name="message">可选操作状态。</param>
        private void RefreshStatus(string message)
        {
            string assetName = currentAsset == null ? "None" : currentAsset.name;
            string selectedName = selectedNode == null ? "None" : GetNodeDisplayName(selectedNode);
            editorView.RefreshStatus($"Asset: {assetName}\nSelected: {selectedName}\n{message ?? "MVC · SerializedObject binding"}");
        }

        /// <summary>获取节点显示名称。</summary>
        /// <param name="node">领域节点。</param>
        /// <returns>节点名称。</returns>
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

        #endregion
    }
}
#endif
