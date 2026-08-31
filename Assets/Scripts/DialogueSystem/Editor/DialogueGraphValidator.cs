using System;
using System.Collections.Generic;
using System.Text;

namespace RPG.DialogueSystemModule
{
    #region 校验结果

    /// <summary>
    /// 表示编辑器或运行时图校验消息的严重程度。
    /// </summary>
    public enum DialogueValidationSeverity
    {
        /// <summary>不阻止保存或运行的提示。</summary>
        Info,

        /// <summary>需要修复但不一定阻止编辑的警告。</summary>
        Warning,

        /// <summary>阻止对话启动的错误。</summary>
        Error
    }

    /// <summary>
    /// 表示一个带有节点上下文的图校验消息。
    /// </summary>
    public readonly struct DialogueValidationMessage
    {
        /// <summary>创建图校验消息。</summary>
        /// <param name="severity">消息严重程度。</param>
        /// <param name="message">消息内容。</param>
        /// <param name="nodeId">关联节点标识，可为空。</param>
        public DialogueValidationMessage(DialogueValidationSeverity severity, string message, string nodeId = null)
        {
            Severity = severity;
            Message = message ?? string.Empty;
            NodeId = nodeId ?? string.Empty;
        }

        /// <summary>获取消息严重程度。</summary>
        public DialogueValidationSeverity Severity { get; }

        /// <summary>获取消息文本。</summary>
        public string Message { get; }

        /// <summary>获取关联节点标识。</summary>
        public string NodeId { get; }
    }

    #endregion

    #region 图校验服务

    /// <summary>
    /// 执行 DialogueAsset 的结构、引用、标识和命令配置校验。
    /// </summary>
    public static class DialogueGraphValidator
    {
        #region 公开校验

        /// <summary>
        /// 校验整个对话图；不自动修改资产内容。
        /// </summary>
        /// <param name="asset">待校验的对话资产。</param>
        /// <returns>按发现顺序排列的校验消息。</returns>
        public static IReadOnlyList<DialogueValidationMessage> Validate(DialogueAsset asset)
        {
            List<DialogueValidationMessage> messages = new List<DialogueValidationMessage>();
            if (asset == null)
            {
                messages.Add(new DialogueValidationMessage(
                    DialogueValidationSeverity.Error, "DialogueAsset 为空。"));
                return messages;
            }

            if (string.IsNullOrWhiteSpace(asset.DialogueId))
                messages.Add(new DialogueValidationMessage(
                    DialogueValidationSeverity.Error, "DialogueId 不能为空。"));
            if (asset.EntryNode == null)
            {
                messages.Add(new DialogueValidationMessage(
                    DialogueValidationSeverity.Error, "DialogueAsset 必须配置唯一 EntryNode。"));
                return messages;
            }
            if (asset.EntryNode.FirstSpeechNode == null)
                messages.Add(new DialogueValidationMessage(
                    DialogueValidationSeverity.Error, "EntryNode 必须直接引用首个 SpeechNode。", asset.EntryNode.NodeId));

            IReadOnlyList<DialogueNode> nodes = asset.Nodes;
            if (nodes == null || nodes.Count == 0)
            {
                messages.Add(new DialogueValidationMessage(
                    DialogueValidationSeverity.Error, "DialogueAsset 节点列表不能为空。"));
                return messages;
            }

            HashSet<DialogueNode> nodeSet = new HashSet<DialogueNode>();
            HashSet<string> nodeIds = new HashSet<string>(StringComparer.Ordinal);
            ValidateNodeList(nodes, nodeSet, nodeIds, messages);
            ValidateEntryReference(asset.EntryNode, nodeSet, messages);
            ValidateNodeReferences(nodes, nodeSet, messages);
            ValidateTermination(asset.EntryNode, nodes, nodeSet, messages);
            return messages;
        }

        /// <summary>
        /// 判断消息集合中是否存在 Error。
        /// </summary>
        /// <param name="messages">待检查的消息集合。</param>
        /// <returns>存在错误时返回 true。</returns>
        public static bool HasErrors(IReadOnlyList<DialogueValidationMessage> messages)
        {
            if (messages == null) return true;
            for (int index = 0; index < messages.Count; index++)
                if (messages[index].Severity == DialogueValidationSeverity.Error) return true;
            return false;
        }

