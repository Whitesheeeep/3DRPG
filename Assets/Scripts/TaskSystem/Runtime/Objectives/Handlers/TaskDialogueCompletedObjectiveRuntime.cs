using System;
using RPG.DialogueSystemModule;
using RPG.Game;
using RPG.NPC;
using UnityEngine;
using WS_Modules.CustomEventSystem;

namespace RPG.TaskSystemNS
{
    /// <summary>
    /// 在所属阶段监听全局对话结束事实，并仅累计匹配资产的正常完成事件。
    /// </summary>
    public sealed class TaskDialogueCompletedObjectiveRuntime : ITaskObjectiveRuntime, ITaskObjectiveNavigationProvider
    {
        #region 依赖字段

        // 依赖字段：匹配对话完成事实、写入所属目标进度；仅导航目标在监听期间缓存 NPCManager。
        private readonly DialogueAsset dialogueAsset;
        private readonly NPCId npcId;
        private readonly ITaskObjectiveRuntimeContext context;
        private NPCManager npcManager;

        #endregion

        #region 监听状态

        private IUnRegister dialogueEndedUnregister;

        /// <summary>获取定义是否指定了 NPC 导航目标。</summary>
        public bool HasNavigationTarget => npcId.IsValid;

        #endregion

        #region 构造与监听生命周期

        /// <summary>
        /// 创建暂不监听的对话目标运行时；构造阶段不会补记历史对话。
        /// </summary>
        /// <param name="dialogueAsset">目标要求完成的对话资源。</param>
        /// <param name="npcId">该目标可选的场景 NPC 身份。</param>
        /// <param name="context">所属目标的受限进度上下文。</param>
        /// <exception cref="ArgumentNullException">资源或上下文为空时抛出。</exception>
        public TaskDialogueCompletedObjectiveRuntime(
            DialogueAsset dialogueAsset,
            NPCId npcId,
            ITaskObjectiveRuntimeContext context)
        {
            this.dialogueAsset = dialogueAsset != null
                ? dialogueAsset
                : throw new ArgumentNullException(nameof(dialogueAsset));
            this.npcId = npcId;
            this.context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>解析当前已注册 NPC 的锚点；NPC 尚未加载时保留导航意图并返回失败。</summary>
        /// <param name="target">查找到的场景导航锚点。</param>
        /// <param name="offset">导航锚点的偏移量。</param>
        /// <returns>目标配置了 NPC 且身份当前已注册时返回 true。</returns>
        public bool TryGetNavigationTarget(out Transform target, out Vector3 offset)
        {
            if (npcId.IsValid && npcManager != null && npcManager.TryGetNPC(npcId, out NPCIdentity identity))
            {
                target = identity.NavigationAnchor;
                offset = identity.NavigationOffset;
                return target != null;
            }

            offset = Vector3.zero;
            target = null;
            return false;
        }

        /// <summary>
        /// 订阅对话结束事实；重复调用不会创建重复订阅。
        /// </summary>
        /// <exception cref="InvalidOperationException">已配置导航但架构未注册 NPCManager 时抛出。</exception>
        /// <exception cref="Exception">EventSystem 拒绝注册监听时继续传播。</exception>
        public void StartListening()
        {
            if (dialogueEndedUnregister != null)
            {
                return;
            }

            try
            {
                if (npcId.IsValid)
                {
                    // 只在需要 NPC 导航的 Runtime 启动时获取一次，普通对话目标不依赖 NPC 子系统。
                    npcManager = GameArchitecture.Interface.GetManager<NPCManager>();
                    if (npcManager == null)
                    {
                        throw new InvalidOperationException(
                            $"[TaskDialogueCompletedObjectiveRuntime] NPCManager 未注册，无法启动导航目标；" +
                            $"taskId={context.TaskId}, objectiveId={context.ObjectiveId}, npcId={npcId}。");
                    }
                }

                dialogueEndedUnregister = EventSystem.Register_Type<DialogueEndedEvent>(
                    typeof(DialogueEndedEvent),
                    OnDialogueEnded);
            }
            catch (Exception exception)
            {
                npcManager = null;
                Debug.LogException(exception);
                throw;
            }
            Debug.Log(
                $"[TaskDialogueCompletedObjectiveRuntime] 已开始监听对话完成事件，taskId={context.TaskId}, " +
                $"objectiveId={context.ObjectiveId}, dialogueId={dialogueAsset.DialogueId}, " +
                $"navigationConfigured={npcId.IsValid}。");
        }

        /// <summary>
        /// 释放对话结束订阅；重复调用安全。
        /// </summary>
        /// <exception cref="Exception">EventSystem 注销监听失败时保留句柄并继续传播。</exception>
        public void StopListening()
        {
            if (dialogueEndedUnregister == null)
            {
                npcManager = null;
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
            npcManager = null;
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
