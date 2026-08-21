using System;
using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace WS_Modules.UIToolkitExtensions.Editor.GraphView
{
    /// <summary>
    /// 定义 GraphView 右键菜单所作用的元素种类。
    /// </summary>
    public enum GraphContextTarget
    {
        /// <summary>空白画布。</summary>
        Canvas,
        /// <summary>图节点。</summary>
        Node,
        /// <summary>节点之间的连线。</summary>
        Edge
    }

    /// <summary>
    /// 定义通用图对外发送的变更种类。
    /// </summary>
    public enum GraphChangeType
    {
        /// <summary>节点已经加入图。</summary>
        NodesAdded,
        /// <summary>节点位置已经改变。</summary>
        NodesMoved,
        /// <summary>连接已经建立。</summary>
        ConnectionsCreated,
        /// <summary>连接已经断开。</summary>
        ConnectionsRemoved,
        /// <summary>节点已经从图中删除。</summary>
        NodesRemoved
    }

    /// <summary>
    /// 定义节点布局操作的方向与排列方式。
    /// </summary>
    public enum GraphLayoutOperation
    {
        /// <summary>按节点左边缘对齐。</summary>
        HorizontalAlignLeft,

        /// <summary>按节点水平中心对齐。</summary>
        HorizontalAlignCenter,

        /// <summary>按节点右边缘对齐。</summary>
        HorizontalAlignRight,

        /// <summary>按节点左上角的水平坐标均匀分布。</summary>
        HorizontalDistributeEvenly,

        /// <summary>按节点上边缘对齐。</summary>
        VerticalAlignTop,

        /// <summary>按节点垂直中心对齐。</summary>
        VerticalAlignCenter,

        /// <summary>按节点下边缘对齐。</summary>
        VerticalAlignBottom,

        /// <summary>按节点左上角的垂直坐标均匀分布。</summary>
        VerticalDistributeEvenly
    }

    /// <summary>
    /// 提供节点中间内容区域的构建能力。
    /// </summary>
    public interface IGraphNodeContentProvider
    {
        /// <summary>
        /// 向节点中间内容容器填充 UI；调用时派生节点已经完成构造。
        /// </summary>
        /// <param name="contentContainer">用于承载节点自定义内容的容器。</param>
        void PopulateContent(VisualElement contentContainer);
    }

    /// <summary>
    /// 提供节点端口声明能力。
    /// </summary>
    public interface IGraphPortProvider
    {
        /// <summary>
        /// 获取当前节点需要创建的端口描述。
        /// </summary>
        /// <returns>端口描述序列；返回空序列表示节点没有端口。</returns>
        IEnumerable<GraphPortDescriptor> GetPortDescriptors();
    }

    /// <summary>
    /// 提供派生节点的额外 USS 样式表和根节点样式类。
    /// </summary>
    public interface IGraphNodeStyleProvider
    {
        /// <summary>
        /// 获取在框架基础 USS 之后加载的项目内样式表路径。
        /// </summary>
        /// <returns>以 Assets 开头的 USS 资源路径；返回空序列表示不加载额外样式。</returns>
        IEnumerable<string> GetStyleSheetPaths();

        /// <summary>
        /// 获取添加到节点根元素的业务样式类。
        /// </summary>
        /// <returns>节点根样式类序列；返回空序列表示不添加业务样式类。</returns>
        IEnumerable<string> GetStyleClassNames();
    }

    /// <summary>
    /// 提供业务层附加连接限制。
    /// </summary>
    public interface IGraphConnectionPolicy
    {
        /// <summary>
        /// 在基础端口规则通过后校验候选连接。
        /// </summary>
        /// <param name="context">采用 Output 到 Input 顺序规范化的连接上下文。</param>
        /// <returns>连接是否允许以及可选的拒绝原因。</returns>
        GraphConnectionValidationResult ValidateConnection(GraphConnectionContext context);
    }

    /// <summary>
    /// 接收 GraphView 已经实际完成的交互变更。
    /// </summary>
    public interface IGraphChangeListener
    {
        /// <summary>
        /// 在图元素完成加入、移动、连接、断开或删除后接收变更结果。
        /// </summary>
        /// <param name="change">本次已经生效的图变更。</param>
        void OnGraphChanged(GraphChangeEvent change);
    }

    /// <summary>
    /// 接收用户节点点击以及最终节点选择状态变化。
    /// </summary>
    public interface IGraphNodeInteractionListener
    {
        /// <summary>
        /// 在用户左键点击节点后接收具体点击信息。
        /// </summary>
        /// <param name="context">节点点击上下文。</param>
        void OnNodeClicked(GraphNodeClickContext context);

        /// <summary>
        /// 在 GraphView 的节点选择集合实际发生变化后接收最终状态。
        /// </summary>
        /// <param name="change">节点选择变化快照。</param>
        void OnNodeSelectionChanged(GraphNodeSelectionChange change);
    }

    /// <summary>
    /// 提供画布、节点和连线的右键菜单扩展能力。
    /// </summary>
    public interface IGraphContextMenuProvider
    {
        /// <summary>
        /// 向 Unity 默认菜单之后追加业务菜单项。
        /// </summary>
        /// <param name="context">当前右键目标与坐标上下文。</param>
        /// <param name="menu">待追加菜单项的 DropdownMenu。</param>
        void PopulateContextMenu(GraphContextMenuContext context, DropdownMenu menu);
    }

    /// <summary>
    /// 表示业务连接规则的校验结果。
    /// </summary>
    public readonly struct GraphConnectionValidationResult
    {
        #region 属性

        /// <summary>
        /// 获取连接是否允许。
        /// </summary>
        public bool IsAllowed { get; }

        /// <summary>
        /// 获取连接被拒绝时的说明；允许连接时为空字符串。
        /// </summary>
        public string Reason { get; }

        /// <summary>
        /// 获取表示允许连接的共享结果。
        /// </summary>
        public static GraphConnectionValidationResult Allowed { get; } = new GraphConnectionValidationResult(true, string.Empty);

        #endregion

        #region 生命周期

        /// <summary>
        /// 创建连接校验结果。
        /// </summary>
        /// <param name="isAllowed">连接是否允许。</param>
        /// <param name="reason">拒绝连接时的说明。</param>
        public GraphConnectionValidationResult(bool isAllowed, string reason)
        {
            IsAllowed = isAllowed;
            Reason = reason ?? string.Empty;
        }

        #endregion

        #region 创建结果

        /// <summary>
        /// 创建拒绝连接的结果。
        /// </summary>
        /// <param name="reason">拒绝连接的说明。</param>
        /// <returns>不允许连接的校验结果。</returns>
        public static GraphConnectionValidationResult Reject(string reason) =>
            new GraphConnectionValidationResult(false, reason);

        #endregion
    }

    /// <summary>
    /// 表示采用 Output 到 Input 顺序规范化的连接信息。
    /// </summary>
    public readonly struct GraphConnectionContext
    {
        #region 属性

        /// <summary>获取连接所属的图。</summary>
        public WSGraphView GraphView { get; }
        /// <summary>获取连接的输出节点。</summary>
        public WSGraphNode OutputNode { get; }
        /// <summary>获取连接的输出端口。</summary>
        public Port OutputPort { get; }
        /// <summary>获取输出端口描述。</summary>
        public GraphPortDescriptor OutputDescriptor { get; }
        /// <summary>获取连接的输入节点。</summary>
        public WSGraphNode InputNode { get; }
        /// <summary>获取连接的输入端口。</summary>
        public Port InputPort { get; }
        /// <summary>获取输入端口描述。</summary>
        public GraphPortDescriptor InputDescriptor { get; }
        /// <summary>获取对应 Edge；候选连接校验阶段可能为空。</summary>
        public Edge Edge { get; }

        #endregion

        #region 生命周期

        /// <summary>
        /// 创建规范化连接上下文。
        /// </summary>
        /// <param name="graphView">连接所属的图。</param>
        /// <param name="outputPort">输出端口。</param>
        /// <param name="inputPort">输入端口。</param>
        /// <param name="edge">已经存在或即将加入的连线。</param>
        /// <exception cref="ArgumentNullException">图或端口为空时抛出。</exception>
        /// <exception cref="ArgumentException">端口不属于 WSGraphNode 或缺少描述时抛出。</exception>
        public GraphConnectionContext(WSGraphView graphView, Port outputPort, Port inputPort, Edge edge = null)
        {
            GraphView = graphView ?? throw new ArgumentNullException(nameof(graphView));
            OutputPort = outputPort ?? throw new ArgumentNullException(nameof(outputPort));
            InputPort = inputPort ?? throw new ArgumentNullException(nameof(inputPort));
            OutputNode = outputPort.node as WSGraphNode;
            InputNode = inputPort.node as WSGraphNode;
            OutputDescriptor = outputPort.userData as GraphPortDescriptor;
            InputDescriptor = inputPort.userData as GraphPortDescriptor;
            Edge = edge;

            if (OutputNode == null || InputNode == null)
                throw new ArgumentException("连接两端必须属于 WSGraphNode。", nameof(outputPort));
            if (OutputDescriptor == null || InputDescriptor == null)
                throw new ArgumentException("连接两端必须包含 GraphPortDescriptor。", nameof(outputPort));
            if (outputPort.direction != Direction.Output || inputPort.direction != Direction.Input)
                throw new ArgumentException("连接上下文必须按 Output 到 Input 的顺序创建。", nameof(outputPort));
        }

        #endregion
    }

    /// <summary>
    /// 表示一次右键菜单请求的目标与坐标。
    /// </summary>
    public readonly struct GraphContextMenuContext
    {
        #region 属性

        /// <summary>获取菜单所属的图。</summary>
        public WSGraphView GraphView { get; }
        /// <summary>获取右键目标种类。</summary>
        public GraphContextTarget Target { get; }
        /// <summary>获取右键命中的节点或连线；画布上下文为空。</summary>
        public GraphElement Element { get; }
        /// <summary>获取鼠标在 GraphView 内容坐标空间中的位置。</summary>
        public Vector2 GraphPosition { get; }

        #endregion

        #region 生命周期

        /// <summary>
        /// 创建右键菜单上下文。
        /// </summary>
        /// <param name="graphView">菜单所属的图。</param>
        /// <param name="target">右键目标种类。</param>
        /// <param name="element">右键命中的图元素。</param>
        /// <param name="graphPosition">鼠标在 GraphView 内容坐标空间中的位置。</param>
        public GraphContextMenuContext(WSGraphView graphView, GraphContextTarget target,
            GraphElement element, Vector2 graphPosition)
        {
            GraphView = graphView;
            Target = target;
            Element = element;
            GraphPosition = graphPosition;
        }

        #endregion
    }

    /// <summary>
    /// 表示一次用户左键点击节点的交互信息。
    /// </summary>
    public readonly struct GraphNodeClickContext
    {
        #region 属性

        /// <summary>获取点击所属的图。</summary>
        public WSGraphView GraphView { get; }
        /// <summary>获取被点击的节点。</summary>
        public WSGraphNode Node { get; }
        /// <summary>获取鼠标在 GraphView 内容坐标空间中的位置。</summary>
        public Vector2 GraphPosition { get; }
        /// <summary>获取系统累计的连续点击次数。</summary>
        public int ClickCount { get; }
        /// <summary>获取点击时按下的键盘修饰键。</summary>
        public EventModifiers Modifiers { get; }

        #endregion

        #region 生命周期

        /// <summary>
        /// 创建节点点击上下文。
        /// </summary>
        /// <param name="graphView">点击所属的图。</param>
        /// <param name="node">被点击的节点。</param>
        /// <param name="graphPosition">鼠标在 GraphView 内容坐标空间中的位置。</param>
        /// <param name="clickCount">系统累计的连续点击次数。</param>
        /// <param name="modifiers">点击时按下的键盘修饰键。</param>
        public GraphNodeClickContext(WSGraphView graphView, WSGraphNode node,
            Vector2 graphPosition, int clickCount, EventModifiers modifiers)
        {
            GraphView = graphView;
            Node = node;
            GraphPosition = graphPosition;
            ClickCount = clickCount;
            Modifiers = modifiers;
        }

        #endregion
    }

    /// <summary>
    /// 表示 GraphView 节点选择集合的一次实际变化。
    /// </summary>
    public struct GraphNodeSelectionChange
    {
        #region 属性

        /// <summary>获取选择所属的图。</summary>
        public WSGraphView GraphView { get; }
        /// <summary>获取变化后的完整节点选择快照。</summary>
        public IReadOnlyList<WSGraphNode> SelectedNodes { get; }
        /// <summary>获取本次新加入选择的节点。</summary>
        public IReadOnlyList<WSGraphNode> AddedNodes { get; }
        /// <summary>获取本次退出选择的节点。</summary>
        public IReadOnlyList<WSGraphNode> RemovedNodes { get; }
        /// <summary>获取直接触发本次同步的节点；框选或点击空白时为空。</summary>
        public WSGraphNode TriggerNode { get; }

        #endregion

        #region 生命周期

        /// <summary>
        /// 创建节点选择变化快照。
        /// </summary>
        /// <param name="graphView">选择所属的图。</param>
        /// <param name="selectedNodes">变化后的完整节点选择。</param>
        /// <param name="addedNodes">本次新加入选择的节点。</param>
        /// <param name="removedNodes">本次退出选择的节点。</param>
        /// <param name="triggerNode">直接触发同步的节点。</param>
        public GraphNodeSelectionChange(WSGraphView graphView,
            IReadOnlyList<WSGraphNode> selectedNodes, IReadOnlyList<WSGraphNode> addedNodes,
            IReadOnlyList<WSGraphNode> removedNodes, WSGraphNode triggerNode)
        {
            GraphView = graphView;
            SelectedNodes = selectedNodes ?? Array.Empty<WSGraphNode>();
            AddedNodes = addedNodes ?? Array.Empty<WSGraphNode>();
            RemovedNodes = removedNodes ?? Array.Empty<WSGraphNode>();
            TriggerNode = triggerNode;
        }

        #endregion
    }

    /// <summary>
    /// 表示一批已经在 GraphView 中生效的同类变更。
    /// </summary>
    public struct GraphChangeEvent
    {
        #region 属性

        /// <summary>获取变更种类。</summary>
        public GraphChangeType Type { get; }
        /// <summary>获取涉及的节点。</summary>
        public IReadOnlyList<WSGraphNode> Nodes { get; }
        /// <summary>获取涉及的规范化连接。</summary>
        public IReadOnlyList<GraphConnectionContext> Connections { get; }
        /// <summary>获取节点移动增量；非移动事件为 Vector2.zero。</summary>
        public Vector2 MoveDelta { get; }

        #endregion

        #region 生命周期

        /// <summary>
        /// 创建一批图变更结果。
        /// </summary>
        /// <param name="type">变更种类。</param>
        /// <param name="nodes">涉及的节点。</param>
        /// <param name="connections">涉及的连接。</param>
        /// <param name="moveDelta">节点移动增量。</param>
        public GraphChangeEvent(GraphChangeType type, IReadOnlyList<WSGraphNode> nodes,
            IReadOnlyList<GraphConnectionContext> connections, Vector2 moveDelta)
        {
            Type = type;
            Nodes = nodes ?? Array.Empty<WSGraphNode>();
            Connections = connections ?? Array.Empty<GraphConnectionContext>();
            MoveDelta = moveDelta;
        }

        #endregion
    }

    /// <summary>
    /// 表示一次已经应用到 GraphView 节点视觉位置的布局结果。
    /// </summary>
    public readonly struct GraphLayoutChange
    {
        #region 属性

        /// <summary>获取本次布局操作。</summary>
        public GraphLayoutOperation Operation { get; }

        /// <summary>获取位置发生变化的节点。</summary>
        public IReadOnlyList<WSGraphNode> Nodes { get; }

        #endregion

        #region 生命周期

        /// <summary>
        /// 创建布局结果；节点集合保存为只读快照，供业务层写回对应 Model。
        /// </summary>
        /// <param name="operation">执行的布局操作。</param>
        /// <param name="nodes">位置发生变化的节点。</param>
        public GraphLayoutChange(GraphLayoutOperation operation, IReadOnlyList<WSGraphNode> nodes)
        {
            Operation = operation;
            Nodes = nodes ?? Array.Empty<WSGraphNode>();
        }

        #endregion
    }
}
