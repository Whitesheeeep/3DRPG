using System;
using System.Collections.Generic;
using RPG.Game;
using RPG.InteractionSystem;
using RPG.NPC;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.DialogueSystemModule
{
    #region 对话交互适配

    /// <summary>把 NPC 的多条对话配置适配为通用交互选项。</summary>
    [DisallowMultipleComponent]
    public sealed class DialogueInteractable : MonoBehaviour, IInteractable
    {
        #region 序列化配置

        [InfoBox("每个 NPCIdentity 层级只配置一个 DialogueInteractable；启动时会从 NPCIdentity 向下检查重复组件。请将组件放在交互碰撞体节点或其父节点。玩家和 NPC 的 DialogueParticipant 会从各自所在节点向父级查找；NPCIdentity 可显式指定，未指定时从 ParticipantRoot 或本组件父级查找。Toggle 选项必须配置有效 NPCIdentity。")]
        [SerializeField, ListDrawerSettings(ShowFoldout = true, DraggableItems = true)]
        private List<DialogueInteractionEntry> dialogueEntries = new List<DialogueInteractionEntry>();

        // 旧场景仍序列化此单资产字段；新列表非空时只读取列表配置。
        [SerializeField, HideInInspector]
        private DialogueAsset dialogueAsset;

        [SerializeField, LabelText("参与者根节点")]
        private Transform participantRoot;

        [SerializeField, LabelText("NPC 身份")]
        private NPCIdentity npcIdentity;

        #endregion

        #region 依赖字段与缓存

        // 依赖引用与缓存命令分组：场景组件不自行构建架构，也不在每次扫描时分配 Option。
        // 依赖字段：对话命令由 GameArchitecture 中的 DialogueSystem 执行。
        private DialogueSystem dialogueSystem;

        // 依赖字段：Toggle 完成状态由架构 Manager 持有并注册到 SaveManager。
        private DialogueInteractionManager dialogueInteractionManager;

        private readonly List<CachedDialogueOption> cachedDialogueOptions =
            new List<CachedDialogueOption>();
        private bool toggleIdentityValid = true;

        #endregion

        #region 属性

        /// <inheritdoc />
        public GameObject InteractionObject => gameObject;

        /// <inheritdoc />
        public Transform InteractionOrigin => participantRoot != null ? participantRoot : transform;

        /// <summary>获取旧版单资产字段，仅用于兼容尚未迁移的场景。</summary>
        public DialogueAsset DialogueAsset => dialogueAsset;

        /// <summary>获取有序多对话配置的只读接口。</summary>
        public IReadOnlyList<DialogueInteractionEntry> DialogueEntries => dialogueEntries;

        #endregion

        #region Unity 生命周期

        /// <summary>取得架构依赖、校验稳定身份并构建缓存交互命令。</summary>
        private void Awake()
        {
            // GameArchitectureStartup 先于普通场景组件启动，场景 Provider 只读取统一架构依赖。
            dialogueSystem = GameArchitecture.Interface.GetSystem<DialogueSystem>();
            dialogueInteractionManager = GameArchitecture.Interface.GetManager<DialogueInteractionManager>();
            npcIdentity = npcIdentity != null ? npcIdentity : FindNpcIdentity();
            ValidateSingleNpcProvider();
            ValidateEntryConfiguration();
            ValidateToggleIdentity();
            BuildCachedOptions();
        }

        /// <summary>在 Inspector 配置变化时补齐缺失 ID，并修复复制列表元素产生的重复身份。</summary>
        private void OnValidate()
        {
            if (dialogueEntries == null)
                dialogueEntries = new List<DialogueInteractionEntry>();

            var optionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (DialogueInteractionEntry entry in dialogueEntries)
            {
                if (entry == null)
                    continue;

                entry.EnsureOptionId();
                if (!optionIds.Add(entry.OptionId))
                {
                    entry.RegenerateOptionId();
                    optionIds.Add(entry.OptionId);
                }
            }
        }

        #endregion

        #region Provider 契约

        /// <inheritdoc />
        public void CollectInteractionOptions(in InteractionQueryContext context, List<InteractionOption> results)
        {
            // 完成记录只过滤对应 Toggle；其他配置项和重复对话维持 Inspector 顺序。
            foreach (CachedDialogueOption cachedOption in cachedDialogueOptions)
            {
                DialogueInteractionEntry entry = cachedOption.Entry;
                if (entry != null && entry.InteractionType == E_DialogueInteractionType.Toggle &&
                    toggleIdentityValid && dialogueInteractionManager.IsCompleted(npcIdentity.Id, entry.OptionId))
                {
                    continue;
                }

                results.Add(cachedOption.Option);
            }
        }

        #endregion

        #region 对话命令

        /// <summary>确保一个 NPC 身份层级只由一个组件贡献对话选项。</summary>
        /// <exception cref="InvalidOperationException">同一 NPC 层级存在另一个 DialogueInteractable 时抛出。</exception>
        private void ValidateSingleNpcProvider()
        {
            if (npcIdentity == null)
                return;

            // 只扫描本 NPC 的 Transform 子树，并逐个核对身份，避免重复 Option 列表进入交互检测。
            DialogueInteractable[] providers =
                npcIdentity.GetComponentsInChildren<DialogueInteractable>(true);
            foreach (DialogueInteractable provider in providers)
            {
                if (provider == this)
                    continue;

                NPCIdentity providerIdentity =
                    provider.npcIdentity != null ? provider.npcIdentity : provider.FindNpcIdentity();
                if (providerIdentity != npcIdentity)
                    continue;

                string message =
                    $"[DialogueInteractable] NPCId={npcIdentity.Id} 的层级中存在多个 DialogueInteractable；请保留一个组件并集中配置选项。";
                Debug.LogError(message, this);
                throw new InvalidOperationException(message);
            }
        }

        /// <summary>按条目检查资产、参与者、架构和 Toggle 完成状态。</summary>
        /// <param name="interactor">发起对话的玩家对象。</param>
        /// <param name="entry">多选项配置；旧版单资产选项传入 null。</param>
        /// <returns>请求可以被构造并提交时返回 true。</returns>
        private bool CanStartDialogue(GameObject interactor, DialogueInteractionEntry entry)
        {
            DialogueAsset asset = entry != null ? entry.DialogueAsset : dialogueAsset;
            if (interactor == null || asset == null || dialogueSystem == null ||
                interactor.GetComponentInParent<DialogueParticipant>() == null ||
                FindNpcParticipant() == null)
            {
                return false;
            }

            if (entry == null || entry.InteractionType != E_DialogueInteractionType.Toggle)
                return true;

            return toggleIdentityValid && dialogueInteractionManager != null &&
                   !dialogueInteractionManager.IsCompleted(npcIdentity.Id, entry.OptionId);
        }

        /// <summary>为指定配置构造请求并在成功启动后注册 Toggle 完成跟踪。</summary>
        /// <param name="interactor">发起对话的玩家对象。</param>
        /// <param name="entry">多选项配置；旧版单资产选项传入 null。</param>
        /// <returns>对话成功启动时返回 true。</returns>
        private bool TryStartDialogue(GameObject interactor, DialogueInteractionEntry entry)
        {
            if (!CanStartDialogue(interactor, entry))
                return false;

            IDialogueParticipantContext initiator =
                interactor.GetComponentInParent<DialogueParticipant>();
            IDialogueParticipantContext participant = FindNpcParticipant();
            DialogueAsset asset = entry != null ? entry.DialogueAsset : dialogueAsset;
            var request = new DialogueRequest(asset, initiator, new[] { participant }, this);
            DialogueStartResult result = dialogueSystem.TryStartDialogue(request);
            if (!result.Succeeded)
            {
                Debug.LogWarning(
                    $"[DialogueInteractable] 对话选项启动失败，name={entry?.DisplayName ?? "对话"}, status={result.Status}, message={result.Message}。",
                    this);
                return false;
            }

            if (entry != null && entry.InteractionType == E_DialogueInteractionType.Toggle)
            {
                // 只把实际启动成功的 Session 交给 Manager，初始化失败和运行失败都保留重试机会。
                dialogueInteractionManager.TrackToggleSession(npcIdentity.Id, entry.OptionId, result.Session);
            }

            return true;
        }

        /// <summary>创建当前 Inspector 配置对应的缓存 InteractionOption。</summary>
        private void BuildCachedOptions()
        {
            cachedDialogueOptions.Clear();
            if (dialogueEntries.Count > 0)
            {
                foreach (DialogueInteractionEntry entry in dialogueEntries)
                {
                    string optionId = entry.OptionId;
                    var option = new InteractionOption(
                        new InteractionOptionId(GetInstanceID(), optionId),
                        entry.DisplayName,
                        gameObject,
                        InteractionOrigin,
                        0,
                        0f,
                        interactor => CanStartDialogue(interactor, entry),
                        interactor => TryStartDialogue(interactor, entry));
                    cachedDialogueOptions.Add(new CachedDialogueOption(entry, option));
                }

                return;
            }

            if (dialogueAsset == null)
                return;

            var legacyOption = new InteractionOption(
                new InteractionOptionId(GetInstanceID(), "Dialogue"),
                "对话",
                gameObject,
                InteractionOrigin,
                0,
                0f,
                interactor => CanStartDialogue(interactor, null),
                interactor => TryStartDialogue(interactor, null));
            cachedDialogueOptions.Add(new CachedDialogueOption(null, legacyOption));
        }

        /// <summary>确认列表中没有 null、非法类型或重复 OptionId。</summary>
        /// <exception cref="InvalidOperationException">运行时配置结构损坏时抛出。</exception>
        private void ValidateEntryConfiguration()
        {
            var optionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (DialogueInteractionEntry entry in dialogueEntries)
            {
                if (entry == null || !entry.HasValidOptionId() || !optionIds.Add(entry.OptionId))
                {
                    throw new InvalidOperationException(
                        $"[DialogueInteractable] '{name}' 的对话列表包含空项、非法 OptionId 或重复 OptionId；请检查 Inspector 配置。");
                }

                if (!Enum.IsDefined(typeof(E_DialogueInteractionType), entry.InteractionType))
                    throw new InvalidOperationException(
                        $"[DialogueInteractable] '{name}' 的 OptionId={entry.OptionId} 配置了未知交互类型。");
            }
        }

        /// <summary>检查 Toggle 是否具有可用于跨场景存档的稳定 NPCId。</summary>
        private void ValidateToggleIdentity()
        {
            foreach (DialogueInteractionEntry entry in dialogueEntries)
            {
                if (entry.InteractionType != E_DialogueInteractionType.Toggle)
                    continue;

                if (npcIdentity != null && npcIdentity.NPCIdIsValid)
                    return;

                toggleIdentityValid = false;
                Debug.LogError(
                    $"[DialogueInteractable] '{name}' 配置了 Toggle 对话，但未找到具有有效 NPCId 的 NPCIdentity；Toggle 选项将不可用。",
                    this);
                return;
            }
        }

        /// <summary>优先从参与者根节点查找 NPCIdentity，再从交互组件父级查找。</summary>
        /// <returns>找到的 NPC 身份组件；未配置时返回 null。</returns>
        private NPCIdentity FindNpcIdentity()
        {
            if (participantRoot != null)
            {
                NPCIdentity identity = participantRoot.GetComponentInParent<NPCIdentity>();
                if (identity != null)
                    return identity;
            }

            return GetComponentInParent<NPCIdentity>();
        }

        /// <summary>按配置的参与者根节点查找 NPC Context，兼容交互体挂在 NPC 子节点的布局。</summary>
        /// <returns>找到的 NPC Participant；不存在时为空。</returns>
        private DialogueParticipant FindNpcParticipant()
        {
            if (participantRoot != null)
            {
                DialogueParticipant participant = participantRoot.GetComponent<DialogueParticipant>();
                if (participant != null)
                    return participant;
                participant = participantRoot.GetComponentInParent<DialogueParticipant>();
                if (participant != null)
                    return participant;
            }

            return GetComponentInParent<DialogueParticipant>();
        }

        #endregion

        #region 缓存类型

        /// <summary>保持配置条目与缓存交互命令的一对一关系。</summary>
        private sealed class CachedDialogueOption
        {
            /// <summary>创建缓存记录。</summary>
            /// <param name="entry">有序配置项；旧字段兼容项为空。</param>
            /// <param name="option">可由 PlayerInteractor 重复查询的交互命令。</param>
            public CachedDialogueOption(DialogueInteractionEntry entry, InteractionOption option)
            {
                Entry = entry;
                Option = option;
            }

            /// <summary>获取原始有序配置项。</summary>
            public DialogueInteractionEntry Entry { get; }

            /// <summary>获取缓存交互命令。</summary>
            public InteractionOption Option { get; }
        }

        #endregion
    }

    #endregion
}