        /// <summary>
        /// 将校验消息格式化为单段日志文本。
        /// </summary>
        /// <param name="messages">待格式化消息。</param>
        /// <returns>适合结果对象和状态栏显示的文本。</returns>
        public static string Format(IReadOnlyList<DialogueValidationMessage> messages)
        {
            if (messages == null || messages.Count == 0) return "Graph 校验通过。";
            StringBuilder builder = new StringBuilder();
            for (int index = 0; index < messages.Count; index++)
            {
                if (index > 0) builder.AppendLine();
                DialogueValidationMessage message = messages[index];
                builder.Append('[').Append(message.Severity).Append("] ").Append(message.Message);
            }

            return builder.ToString();
        }

        #endregion

        #region 节点校验

        /// <summary>校验节点集合、稳定 ID 和重复引用。</summary>
        /// <param name="nodes">资产节点集合。</param>
        /// <param name="nodeSet">输出节点引用集合。</param>
        /// <param name="nodeIds">输出节点标识集合。</param>
        /// <param name="messages">输出校验消息。</param>
        private static void ValidateNodeList(IReadOnlyList<DialogueNode> nodes, ISet<DialogueNode> nodeSet,
            ISet<string> nodeIds, ICollection<DialogueValidationMessage> messages)
        {
            for (int index = 0; index < nodes.Count; index++)
            {
                DialogueNode node = nodes[index];
                if (node == null)
                {
                    messages.Add(new DialogueValidationMessage(
                        DialogueValidationSeverity.Error, $"Nodes[{index}] 为空。"));
                    continue;
                }

                nodeSet.Add(node);
                if (string.IsNullOrWhiteSpace(node.NodeId))
                    messages.Add(new DialogueValidationMessage(
                        DialogueValidationSeverity.Error, "节点 NodeId 不能为空。"));
                else if (!nodeIds.Add(node.NodeId))
                    messages.Add(new DialogueValidationMessage(
                        DialogueValidationSeverity.Error, $"节点 NodeId 重复：{node.NodeId}。", node.NodeId));
            }
        }

        /// <summary>校验入口节点是否包含在资产节点集合中。</summary>
        /// <param name="entryNode">资产入口节点。</param>
        /// <param name="nodeSet">资产节点引用集合。</param>
        /// <param name="messages">输出校验消息。</param>
        private static void ValidateEntryReference(DialogueEntryNode entryNode, ISet<DialogueNode> nodeSet,
            ICollection<DialogueValidationMessage> messages)
        {
            if (!nodeSet.Contains(entryNode))
                messages.Add(new DialogueValidationMessage(
                    DialogueValidationSeverity.Error, "EntryNode 必须属于 DialogueAsset.Nodes。", entryNode.NodeId));
        }

        /// <summary>校验各节点的直接引用和命令定义。</summary>
        /// <param name="nodes">资产节点集合。</param>
        /// <param name="nodeSet">资产节点引用集合。</param>
        /// <param name="messages">输出校验消息。</param>
        private static void ValidateNodeReferences(IReadOnlyList<DialogueNode> nodes, ISet<DialogueNode> nodeSet,
            ICollection<DialogueValidationMessage> messages)
        {
            for (int index = 0; index < nodes.Count; index++)
            {
                DialogueNode node = nodes[index];
                if (node is DialogueSpeechNode speech)
                    ValidateSpeechNode(speech, nodeSet, messages);
                else if (node is DialogueChoiceNode choice)
                    ValidateChoiceNode(choice, nodeSet, messages);
            }
        }

        /// <summary>校验 SpeechNode 的后续引用、Speaker 资产和 Choice 子节点。</summary>
        /// <param name="speech">待校验节点。</param>
        /// <param name="nodeSet">资产节点引用集合。</param>
        /// <param name="messages">输出校验消息。</param>
        private static void ValidateSpeechNode(DialogueSpeechNode speech, ISet<DialogueNode> nodeSet,
            ICollection<DialogueValidationMessage> messages)
        {
            if (speech.Speaker == null)
                messages.Add(new DialogueValidationMessage(
                    DialogueValidationSeverity.Error, "SpeechNode 的 Speaker 资产为空。", speech.NodeId));

            IReadOnlyList<DialogueChoiceNode> choices = speech.Choices;
            if (choices == null || choices.Count == 0)
            {
                ValidateTarget(speech.NextNode, nodeSet, messages, speech.NodeId, "NextNode", false);
                return;
            }

            if (speech.NextNode != null)
            {
                messages.Add(new DialogueValidationMessage(
                    DialogueValidationSeverity.Error,
                    "SpeechNode 不能同时配置 Choices 和 NextNode；请选择一种输出模式。",
                    speech.NodeId));
                // 混合结构本身非法，但仍校验 NextNode 的引用，避免一个错误掩盖另一个错误。
                ValidateTarget(speech.NextNode, nodeSet, messages, speech.NodeId, "NextNode", true);
            }
            for (int index = 0; index < choices.Count; index++)
            {
                DialogueChoiceNode choice = choices[index];
                if (choice == null)
                {
                    messages.Add(new DialogueValidationMessage(
                        DialogueValidationSeverity.Error, $"SpeechNode 的 Choices[{index}] 为空。", speech.NodeId));
                    continue;
                }

                if (!nodeSet.Contains(choice))
                    messages.Add(new DialogueValidationMessage(
                        DialogueValidationSeverity.Error, "ChoiceNode 必须属于 DialogueAsset.Nodes。", choice.NodeId));
            }
        }

