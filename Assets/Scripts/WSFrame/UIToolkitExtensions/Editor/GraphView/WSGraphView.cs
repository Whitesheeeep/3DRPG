using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace WS_Modules.UIToolkitExtensions.Editor.GraphView
{
    /// <summary>
    /// 封装 GraphView 常用交互、连接校验、结果通知和右键菜单路由的通用基类。
    /// </summary>
    public abstract class WSGraphView : UnityEditor.Experimental.GraphView.GraphView
    {
        #region 常量与字段
        private const float MinimumScale = 0.05f;
        private const float MaximumScale = 4f;
        private static readonly Vector2 DefaultNodeSize = new Vector2(220f, 120f);

        private readonly IGraphConnectionPolicy connectionPolicy;
        private readonly IGraphChangeListener changeListener;
        private readonly IGraphNodeInteractionListener nodeInteractionListener;
        private int changeNotificationSuppressionDepth;

        // 选择的数据快照，避免每次都从 selection 里遍历 GraphElement。
        private List<WSGraphNode> selectedNodes = new List<WSGraphNode>();
        public IReadOnlyList<WSGraphNode> SelectedNodes => selectedNodes;
        #endregion

        #region 生命周期
        /// <summary>
        /// 创建通用 GraphView，并根据当前派生类型发现可选的连接策略与变更监听器。
        /// </summary>
        protected WSGraphView()
        {
            // 获取当前 GraphView 派生类的接口实现，允许业务层在同一对象上同时实现多个接口。
            // 没有实现时为 null，GraphView 仍可正常使用，但无法获得连接策略或变更通知。
            connectionPolicy = this as IGraphConnectionPolicy;
            changeListener = this as IGraphChangeListener;
            nodeInteractionListener = this as IGraphNodeInteractionListener;

            ConfigureViewport();
            RegisterNodeInteractionCallbacks();
            graphViewChanged = HandleGraphViewChanged;
        }
        #endregion

        #region 公开操作
        /// <summary>
        /// 暂停 GraphView 变更通知，供从 Model 重建视觉元素时使用。
        /// </summary>
        /// <returns>释放后恢复原有通知状态的作用域。</returns>
        protected IDisposable SuppressChangeNotifications()
        {
            changeNotificationSuppressionDepth++;
            return new ChangeNotificationScope(this);
        }

        /// <summary>
        /// 初始化并加入一个通用节点，然后在节点实际进入图后发送加入结果。
        /// </summary>
        /// <param name="node">待加入的节点。</param>
        /// <param name="position">节点左上角在 GraphView 内容坐标空间中的位置。</param>
        /// <exception cref="ArgumentNullException">节点为空时抛出。</exception>
        public void AddGraphNode(WSGraphNode node, Vector2 position)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));

            node.InitializeNode();
            Rect nodeRect = node.GetPosition();
            Vector2 nodeSize = nodeRect.width > 0f && nodeRect.height > 0f
                ? nodeRect.size
                : DefaultNodeSize;
            node.SetPosition(new Rect(position, nodeSize));
            AddElement(node);

            NotifyChange(new GraphChangeEvent(GraphChangeType.NodesAdded,
                new[] { node }, Array.Empty<GraphConnectionContext>(), Vector2.zero));
        }

        /// <summary>
        /// 请求 GraphView 删除指定元素；节点与连线结果由统一变更管线发送。
        /// </summary>
        /// <param name="elements">待删除的图元素。</param>
        public void RemoveGraphElements(IEnumerable<GraphElement> elements)
        {
            if (elements == null) throw new ArgumentNullException(nameof(elements));
            DeleteElements(elements.ToList());
        }

        /// <summary>
        /// 以替换或追加方式选择一个属于当前 GraphView 的节点。
        /// </summary>
        /// <param name="node">待选择的节点。</param>
        /// <param name="additive">为 true 时保留现有选择，否则先清空全部 GraphElement 选择。</param>
        /// <exception cref="ArgumentNullException">节点为空时抛出。</exception>
        /// <exception cref="ArgumentException">节点不属于当前 GraphView 时抛出。</exception>
        public void SelectGraphNode(WSGraphNode node, bool additive = false)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (!nodes.Contains(node))
                throw new ArgumentException("只能选择属于当前 GraphView 的节点。", nameof(node));

            if (!additive) ClearSelection();
            if (!selection.Contains(node)) AddToSelection(node);
            SynchronizeNodeSelection(node);
        }

        /// <summary>
        /// 清空当前 GraphView 的全部元素选择，并发送节点选择变化结果。
        /// </summary>
        public void ClearGraphNodeSelection()
        {
            ClearSelection();
            SynchronizeNodeSelection(null);
        }

        /// <summary>
        /// 返回可与起始端口建立连接的端口集合。
        /// </summary>
        /// <param name="startPort">当前正在拖出连线的端口。</param>
        /// <param name="nodeAdapter">GraphView 节点适配器。</param>
        /// <returns>满足基础约束和业务策略的端口集合。</returns>
        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            if (startPort == null) throw new ArgumentNullException(nameof(startPort));

            return ports.Where(candidate => IsCompatiblePort(startPort, candidate)).ToList();
        }
        #endregion

        #region 节点点击与选择
        /// <summary>
        /// 注册节点点击、鼠标选择完成和键盘选择变化监听，不改变事件默认传播行为。
        /// </summary>
        private void RegisterNodeInteractionCallbacks()
        {
            RegisterCallback<MouseDownEvent>(HandleNodeMouseDown, TrickleDown.TrickleDown);
            RegisterCallback<MouseUpEvent>(HandleSelectionMouseUp, TrickleDown.TrickleDown);
            RegisterCallback<KeyUpEvent>(HandleSelectionKeyUp, TrickleDown.TrickleDown);
        }

        /// <summary>
        /// 捕获左键命中的节点，并在 GraphView 完成默认选择处理后发送点击与选择结果。
        /// </summary>
        /// <param name="evt">鼠标按下事件。</param>
        private void HandleNodeMouseDown(MouseDownEvent evt)
        {
            if (evt.button != 0) return;
            WSGraphNode clickedNode = FindTargetGraphElement(evt.target as VisualElement) as WSGraphNode;
            if (clickedNode == null) return;

            Vector2 graphPosition = contentViewContainer.WorldToLocal(evt.mousePosition);
            GraphNodeClickContext context = new GraphNodeClickContext(
                this, clickedNode, graphPosition, evt.clickCount, evt.modifiers);

            // 延迟到 GraphView 自己的 SelectionDragger 处理完本次 MouseDown，再向业务暴露最终选择状态。
            schedule.Execute(() =>
            {
                if (clickedNode.panel != panel) return;
                nodeInteractionListener?.OnNodeClicked(context);
                SynchronizeNodeSelection(clickedNode);
            });
        }

        /// <summary>
        /// 鼠标交互结束后同步框选、空白取消以及 Edge 选择引起的节点选择变化。
        /// </summary>
        /// <param name="evt">鼠标抬起事件。</param>
        private void HandleSelectionMouseUp(MouseUpEvent evt)
        {
            if (evt.button != 0) return;
            schedule.Execute(() => SynchronizeNodeSelection(null));
        }

        /// <summary>
        /// 键盘交互结束后同步全选、取消或删除操作引起的节点选择变化。
        /// </summary>
        /// <param name="evt">键盘抬起事件。</param>
        private void HandleSelectionKeyUp(KeyUpEvent evt)
        {
            schedule.Execute(() => SynchronizeNodeSelection(null));
        }

        /// <summary>
        /// 比较 GraphView 当前选择与上次节点快照，更新选择状态 class 并发送一次差量通知。
        /// </summary>
        /// <param name="triggerNode">直接触发同步的节点；框选、空白或键盘操作时为空。</param>
        private void SynchronizeNodeSelection(WSGraphNode triggerNode)
        {
            List<WSGraphNode> currentSelection = selection.OfType<WSGraphNode>()
                .Where(node => node.parent != null).Distinct().ToList();
            HashSet<WSGraphNode> currentSet = new HashSet<WSGraphNode>(currentSelection);
            HashSet<WSGraphNode> previousSet = new HashSet<WSGraphNode>(selectedNodes);

            // 由框架统一维护稳定选中 class，业务 USS 不需要依赖 Unity GraphView 的内部选择器。
            foreach (WSGraphNode node in nodes.OfType<WSGraphNode>())
                node.EnableInClassList(WSGraphNode.SelectedClassName, currentSet.Contains(node));
            foreach (WSGraphNode removedNode in previousSet.Where(node => !currentSet.Contains(node)))
                removedNode.EnableInClassList(WSGraphNode.SelectedClassName, false);

            if (currentSet.SetEquals(previousSet)) return;

            WSGraphNode[] addedNodes = currentSelection.Where(node => !previousSet.Contains(node)).ToArray();
            WSGraphNode[] removedNodes = selectedNodes.Where(node => !currentSet.Contains(node)).ToArray();
            selectedNodes = currentSelection;

            nodeInteractionListener?.OnNodeSelectionChanged(new GraphNodeSelectionChange(
                this, currentSelection.ToArray(), addedNodes, removedNodes, triggerNode));
        }
        #endregion

        #region 视口配置
        /// <summary>
        /// 配置网格、缩放、平移、元素拖动和框选等通用画布交互。
        /// </summary>
        private void ConfigureViewport()
        {
            style.flexGrow = 1f;

            GridBackground grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();

            SetupZoom(MinimumScale, MaximumScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());
        }
        #endregion

        #region 连接校验
        /// <summary>
        /// 按基础约束和可选业务策略判断候选端口是否兼容。
        /// </summary>
        /// <param name="startPort">拖线起始端口。</param>
        /// <param name="candidate">候选目标端口。</param>
        /// <returns>候选端口可连接时返回 true。</returns>
        private bool IsCompatiblePort(Port startPort, Port candidate)
        {
            if (candidate == null || ReferenceEquals(startPort, candidate)) return false;
            if (startPort.node is not WSGraphNode || candidate.node is not WSGraphNode) return false;
            if (ReferenceEquals(startPort.node, candidate.node)) return false;
            if (startPort.direction == candidate.direction) return false;
            if (startPort.portType != candidate.portType) return false;
            if (IsSingleCapacityOccupied(startPort) || IsSingleCapacityOccupied(candidate)) return false;

            Port outputPort = startPort.direction == Direction.Output ? startPort : candidate;
            Port inputPort = startPort.direction == Direction.Input ? startPort : candidate;
            GraphConnectionContext context = new GraphConnectionContext(this, outputPort, inputPort);
            return connectionPolicy == null || connectionPolicy.ValidateConnection(context).IsAllowed;
        }

        /// <summary>
        /// 判断单连接容量端口是否已经被占用。
        /// </summary>
        /// <param name="port">待检查端口。</param>
        /// <returns>端口容量为 Single 且已有连接时返回 true。</returns>
        private static bool IsSingleCapacityOccupied(Port port) =>
            port.capacity == Port.Capacity.Single && port.connected;
        #endregion

        #region 变更处理
        /// <summary>
        /// 捕获 GraphView 即将执行的变更，并把结果延迟到元素层级完成更新后发送。
        /// </summary>
        /// <param name="change">Unity 提供的待执行变更。</param>
        /// <returns>保持原内容的变更对象。</returns>
        private GraphViewChange HandleGraphViewChanged(GraphViewChange change)
        {
            // 重建期间只需要让 Unity 完成视觉层级变更，不能把内部清空过程写回领域 Model。
            if (changeNotificationSuppressionDepth > 0) return change;

            List<WSGraphNode> movedNodes = change.movedElements?
                .OfType<WSGraphNode>().Distinct().ToList() ?? new List<WSGraphNode>();
            List<WSGraphNode> removedNodes = change.elementsToRemove?
                .OfType<WSGraphNode>().Distinct().ToList() ?? new List<WSGraphNode>();
            List<Edge> createdEdges = change.edgesToCreate?
                .Where(edge => edge != null).Distinct().ToList() ?? new List<Edge>();
            List<Edge> removedEdges = CollectRemovedEdges(change.elementsToRemove, removedNodes);

            if (movedNodes.Count == 0 && removedNodes.Count == 0 &&
                createdEdges.Count == 0 && removedEdges.Count == 0)
                return change;

            Vector2 moveDelta = change.moveDelta;
            List<GraphConnectionContext> createdConnections = CreateConnectionContexts(createdEdges);
            List<GraphConnectionContext> removedConnections = CreateConnectionContexts(removedEdges);

            // 删除 Edge 可能清空端口引用，必须先快照端点；随后延迟一拍检查层级状态，避免发送未生效结果。
            schedule.Execute(() => DispatchAppliedChanges(
                movedNodes, removedNodes, createdConnections, removedConnections, moveDelta));
            return change;
        }

        /// <summary>
        /// 收集显式删除的连线以及随节点删除而断开的连线，并按 Edge 实例去重。
        /// </summary>
        /// <param name="elementsToRemove">GraphView 请求删除的元素。</param>
        /// <param name="removedNodes">本次请求删除的节点。</param>
        /// <returns>本次将断开的所有连线。</returns>
        private static List<Edge> CollectRemovedEdges(List<GraphElement> elementsToRemove,
            IReadOnlyList<WSGraphNode> removedNodes)
        {
            HashSet<Edge> removedEdges = new HashSet<Edge>();
            if (elementsToRemove != null)
            {
                foreach (Edge edge in elementsToRemove.OfType<Edge>()) removedEdges.Add(edge);
            }

            // 节点删除可能只把 Node 放入变更列表，因此需要在端口断开前主动保存关联 Edge。
            foreach (WSGraphNode node in removedNodes)
            {
                foreach (Port port in node.inputContainer.Children().OfType<Port>())
                {
                    foreach (Edge edge in port.connections)
                        removedEdges.Add(edge);
                }
                foreach (Port port in node.outputContainer.Children().OfType<Port>())
                {
                    foreach (Edge edge in port.connections)
                        removedEdges.Add(edge);
                }
            }

            return removedEdges.ToList();
        }

        /// <summary>
        /// 根据元素最终层级发送已生效变更；断开连接先于节点删除，保证业务层仍可识别端点。
        /// </summary>
        /// <param name="movedNodes">请求移动的节点。</param>
        /// <param name="removedNodes">请求删除的节点。</param>
        /// <param name="createdConnections">请求创建的连接快照。</param>
        /// <param name="removedConnections">请求删除的连接快照。</param>
        /// <param name="moveDelta">节点移动增量。</param>
        private void DispatchAppliedChanges(IReadOnlyList<WSGraphNode> movedNodes,
            IReadOnlyList<WSGraphNode> removedNodes, IReadOnlyList<GraphConnectionContext> createdConnections,
            IReadOnlyList<GraphConnectionContext> removedConnections, Vector2 moveDelta)
        {
            List<GraphConnectionContext> appliedRemovedConnections = removedConnections
                .Where(context => context.Edge.parent == null).ToList();
            NotifyConnections(GraphChangeType.ConnectionsRemoved, appliedRemovedConnections);

            List<GraphConnectionContext> appliedCreatedConnections = createdConnections
                .Where(context => context.Edge.parent != null).ToList();
            NotifyConnections(GraphChangeType.ConnectionsCreated, appliedCreatedConnections);

            List<WSGraphNode> appliedMovedNodes = movedNodes.Where(node => node.parent != null).ToList();
            if (appliedMovedNodes.Count > 0)
                NotifyChange(new GraphChangeEvent(GraphChangeType.NodesMoved, appliedMovedNodes,
                    Array.Empty<GraphConnectionContext>(), moveDelta));

            List<WSGraphNode> appliedRemovedNodes = removedNodes.Where(node => node.parent == null).ToList();
            if (appliedRemovedNodes.Count > 0)
                NotifyChange(new GraphChangeEvent(GraphChangeType.NodesRemoved, appliedRemovedNodes,
                    Array.Empty<GraphConnectionContext>(), Vector2.zero));

            // GraphView 删除元素时会同时调整 selection，此处同步被删除节点的取消选择结果与 USS 状态。
            if (appliedRemovedNodes.Count > 0) SynchronizeNodeSelection(null);
        }

        /// <summary>
        /// 把仍具备完整端点信息的 Edge 转换为连接上下文。
        /// </summary>
        /// <param name="edges">待转换的连线。</param>
        /// <returns>成功规范化的连接上下文。</returns>
        private List<GraphConnectionContext> CreateConnectionContexts(IEnumerable<Edge> edges)
        {
            List<GraphConnectionContext> contexts = new List<GraphConnectionContext>();
            foreach (Edge edge in edges)
            {
                if (edge.output == null || edge.input == null) continue;
                contexts.Add(new GraphConnectionContext(this, edge.output, edge.input, edge));
            }

            return contexts;
        }

        /// <summary>
        /// 在集合非空时发送连接类变更。
        /// </summary>
        /// <param name="type">连接建立或断开类型。</param>
        /// <param name="connections">已生效的连接集合。</param>
        private void NotifyConnections(GraphChangeType type, IReadOnlyList<GraphConnectionContext> connections)
        {
            if (connections.Count == 0) return;
            NotifyChange(new GraphChangeEvent(type, Array.Empty<WSGraphNode>(), connections, Vector2.zero));
        }

        /// <summary>
        /// 将变更直接交给业务监听器，不捕获业务异常以便尽早暴露契约错误。
        /// </summary>
        /// <param name="change">已经生效的图变更。</param>
        private void NotifyChange(GraphChangeEvent change)
        {
            if (changeNotificationSuppressionDepth > 0) return;
            changeListener?.OnGraphChanged(change);
        }

        /// <summary>
        /// 结束一次 GraphView 变更通知抑制作用域。
        /// </summary>
        private void EndChangeNotificationSuppression()
        {
            if (changeNotificationSuppressionDepth == 0)
                throw new InvalidOperationException("GraphView 变更通知抑制作用域未正确配对。");

            changeNotificationSuppressionDepth--;
        }

        /// <summary>
        /// 管理 GraphView 变更通知抑制生命周期的内部作用域。
        /// </summary>
        private sealed class ChangeNotificationScope : IDisposable
        {
            private WSGraphView graphView;

            /// <summary>
            /// 创建绑定 GraphView 的通知抑制作用域。
            /// </summary>
            /// <param name="graphView">需要恢复通知状态的 GraphView。</param>
            public ChangeNotificationScope(WSGraphView graphView)
            {
                this.graphView = graphView;
            }

            /// <summary>
            /// 恢复 GraphView 的变更通知。
            /// </summary>
            public void Dispose()
            {
                if (graphView == null) return;
                graphView.EndChangeNotificationSuppression();
                graphView = null;
            }
        }
        #endregion

        #region 右键菜单
        /// <summary>
        /// 保留 GraphView 默认菜单，并向图或节点的菜单提供者分发当前目标上下文。
        /// </summary>
        /// <param name="evt">Unity 右键菜单填充事件。</param>
        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            base.BuildContextualMenu(evt);

            GraphElement element = FindTargetGraphElement(evt.target as VisualElement);
            GraphContextTarget target = element is WSGraphNode
                ? GraphContextTarget.Node
                : element is Edge
                    ? GraphContextTarget.Edge
                    : GraphContextTarget.Canvas;

            // mousePosition 是 Panel 坐标，创建节点前必须转换到可缩放和平移的内容坐标空间。
            Vector2 graphPosition = contentViewContainer.WorldToLocal(evt.mousePosition);
            GraphContextMenuContext context = new GraphContextMenuContext(this, target, element, graphPosition);

            if (element is IGraphContextMenuProvider elementProvider)
                elementProvider.PopulateContextMenu(context, evt.menu);
            if (this is IGraphContextMenuProvider graphProvider)
                graphProvider.PopulateContextMenu(context, evt.menu);
        }

        /// <summary>
        /// 从事件目标向上查找当前 GraphView 内最近的节点或连线。
        /// </summary>
        /// <param name="target">右键事件的视觉元素目标。</param>
        /// <returns>命中的节点或连线；右键空白画布时返回 null。</returns>
        private GraphElement FindTargetGraphElement(VisualElement target)
        {
            for (VisualElement current = target;
                 current != null && !ReferenceEquals(current, this);
                 current = current.parent)
            {
                if (current is WSGraphNode || current is Edge) return current as GraphElement;
            }

            return null;
        }
        #endregion
    }
}
