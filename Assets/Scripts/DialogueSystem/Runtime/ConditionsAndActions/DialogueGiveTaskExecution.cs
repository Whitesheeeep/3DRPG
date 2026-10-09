using System;
using System.Linq;
using RPG.TaskSystemNS;
using UnityEngine;

namespace RPG.DialogueSystemModule
{
    /// <summary>
    /// 通过对话 Choice 请求 TaskSystem 接取一个任务。
    /// </summary>
    [Serializable]
    public sealed class DialogueGiveTaskExecution : DialogueAction
    {
        #region 序列化配置

        [SerializeField, TaskIdDropdown]
        private string taskId = string.Empty;

        #endregion

        #region 构造

        /// <summary>
        /// 创建供 Unity SerializeReference 和 Dialogue 命令 Drawer 使用的任务接取命令。
        /// </summary>
        public DialogueGiveTaskExecution()
        {
        }

        #endregion

        #region 配置校验

        /// <summary>
        /// 校验配置的任务标识格式。
        /// </summary>
        /// <exception cref="ArgumentException">任务标识为空或格式非法时抛出。</exception>
        public override void Validate()
        {
            _ = new TaskId(taskId);
        }

        #endregion

        #region 任务接取

        /// <summary>
        /// 请求 TaskSystem 接取任务，并将重复给予活动或已完成任务视为已满足。
        /// </summary>
        /// <param name="context">当前对话会话及其 Choice 执行上下文。</param>
        /// <exception cref="InvalidOperationException">任务接取被拒绝时抛出。</exception>
        public override void Execute(DialogueCommandContext context)
        {
            TaskId configuredTaskId = new TaskId(taskId);
            TaskSystem taskSystem = context.Architecture.GetSystem<TaskSystem>();
            TaskAcceptResult result = taskSystem.TryAcceptTask(
                configuredTaskId,
                E_TaskAcceptSource.Dialogue);

            if (result.Succeeded)
            {
                Debug.Log(
                    $"[DialogueGiveTaskExecution] 对话接取任务成功，taskId={configuredTaskId}, " +
                    $"sessionId={context.Session.SessionId}, choiceNodeId={context.Choice.NodeId}。");
                return;
            }

            if (result.Failure == TaskCommandFailure.AlreadyActive ||
                result.Failure == TaskCommandFailure.AlreadyCompleted)
            {
                Debug.Log(
                    $"[DialogueGiveTaskExecution] 任务已满足，跳过重复接取，taskId={configuredTaskId}, " +
                    $"availability={result.Availability.Status}, sessionId={context.Session.SessionId}, " +
                    $"choiceNodeId={context.Choice.NodeId}。");
                return;
            }

            string failureReasons = string.Join(
                "；",
                result.Availability.Reasons.Select(reason => reason.Message));
            throw new InvalidOperationException(
                $"[DialogueGiveTaskExecution] 对话任务接取失败，taskId={configuredTaskId}, " +
                $"failure={result.Failure}, availability={result.Availability.Status}, " +
                $"reasons={failureReasons}, sessionId={context.Session.SessionId}, " +
                $"choiceNodeId={context.Choice.NodeId}。");
        }

        #endregion
    }
}