        /// <summary>校验 ChoiceNode 的目标和命令配置。</summary>
        /// <param name="choice">待校验选项。</param>
        /// <param name="nodeSet">资产节点引用集合。</param>
        /// <param name="messages">输出校验消息。</param>
        private static void ValidateChoiceNode(DialogueChoiceNode choice, ISet<DialogueNode> nodeSet,
            ICollection<DialogueValidationMessage> messages)
        {
            if (string.IsNullOrWhiteSpace(choice.Text))
                messages.Add(new DialogueValidationMessage(
                    DialogueValidationSeverity.Warning, "Choice 文本为空。", choice.NodeId));
            ValidateTarget(choice.TargetNode, nodeSet, messages, choice.NodeId, "TargetNode", false);

            for (int index = 0; index < choice.Conditions.Count; index++)
            {
                DialogueCondition definition = choice.Conditions[index];
                ValidateDefinition(definition, messages, choice, "Condition", index);
            }
            for (int index = 0; index < choice.Actions.Count; index++)
            {
                DialogueAction definition = choice.Actions[index];
                ValidateDefinition(definition, messages, choice, "Action", index);
            }
        }

        /// <summary>校验直接节点引用存在且类型符合跳转约束。</summary>
        /// <param name="target">待校验目标。</param>
        /// <param name="nodeSet">资产节点引用集合。</param>
        /// <param name="messages">输出校验消息。</param>
        /// <param name="nodeId">来源节点标识。</param>
        /// <param name="fieldName">来源字段名称。</param>
        /// <param name="allowNull">是否允许空引用。</param>
        private static void ValidateTarget(DialogueNode target, ISet<DialogueNode> nodeSet,
            ICollection<DialogueValidationMessage> messages, string nodeId, string fieldName, bool allowNull)
        {
            if (target == null)
            {
                if (!allowNull)
                    messages.Add(new DialogueValidationMessage(
                        DialogueValidationSeverity.Error, $"{fieldName} 不能为空。", nodeId));
                return;
            }

            if (!nodeSet.Contains(target))
                messages.Add(new DialogueValidationMessage(
                    DialogueValidationSeverity.Error, $"{fieldName} 引用了不属于当前资产的节点。", nodeId));
            if (!(target is DialogueSpeechNode) && !(target is DialogueEndNode))
                messages.Add(new DialogueValidationMessage(
                    DialogueValidationSeverity.Error, $"{fieldName} 只能指向 SpeechNode 或 EndNode。", nodeId));
        }

        /// <summary>校验命令定义非空并执行其静态配置校验。</summary>
        /// <param name="definition">待校验的派生定义。</param>
        /// <param name="messages">输出校验消息。</param>
        /// <param name="choice">关联的 Choice 节点。</param>
        /// <param name="commandName">Condition 或 Action 名称。</param>
        /// <param name="index">命令在 Choice 列表中的索引。</param>
        private static void ValidateDefinition(object definition,
            ICollection<DialogueValidationMessage> messages, DialogueChoiceNode choice,
            string commandName, int index)
        {
            if (definition == null)
            {
                messages.Add(new DialogueValidationMessage(
                    DialogueValidationSeverity.Error,
                    $"Choice '{choice.NodeName}' ({choice.NodeId}) 的 {commandName}[{index}] 不能为空。",
                    choice.NodeId));
                return;
            }

            // 命令 Validate 只检查序列化字段，不访问运行时架构或执行任何业务副作用。
            try
            {
                if (definition is DialogueCondition condition)
                    condition.Validate();
                else if (definition is DialogueAction action)
                    action.Validate();
            }
            catch (Exception exception)
            {
                messages.Add(new DialogueValidationMessage(
                    DialogueValidationSeverity.Error,
                    $"Choice '{choice.NodeName}' ({choice.NodeId}) 的 {commandName}[{index}] " +
                    $"({definition.GetType().FullName}) 配置无效：{exception.Message}",
                    choice.NodeId));
            }
        }

        #endregion

        #region 内部辅助

