#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;
using WS_Modules.UIToolkitExtensions.Editor.GraphView;

namespace RPG.DialogueSystemModule.Editor
{
    /// <summary>
    /// 将一个 DialogueNode 映射为 WSFrame GraphView 节点，并提供稳定端口语义。
    /// </summary>
    internal sealed class DialogueGraphNodeView : WSGraphNode, IGraphNodeContentProvider,
        IGraphPortProvider, IGraphContextMenuProvider, IGraphNodeStyleProvider
    {
        #region 常量与属性

        private const string StyleSheetPath = "Assets/Scripts/DialogueSystem/Editor/Style/DialogueGraphEditor.uss";

        /// <summary>获取该 View 绑定的领域节点。</summary>
        internal DialogueNode Model { get; }

        /// <summary>获取该 View 绑定的节点类型名称。</summary>
        internal string NodeKind { get; }

        #endregion

        #region 生命周期

        /// <summary>
        /// 创建一个绑定领域节点的 GraphView 节点。
        /// </summary>
        /// <param name="model">待显示的领域节点。</param>
        internal DialogueGraphNodeView(DialogueNode model)
        {
            Model = model;
            NodeKind = GetNodeKind(model);
            title = NodeKind;
        }

        #endregion

        #region GraphView 内容

        /// <summary>
        /// 填充节点摘要，保持详细编辑字段由右侧 Inspector 负责。
        /// </summary>
        /// <param name="contentContainer">节点中间内容容器。</param>
        public void PopulateContent(VisualElement contentContainer)
        {
            if (Model is DialogueSpeechNode speech)
            {
                contentContainer.Add(new Label(string.IsNullOrWhiteSpace(speech.SpeakerId)
                    ? "SpeakerId: <empty>"
                    : $"SpeakerId: {speech.SpeakerId}"));
                contentContainer.Add(new Label(TrimText(speech.Text)));
                contentContainer.Add(new Label($"Choices: {speech.Choices.Count}"));
            }
            else if (Model is DialogueChoiceNode choice)
            {
                contentContainer.Add(new Label(string.IsNullOrWhiteSpace(choice.ChoiceId)
                    ? "ChoiceId: <empty>"
                    : choice.ChoiceId));
                contentContainer.Add(new Label(TrimText(choice.Text)));
                contentContainer.Add(new Label($"Conditions: {choice.Conditions.Count}"));
                contentContainer.Add(new Label($"Actions: {choice.Actions.Count}"));
            }
            else if (Model is DialogueEntryNode entry)
            {
                contentContainer.Add(new Label(entry.FirstSpeechNode == null
                    ? "First Speech: <empty>"
                    : "First Speech configured"));
            }
        }

        /// <summary>
        /// 重新生成节点摘要内容，保留端口、位置、选中状态和连线实例。
        /// </summary>
        internal void RefreshContent()
        {
            extensionContainer.Clear();
            PopulateContent(extensionContainer);
            RefreshExpandedState();
        }

        /// <summary>
        /// 返回节点端口声明；端口 ID 用于 Controller 还原直接 ScriptableObject 引用。
        /// </summary>
        /// <returns>当前节点的稳定端口描述。</returns>
        public IEnumerable<GraphPortDescriptor> GetPortDescriptors()
        {
            switch (Model)
            {
                case DialogueEntryNode:
                    yield return new GraphPortDescriptor("entry-output", "Start", Direction.Output,
                        Port.Capacity.Single, typeof(DialogueNode));
                    yield break;
                case DialogueSpeechNode:
                    yield return new GraphPortDescriptor("speech-input", "In", Direction.Input,
                        Port.Capacity.Multi, typeof(DialogueNode));
                    yield return new GraphPortDescriptor("speech-output", "Out", Direction.Output,
                        Port.Capacity.Multi, typeof(DialogueNode));
                    yield break;
                case DialogueChoiceNode:
                    yield return new GraphPortDescriptor("choice-owner", "Owner", Direction.Input,
                        Port.Capacity.Single, typeof(DialogueNode));
                    yield return new GraphPortDescriptor("choice-target", "Target", Direction.Output,
                        Port.Capacity.Single, typeof(DialogueNode));
                    yield break;
                case DialogueEndNode:
                    yield return new GraphPortDescriptor("end-input", "In", Direction.Input,
                        Port.Capacity.Multi, typeof(DialogueNode));
                    break;
            }
        }

        /// <summary>
        /// 提供节点专属 USS 路径。
        /// </summary>
        /// <returns>当前节点加载的 USS 路径。</returns>
        public IEnumerable<string> GetStyleSheetPaths()
        {
            yield return StyleSheetPath;
        }

        /// <summary>
        /// 提供 Entry、Speech、Choice 和 End 的根样式类。
        /// </summary>
        /// <returns>当前节点根样式类。</returns>
        public IEnumerable<string> GetStyleClassNames()
        {
            yield return $"dialogue-node--{NodeKind.ToLowerInvariant()}";
        }

        /// <summary>
        /// 为节点提供删除菜单；实际领域删除由 GraphView Controller 处理。
        /// </summary>
        /// <param name="context">当前右键菜单上下文。</param>
        /// <param name="menu">待追加菜单的对象。</param>
        public void PopulateContextMenu(GraphContextMenuContext context, DropdownMenu menu)
        {
            menu.AppendSeparator();
            menu.AppendAction("删除对话节点", _ => context.GraphView.RemoveGraphElements(new[] { this }));
        }

        #endregion

        #region 内部辅助

        /// <summary>获取领域节点的显示类型名称。</summary>
        /// <param name="node">领域节点。</param>
        /// <returns>稳定显示名称。</returns>
        private static string GetNodeKind(DialogueNode node)
        {
            if (node is DialogueEntryNode) return "EntryNode";
            if (node is DialogueSpeechNode) return "SpeechNode";
            if (node is DialogueChoiceNode) return "ChoiceNode";
            if (node is DialogueEndNode) return "EndNode";
            return "DialogueNode";
        }

        /// <summary>把多行对白压缩为节点摘要，避免节点内容撑开画布。</summary>
        /// <param name="value">原始文本。</param>
        /// <returns>单行摘要。</returns>
        private static string TrimText(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Text: <empty>";
            string normalized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return normalized.Length <= 34 ? normalized : normalized.Substring(0, 34) + "…";
        }

        #endregion
    }
}
#endif
