using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.DialogueSystemModule
{
    /// <summary>
    /// 表示一段由 3D 参与者播放动画并展示文本的对话内容。
    /// </summary>
    public sealed class DialogueSpeechNode : DialogueNode
    {
        #region 序列化字段

        [SerializeField] private string nodeName = string.Empty;
        [SerializeField] private DialogueSpeaker speaker;
        // 留空表示跟随 Speaker 资产名称，避免把默认名称复制到每个节点中。
        [SerializeField, Tooltip("留空时使用 DialogueSpeaker 的 SpeakerName。")]
        private string dialogueName = string.Empty;
        [SerializeField, TextArea(3, 8)] private string text = string.Empty;
        [SerializeField] private AnimationClip animationClip;
        [SerializeField] private AudioClip voiceClip;
        [SerializeField, MinValue(0f)] private float animationFadeDuration;
        [SerializeField] private DialogueNode nextNode;
        [SerializeField] private List<DialogueChoiceNode> choices = new List<DialogueChoiceNode>();

        #endregion

        #region 属性

        /// <summary>获取编辑器显示用的节点名称。</summary>
        public string NodeName => nodeName;

        /// <summary>获取当前对白使用的 Speaker 资产身份。</summary>
        public DialogueSpeaker Speaker => speaker;

        /// <summary>
        /// 获取当前对白最终展示给玩家的说话人名称；空覆盖值会动态回退到 Speaker 资产名称。
        /// </summary>
        public string Name => string.IsNullOrWhiteSpace(dialogueName)
            ? speaker?.SpeakerName ?? string.Empty
            : dialogueName.Trim();

        /// <summary>获取直接保存的对白文本。</summary>
        public string Text => text;

        /// <summary>获取可选的全身说话动画。</summary>
        public AnimationClip AnimationClip => animationClip;

        /// <summary>获取可选的对白语音片段。</summary>
        public AudioClip VoiceClip => voiceClip;

        /// <summary>获取动画在固定 Action 层的淡入时长。</summary>
        public float AnimationFadeDuration => animationFadeDuration;

        /// <summary>获取无 Choice 时的线性后续节点。</summary>
        public DialogueNode NextNode => nextNode;

        /// <summary>获取当前 SpeechNode 的 Choice 子节点集合。</summary>
        public IReadOnlyList<DialogueChoiceNode> Choices =>
            choices ?? (IReadOnlyList<DialogueChoiceNode>)Array.Empty<DialogueChoiceNode>();

        #endregion

        #region 编辑操作

        /// <summary>
        /// 设置 Inspector 可编辑的 SpeechNode 字段。
        /// </summary>
        /// <param name="value">对白使用的 Speaker 资产。</param>
        /// <param name="speechText">对白文本。</param>
        /// <param name="clip">全身说话动画，可为空。</param>
        /// <param name="fadeDuration">动画淡入秒数。</param>
        /// <param name="voice">对白语音，可为空。</param>
        /// <param name="displayName">当前对白的显示名称覆盖值；为空时使用 SpeakerName。</param>
        public void Configure(
            DialogueSpeaker value,
            string speechText,
            AnimationClip clip,
            float fadeDuration,
            AudioClip voice = null,
            string displayName = null)
        {
            speaker = value;
            dialogueName = displayName ?? string.Empty;
            text = speechText ?? string.Empty;
            animationClip = clip;
            voiceClip = voice;
            animationFadeDuration = Mathf.Max(0f, fadeDuration);
        }

        /// <summary>
        /// 设置编辑器显示用的节点名称；该名称不参与运行时寻址。
        /// </summary>
        /// <param name="value">新的节点名称。</param>
        public void SetNodeName(string value) => nodeName = value ?? string.Empty;

        /// <summary>
        /// 设置 SpeechNode 的线性后续节点引用。
        /// </summary>
        /// <param name="node">SpeechNode 或 EndNode；有 Choice 时通常为空。</param>
        public void SetNextNode(DialogueNode node) => nextNode = node;

        /// <summary>
        /// 将 ChoiceNode 添加到当前 SpeechNode 的子节点集合。
        /// </summary>
        /// <param name="choice">待添加的 ChoiceNode。</param>
        public void AddChoice(DialogueChoiceNode choice)
        {
            if (choice == null) throw new ArgumentNullException(nameof(choice));
            choices ??= new List<DialogueChoiceNode>();
            if (!choices.Contains(choice)) choices.Add(choice);
        }

        /// <summary>
        /// 从当前 SpeechNode 的子节点集合移除 ChoiceNode。
        /// </summary>
        /// <param name="choice">待移除的 ChoiceNode。</param>
        /// <returns>集合中存在并成功移除时返回 true。</returns>
        public bool RemoveChoice(DialogueChoiceNode choice) => choices != null && choices.Remove(choice);

        #endregion
    }
}
