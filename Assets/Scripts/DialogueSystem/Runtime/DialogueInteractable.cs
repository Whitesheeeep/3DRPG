using System.Collections.Generic;
using RPG.Game;
using RPG.InteractionSystem;
using UnityEngine;

namespace RPG.DialogueSystem
{
    #region 对话交互适配

    /// <summary>
    /// 将 NPC 的 DialogueAsset 和 DialogueParticipant 适配为通用交互 Provider。
    /// </summary>
    public sealed class DialogueInteractable : MonoBehaviour, IInteractable
    {
        #region 序列化字段与状态

        [SerializeField] private DialogueAsset dialogueAsset;
        [SerializeField] private Transform participantRoot;
        [SerializeField] private MonoBehaviour dialogueSystemProvider;

        private InteractionOption dialogueOption;

        #endregion

        #region 属性

        /// <inheritdoc />
        public GameObject InteractionObject => gameObject;

        /// <inheritdoc />
        public Transform InteractionOrigin => participantRoot != null ? participantRoot : transform;

        /// <summary>获取配置的 DialogueAsset。</summary>
        public DialogueAsset DialogueAsset => dialogueAsset;

        /// <summary>设置用于组合根注入的对话系统 Provider。</summary>
        /// <param name="provider">提供 DialogueSystem 的组件。</param>
        public void SetDialogueSystemProvider(MonoBehaviour provider) => dialogueSystemProvider = provider;

        #endregion

        #region Unity 生命周期

        /// <summary>创建可缓存的 Dialogue Option；业务可用性在每次查询时动态判断。</summary>
        private void Awake()
        {
            dialogueOption = new InteractionOption(
                new InteractionOptionId(GetInstanceID(), "Dialogue"),
                "对话",
                gameObject,
                InteractionOrigin,
                0,
                0f,
                CanStartDialogue,
                TryStartDialogue);
        }

        #endregion

        #region Provider 契约

        /// <inheritdoc />
        public void CollectInteractionOptions(in InteractionQueryContext context, List<InteractionOption> results)
        {
            // Provider 只贡献缓存的命令对象，距离、视野和 CanExecute 由玩家交互层统一处理。
            results.Add(dialogueOption);
        }

        #endregion

        #region 对话命令

        /// <summary>判断当前对话配置和组合根是否允许启动对话。</summary>
        /// <param name="interactor">发起对话的玩家对象。</param>
        /// <returns>可以构造有效对话请求时返回 true。</returns>
        private bool CanStartDialogue(GameObject interactor)
        {
            return interactor != null && dialogueAsset != null &&
                   interactor.GetComponentInParent<DialogueParticipant>() != null &&
                   FindNpcParticipant() != null &&
                   dialogueSystemProvider is DialogueSystemProvider provider && provider.System != null;
        }

        /// <summary>构造并提交一次对话启动请求。</summary>
        /// <param name="interactor">发起对话的玩家对象。</param>
        /// <returns>对话系统成功创建会话时返回 true。</returns>
        private bool TryStartDialogue(GameObject interactor)
        {
            if (!CanStartDialogue(interactor)) return false;

            DialogueSystemProvider provider = (DialogueSystemProvider)dialogueSystemProvider;
            IDialogueParticipantContext initiator =
                interactor.GetComponentInParent<DialogueParticipant>();
            IDialogueParticipantContext participant =
                FindNpcParticipant();
            DialogueRequest request = new DialogueRequest(
                dialogueAsset,
                initiator,
                new[] { participant },
                this);
            return provider.System.TryStartDialogue(request).Succeeded;
        }

        /// <summary>按配置的参与者根节点查找 NPC Context，兼容交互体挂在 NPC 子节点的布局。</summary>
        /// <returns>找到的 NPC Participant；不存在时为空。</returns>
        private DialogueParticipant FindNpcParticipant()
        {
            if (participantRoot != null)
            {
                DialogueParticipant participant = participantRoot.GetComponent<DialogueParticipant>();
                if (participant != null) return participant;
                participant = participantRoot.GetComponentInParent<DialogueParticipant>();
                if (participant != null) return participant;
            }

            return GetComponentInParent<DialogueParticipant>();
        }

        #endregion
    }

    /// <summary>
    /// 从项目 GameArchitecture 获取对话系统的场景组合根。
    /// </summary>
    public sealed class DialogueSystemProvider : MonoBehaviour
    {
        #region 属性

        /// <summary>在 Provider 生命周期内持有的对话系统实例。</summary>
        public DialogueSystem System { get; private set; }

        #endregion

        #region Unity 生命周期

        /// <summary>确保项目架构已初始化，并绑定架构中唯一的 DialogueSystem。</summary>
        private void Awake()
        {
            if (GameArchitecture.Interface == null) GameArchitecture.InitArchitecture();
            System = GameArchitecture.Interface!.GetSystem<DialogueSystem>();
        }

        #endregion
    }

    #endregion
}
