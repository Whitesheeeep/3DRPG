using System;
using RPG.Game;
using RPG.NPC;
using UnityEngine;
using WS_Modules.CustomEventSystem;

namespace RPG.TaskSystemNS
{
    /// <summary>在所属任务阶段监听目标 NPC 的击败事实，并提供当前 NPC 导航位置。</summary>
    public sealed class TaskNPCDefeatedObjectiveRuntime : ITaskObjectiveRuntime, ITaskObjectiveNavigationProvider
    {
        #region 依赖字段

        // 目标 ID 匹配死亡事实；上下文只允许推进当前任务阶段的当前目标。
        private readonly NPCId targetNpcId;
        private readonly ITaskObjectiveRuntimeContext context;
        private NPCManager npcManager;

        #endregion

        #region 监听生命周期

        private IUnRegister defeatedEventUnregister;

        /// <summary>获取该目标是否具有可导航的 NPC 身份。</summary>
        public bool HasNavigationTarget => targetNpcId.IsValid;

        /// <summary>创建尚未订阅全局事件的击败目标运行时。</summary>
        /// <param name="targetNpcId">需要击败的稳定 NPC 身份。</param>
        /// <param name="context">所属任务阶段受限的进度上下文。</param>
        public TaskNPCDefeatedObjectiveRuntime(NPCId targetNpcId, ITaskObjectiveRuntimeContext context)
        {
            this.targetNpcId = targetNpcId.IsValid
                ? targetNpcId
                : throw new ArgumentException("击败目标需要有效 NPCId。", nameof(targetNpcId));
            this.context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>在任务阶段激活时订阅击败事件并准备 NPC 导航查询。</summary>
        public void StartListening()
        {
            if (defeatedEventUnregister != null)
                return;

            npcManager = GameArchitecture.Interface.GetManager<NPCManager>();
            defeatedEventUnregister = EventSystem.Register_Type<NPCDefeatedEventArgs>(
                typeof(NPCDefeatedEventArgs),
                OnNPCDefeated);
            Debug.Log(
                $"[TaskNPCDefeatedObjectiveRuntime] 开始监听 NPC 击败，taskId={context.TaskId}, " +
                $"objectiveId={context.ObjectiveId}, npcId={targetNpcId}。");
        }

        /// <summary>取消事件订阅并释放对 NPCManager 的场景引用。</summary>
        public void StopListening()
        {
            if (defeatedEventUnregister == null)
            {
                npcManager = null;
                return;
            }

            defeatedEventUnregister.UnRegister();
            defeatedEventUnregister = null;
            npcManager = null;
            Debug.Log(
                $"[TaskNPCDefeatedObjectiveRuntime] 停止监听 NPC 击败，taskId={context.TaskId}, " +
                $"objectiveId={context.ObjectiveId}, npcId={targetNpcId}。");
        }

        #endregion

        #region 导航与事件处理

        /// <summary>解析当前 NPC 注册实例的明确导航锚点。</summary>
        /// <param name="target">NPC 的导航锚点。</param>
        /// <param name="offset">锚点偏移。</param>
        /// <returns>当前目标 NPC 已在场景注册且锚点有效时返回 true。</returns>
        public bool TryGetNavigationTarget(out Transform target, out Vector3 offset)
        {
            if (npcManager != null && npcManager.TryGetNPC(targetNpcId, out NPCIdentity identity))
            {
                target = identity.NavigationAnchor;
                offset = identity.NavigationOffset;
                return target != null;
            }

            target = null;
            offset = Vector3.zero;
            return false;
        }

        /// <summary>只累计当前监听期内匹配身份的击败事实，不补记历史。</summary>
        /// <param name="eventArgs">NPCController 在进入 Dead 时发布的事实。</param>
        private void OnNPCDefeated(NPCDefeatedEventArgs eventArgs)
        {
            if (eventArgs.NPCId != targetNpcId)
                return;

            context.AddProgress(1);
            Debug.Log(
                $"[TaskNPCDefeatedObjectiveRuntime] 目标 NPC 击败进度增加，taskId={context.TaskId}, " +
                $"objectiveId={context.ObjectiveId}, npcId={targetNpcId}。");
        }

        #endregion
    }
}
