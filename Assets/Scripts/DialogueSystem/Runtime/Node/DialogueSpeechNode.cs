using System;
using System.Collections.Generic;
using UnityEngine;

namespace RPG.DialogueSystemModule
{
    /// <summary>
    /// 表示一段由 3D 参与者播放动画并展示文本的对话内容。
    /// </summary>
    public sealed class DialogueSpeechNode : DialogueNode
    {
        #region 序列化字段

        [SerializeField] private string speakerId = string.Empty;
        [SerializeField, TextArea(3, 8)] private string text = string.Empty;
        [SerializeField] private AnimationClip animationClip;
        [SerializeField] private AudioClip voiceClip;
        [SerializeField, Min(0f)] private float animationFadeDuration;
        [SerializeField] private DialogueNode nextNode;
        [SerializeField] private List<DialogueChoiceNode> choices = new List<DialogueChoiceNode>();

        #endregion

        #region 属性

        /// <summary>获取静态 SpeakerId。</summary>
        public string SpeakerId => speakerId;

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
        /// <param name="value">SpeakerId。</param>
        /// <param name="speechText">对白文本。</param>
        /// <param name="clip">全身说话动画，可为空。</param>
        /// <param name="fadeDuration">动画淡入秒数。</param>
        /// <param name="voice">对白语音，可为空。</param>
        public void Configure(
            string value,
            string speechText,
            AnimationClip clip,
            float fadeDuration,
            AudioClip voice = null)
        {
            speakerId = value ?? string.Empty;
            text = speechText ?? string.Empty;
            animationClip = clip;
            voiceClip = voice;
            animationFadeDuration = Mathf.Max(0f, fadeDuration);
        }

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
