using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.UIModule.Editor;

namespace WS_Modules.UIToolkitExtensions.Editor.GraphView.Samples
{
    /// <summary>
    /// 演示通用 GraphView 节点内容、端口限制、结果通知和右键菜单的最小窗口。
    /// </summary>
    public sealed class GraphViewFrameworkSampleWindow : EditorWindow
    {
        #region 常量与字段

        private const string WindowTitle = "WS GraphView 示例";
        private SampleGraphView graphView;

        #endregion

        #region 窗口入口与生命周期

        /// <summary>
        /// 打开 GraphView 框架示例窗口。
        /// </summary>
        [MenuItem("WSFrame/GraphView 框架示例")]
        private static void ShowWindow()
        {
            GraphViewFrameworkSampleWindow window = GetWindow<GraphViewFrameworkSampleWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(720f, 420f);
            window.Show();
        }

        /// <summary>
        /// 创建示例 GraphView 和底部状态栏。
        /// </summary>
        private void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.flexDirection = FlexDirection.Column;

            Label statusLabel = new Label("在空白画布右键创建节点");
            statusLabel.style.height = 24f;
            statusLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            statusLabel.style.paddingLeft = 8f;

            graphView = new SampleGraphView(message => statusLabel.text = message);
            rootVisualElement.Add(graphView);
            rootVisualElement.Add(statusLabel);
        }

        #endregion

        #region 示例图

        /// <summary>
        /// 演示连接策略、结果监听和画布菜单的 GraphView。
        /// </summary>
        private sealed class SampleGraphView : WSGraphView, IGraphConnectionPolicy,
            IGraphChangeListener, IGraphContextMenuProvider, IGraphNodeInteractionListener
        {
            private readonly Action<string> setStatus;
            private int nextNodeIndex = 1;
            private string lastClickSummary = "尚未点击节点";
            private string selectionSummary = "未选择节点";

            /// <summary>
            /// 创建示例图。
            /// </summary>
            /// <param name="setStatus">用于更新窗口状态栏的回调。</param>
            public SampleGraphView(Action<string> setStatus)
            {
                this.setStatus = setStatus ?? throw new ArgumentNullException(nameof(setStatus));
            }

            /// <summary>
            /// 禁止同一对节点之间建立重复连接，演示业务附加规则。
            /// </summary>
            /// <param name="context">候选连接上下文。</param>
            /// <returns>连接校验结果。</returns>
            public GraphConnectionValidationResult ValidateConnection(GraphConnectionContext context)
            {
                bool duplicateNodePair = edges.Any(edge =>
                    ReferenceEquals(edge.output?.node, context.OutputNode) &&
                    ReferenceEquals(edge.input?.node, context.InputNode));
                return duplicateNodePair
                    ? GraphConnectionValidationResult.Reject("同一对示例节点之间只允许一条连接。")
                    : GraphConnectionValidationResult.Allowed;
            }

            /// <summary>
            /// 把通用图变更结果显示到示例窗口底部状态栏。
            /// </summary>
            /// <param name="change">已经生效的图变更。</param>
            public void OnGraphChanged(GraphChangeEvent change)
            {
                string detail = change.Connections.Count > 0
                    ? $"{change.Connections[0].OutputDescriptor.DisplayName} → {change.Connections[0].InputDescriptor.DisplayName}"
                    : $"节点数：{change.Nodes.Count}";
                setStatus($"{change.Type} / {detail}");
            }

            /// <summary>
            /// 记录最近一次节点点击，并与当前选择摘要一起显示。
            /// </summary>
            /// <param name="context">节点点击上下文。</param>
            public void OnNodeClicked(GraphNodeClickContext context)
            {
                lastClickSummary = $"点击：{context.Node.title} ×{context.ClickCount} " +
                                   $"({context.GraphPosition.x:F0}, {context.GraphPosition.y:F0})";
                RefreshInteractionStatus();
            }

            /// <summary>
            /// 记录当前节点选择集合，并与最近点击摘要一起显示。
            /// </summary>
            /// <param name="change">节点选择变化快照。</param>
            public void OnNodeSelectionChanged(GraphNodeSelectionChange change)
            {
                selectionSummary = change.SelectedNodes.Count == 0
                    ? "未选择节点"
                    : $"已选 {change.SelectedNodes.Count} 个：" +
                      string.Join("、", change.SelectedNodes.Select(node => node.title));
                RefreshInteractionStatus();
            }

            /// <summary>
            /// 为画布添加节点创建菜单，为连线添加显式断开菜单。
            /// </summary>
            /// <param name="context">右键菜单上下文。</param>
            /// <param name="menu">待追加菜单项的菜单。</param>
            public void PopulateContextMenu(GraphContextMenuContext context, DropdownMenu menu)
            {
                if (context.Target == GraphContextTarget.Canvas)
                {
                    menu.AppendAction("创建/输出节点", _ => CreateNode(SampleNodeKind.Output, context.GraphPosition));
                    menu.AppendAction("创建/输入节点", _ => CreateNode(SampleNodeKind.Input, context.GraphPosition));
                }
                else if (context.Target == GraphContextTarget.Edge)
                {
                    menu.AppendSeparator();
                    menu.AppendAction("断开连接", _ => RemoveGraphElements(new[] { context.Element }));
                }
            }

            /// <summary>
            /// 在指定内容坐标创建示例节点。
            /// </summary>
            /// <param name="kind">示例节点种类。</param>
            /// <param name="position">节点在 GraphView 内容坐标中的位置。</param>
            private void CreateNode(SampleNodeKind kind, Vector2 position)
            {
                SampleGraphNode node = new SampleGraphNode(kind, nextNodeIndex++);
                AddGraphNode(node, position);
            }

            /// <summary>
            /// 把最近点击与最终选择状态合并写入窗口状态栏。
            /// </summary>
            private void RefreshInteractionStatus() =>
                setStatus($"{lastClickSummary} / {selectionSummary}");
        }

