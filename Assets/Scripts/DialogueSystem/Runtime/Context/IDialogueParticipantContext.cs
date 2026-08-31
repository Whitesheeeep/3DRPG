using RPG.Character.Animation;
using UnityEngine;

namespace RPG.DialogueSystemModule
{
    /// <summary>
    /// 提供一个对话参与者的身份、语音和动画表现能力，不包含业务控制依赖。
    /// </summary>
    public interface IDialogueParticipantContext
    {
        /// <summary>获取与 SpeechNode 匹配的 Speaker 资产身份。</summary>
        DialogueSpeaker Speaker { get; }

        /// <summary>获取参与者所属的场景对象，用于事件目标和日志。</summary>
        GameObject ParticipantObject { get; }

        /// <summary>获取该参与者专用的语音 AudioSource。</summary>
        AudioSource VoiceAudioSource { get; }

        /// <summary>获取该参与者的动画播放接口。</summary>
        IAnimationPlayer AnimationPlayer { get; }
    }
}
