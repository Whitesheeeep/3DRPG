using System;
using RPG.DialogueSystemModule;
using RPG.NPC;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.TaskSystemNS
{
    /// <summary>
    /// 配置玩家正常完成指定 DialogueAsset 后推进的任务目标。
    /// </summary>
    [Serializable]
    public sealed class TaskDialogueCompletedObjectiveDefinition : TaskObjectiveDefinition
    {
        #region 配置字段

        [SerializeField] private DialogueAsset dialogueAsset;
        [SerializeField, AssetsOnly, LabelText("导航 NPC")]
        private NPCIdentityDefinition npcIdentity;
        [SerializeField, AssetsOnly, LabelText("指定对白节点")]
        private DialogueSpeechNode speechNode;

        #endregion

        #region 构造

        /// <summary>
        /// 创建供 Unity SerializeReference 反序列化使用的目标定义。
        /// </summary>
        public TaskDialogueCompletedObjectiveDefinition()
        {
        }

        /// <summary>
        /// 创建引用指定对话图的目标定义。
        /// </summary>
        /// <param name="objectiveId">所属阶段内唯一的目标标识。</param>
        /// <param name="dialogueAsset">需要正常完成的对话资源。</param>
        /// <param name="required">需要完成对话的次数，默认一次。</param>
        /// <param name="npcIdentity">可选的场景 NPC 导航身份。</param>
        /// <param name="speechNode">可选的指定对白节点；配置后进入节点即计数。</param>
        /// <exception cref="ArgumentException">目标标识非法时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException">需求数量不是正数时抛出。</exception>
        public TaskDialogueCompletedObjectiveDefinition(
            string objectiveId,
            DialogueAsset dialogueAsset,
            int required = 1,
            NPCIdentityDefinition npcIdentity = null,
            DialogueSpeechNode speechNode = null)
            : base(objectiveId, required)
        {
            this.dialogueAsset = dialogueAsset;
            this.npcIdentity = npcIdentity;
            this.speechNode = speechNode;
        }

        #endregion

        #region 属性与校验

        /// <summary>
        /// 获取目标要求完成的 DialogueAsset。
        /// </summary>
        public DialogueAsset DialogueAsset => dialogueAsset;

        /// <summary>获取可选的对话节点目标；为空时仍按正常结束事件计数。</summary>
        public DialogueSpeechNode SpeechNode => speechNode;

        /// <summary>获取该目标可选导航身份解析出的 NPCId；未配置时返回无效 ID。</summary>
        public NPCId NPCId => npcIdentity != null ? npcIdentity.Id : default;

        /// <summary>由对话目标配置创建独立事件监听和导航查询 Runtime。</summary>
        /// <param name="context">当前任务实例受限的进度上下文。</param>
        /// <returns>持有本目标资源与进度上下文的对话 Runtime。</returns>
        /// <exception cref="ArgumentNullException">上下文或 DialogueAsset 为空时抛出。</exception>
        public override ITaskObjectiveRuntime CreateRuntime(ITaskObjectiveRuntimeContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            return new TaskDialogueCompletedObjectiveRuntime(
                dialogueAsset,
                NPCId,
                speechNode,
                context);
        }

        /// <summary>
        /// 校验目标基础字段和对话资源引用。
        /// </summary>
        /// <exception cref="ArgumentException">目标基础字段或对话资源未配置时抛出。</exception>
        public override void Validate()
        {
            base.Validate();
            npcIdentity?.Validate();
            if (dialogueAsset == null)
            {
                const string message = "对话完成目标必须配置 DialogueAsset。";
                Debug.LogError($"[TaskDialogueCompletedObjectiveDefinition] {message}");
                throw new ArgumentException(message, nameof(dialogueAsset));
            }

            if (speechNode == null)
                return;

            for (int index = 0; index < dialogueAsset.Nodes.Count; index++)
            {
                if (ReferenceEquals(dialogueAsset.Nodes[index], speechNode))
                    return;
            }

            string nodeName = string.IsNullOrWhiteSpace(speechNode.NodeName)
                ? speechNode.NodeId
                : speechNode.NodeName;
            string nodeMessage = $"指定对白节点不属于目标 DialogueAsset；dialogueId={dialogueAsset.DialogueId}, node={nodeName}。";
            Debug.LogError($"[TaskDialogueCompletedObjectiveDefinition] {nodeMessage}");
            throw new ArgumentException(nodeMessage, nameof(speechNode));
        }

        #endregion
    }
}