        /// <summary>
        /// 校验 Entry 可达子图是否存在终点，并确保每个可达节点都有通往 EndNode 的路径。
        /// </summary>
        /// <param name="entryNode">资产入口节点。</param>
        /// <param name="nodes">资产全部节点。</param>
        /// <param name="nodeSet">资产节点引用集合。</param>
        /// <param name="messages">输出校验消息。</param>
        private static void ValidateTermination(
            DialogueEntryNode entryNode,
            IReadOnlyList<DialogueNode> nodes,
            ISet<DialogueNode> nodeSet,
            ICollection<DialogueValidationMessage> messages)
        {
            Dictionary<DialogueNode, List<DialogueNode>> edges = BuildEdges(entryNode, nodes, nodeSet);
            HashSet<DialogueNode> reachable = new HashSet<DialogueNode>();
            Queue<DialogueNode> pending = new Queue<DialogueNode>();
            if (entryNode != null) pending.Enqueue(entryNode);
            while (pending.Count > 0)
            {
                DialogueNode current = pending.Dequeue();
                if (!reachable.Add(current) || !edges.TryGetValue(current, out List<DialogueNode> nextNodes)) continue;
                for (int index = 0; index < nextNodes.Count; index++)
                    if (!reachable.Contains(nextNodes[index])) pending.Enqueue(nextNodes[index]);
            }

            HashSet<DialogueNode> canReachEnd = new HashSet<DialogueNode>();
            foreach (DialogueNode node in reachable)
                if (node is DialogueEndNode) canReachEnd.Add(node);

            bool changed;
            do
            {
                changed = false;
                foreach (KeyValuePair<DialogueNode, List<DialogueNode>> pair in edges)
                {
                    if (!reachable.Contains(pair.Key) || canReachEnd.Contains(pair.Key)) continue;
                    for (int index = 0; index < pair.Value.Count; index++)
                    {
                        if (!canReachEnd.Contains(pair.Value[index])) continue;
                        canReachEnd.Add(pair.Key);
                        changed = true;
                        break;
                    }
                }
            } while (changed);

            bool hasReachableEnd = false;
            foreach (DialogueNode node in canReachEnd)
                if (node is DialogueEndNode) { hasReachableEnd = true; break; }
            if (!hasReachableEnd)
            {
                messages.Add(new DialogueValidationMessage(
                    DialogueValidationSeverity.Error,
                    "EntryNode 可达子图至少需要一个 EndNode。",
                    entryNode?.NodeId));
            }

            foreach (DialogueNode node in reachable)
            {
                if (node is DialogueEndNode || canReachEnd.Contains(node)) continue;
                messages.Add(new DialogueValidationMessage(
                    DialogueValidationSeverity.Error,
                    "可达节点没有通往 EndNode 的路径，可能存在封闭循环或死分支。",
                    node.NodeId));
            }
        }

        /// <summary>按运行时节点跳转规则构建图边集合。</summary>
        /// <param name="entryNode">入口节点。</param>
        /// <param name="nodes">资产节点集合。</param>
        /// <param name="nodeSet">资产节点引用集合。</param>
        /// <returns>每个节点的直接后继集合。</returns>
        private static Dictionary<DialogueNode, List<DialogueNode>> BuildEdges(
            DialogueEntryNode entryNode,
            IReadOnlyList<DialogueNode> nodes,
            ISet<DialogueNode> nodeSet)
        {
            Dictionary<DialogueNode, List<DialogueNode>> edges = new Dictionary<DialogueNode, List<DialogueNode>>();
            for (int index = 0; index < nodes.Count; index++)
            {
                DialogueNode node = nodes[index];
                if (node == null) continue;
                List<DialogueNode> next = new List<DialogueNode>();
                if (node is DialogueEntryNode entry && entry.FirstSpeechNode != null && nodeSet.Contains(entry.FirstSpeechNode))
                    next.Add(entry.FirstSpeechNode);
                else if (node is DialogueSpeechNode speech)
                {
                    if (speech.Choices.Count > 0)
                    {
                        for (int choiceIndex = 0; choiceIndex < speech.Choices.Count; choiceIndex++)
                        {
                            DialogueChoiceNode choice = speech.Choices[choiceIndex];
                            if (choice != null && nodeSet.Contains(choice)) next.Add(choice);
                        }
                    }
                    else if (speech.NextNode != null && nodeSet.Contains(speech.NextNode))
                        next.Add(speech.NextNode);
                }
                else if (node is DialogueChoiceNode choiceNode &&
                         choiceNode.TargetNode != null && nodeSet.Contains(choiceNode.TargetNode))
                    next.Add(choiceNode.TargetNode);

                edges[node] = next;
            }

            return edges;
        }

        #endregion
    }

    #endregion
}
