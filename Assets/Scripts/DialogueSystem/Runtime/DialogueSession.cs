using System;
using System.Collections.Generic;
using RPG.Character.Animation;
using UnityEngine;

namespace RPG.DialogueSystemModule
{
    /// <summary>
    /// 管理一段完整对话周期中的当前节点、参与者表现和事实事件。
    /// </summary>
    public sealed class DialogueSession
    {
        #region 字段与属性
        private readonly string sessionId;
        // 表现引用只指向本会话最近一次播放的对象，推进或结束时立即释放。
        private IAnimationPlayer activeAnimationPlayer;
        private AudioSource activeVoiceAudioSource;

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

        /// <summary>会话结束后的事件。</summary>
        public event Action<DialogueEndedEvent> Ended;
        #endregion

        #region 节点进入
        /// <summary>
        /// 进入指定 SpeechNode，播放其语音、固定 Action 层动画并发布展示事件。
        /// </summary>
        /// <param name="speech">待进入的 SpeechNode。</param>
        internal void EnterSpeech(DialogueSpeechNode speech)
        {
            if (speech == null) throw new ArgumentNullException(nameof(speech));
            if (IsEnded) return;

            StopActivePresentation();
            CurrentSpeech = speech;
            State = speech.Choices.Count > 0
                ? DialogueSessionState.WaitingForChoice
                : DialogueSessionState.Running;

            // 放音频
            IDialogueParticipantContext participant = Request.FindParticipant(speech.Speaker);
            if (speech.Speaker == null)
            {
                // Speaker 资产是运行时身份契约；即使没有语音或动画，文本仍可继续展示，但必须暴露配置错误。
                Debug.LogError($"Dialogue SpeechNode '{speech.NodeId}' 未配置 Speaker 资产。",
                    Request.Target?.InteractionObject);
            }
            if (speech.VoiceClip != null)
            {
                if (participant?.VoiceAudioSource == null)
                {
                    Debug.LogError(
                        $"Dialogue SpeechNode '{speech.NodeId}' 配置了语音，但 Speaker '{speech.Speaker?.SpeakerName ?? "<empty>"}' 没有 AudioSource。",
                        participant?.ParticipantObject);
                }
                else
                {
                    activeVoiceAudioSource = participant.VoiceAudioSource;
                    activeVoiceAudioSource.clip = speech.VoiceClip;
                    activeVoiceAudioSource.Play();
                }
            }

            // 放动画
            if (speech.AnimationClip != null && participant?.AnimationPlayer != null)
            {
                // 对话动画固定进入全身 Action 层；新对白先停止本会话上一段动作，避免层内残留。
                activeAnimationPlayer = participant.AnimationPlayer;
                activeAnimationPlayer.Play(
                    AnimationLayerType.Action,
                    speech.AnimationClip,
                    speech.AnimationFadeDuration);
            }
            else if (speech.AnimationClip != null)
            {
                Debug.LogError(
                    $"Dialogue SpeechNode '{speech.NodeId}' 配置了动画，但 Speaker '{speech.Speaker?.SpeakerName ?? "<empty>"}' 没有 IAnimationPlayer。",
                    participant?.ParticipantObject);
            }

            // DialogueSystem 在收到该事实后计算 Condition 快照并发布 ChoicePresented，Session 不直接执行命令。
            SpeechPresented?.Invoke(new DialogueSpeechPresentedEvent(this, speech));
        }
        #endregion

        #region 结束与资源清理
        /// <summary>
        /// 关闭会话并清理本会话启动的语音和动画。
        /// </summary>
        /// <param name="message">结束补充说明。</param>
        internal void End(string message, DialogueEndStatus status = DialogueEndStatus.Failed)
        {
            if (IsEnded) return;

            StopActivePresentation();
            EndStatus = status;
            State = DialogueSessionState.Ended;
            Ended?.Invoke(new DialogueEndedEvent(this, message));
        }

        /// <summary>获取本次会话已经确定的结束原因。</summary>
        public DialogueEndStatus EndStatus { get; private set; } = DialogueEndStatus.Failed;

        /// <summary>
        /// 停止当前会话启动的语音和 Action 层动画，并清除表现引用。
        /// </summary>
        private void StopActivePresentation()
        {
            if (activeVoiceAudioSource != null)
            {
                activeVoiceAudioSource.Stop();
                activeVoiceAudioSource = null;
            }

            if (activeAnimationPlayer == null) return;
            activeAnimationPlayer.StopLayer(AnimationLayerType.Action);
            activeAnimationPlayer = null;
        }
        #endregion
    }
}
