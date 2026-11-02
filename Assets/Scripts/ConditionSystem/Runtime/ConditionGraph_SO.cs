using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RPG.ConditionSystemNS
{
    /// <summary>区分阻止求值的错误和不参与求值的草稿警告。</summary>
    public enum E_ConditionIssueSeverity
    {
        /// <summary>草稿或未连接节点提示。</summary>
        Warning,
        /// <summary>结构或参数错误。</summary>
        Error
    }

    /// <summary>携带节点定位信息，供运行时和编辑器共用同一套校验结果。</summary>
    public sealed class ConditionValidationIssue
    {
        /// <summary>获取严重程度。</summary>
        public E_ConditionIssueSeverity Severity { get; }
        /// <summary>获取关联节点；图级问题可以为 null。</summary>
        public ConditionNode Node { get; }
        /// <summary>获取具体说明。</summary>
        public string Message { get; }

        /// <summary>创建可定位的配置问题。</summary>
        /// <param name="severity">严重程度。</param>
        /// <param name="node">关联节点。</param>
        /// <param name="message">问题说明。</param>
        public ConditionValidationIssue(E_ConditionIssueSeverity severity, ConditionNode node, string message)
        { Severity = severity; Node = node; Message = message; }
    }

    /// <summary>保存条件树及节点子资产；每次求值先校验配置，不维护业务状态或交互行为。</summary>
    [CreateAssetMenu(fileName = "ConditionGraph", menuName = "RPG/Condition/Condition Graph")]
    public sealed class ConditionGraph_SO : ScriptableObject
    {
        #region 序列化字段与属性
        // 序列化字段：节点是当前 SO 的子资产，连接直接保存在父节点的子引用列表中。
        [SerializeField, HideInInspector] private ConditionNode root;
        [SerializeField, HideInInspector] private List<ConditionNode> nodes = new List<ConditionNode>();
        // 属性：仅暴露只读配置引用，结构修改统一走图操作。
        /// <summary>获取唯一求值入口。</summary>
        public ConditionNode Root => root;
        /// <summary>获取只读节点集合，包含未连接的编辑草稿。</summary>
        public IReadOnlyList<ConditionNode> Nodes => nodes.AsReadOnly();
        #endregion

        #region 公开求值与校验
        // 求值：图结构校验必须先于组合逻辑执行。
        /// <summary>先校验整图，再从根条件同步求值；配置异常不会转换成未满足。</summary>
        /// <param name="context">业务创建的非空上下文。</param>
        /// <returns>根条件的本次结果。</returns>
        /// <exception cref="ArgumentNullException">上下文为空时抛出。</exception>
        /// <exception cref="InvalidOperationException">图配置无效时抛出。</exception>
        public ConditionResult Evaluate(ConditionContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            // 必须在 Or 短路和 Not 取反之前发现结构错误。
            Validate();
            ConditionResult result = root.Evaluate(context);
            if (!result.IsMet && result.Reason == null)
                throw new InvalidOperationException($"[ConditionGraph] 图 {name} 的节点返回了没有原因的未满足结果。");
            return result;
        }

        // 校验：运行时和编辑器共用同一套问题数据。
        /// <summary>报告并抛出图中的配置错误；警告不阻止求值。</summary>
        /// <exception cref="InvalidOperationException">存在结构或参数错误时抛出。</exception>
        public void Validate()
        {
            string[] errors = GetValidationIssues().Where(issue => issue.Severity == E_ConditionIssueSeverity.Error)
                .Select(issue => issue.Message).ToArray();
            if (errors.Length == 0) return;
            string message = $"[ConditionGraph] 图 {name} 校验失败：\n{string.Join("\n", errors)}";
            Debug.LogError(message, this);
            throw new InvalidOperationException(message);
        }

        /// <summary>收集可定位的校验结果，不修改配置且不输出重复日志。</summary>
        /// <returns>错误和草稿警告。</returns>
        public IReadOnlyList<ConditionValidationIssue> GetValidationIssues()
        {
            var issues = new List<ConditionValidationIssue>();
            var nodeSet = new HashSet<ConditionNode>(nodes.Where(node => node != null));
            var ids = new HashSet<string>();
            var hasParent = new HashSet<ConditionNode>();
            if (root == null || !nodeSet.Contains(root)) AddError(issues, root, "必须指定属于当前图的根条件。");
            if (nodeSet.Count != nodes.Count) AddError(issues, null, "节点集合包含空引用或重复节点。");
            foreach (ConditionNode node in nodeSet)
            {
                if (node.Owner != this) AddError(issues, node, $"节点 {node.name} 不属于当前图。");
                if (string.IsNullOrEmpty(node.NodeId) || !ids.Add(node.NodeId))
                    AddError(issues, node, $"节点 {node.name} 的 ID 为空或重复。");
                // 参数校验位于序列化边界，只将明确的配置异常转换成可定位问题。
                try { node.Validate(); }
                catch (ArgumentException exception) { AddError(issues, node, exception.Message); }
                catch (InvalidOperationException exception) { AddError(issues, node, exception.Message); }
                foreach (ConditionNode child in node.Children)
                {
                    if (child == null) continue;
                    if (!nodeSet.Contains(child) || child.Owner != this)
                        AddError(issues, node, $"节点 {node.name} 引用了图外节点 {child.name}。");
                    if (!hasParent.Add(child)) AddError(issues, child, $"节点 {child.name} 存在重复连接或多个父节点。");
                    if (child == root) AddError(issues, child, "根条件不能拥有父节点。");
                }
            }
            var visited = new HashSet<ConditionNode>();
            var active = new HashSet<ConditionNode>();
            foreach (ConditionNode node in nodeSet) VisitForCycles(node, nodeSet, visited, active, issues);
            var reachable = new HashSet<ConditionNode>();
            CollectReachable(root, nodeSet, reachable);
            foreach (ConditionNode node in nodeSet)
                if (!reachable.Contains(node)) issues.Add(new ConditionValidationIssue(
                    E_ConditionIssueSeverity.Warning, node, $"节点 {node.name} 未连接到根条件，不参与求值。"));
            return issues.AsReadOnly();
        }
        #endregion

        #region 图编辑操作
        /// <summary>登记节点所有权；持久化子资产由 Editor 调用方负责。</summary>
        /// <param name="node">新节点。</param>
        /// <exception cref="ArgumentException">节点已属于其他图时抛出。</exception>
        public void AddNode(ConditionNode node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (node.Owner != null && node.Owner != this) throw new ArgumentException("节点已经属于其他条件图。", nameof(node));
            if (nodes.Contains(node)) return;
            node.SetOwner(this);
            nodes.Add(node);
            Debug.Log($"[ConditionGraph] 登记节点，graph={name}，node={node.name}，count={nodes.Count}");
        }

        /// <summary>指定无父节点的入口；允许清空入口以保存编辑草稿。</summary>
        /// <param name="node">当前图内的无父节点，或 null。</param>
        /// <exception cref="ArgumentException">节点不属于当前图或已被父节点引用时抛出。</exception>
        public void SetRoot(ConditionNode node)
        {
            if (node != null && (!nodes.Contains(node) || nodes.Any(parent => parent != null && parent.Children.Contains(node))))
                throw new ArgumentException("根条件必须属于当前图且不能有父节点。", nameof(node));
            if (root == node) return;
            root = node;
            Debug.Log($"[ConditionGraph] 设置根条件，graph={name}，root={(node == null ? "<empty>" : node.name)}");
        }

        /// <summary>获取候选连接的问题，供 UI 拖线和真实写入共用。</summary>
        /// <param name="parent">父节点。</param>
        /// <param name="child">子节点。</param>
        /// <returns>允许连接时为 null，否则为具体错误。</returns>
        public ConditionValidationIssue GetConnectionIssue(ConditionNode parent, ConditionNode child)
        {
            string message = null;
            if (parent == null || child == null || !nodes.Contains(parent) || !nodes.Contains(child) ||
                parent.Owner != this || child.Owner != this) message = "连接两端必须属于当前图。";
            else if (parent == child) message = "不能连接节点自身。";
            else if (child == root) message = "根条件不能拥有父节点。";
            else if (parent.Children.Contains(child)) message = "不能重复连接。";
            else if (parent.Children.Count >= parent.MaximumChildren) message = "父节点的子条件容量已满。";
            else if (nodes.Any(node => node != null && node.Children.Contains(child))) message = "子条件已经拥有父节点。";
            else
            {
                var reachable = new HashSet<ConditionNode>();
                CollectReachable(child, new HashSet<ConditionNode>(nodes), reachable);
                if (reachable.Contains(parent)) message = "该连接会形成循环。";
            }
            return message == null ? null : new ConditionValidationIssue(E_ConditionIssueSeverity.Error, parent, message);
        }

        /// <summary>校验后建立父子引用，追加到确定的求值顺序末尾。</summary>
        /// <param name="parent">父节点。</param>
        /// <param name="child">子节点。</param>
        /// <exception cref="InvalidOperationException">候选连接不合法时抛出。</exception>
        public void Connect(ConditionNode parent, ConditionNode child)
        {
            ConditionValidationIssue issue = GetConnectionIssue(parent, child);
            if (issue != null)
            {
                Debug.LogError($"[ConditionGraph] 连接失败，graph={name}，reason={issue.Message}");
                throw new InvalidOperationException(issue.Message);
            }
            parent.AddChild(child);
            Debug.Log($"[ConditionGraph] 连接条件，graph={name}，parent={parent.name}，child={child.name}");
        }

        /// <summary>断开父子引用，子节点作为草稿保留。</summary>
        /// <param name="parent">父节点。</param>
        /// <param name="child">子节点。</param>
        public void Disconnect(ConditionNode parent, ConditionNode child)
        {
            if (!parent.Children.Contains(child)) return;
            parent.RemoveChild(child);
            Debug.Log($"[ConditionGraph] 断开条件，graph={name}，parent={parent.name}，child={child.name}");
        }

        /// <summary>清除所有入向引用并移出集合；其子节点仍属于图，销毁资源由 Editor 调用方负责。</summary>
        /// <param name="node">待移除节点。</param>
        public void RemoveNode(ConditionNode node)
        {
            if (!nodes.Remove(node)) return;
            foreach (ConditionNode parent in nodes) if (parent != null) parent.RemoveChild(node);
            if (root == node) root = null;
            node.SetOwner(null);
            Debug.Log($"[ConditionGraph] 移除节点，graph={name}，node={node.name}，remaining={nodes.Count}");
        }
        #endregion

        #region 校验辅助
        /// <summary>记录一个不会直接修改资产的结构错误。</summary>
        /// <param name="issues">输出问题集合。</param>
        /// <param name="node">关联节点。</param>
        /// <param name="message">错误说明。</param>
        private static void AddError(ICollection<ConditionValidationIssue> issues, ConditionNode node, string message) =>
            issues.Add(new ConditionValidationIssue(E_ConditionIssueSeverity.Error, node, message));

        /// <summary>使用访问栈检测所有草稿分支中的循环，避免求值递归无法结束。</summary>
        /// <param name="node">当前节点。</param>
        /// <param name="members">当前图成员。</param>
        /// <param name="visited">已完成的节点。</param>
        /// <param name="active">当前递归栈。</param>
        /// <param name="issues">输出问题集合。</param>
        private static void VisitForCycles(ConditionNode node, HashSet<ConditionNode> members,
            HashSet<ConditionNode> visited, HashSet<ConditionNode> active, ICollection<ConditionValidationIssue> issues)
        {
            if (active.Contains(node)) { AddError(issues, node, $"节点 {node.name} 存在循环引用。"); return; }
            if (!visited.Add(node)) return;
            active.Add(node);
            foreach (ConditionNode child in node.Children)
                if (child != null && members.Contains(child)) VisitForCycles(child, members, visited, active, issues);
            active.Remove(node);
        }

        /// <summary>收集图内可达节点；访问集合保证损坏图也不会无限递归。</summary>
        /// <param name="node">起始节点。</param>
        /// <param name="members">允许访问的图成员。</param>
        /// <param name="reachable">可达节点集合。</param>
        private static void CollectReachable(ConditionNode node, HashSet<ConditionNode> members, HashSet<ConditionNode> reachable)
        {
            if (node == null || !members.Contains(node) || !reachable.Add(node)) return;
            foreach (ConditionNode child in node.Children) CollectReachable(child, members, reachable);
        }
        #endregion
    }
}
