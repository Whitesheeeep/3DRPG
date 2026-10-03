using System;
using RPG.DialogueSystemModule;
using UnityEngine;
using WS_Modules.CustomEventSystem;

namespace RPG.TaskSystemNS
{
    /// <summary>
    /// 在所属阶段监听全局对话结束事实，并仅累计匹配资产的正常完成事件。
    /// </summary>
    public sealed class TaskDialogueCompletedObjectiveRuntime : ITaskObjectiveRuntime
    {
        #region 依赖字段

        // 依赖字段：仅目标资源引用匹配且会话进入正常结束节点时写入当前阶段进度。
        private readonly DialogueAsset dialogueAsset;
        private readonly ITaskObjectiveRuntimeContext context;

        #endregion

        #region 监听状态

        private IUnRegister dialogueEndedUnregister;

        #endregion

        #region 构造与监听生命周期

        /// <summary>
        /// 创建暂不监听的对话目标运行时；构造阶段不会补记历史对话。
        /// </summary>
        /// <param name="dialogueAsset">目标要求完成的对话资源。</param>
        /// <param name="context">所属目标的受限进度上下文。</param>
        /// <exception cref="ArgumentNullException">资源或上下文为空时抛出。</exception>
        public TaskDialogueCompletedObjectiveRuntime(
            DialogueAsset dialogueAsset,
            ITaskObjectiveRuntimeContext context)
        {
            this.dialogueAsset = dialogueAsset != null
                ? dialogueAsset
                : throw new ArgumentNullException(nameof(dialogueAsset));
            this.context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>
        /// 订阅对话结束事实；重复调用不会创建重复订阅。
        /// </summary>
        /// <exception cref="Exception">EventSystem 拒绝注册监听时继续传播。</exception>
        public void StartListening()
        {
            if (dialogueEndedUnregister != null)
            {
                return;
            }

            try
            {
                dialogueEndedUnregister = EventSystem.Register_Type<DialogueEndedEvent>(
                    typeof(DialogueEndedEvent),
                    OnDialogueEnded);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                throw;
            }
            Debug.Log(
                $"[TaskDialogueCompletedObjectiveRuntime] 已开始监听对话完成事件，taskId={context.TaskId}, " +
                $"objectiveId={context.ObjectiveId}, dialogueId={dialogueAsset.DialogueId}。");
        }

        /// <summary>
        /// 释放对话结束订阅；重复调用安全。
        /// </summary>
        /// <exception cref="Exception">EventSystem 注销监听失败时保留句柄并继续传播。</exception>
        public void StopListening()
        {
            if (dialogueEndedUnregister == null)
            {
                return;
            }

            try
            {
                dialogueEndedUnregister.UnRegister();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                throw;
            }
            dialogueEndedUnregister = null;
            Debug.Log(
                $"[TaskDialogueCompletedObjectiveRuntime] 已停止监听对话完成事件，taskId={context.TaskId}, " +
                $"objectiveId={context.ObjectiveId}, dialogueId={dialogueAsset.DialogueId}。");
        }

        #endregion

        #region 结束事件处理

        /// <summary>
        /// 仅将匹配资产且正常结束的会话计入当前阶段。
        /// </summary>
        /// <param name="eventArgs">对话系统发布的结束事实。</param>
        /// <exception cref="Exception">任务进度上下文拒绝写入时继续传播。</exception>
        private void OnDialogueEnded(DialogueEndedEvent eventArgs)
        {
            if (eventArgs.Status != DialogueEndStatus.Completed ||
                eventArgs.Session.Request.Asset != dialogueAsset)
            {
                return;
            }

            context.AddProgress(1);
        }

        #endregion
    }
}
