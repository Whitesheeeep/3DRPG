using System;
using System.Collections.Generic;
using System.Text;

namespace RPG.DialogueSystem
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
    /// 执行 DialogueAsset 的结构、引用、标识和 Handler 注册校验。
    /// </summary>
    public static class DialogueGraphValidator
    {
        #region 公开校验

        /// <summary>
        /// 校验整个对话图；不自动修改资产内容。
        /// </summary>
        /// <param name="asset">待校验的对话资产。</param>
        /// <param name="conditionDefinitionTypes">当前已注册的 Condition 定义类型。</param>
        /// <param name="actionDefinitionTypes">当前已注册的 Action 定义类型。</param>
        /// <returns>按发现顺序排列的校验消息。</returns>
        public static IReadOnlyList<DialogueValidationMessage> Validate(
            DialogueAsset asset,
            IEnumerable<Type> conditionDefinitionTypes = null,
            IEnumerable<Type> actionDefinitionTypes = null)
        {
            bool validateConditionHandlers = conditionDefinitionTypes != null;
            bool validateActionHandlers = actionDefinitionTypes != null;
            HashSet<Type> conditionTypes = CreateTypeSet(conditionDefinitionTypes);
            HashSet<Type> actionTypes = CreateTypeSet(actionDefinitionTypes);
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
            ValidateNodeReferences(nodes, nodeSet, messages, conditionTypes, actionTypes,
                validateConditionHandlers, validateActionHandlers);
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

        /// <summary>校验各节点的直接引用、ChoiceId 和 Handler 定义。</summary>
        /// <param name="nodes">资产节点集合。</param>
        /// <param name="nodeSet">资产节点引用集合。</param>
        /// <param name="messages">输出校验消息。</param>
        /// <param name="conditionTypes">已注册 Condition 定义类型。</param>
        /// <param name="actionTypes">已注册 Action 定义类型。</param>
        /// <param name="validateConditionHandlers">是否检查 Condition 定义类型是否已注册。</param>
        /// <param name="validateActionHandlers">是否检查 Action 定义类型是否已注册。</param>
        private static void ValidateNodeReferences(IReadOnlyList<DialogueNode> nodes, ISet<DialogueNode> nodeSet,
            ICollection<DialogueValidationMessage> messages, ISet<Type> conditionTypes, ISet<Type> actionTypes,
            bool validateConditionHandlers, bool validateActionHandlers)
        {
            for (int index = 0; index < nodes.Count; index++)
            {
                DialogueNode node = nodes[index];
                if (node is DialogueSpeechNode speech)
                    ValidateSpeechNode(speech, nodeSet, messages, conditionTypes, actionTypes,
                        validateConditionHandlers, validateActionHandlers);
                else if (node is DialogueChoiceNode choice)
                    ValidateChoiceNode(choice, nodeSet, messages, conditionTypes, actionTypes,
                        validateConditionHandlers, validateActionHandlers);
            }
        }

        /// <summary>校验 SpeechNode 的后续引用、SpeakerId 和 Choice 子节点。</summary>
        /// <param name="speech">待校验节点。</param>
        /// <param name="nodeSet">资产节点引用集合。</param>
        /// <param name="messages">输出校验消息。</param>
        /// <param name="conditionTypes">已注册 Condition 定义类型。</param>
        /// <param name="actionTypes">已注册 Action 定义类型。</param>
        private static void ValidateSpeechNode(DialogueSpeechNode speech, ISet<DialogueNode> nodeSet,
            ICollection<DialogueValidationMessage> messages, ISet<Type> conditionTypes, ISet<Type> actionTypes,
            bool validateConditionHandlers, bool validateActionHandlers)
        {
            if (string.IsNullOrWhiteSpace(speech.SpeakerId))
                messages.Add(new DialogueValidationMessage(
                    DialogueValidationSeverity.Warning, "SpeechNode 的 SpeakerId 为空。", speech.NodeId));

            IReadOnlyList<DialogueChoiceNode> choices = speech.Choices;
            if (choices == null || choices.Count == 0)
            {
                ValidateTarget(speech.NextNode, nodeSet, messages, speech.NodeId, "NextNode", false);
                return;
            }

            ValidateTarget(speech.NextNode, nodeSet, messages, speech.NodeId, "NextNode", true);
            HashSet<string> choiceIds = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < choices.Count; index++)
            {
                DialogueChoiceNode choice = choices[index];
                if (choice == null)
                {
                    messages.Add(new DialogueValidationMessage(
                        DialogueValidationSeverity.Error, $"SpeechNode 的 Choices[{index}] 为空。", speech.NodeId));
                    continue;
                }

                if (!choiceIds.Add(choice.ChoiceId))
                    messages.Add(new DialogueValidationMessage(
                        DialogueValidationSeverity.Error, $"ChoiceId 重复：{choice.ChoiceId}。", choice.NodeId));
                if (!nodeSet.Contains(choice))
                    messages.Add(new DialogueValidationMessage(
                        DialogueValidationSeverity.Error, "ChoiceNode 必须属于 DialogueAsset.Nodes。", choice.NodeId));
            }
        }

        /// <summary>校验 ChoiceNode 的目标和 Handler 配置。</summary>
        /// <param name="choice">待校验选项。</param>
        /// <param name="nodeSet">资产节点引用集合。</param>
        /// <param name="messages">输出校验消息。</param>
        /// <param name="conditionTypes">已注册 Condition 定义类型。</param>
        /// <param name="actionTypes">已注册 Action 定义类型。</param>
        private static void ValidateChoiceNode(DialogueChoiceNode choice, ISet<DialogueNode> nodeSet,
            ICollection<DialogueValidationMessage> messages, ISet<Type> conditionTypes, ISet<Type> actionTypes,
            bool validateConditionHandlers, bool validateActionHandlers)
        {
            if (string.IsNullOrWhiteSpace(choice.ChoiceId))
                messages.Add(new DialogueValidationMessage(
                    DialogueValidationSeverity.Error, "ChoiceId 不能为空。", choice.NodeId));
            if (string.IsNullOrWhiteSpace(choice.Text))
                messages.Add(new DialogueValidationMessage(
                    DialogueValidationSeverity.Warning, "Choice 文本为空。", choice.NodeId));
            ValidateTarget(choice.TargetNode, nodeSet, messages, choice.NodeId, "TargetNode", false);

            for (int index = 0; index < choice.Conditions.Count; index++)
            {
                DialogueCondition definition = choice.Conditions[index];
                ValidateHandler(definition, conditionTypes, validateConditionHandlers,
                    messages, choice.NodeId, "Condition");
            }
            for (int index = 0; index < choice.Actions.Count; index++)
            {
                DialogueAction definition = choice.Actions[index];
                ValidateHandler(definition, actionTypes, validateActionHandlers,
                    messages, choice.NodeId, "Action");
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

        /// <summary>校验派生定义非空且存在于当前注册表。</summary>
        /// <param name="definition">待校验的派生定义。</param>
        /// <param name="registeredTypes">当前注册的定义类型。</param>
        /// <param name="messages">输出校验消息。</param>
        /// <param name="nodeId">关联节点标识。</param>
        /// <param name="handlerName">Condition 或 Action 名称。</param>
        private static void ValidateHandler(object definition, ISet<Type> registeredTypes, bool validateHandlers,
            ICollection<DialogueValidationMessage> messages, string nodeId, string handlerName)
        {
            if (definition == null)
            {
                messages.Add(new DialogueValidationMessage(
                    DialogueValidationSeverity.Error, $"{handlerName} Definition 不能为空。", nodeId));
                return;
            }
            Type definitionType = definition.GetType();
            if (validateHandlers && !registeredTypes.Contains(definitionType))
                messages.Add(new DialogueValidationMessage(
                    DialogueValidationSeverity.Error,
                    $"未注册 {handlerName} Handler：{definitionType.FullName}。", nodeId));

            // 先检查定义自身字段，再由注册表检查运行时 Handler；两类错误都只记录到校验结果，不修改资产。
            try
            {
                if (definition is DialogueCondition condition)
                    condition.Validate();
                else if (definition is DialogueAction action)
                    action.Validate();
            }
            catch (ArgumentException exception)
            {
                messages.Add(new DialogueValidationMessage(
                    DialogueValidationSeverity.Error,
                    $"{handlerName} Definition 配置无效：{exception.Message}", nodeId));
            }
        }

        #endregion

        #region 内部辅助

        /// <summary>复制 Handler 定义类型集合。</summary>
        /// <param name="types">待复制的定义类型集合。</param>
        /// <returns>定义类型集合。</returns>
        private static HashSet<Type> CreateTypeSet(IEnumerable<Type> types)
        {
            HashSet<Type> result = new HashSet<Type>();
            if (types == null) return result;
            foreach (Type type in types)
                if (type != null) result.Add(type);
            return result;
        }

        #endregion
    }

    #endregion
}
