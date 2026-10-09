using System;
using System.Collections.Generic;
using RPG.DialogueSystemModule;
using RPG.Game;
using RPG.NPC;
using UnityEngine;
using WS_Modules.CustomEventSystem;

namespace RPG.TaskSystemNS
{
    /// <summary>
    /// 在所属阶段监听指定对话结束或 SpeechNode 展示事实，并只累计匹配配置的事件。
    /// </summary>
    public sealed class TaskDialogueCompletedObjectiveRuntime : ITaskObjectiveRuntime, ITaskObjectiveNavigationProvider
    {
        #region 依赖字段

        // 依赖字段：匹配对话完成事实、写入所属目标进度；仅导航目标在监听期间缓存 NPCManager。
        private readonly DialogueAsset dialogueAsset;
        private readonly NPCId npcId;
        private readonly DialogueSpeechNode speechNode;
        private readonly ITaskObjectiveRuntimeContext context;
        private readonly HashSet<string> countedSessionIds = new HashSet<string>();
        private NPCManager npcManager;
        private DialogueSystem dialogueSystem;

        #endregion

        #region 监听状态

        private IUnRegister dialogueEndedUnregister;
        private bool speechPresentedSubscribed;

        /// <summary>获取定义是否指定了 NPC 导航目标。</summary>
        public bool HasNavigationTarget => npcId.IsValid;

        #endregion

        #region 构造与监听生命周期

        /// <summary>
        /// 创建暂不监听的对话目标运行时；构造阶段不会补记历史对话。
        /// </summary>
        /// <param name="dialogueAsset">目标要求完成的对话资源。</param>
        /// <param name="npcId">该目标可选的场景 NPC 身份。</param>
        /// <param name="speechNode">可选的指定对白节点；为空时监听正常结束。</param>
        /// <param name="context">所属目标的受限进度上下文。</param>
        /// <exception cref="ArgumentNullException">资源或上下文为空时抛出。</exception>
        public TaskDialogueCompletedObjectiveRuntime(
            DialogueAsset dialogueAsset,
            NPCId npcId,
            DialogueSpeechNode speechNode,
            ITaskObjectiveRuntimeContext context)
        {
            this.dialogueAsset = dialogueAsset != null
                ? dialogueAsset
                : throw new ArgumentNullException(nameof(dialogueAsset));
            this.npcId = npcId;
            this.speechNode = speechNode;
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
        /// 按配置订阅正常结束或指定 SpeechNode 事实；重复调用不会创建重复订阅。
        /// </summary>
        /// <exception cref="InvalidOperationException">已配置导航但架构未注册 NPCManager 时抛出。</exception>
        /// <exception cref="Exception">架构、C# 事件或 EventSystem 订阅失败时继续传播。</exception>
        public void StartListening()
        {
            if (dialogueEndedUnregister != null || speechPresentedSubscribed)
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

                if (speechNode != null)
                {
                    // 指定节点是即时事实，直接监听 DialogueSystem；不等待整场对话结束或 UI 表现完成。
                    dialogueSystem = GameArchitecture.Interface.GetSystem<DialogueSystem>();
                    dialogueSystem.SpeechPresented += OnSpeechPresented;
                    speechPresentedSubscribed = true;
                }
                else
                {
                    dialogueEndedUnregister = EventSystem.Register_Type<DialogueEndedEvent>(
                        typeof(DialogueEndedEvent),
                        OnDialogueEnded);
                }
            }
            catch (Exception exception)
            {
                npcManager = null;
                dialogueSystem = null;
                speechPresentedSubscribed = false;
                Debug.LogException(exception);
                throw;
            }
            Debug.Log(
                $"[TaskDialogueCompletedObjectiveRuntime] 已开始监听对话完成事件，taskId={context.TaskId}, " +
                $"objectiveId={context.ObjectiveId}, dialogueId={dialogueAsset.DialogueId}, " +
                $"navigationConfigured={npcId.IsValid}, trigger={(speechNode == null ? "DialogueEnded" : speechNode.NodeId)}。");
        }

        /// <summary>
        /// 释放对话事实订阅；重复调用安全。
        /// </summary>
        /// <exception cref="Exception">EventSystem 注销监听失败时保留句柄并继续传播。</exception>
        public void StopListening()
        {
            if (dialogueEndedUnregister == null && !speechPresentedSubscribed)
            {
                npcManager = null;
                return;
            }

            if (speechPresentedSubscribed)
            {
                dialogueSystem.SpeechPresented -= OnSpeechPresented;
                speechPresentedSubscribed = false;
                dialogueSystem = null;
            }

            if (dialogueEndedUnregister != null)
            {
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
            }

            npcManager = null;
            countedSessionIds.Clear();
            Debug.Log(
                $"[TaskDialogueCompletedObjectiveRuntime] 已停止监听对话完成事件，taskId={context.TaskId}, " +
                $"objectiveId={context.ObjectiveId}, dialogueId={dialogueAsset.DialogueId}, " +
                $"trigger={(speechNode == null ? "DialogueEnded" : speechNode.NodeId)}。");
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
            Debug.Log(
                $"[TaskDialogueCompletedObjectiveRuntime] 对话正常完成并计入任务，taskId={context.TaskId}, " +
                $"objectiveId={context.ObjectiveId}, dialogueId={dialogueAsset.DialogueId}。");
        }

        /// <summary>只在目标 DialogueAsset 的指定 SpeechNode 展示时累计一次会话进度。</summary>
        /// <param name="eventArgs">DialogueSystem 发布的节点展示事实。</param>
        private void OnSpeechPresented(DialogueSpeechPresentedEvent eventArgs)
        {
            if (eventArgs.Session.Request.Asset != dialogueAsset ||
                !ReferenceEquals(eventArgs.Speech, speechNode) ||
                !countedSessionIds.Add(eventArgs.Session.SessionId))
                return;

            context.AddProgress(1);
            Debug.Log(
                $"[TaskDialogueCompletedObjectiveRuntime] 指定对白节点已展示并计入任务，taskId={context.TaskId}, " +
                $"objectiveId={context.ObjectiveId}, dialogueId={dialogueAsset.DialogueId}, " +
                $"nodeId={speechNode.NodeId}, sessionId={eventArgs.Session.SessionId}。");
        }

        #endregion
    }
}
