#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.UIToolkitExtensions.Editor.GraphView;

namespace RPG.DialogueSystemModule.Editor
{
    /// <summary>
    /// 定义画布右键创建节点的类型。
    /// </summary>
    internal enum DialogueNodeKind
    {
        /// <summary>对话图入口节点。</summary>
        Entry,

        /// <summary>对白节点。</summary>
        Speech,

        /// <summary>选项子节点。</summary>
        Choice,

        /// <summary>结束节点。</summary>
        End
    }

    /// <summary>
    /// 基于 WSGraphView 的对话节点画布，负责 UI 连接约束和交互事件转发。
    /// </summary>
    internal sealed class DialogueGraphView : WSGraphView, IGraphConnectionPolicy,
        IGraphChangeListener, IGraphContextMenuProvider, IGraphNodeInteractionListener
    {
        #region 字段与事件

        private readonly Dictionary<DialogueNode, DialogueGraphNodeView> viewsByModel =
            new Dictionary<DialogueNode, DialogueGraphNodeView>();

        /// <summary>节点结构或布局已经在 GraphView 中生效。</summary>
        internal event Action<GraphChangeEvent> GraphChanged;

        /// <summary>用户选择节点后的领域节点变化。</summary>
        internal event Action<DialogueNode> NodeSelected;

        /// <summary>用户请求在画布创建节点。</summary>
        internal event Action<DialogueNodeKind, Vector2> NodeCreateRequested;

        #endregion

        #region GraphView 构建

        /// <summary>
        /// 创建空对话 GraphView，并启用现有 WSFrame 画布交互能力。
        /// </summary>
        internal DialogueGraphView()
        {
            name = "DialogueGraphView";
            AddToClassList("dialogue-graph-view");
        }

        /// <summary>
        /// 清空当前 View 并根据资产节点集合重建节点与直接引用连线。
        /// </summary>
        /// <param name="nodes">待显示的资产节点集合。</param>
        internal void Rebuild(IReadOnlyList<DialogueNode> nodes)
        {
            // 重建只操作视觉层，避免清空旧节点时被误认为用户删除并写回资产。
            ClearGraphView();
            viewsByModel.Clear();
            if (nodes == null) return;
            for (int index = 0; index < nodes.Count; index++)
            {
                DialogueNode node = nodes[index];
                if (node == null) continue;
                AddNodeView(node);
            }

            CreateModelEdges(nodes);
        }

        /// <summary>
        /// 为领域节点创建一个视觉节点，不修改领域数据或发送变更通知。
        /// </summary>
        /// <param name="node">待显示的领域节点。</param>
        /// <returns>新建的节点 View。</returns>
        internal DialogueGraphNodeView AddNodeView(DialogueNode node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (viewsByModel.TryGetValue(node, out DialogueGraphNodeView existingView))
                return existingView;

            DialogueGraphNodeView view = new DialogueGraphNodeView(node);
            viewsByModel.Add(node, view);
            AddGraphNodeView(view, node.EditorPosition);
            return view;
        }

        /// <summary>
        /// 移除一个领域节点对应的视觉节点，不修改领域数据。
        /// </summary>
        /// <param name="node">待移除的领域节点。</param>
        /// <returns>成功移除时返回 true。</returns>
        internal bool RemoveNodeView(DialogueNode node)
        {
            if (node == null || !viewsByModel.TryGetValue(node, out DialogueGraphNodeView view))
                return false;
            RemoveGraphNodeView(view);
            viewsByModel.Remove(node);
            return true;
        }

        /// <summary>
        /// 刷新一个节点 View 的摘要内容，不重建节点、连线或画布状态。
        /// </summary>
        /// <param name="node">需要刷新的领域节点。</param>
        internal void RefreshNodeView(DialogueNode node)
        {
            if (FindView(node) is DialogueGraphNodeView view) view.RefreshContent();
        }

        /// <summary>
        /// 刷新多个节点 View 的摘要内容。
        /// </summary>
        /// <param name="nodes">需要刷新的领域节点集合。</param>
        internal void RefreshNodeViews(IEnumerable<DialogueNode> nodes)
        {
            if (nodes == null) return;
            foreach (DialogueNode node in nodes) RefreshNodeView(node);
        }

        /// <summary>
        /// 按领域节点最新引用局部重建相关视觉连线，不重建节点和画布状态。
        /// </summary>
        /// <param name="node">引用发生变化的领域节点。</param>
        internal void RefreshEdgesForNode(DialogueNode node)
        {
            if (FindView(node) == null) return;

            // 只重建 Edge 层，节点实例、位置、选择状态和画布变换均保持不变。
            ClearGraphEdgesView();
            foreach (DialogueNode model in viewsByModel.Keys.ToList())
                CreateModelEdgesForNode(model);
        }

        /// <summary>按领域节点查找对应的 GraphView 节点。</summary>
        /// <param name="node">领域节点。</param>
        /// <returns>对应 View；找不到时为空。</returns>
        internal DialogueGraphNodeView FindView(DialogueNode node)
        {
            return node != null && viewsByModel.TryGetValue(node, out DialogueGraphNodeView view) ? view : null;
        }

        /// <summary>获取当前 GraphView 中所有业务节点 View。</summary>
        /// <returns>节点 View 集合。</returns>
        internal IReadOnlyCollection<DialogueGraphNodeView> GetNodeViews() => viewsByModel.Values;

        #endregion

        #region 连接约束

        /// <summary>
        /// 限制 Entry、Speech、Choice 和 End 的合法连接方向及目标类型。
        /// </summary>
        /// <param name="context">候选连接上下文。</param>
        /// <returns>允许或拒绝连接的结果。</returns>
        public GraphConnectionValidationResult ValidateConnection(GraphConnectionContext context)
        {
            if (!(context.OutputNode is DialogueGraphNodeView output) ||
                !(context.InputNode is DialogueGraphNodeView input))
                return GraphConnectionValidationResult.Reject("连接端点必须是对话节点。");

            string outputId = context.OutputDescriptor.Id;
            string inputId = context.InputDescriptor.Id;
            bool allowed = outputId == "entry-output" && inputId == "speech-input" &&
                           input.Model is DialogueSpeechNode;
            allowed |= outputId == "speech-output" && inputId == "speech-input" &&
                       input.Model is DialogueSpeechNode;
            allowed |= outputId == "speech-output" && inputId == "end-input" &&
                       input.Model is DialogueEndNode;
            allowed |= outputId == "speech-output" && inputId == "choice-owner" &&
                       input.Model is DialogueChoiceNode;
            allowed |= outputId == "choice-target" && inputId == "speech-input" &&
                       input.Model is DialogueSpeechNode;
            allowed |= outputId == "choice-target" && inputId == "end-input" &&
                       input.Model is DialogueEndNode;
            if (!allowed) return GraphConnectionValidationResult.Reject("当前节点类型不允许该连接。");

            if (output.Model is DialogueSpeechNode speech)
            {
                bool isLinearTarget = inputId == "speech-input" || inputId == "end-input";
                bool isChoiceTarget = inputId == "choice-owner";
                if (outputId == "speech-output" && isLinearTarget &&
                    (speech.NextNode != null || HasLinearOutputConnection(output)))
                    return GraphConnectionValidationResult.Reject(
                        "当前 SpeechNode 已经存在一个线性目标节点。");
                if (outputId == "speech-output" && isLinearTarget &&
                    (speech.Choices.Count > 0 || HasChoiceOutputConnection(output)))
                    return GraphConnectionValidationResult.Reject(
                        "当前 SpeechNode 已经存在 Choice，不能再连接线性目标。");
                if (outputId == "speech-output" && isChoiceTarget &&
                    (speech.NextNode != null || HasLinearOutputConnection(output)))
                    return GraphConnectionValidationResult.Reject(
                        "当前 SpeechNode 已经存在线性目标，不能再连接 Choice。");
            }

            bool duplicate = edges.Any(edge =>
                ReferenceEquals(edge.output?.node, context.OutputNode) &&
                ReferenceEquals(edge.input?.node, context.InputNode) &&
                edge.output?.userData is GraphPortDescriptor existingOutput &&
                existingOutput.Id == outputId &&
                edge.input?.userData is GraphPortDescriptor existingInput &&
                existingInput.Id == inputId);
            return duplicate
                ? GraphConnectionValidationResult.Reject("相同端口之间不允许重复连接。")
                : GraphConnectionValidationResult.Allowed;
        }

        /// <summary>判断 SpeechNode 是否已经连接 SpeechNode 或 EndNode 线性目标。</summary>
        /// <param name="node">待检查的 SpeechNode View。</param>
        /// <returns>存在线性目标边时返回 true。</returns>
        private bool HasLinearOutputConnection(DialogueGraphNodeView node)
        {
            return edges.Any(edge =>
                ReferenceEquals(edge.output?.node, node) &&
                edge.output.userData is GraphPortDescriptor outputDescriptor &&
                outputDescriptor.Id == "speech-output" &&
                edge.input?.userData is GraphPortDescriptor inputDescriptor &&
                (inputDescriptor.Id == "speech-input" || inputDescriptor.Id == "end-input"));
        }

        /// <summary>
        /// 判断 SpeechNode 是否已经存在 Choice 输出边。
        /// </summary>
        /// <param name="node">待检查的 SpeechNode View。</param>
        /// <returns>存在 Choice 输出边时返回 true。</returns>
        private bool HasChoiceOutputConnection(DialogueGraphNodeView node)
        {
            return edges.Any(edge =>
                ReferenceEquals(edge.output?.node, node) &&
                edge.output.userData is GraphPortDescriptor outputDescriptor &&
                outputDescriptor.Id == "speech-output" &&
                edge.input?.userData is GraphPortDescriptor inputDescriptor &&
                inputDescriptor.Id == "choice-owner");
        }

        #endregion

        #region WSFrame 事件转发

        /// <summary>把 GraphView 已生效变更转发给窗口 Controller。</summary>
        /// <param name="change">已经生效的图变更。</param>
        public void OnGraphChanged(GraphChangeEvent change)
        {
            GraphChanged?.Invoke(change);
        }

        /// <summary>记录点击节点并由统一选择事件刷新 Inspector。</summary>
        /// <param name="context">节点点击上下文。</param>
        public void OnNodeClicked(GraphNodeClickContext context)
        {
            if (context.Node is DialogueGraphNodeView nodeView) NodeSelected?.Invoke(nodeView.Model);
        }

        /// <summary>按最终选择集合刷新当前 Inspector 选择。</summary>
        /// <param name="change">节点选择变化快照。</param>
        public void OnNodeSelectionChanged(GraphNodeSelectionChange change)
        {
            DialogueGraphNodeView nodeView = change.SelectedNodes.FirstOrDefault() as DialogueGraphNodeView;
            NodeSelected?.Invoke(nodeView?.Model);
        }

        /// <summary>
        /// 提供空白画布创建节点以及连线删除菜单。
        /// </summary>
        /// <param name="context">当前右键上下文。</param>
        /// <param name="menu">待追加菜单。</param>
        public void PopulateContextMenu(GraphContextMenuContext context, DropdownMenu menu)
        {
            if (context.Target == GraphContextTarget.Canvas)
            {
                menu.AppendSeparator();
                AppendCreateAction(menu, "创建 SpeechNode", DialogueNodeKind.Speech, context.GraphPosition);
                AppendCreateAction(menu, "创建 ChoiceNode", DialogueNodeKind.Choice, context.GraphPosition);
                AppendCreateAction(menu, "创建 EndNode", DialogueNodeKind.End, context.GraphPosition);
                return;
            }

            if (context.Target == GraphContextTarget.Edge)
            {
                menu.AppendSeparator();
                menu.AppendAction("断开对话连接", _ => RemoveGraphElements(new[] { context.Element }));
            }
        }

        #endregion

        #region 领域连线

        /// <summary>根据领域节点直接引用创建 GraphView Edge。</summary>
        /// <param name="nodes">当前资产节点集合。</param>
        private void CreateModelEdges(IReadOnlyList<DialogueNode> nodes)
        {
            for (int index = 0; index < nodes.Count; index++)
            {
                CreateModelEdgesForNode(nodes[index]);
            }
        }

        /// <summary>
        /// 根据一个领域节点的直接引用创建其输出连线。
        /// </summary>
        /// <param name="node">待恢复连线的领域节点。</param>
        private void CreateModelEdgesForNode(DialogueNode node)
        {
            if (node is DialogueEntryNode entry)
                Connect(entry, "entry-output", entry.FirstSpeechNode, "speech-input");
            else if (node is DialogueSpeechNode speech)
            {
                Connect(speech, "speech-output", speech.NextNode, GetInputPortId(speech.NextNode));
                for (int choiceIndex = 0; choiceIndex < speech.Choices.Count; choiceIndex++)
                    Connect(speech, "speech-output", speech.Choices[choiceIndex], "choice-owner");
            }
            else if (node is DialogueChoiceNode choice)
                Connect(choice, "choice-target", choice.TargetNode, GetInputPortId(choice.TargetNode));
        }

        /// <summary>创建一条模型直接引用对应的 Edge。</summary>
        /// <param name="outputNode">输出节点。</param>
        /// <param name="outputPortId">输出端口 ID。</param>
        /// <param name="inputNode">输入节点。</param>
        /// <param name="inputPortId">输入端口 ID。</param>
        private void Connect(DialogueNode outputNode, string outputPortId, DialogueNode inputNode, string inputPortId)
        {
            if (inputNode == null || string.IsNullOrEmpty(inputPortId)) return;
            DialogueGraphNodeView outputView = FindView(outputNode);
            DialogueGraphNodeView inputView = FindView(inputNode);
            if (outputView == null || inputView == null ||
                !outputView.TryGetPort(outputPortId, out Port outputPort) ||
                !inputView.TryGetPort(inputPortId, out Port inputPort)) return;

            Edge edge = outputPort.ConnectTo(inputPort);
            AddGraphEdgeView(edge);
        }

        /// <summary>按目标节点类型选择输入端口 ID。</summary>
        /// <param name="node">目标节点。</param>
        /// <returns>目标输入端口 ID。</returns>
        private static string GetInputPortId(DialogueNode node)
        {
            if (node is DialogueSpeechNode) return "speech-input";
            if (node is DialogueChoiceNode) return "choice-owner";
            if (node is DialogueEndNode) return "end-input";
            return string.Empty;
        }

        /// <summary>追加一个创建节点菜单项。</summary>
        /// <param name="menu">目标菜单。</param>
        /// <param name="label">菜单显示名称。</param>
        /// <param name="kind">创建节点类型。</param>
        /// <param name="position">GraphView 内容坐标。</param>
        private void AppendCreateAction(DropdownMenu menu, string label, DialogueNodeKind kind, Vector2 position)
        {
            menu.AppendAction(label, _ => NodeCreateRequested?.Invoke(kind, position));
        }

        #endregion
    }
}
#endif
