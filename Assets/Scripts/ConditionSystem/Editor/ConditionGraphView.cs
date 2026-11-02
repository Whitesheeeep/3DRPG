#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.UIToolkitExtensions.Editor.GraphView;

namespace RPG.ConditionSystemNS.Editor
{
    /// <summary>提供条件树的图交互，所有结构规则复用运行时图契约。</summary>
    internal sealed class ConditionGraphView : WSGraphView, IGraphConnectionPolicy, IGraphChangeListener,
        IGraphContextMenuProvider, IGraphNodeInteractionListener
    {
        #region 依赖字段
        // key：条件领域节点；value：当前画布中的节点视图。
        private readonly Dictionary<ConditionNode, ConditionGraphNodeView> viewByNodeMap =
            new Dictionary<ConditionNode, ConditionGraphNodeView>();
        internal ConditionGraph_SO Graph { get; private set; }
        #endregion

        #region 事件
        internal event Action<GraphChangeEvent> Changed;
        internal event Action<ConditionNode> Selected;
        internal event Action<Type, Vector2> CreateRequested;
        internal event Action<ConditionNode> RootRequested;
        #endregion

        #region 生命周期与刷新
        // 生命周期：使用框架既有交互和默认端口连接器。
        /// <summary>启用框架画布并清除不在第一版范围内的复制粘贴入口。</summary>
        internal ConditionGraphView()
        {
            AddToClassList("condition-graph-view");
            serializeGraphElements = null;
            unserializeAndPaste = null;
        }

        // 刷新：从资产引用还原视觉图，不反向执行领域编辑。
        /// <summary>仅重建视觉元素，不发送领域删除或连接通知。</summary>
        /// <param name="graph">待展示资产，可以为空。</param>
        internal void Rebuild(ConditionGraph_SO graph)
        {
            Graph = graph;
            ClearGraphView();
            viewByNodeMap.Clear();
            if (graph == null) return;
            foreach (ConditionNode node in graph.Nodes)
            {
                if (node == null || viewByNodeMap.ContainsKey(node)) continue;
                var view = new ConditionGraphNodeView(node);
                viewByNodeMap.Add(node, view);
                AddGraphNodeView(view, node.EditorPosition);
            }
            foreach (ConditionNode parent in viewByNodeMap.Keys)
            {
                if (!viewByNodeMap[parent].TryGetPort("children", out Port output)) continue;
                foreach (ConditionNode child in parent.Children)
                {
                    if (child == null || !viewByNodeMap.TryGetValue(child, out ConditionGraphNodeView childView)) continue;
                    childView.TryGetPort("parent", out Port input);
                    AddGraphEdgeView(output.ConnectTo(input));
                }
            }
        }

        /// <summary>在参数变化后刷新摘要，不重置画布变换。</summary>
        internal void RefreshSummaries()
        {
            foreach (ConditionGraphNodeView view in viewByNodeMap.Values) view.RefreshContent();
        }

        /// <summary>选择并定位一个领域节点，供校验问题点击使用。</summary>
        /// <param name="node">需要定位的节点。</param>
        internal void SelectNode(ConditionNode node)
        {
            if (node == null || !viewByNodeMap.TryGetValue(node, out ConditionGraphNodeView view)) return;
            SelectGraphNode(view);
            FrameSelection();
        }
        #endregion

        #region 框架扩展
        /// <summary>复用图模型规则检查候选连接，不改变 Unity 默认拖线行为。</summary>
        /// <param name="context">标准化的父输出到子输入连接。</param>
        /// <returns>候选连接的允许状态。</returns>
        public GraphConnectionValidationResult ValidateConnection(GraphConnectionContext context)
        {
            if (Graph == null || !(context.OutputNode is ConditionGraphNodeView parent) ||
                !(context.InputNode is ConditionGraphNodeView child))
                return GraphConnectionValidationResult.Reject("请先打开条件图。");
            ConditionValidationIssue issue = Graph.GetConnectionIssue(parent.Model, child.Model);
            return issue == null ? GraphConnectionValidationResult.Allowed : GraphConnectionValidationResult.Reject(issue.Message);
        }

        /// <summary>转发已经实际生效的图变化，由 Controller 执行 Undo 和领域写入。</summary>
        /// <param name="change">框架变更快照。</param>
        public void OnGraphChanged(GraphChangeEvent change)
        {
            // 框架通知延迟到下一次调度；切图、重建或 Undo 之后，旧视图不能再写入当前资产。
            WSGraphNode[] currentNodes = change.Nodes.Where(IsCurrentView).ToArray();
            GraphConnectionContext[] currentConnections = change.Connections
                .Where(connection => IsCurrentView(connection.OutputNode) && IsCurrentView(connection.InputNode)).ToArray();
            if (currentNodes.Length == 0 && currentConnections.Length == 0) return;
            Changed?.Invoke(new GraphChangeEvent(change.Type, currentNodes, currentConnections, change.MoveDelta));
        }

        /// <summary>确认延迟通知来自当前资产仍有效的视图实例，防止已销毁节点被再次处理。</summary>
        /// <param name="node">框架快照中的节点视图。</param>
        /// <returns>领域对象和视图映射仍属于当前画布时为 true。</returns>
        private bool IsCurrentView(WSGraphNode node)
        {
            if (!(node is ConditionGraphNodeView view) || view.Model == null) return false;
            return viewByNodeMap.TryGetValue(view.Model, out ConditionGraphNodeView current) && ReferenceEquals(current, view);
        }

        /// <summary>点击不独立修改领域状态，选择通知统一负责属性面板。</summary>
        /// <param name="context">点击快照。</param>
        public void OnNodeClicked(GraphNodeClickContext context) { }

        /// <summary>发送当前最终选择，空选时解除属性绑定。</summary>
        /// <param name="change">选择快照。</param>
        public void OnNodeSelectionChanged(GraphNodeSelectionChange change) => Selected?.Invoke(
            change.SelectedNodes.OfType<ConditionGraphNodeView>().LastOrDefault()?.Model);

        /// <summary>发现具体节点扩展类型，提供创建、删除和入口设置操作。</summary>
        /// <param name="context">右键目标和内容坐标。</param>
        /// <param name="menu">框架提供的菜单。</param>
        public void PopulateContextMenu(GraphContextMenuContext context, DropdownMenu menu)
        {
            if (Graph == null) return;
            if (context.Target == GraphContextTarget.Canvas)
            {
                foreach (Type type in TypeCache.GetTypesDerivedFrom<ConditionNode>()
                    .Where(type => !type.IsAbstract && !type.ContainsGenericParameters && !type.IsNestedPrivate)
                    .OrderBy(type => type.FullName))
                    menu.AppendAction("创建/" + type.Name, _ => CreateRequested?.Invoke(type, context.GraphPosition));
            }
            else if (context.Element is ConditionGraphNodeView view)
            {
                menu.AppendAction("设为根条件", _ => RootRequested?.Invoke(view.Model),
                    Graph.Nodes.Any(node => node != null && node.Children.Contains(view.Model))
                        ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
                menu.AppendAction("删除条件节点", _ => RemoveGraphElements(new[] { view }));
            }
        }
        #endregion
    }
}
#endif
