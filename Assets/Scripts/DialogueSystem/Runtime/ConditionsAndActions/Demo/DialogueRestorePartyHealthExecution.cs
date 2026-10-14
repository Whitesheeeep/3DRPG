using System;
using RPG.Character;
using UnityEngine;

namespace RPG.DialogueSystemModule
{
    /// <summary>从对话发起玩家取得当前队伍，并恢复场上与后台角色的生命值。</summary>
    [Serializable]
    public sealed class DialogueRestorePartyHealthExecution : DialogueAction
    {
        #region 构造

        /// <summary>创建供 SerializeReference 和对话命令选择器使用的队伍恢复命令。</summary>
        public DialogueRestorePartyHealthExecution()
        {
        }

        #endregion

        #region 执行

        /// <summary>经 Initiator 的场景对象找到 PlayerController，并调用队伍生命恢复接口。</summary>
        /// <param name="context">当前对话会话及 Choice 执行上下文。</param>
        /// <exception cref="InvalidOperationException">发起者未挂接玩家或角色队伍尚未就绪时抛出。</exception>
        public override void Execute(DialogueCommandContext context)
        {
            IDialogueParticipantContext initiator = context.Request.Initiator;
            GameObject participantObject = initiator?.ParticipantObject;
            if (participantObject == null)
            {
                throw new InvalidOperationException(
                    $"[DialogueRestorePartyHealthExecution] 对话发起者缺少 ParticipantObject，sessionId={context.Session.SessionId}, " +
                    $"choiceNodeId={context.Choice.NodeId}。");
            }

            // Initiator Participant 所在对象应处于 PlayerController 子层级，不缓存场景运行时对象。
            PlayerController playerController = participantObject.GetComponentInParent<PlayerController>();
            if (playerController == null || playerController.CharacterManager == null)
            {
                throw new InvalidOperationException(
                    $"[DialogueRestorePartyHealthExecution] 对话发起者层级中找不到配置完整的 PlayerController，" +
                    $"participant={participantObject.name}, sessionId={context.Session.SessionId}, " +
                    $"choiceNodeId={context.Choice.NodeId}。");
            }

            CharacterManager characterManager = playerController.CharacterManager;
            characterManager.RestorePartyToFullHealth();
            Debug.Log(
                $"[DialogueRestorePartyHealthExecution] 已恢复当前队伍生命值，partyCount={characterManager.CharacterActors.Count}, " +
                $"sessionId={context.Session.SessionId}, choiceNodeId={context.Choice.NodeId}。",
                playerController);
        }

        #endregion
    }
}
