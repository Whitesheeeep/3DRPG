using UnityEngine;
using RPG.Character.Animation;
using Sirenix.OdinInspector;

namespace RPG.DialogueSystemModule
{
    /// <summary>
    /// 将场景对象配置为可参与 DialogueRequest 的通用对话参与者。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DialogueParticipant : MonoBehaviour, IDialogueParticipantContext
    {
        #region 序列化配置
        [InfoBox("请将该组件放到DialogueInteractable的ParticipantRoot节点或者其父节点上，并配置参与者的Speaker资产、语音AudioSource和动画接口。" +
                 "如果是 Player 请放到 PlayerInteractor 的节点或者其父节点上")]
        // 身份直接保存 Speaker ScriptableObject 引用，节点与场景参与者使用同一对象匹配。
        [SerializeField]
        private DialogueSpeaker speaker;
        // 语音和动画引用属于参与者表现，不携带 ASC 或业务 Manager 依赖。
        [SerializeField]
        private AudioSource voiceAudioSource;

        private IAnimationPlayer animationPlayerComponent;

        #endregion

        #region Context 属性

        /// <inheritdoc />
        public DialogueSpeaker Speaker => speaker;

        /// <inheritdoc />
        public GameObject ParticipantObject => gameObject;

        /// <inheritdoc />
        public AudioSource VoiceAudioSource => voiceAudioSource;

        /// <inheritdoc />
        public IAnimationPlayer AnimationPlayer => animationPlayerComponent as IAnimationPlayer;

        #endregion

        #region 表现目标

        /// <summary>切换稳定 Player 参与者当前使用的角色动画播放器。</summary>
        /// <param name="animationPlayer">新 ActiveCharacter 的动画播放器；无动画角色可以为空。</param>
        public void SetAnimationPlayer(IAnimationPlayer animationPlayer) => animationPlayerComponent = animationPlayer;

        #endregion

        #region Unity 生命周期

        /// <summary>
        /// 在未配置引用时从同一对象获取 AudioSource 和动画接口，保持场景组件使用简单。
        /// </summary>
        private void Reset()
        {
            voiceAudioSource = GetComponent<AudioSource>();
            // Unity 的 GetComponent<T> 泛型约束要求 Component，接口能力通过同对象 MonoBehaviour 实例查找。
            MonoBehaviour[] components = GetComponents<MonoBehaviour>();
            for (int index = 0; index < components.Length; index++)
            {
                if (components[index] is IAnimationPlayer animationPlayer)
                {
                    animationPlayerComponent = animationPlayer;
                    break;
                }
            }
        }

        #endregion
    }
}
