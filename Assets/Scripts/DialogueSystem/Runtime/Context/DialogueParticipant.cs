using UnityEngine;
using RPG.Character.Animation;

namespace RPG.DialogueSystemModule
{
    /// <summary>
    /// 将场景对象配置为可参与 DialogueRequest 的通用对话参与者。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DialogueParticipant : MonoBehaviour, IDialogueParticipantContext
    {
        #region 序列化配置

        // 身份只保存稳定 SpeakerId；运行时通过 PropertyDrawer 从项目设置中选择。
        [SerializeField, SpeakerId]
        private string speakerId = string.Empty;
        // 语音和动画引用属于参与者表现，不携带 ASC 或业务 Manager 依赖。
        [SerializeField]
        private AudioSource voiceAudioSource;
        [SerializeField]
        private IAnimationPlayer animationPlayerComponent;

        #endregion

        #region Context 属性

        /// <inheritdoc />
        public string SpeakerId => speakerId;

        /// <inheritdoc />
        public GameObject ParticipantObject => gameObject;

        /// <inheritdoc />
        public AudioSource VoiceAudioSource => voiceAudioSource;

        /// <inheritdoc />
        public IAnimationPlayer AnimationPlayer => animationPlayerComponent as IAnimationPlayer;

        #endregion

        #region Unity 生命周期

        /// <summary>
        /// 在未配置引用时从同一对象获取 AudioSource 和动画接口，保持场景组件使用简单。
        /// </summary>
        private void Reset()
        {
            voiceAudioSource = GetComponent<AudioSource>();
            animationPlayerComponent = GetComponent<IAnimationPlayer>();
        }

        #endregion
    }
}
