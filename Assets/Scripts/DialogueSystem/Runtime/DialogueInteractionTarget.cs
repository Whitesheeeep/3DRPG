using RPG.Game;
using RPG.InteractionSystem;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.TAG;

namespace RPG.DialogueSystem
{
    #region 对话交互适配

    /// <summary>
    /// 将 NPC 的 DialogueAsset 和 SpeakerId 适配为通用交互目标。
    /// </summary>
    public sealed class DialogueInteractionTarget : MonoBehaviour, IInteractionTarget
    {
        #region 序列化字段

        [SerializeField] private DialogueAsset dialogueAsset;
        [SerializeField] private string speakerId = string.Empty;
        [SerializeField] private Transform participantRoot;
        [SerializeField] private MonoBehaviour dialogueSystemProvider;

        #endregion

        #region 属性

        /// <inheritdoc />
        public GameObject InteractionObject => gameObject;

        /// <inheritdoc />
        public Transform InteractionOrigin => participantRoot != null ? participantRoot : transform;

        /// <summary>获取 NPC 在当前对话中的 SpeakerId。</summary>
        public string SpeakerId => speakerId;

        /// <summary>获取配置的 DialogueAsset。</summary>
        public DialogueAsset DialogueAsset => dialogueAsset;

        /// <summary>设置用于组合根注入的 DialogueSystem Provider。</summary>
        /// <param name="provider">提供 DialogueSystem 的组件。</param>
        public void SetDialogueSystemProvider(MonoBehaviour provider) => dialogueSystemProvider = provider;

        #endregion

        #region 交互契约

        /// <inheritdoc />
        public bool CanInteract(GameObject interactor) =>
            interactor != null && dialogueAsset != null &&
            dialogueSystemProvider is DialogueSystemProvider provider && provider.System != null;

        /// <inheritdoc />
        public void Interact(GameObject interactor)
        {
            if (!CanInteract(interactor)) return;
            DialogueSystemProvider provider = (DialogueSystemProvider)dialogueSystemProvider;
            GameplayAbilitySystemComponent playerAsc =
                interactor.GetComponentInParent<GameplayAbilitySystemComponent>();
            DialogueParticipantBinding participant = new DialogueParticipantBinding(speakerId, gameObject);
            DialogueRequest request = new DialogueRequest(
                dialogueAsset,
                interactor,
                playerAsc,
                new[] { participant },
                this,
                provider.DialogueControlTag);
            provider.System.TryStartDialogue(request);
        }

        #endregion
    }

    /// <summary>
    /// 从项目 GameArchitecture 获取对话系统的场景组合根。
    /// </summary>
    public sealed class DialogueSystemProvider : MonoBehaviour
    {
        #region 配置

        [SerializeField] private GameplayTag dialogueControlTag;

        #endregion

        #region 属性

        /// <summary>在 Provider 生命周期内持有的对话系统实例。</summary>
        public DialogueSystem System { get; private set; }

        /// <summary>获取项目配置的对话控制 GameplayTag。</summary>
        public GameplayTag DialogueControlTag => dialogueControlTag;

        #endregion

        #region Unity 生命周期

        /// <summary>确保项目架构已初始化，并绑定架构中唯一的 DialogueSystem。</summary>
        private void Awake()
        {
            GameArchitecture.InitArchitecture();
            System = GameArchitecture.Interface.GetSystem<DialogueSystem>();
        }

        #endregion
    }

    #endregion
}
