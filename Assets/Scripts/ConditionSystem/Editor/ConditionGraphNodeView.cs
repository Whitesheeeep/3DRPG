#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;
using WS_Modules.UIToolkitExtensions.Editor.GraphView;

namespace RPG.ConditionSystemNS.Editor
{
    /// <summary>将条件子资产映射为节点视图，复用框架默认端口连接器。</summary>
    internal sealed class ConditionGraphNodeView : WSGraphNode, IGraphPortProvider, IGraphNodeContentProvider
    {
        #region 依赖字段
        /// <summary>获取只读领域对象引用，具体修改由 Controller 协调。</summary>
        internal ConditionNode Model { get; }
        #endregion

        #region 构建与刷新
        // 构建：保存领域引用并设置类型样式，不创建第二份配置。
        /// <summary>绑定领域节点但不创建第二份配置数据。</summary>
        /// <param name="model">条件节点子资产。</param>
        internal ConditionGraphNodeView(ConditionNode model)
        {
            Model = model;
            AddToClassList("condition-node");
            AddToClassList(model is AndConditionNode ? "condition-node--and" : model is OrConditionNode
                ? "condition-node--or" : model is NotConditionNode ? "condition-node--not" : "condition-node--leaf");
            RefreshTitle();
        }

        // 内容刷新：只生成表达式，不执行条件判断。
        /// <summary>创建类型和组合表达式，详细字段由右侧原生绑定负责。</summary>
        /// <param name="contentContainer">框架内容容器。</param>
        public void PopulateContent(VisualElement contentContainer)
        {
            var kind = new Label(Model is AndConditionNode ? "AND · 全部满足" : Model is OrConditionNode
                ? "OR · 任意满足" : Model is NotConditionNode ? "NOT · 条件取反" : "CONDITION · 具体条件");
            kind.AddToClassList("condition-node-kind");
            contentContainer.Add(kind);
            var expression = new Label(BuildExpression(Model, new HashSet<ConditionNode>(), 0));
            expression.AddToClassList("condition-expression");
            contentContainer.Add(expression);
        }

        /// <summary>刷新摘要与根标记，保留端口、位置和连线实例。</summary>
        internal void RefreshContent()
        {
            RefreshTitle();
            extensionContainer.Clear();
            PopulateContent(extensionContainer);
            RefreshExpandedState();
        }

        /// <summary>根据模型当前入口状态更新标题和样式。</summary>
        private void RefreshTitle()
        {
            bool isRoot = Model.Owner != null && Model.Owner.Root == Model;
            title = (isRoot ? "[根] " : string.Empty) + Model.name;
            EnableInClassList("condition-node--root", isRoot);
        }

        // 端口构建：连接容量与运行时子条件契约一致。
        /// <summary>声明单父输入端口及组合节点的子条件端口。</summary>
        /// <returns>稳定的端口描述。</returns>
        public IEnumerable<GraphPortDescriptor> GetPortDescriptors()
        {
            yield return new GraphPortDescriptor("parent", "父条件", Direction.Input,
                Port.Capacity.Single, typeof(ConditionNode));
            if (Model.MaximumChildren > 0)
                yield return new GraphPortDescriptor("children", "子条件", Direction.Output,
                    Model.MaximumChildren == 1 ? Port.Capacity.Single : Port.Capacity.Multi, typeof(ConditionNode));
        }
        #endregion

        #region 组合表达式
        /// <summary>从配置生成可读表达式；限制显示深度并识别循环，不执行业务判断。</summary>
        /// <param name="node">当前条件。</param>
        /// <param name="active">当前展开路径。</param>
        /// <param name="depth">显示深度。</param>
        /// <returns>用于节点内容区的组合表达式。</returns>
        private static string BuildExpression(ConditionNode node, HashSet<ConditionNode> active, int depth)
        {
            if (node == null) return "<未连接>";
            if (depth >= 8) return "…";
            if (!active.Add(node)) return "<循环引用>";
            string expression;
            if (node is ConstantConditionNode constant) expression = constant.Value ? "TRUE" : "FALSE";
            else if (node is AndConditionNode || node is OrConditionNode)
            {
                string operation = node is AndConditionNode ? " AND " : " OR ";
                expression = node.Children.Count == 0 ? "<等待连接子条件>" : "(" + string.Join(operation,
                    node.Children.Take(4).Select(child => BuildExpression(child, active, depth + 1))) +
                    (node.Children.Count > 4 ? " …" : string.Empty) + ")";
            }
            else if (node is NotConditionNode)
                expression = "NOT " + (node.Children.Count == 0 ? "<未连接>" : BuildExpression(node.Children[0], active, depth + 1));
            else expression = node.name;
            active.Remove(node);
            return expression;
        }

        #endregion
    }
}
#endif
