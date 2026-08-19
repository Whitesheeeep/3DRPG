using System;
using System.Collections.Generic;
using RPG.Character.Animation;

namespace RPG.DialogueSystem
{
    /// <summary>
    /// 管理一段完整对话周期中的当前节点、参与者动画、控制 Tag 和事实事件。
    /// </summary>
    public sealed class DialogueSession
    {
        #region 字段与属性
        private readonly string sessionId;
        private IAnimationPlayer activeAnimationPlayer;
        private bool dialogueControlTagAdded;

        /// <summary>创建一个尚未进入首个 SpeechNode 的会话。</summary>
        /// <param name="request">本次对话请求。</param>
        internal DialogueSession(DialogueRequest request)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
            sessionId = Guid.NewGuid().ToString("D");
            State = DialogueSessionState.Running;
        }

        /// <summary>获取本次对话周期的稳定运行时标识。</summary>
        public string SessionId => sessionId;

        /// <summary>获取本次会话唯一的请求。</summary>
        public DialogueRequest Request { get; }

        /// <summary>获取当前会话状态。</summary>
        public DialogueSessionState State { get; private set; }

        /// <summary>获取当前 SpeechNode；未进入对白时为空。</summary>
        public DialogueSpeechNode CurrentSpeech { get; private set; }

        /// <summary>获取当前 Choice 等待状态下的选项集合。</summary>
        public IReadOnlyList<DialogueChoiceNode> CurrentChoices =>
            CurrentSpeech?.Choices ?? Array.Empty<DialogueChoiceNode>();

        /// <summary>获取当前会话是否已经结束。</summary>
        public bool IsEnded => State == DialogueSessionState.Ended;
        #endregion

        #region 事实事件
        /// <summary>SpeechNode 进入并展示后的事件。</summary>
        public event Action<DialogueSpeechPresentedEvent> SpeechPresented;

        /// <summary>当前 SpeechNode 的 Choice 展示后的事件。</summary>
        public event Action<DialogueChoicePresentedEvent> ChoicePresented;

        /// <summary>会话结束后的事件。</summary>
        public event Action<DialogueEndedEvent> Ended;
        #endregion

        #region 节点进入
        /// <summary>
        /// 进入指定 SpeechNode，播放其固定 Action 层全身动画并发布展示事件。
        /// </summary>
        /// <param name="speech">待进入的 SpeechNode。</param>
        internal void EnterSpeech(DialogueSpeechNode speech)
        {
            if (speech == null) throw new ArgumentNullException(nameof(speech));
            if (IsEnded) return;

            StopActiveAnimation();
            CurrentSpeech = speech;
            State = speech.Choices.Count > 0
                ? DialogueSessionState.WaitingForChoice
                : DialogueSessionState.Running;

            DialogueParticipantBinding participant = Request.FindParticipant(speech.SpeakerId);
            if (speech.AnimationClip != null && participant?.AnimationPlayer != null)
            {
                // 对话动画固定进入全身 Action 层；新对白先停止本会话上一段动作，避免层内残留。
                activeAnimationPlayer = participant.AnimationPlayer;
                activeAnimationPlayer.Play(
                    AnimationLayerType.Action,
                    speech.AnimationClip,
                    speech.AnimationFadeDuration);
            }

            SpeechPresented?.Invoke(new DialogueSpeechPresentedEvent(this, speech));
            if (State == DialogueSessionState.WaitingForChoice)
                ChoicePresented?.Invoke(new DialogueChoicePresentedEvent(this, speech));
        }
        #endregion

        #region 结束与资源清理
        /// <summary>
        /// 关闭会话并清理动画和本会话添加的控制 Tag。
        /// </summary>
        /// <param name="message">结束补充说明。</param>
        internal void End(string message)
        {
            if (IsEnded) return;

            StopActiveAnimation();
            RemoveDialogueControlTag();
            State = DialogueSessionState.Ended;
            Ended?.Invoke(new DialogueEndedEvent(this, message));
        }

        /// <summary>
        /// 为本会话添加一次引用计数式控制 Tag；无效 Tag 不伪造运行时状态。
        /// </summary>
        internal void AddDialogueControlTag()
        {
            if (dialogueControlTagAdded || Request.PlayerAbilitySystem == null ||
                !Request.DialogueControlTag.IsValid)
                return;

            // ASC 内部仍以 GameplayTagCountContainer 维护引用计数，系统只提交本次会话的一次增量。
            Request.PlayerAbilitySystem.UpdateRuntimeTagCount(Request.DialogueControlTag, 1);
            dialogueControlTagAdded = true;
        }

        /// <summary>
        /// 停止当前会话启动的 Action 层动画，并清除引用。
        /// </summary>
        private void StopActiveAnimation()
        {
            if (activeAnimationPlayer == null) return;
            activeAnimationPlayer.StopLayer(AnimationLayerType.Action);
            activeAnimationPlayer = null;
        }

        /// <summary>
        /// 对称移除本会话曾经添加的控制 Tag。
        /// </summary>
        private void RemoveDialogueControlTag()
        {
            if (!dialogueControlTagAdded) return;
            Request.PlayerAbilitySystem.UpdateRuntimeTagCount(Request.DialogueControlTag, -1);
            dialogueControlTagAdded = false;
        }
        #endregion
    }
}
