using System;
using System.Collections.Generic;
using UnityEngine;

namespace RPG.DialogueSystemModule
{
    /// <summary>
    /// 表示一个包含条件、动作和直接目标引用的选项子节点。
    /// </summary>
    public sealed class DialogueChoiceNode : DialogueNode
    {
        #region 序列化字段

        [SerializeField] private string choiceId = string.Empty;
        [SerializeField, TextArea(2, 5)] private string text = string.Empty;
        [SerializeReference] private List<DialogueCondition> conditions =
            new List<DialogueCondition>();
        [SerializeReference] private List<DialogueAction> actions =
            new List<DialogueAction>();
        [SerializeField] private DialogueNode targetNode;

        #endregion

        #region 属性

        /// <summary>获取同一 SpeechNode 内唯一的选项标识。</summary>
        public string ChoiceId => choiceId;

        /// <summary>获取选项显示文本。</summary>
        public string Text => text;

        /// <summary>获取按 AND 规则计算的条件定义集合。</summary>
        public IReadOnlyList<DialogueCondition> Conditions =>
            conditions ?? (IReadOnlyList<DialogueCondition>)Array.Empty<DialogueCondition>();

        /// <summary>获取选择后按顺序触发的动作定义集合。</summary>
        public IReadOnlyList<DialogueAction> Actions =>
            actions ?? (IReadOnlyList<DialogueAction>)Array.Empty<DialogueAction>();

        /// <summary>获取选择后直接进入的 SpeechNode 或 EndNode。</summary>
        public DialogueNode TargetNode => targetNode;

        #endregion

        #region 编辑操作

        /// <summary>
        /// 设置 ChoiceNode 的基本展示字段。
        /// </summary>
        /// <param name="id">同一 SpeechNode 内唯一的 ChoiceId。</param>
        /// <param name="choiceText">选项显示文本。</param>
        public void Configure(string id, string choiceText)
        {
            choiceId = id ?? string.Empty;
            text = choiceText ?? string.Empty;
        }

        /// <summary>
        /// 设置 ChoiceNode 的目标节点直接引用。
        /// </summary>
        /// <param name="node">选择后进入的 SpeechNode 或 EndNode。</param>
        public void SetTargetNode(DialogueNode node) => targetNode = node;

        #endregion
    }
}