        #endregion

        #region 示例节点

        /// <summary>
        /// 定义示例节点的数据方向职责。
        /// </summary>
        private enum SampleNodeKind
        {
            /// <summary>只提供输出端口。</summary>
            Output,
            /// <summary>只提供输入端口。</summary>
            Input
        }

        /// <summary>
        /// 演示自定义中间内容、端口声明和节点菜单的节点。
        /// </summary>
        private sealed class SampleGraphNode : WSGraphNode, IGraphNodeContentProvider,
            IGraphPortProvider, IGraphContextMenuProvider, IGraphNodeStyleProvider
        {
            private const string SampleStyleSheetPath =
                UxmlUssPathConstants.Uss
                    .AssetsScriptsWSFrameUIToolkitExtensionsEditorGraphViewSamplesGraphViewFrameworkSampleNode;
            private const string OutputNodeClassName = "sample-graph-node--output";
            private const string InputNodeClassName = "sample-graph-node--input";

            private readonly SampleNodeKind kind;
            private readonly int index;

            /// <summary>
            /// 创建指定种类的示例节点。
            /// </summary>
            /// <param name="kind">节点的数据方向职责。</param>
            /// <param name="index">用于区分节点的递增编号。</param>
            public SampleGraphNode(SampleNodeKind kind, int index)
            {
                this.kind = kind;
                this.index = index;
                title = kind == SampleNodeKind.Output ? $"输出节点 {index}" : $"输入节点 {index}";
            }

            /// <summary>
            /// 创建节点中间的说明与可编辑文本。
            /// </summary>
            /// <param name="contentContainer">节点中间内容容器。</param>
            public void PopulateContent(VisualElement contentContainer)
            {
                contentContainer.Add(new Label(kind == SampleNodeKind.Output
                    ? "提供字符串和数值"
                    : "接收字符串和数值"));
                contentContainer.Add(new TextField("备注") { value = $"示例 {index}" });
            }

            /// <summary>
            /// 返回用于演示类型与容量限制的端口描述。
            /// </summary>
            /// <returns>当前节点的两个端口描述。</returns>
            public IEnumerable<GraphPortDescriptor> GetPortDescriptors()
            {
                Direction direction = kind == SampleNodeKind.Output ? Direction.Output : Direction.Input;
                Port.Capacity stringCapacity = kind == SampleNodeKind.Output
                    ? Port.Capacity.Multi
                    : Port.Capacity.Single;

                yield return new GraphPortDescriptor("string", "字符串", direction,
                    stringCapacity, typeof(string));
                yield return new GraphPortDescriptor("float", "数值", direction,
                    Port.Capacity.Single, typeof(float));
            }

            /// <summary>
            /// 返回示例节点共用的自定义 USS。
            /// </summary>
            /// <returns>示例节点样式表路径。</returns>
            public IEnumerable<string> GetStyleSheetPaths()
            {
                yield return SampleStyleSheetPath;
            }

            /// <summary>
            /// 根据节点方向返回实例级根样式类。
            /// </summary>
            /// <returns>输出或输入节点的根样式类。</returns>
            public IEnumerable<string> GetStyleClassNames()
            {
                yield return kind == SampleNodeKind.Output
                    ? OutputNodeClassName
                    : InputNodeClassName;
            }

            /// <summary>
            /// 为节点追加删除菜单项。
            /// </summary>
            /// <param name="context">节点菜单上下文。</param>
            /// <param name="menu">待追加菜单项的菜单。</param>
            public void PopulateContextMenu(GraphContextMenuContext context, DropdownMenu menu)
            {
                menu.AppendSeparator();
                menu.AppendAction("删除示例节点",
                    _ => context.GraphView.RemoveGraphElements(new[] { this }));
            }
        }

        #endregion
    }
}
