using System;
using RPG.NPC;
using UnityEngine;
using Sirenix.OdinInspector;

namespace RPG.TaskSystemNS
{
    /// <summary>配置击败指定身份 NPC 后推进的事件型任务目标。</summary>
    [Serializable]
    public sealed class TaskNPCDefeatedObjectiveDefinition : TaskObjectiveDefinition
    {
        #region 配置字段

        [SerializeField, Required, AssetsOnly, LabelText("目标 NPC")]
        private NPCIdentityDefinition npcIdentity;

        #endregion

        #region 构造

        /// <summary>创建供 Unity SerializeReference 反序列化使用的空目标定义。</summary>
        public TaskNPCDefeatedObjectiveDefinition()
        {
        }

        /// <summary>创建指定 NPC 身份的击败目标。</summary>
        /// <param name="objectiveId">所属阶段唯一目标标识。</param>
        /// <param name="npcIdentity">需要击败的稳定 NPC 身份资产。</param>
        /// <param name="required">需要击败的次数，默认一次。</param>
        public TaskNPCDefeatedObjectiveDefinition(
            string objectiveId,
            NPCIdentityDefinition npcIdentity,
            int required = 1)
            : base(objectiveId, required)
        {
            this.npcIdentity = npcIdentity;
        }

        #endregion

        #region 属性与校验

        /// <summary>获取需要击败的 NPC 稳定身份资产。</summary>
        public NPCIdentityDefinition NPCIdentity => npcIdentity;

        /// <summary>根据配置创建订阅击败事实并提供 NPC 导航的独立运行时。</summary>
        /// <param name="context">当前任务阶段受限的进度上下文。</param>
        /// <returns>本目标专属运行时。</returns>
        /// <exception cref="ArgumentNullException">运行时上下文或身份资产为空时抛出。</exception>
        public override ITaskObjectiveRuntime CreateRuntime(ITaskObjectiveRuntimeContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (npcIdentity == null)
                throw new InvalidOperationException("击败 NPC 目标未配置 NPCIdentityDefinition。");

            return new TaskNPCDefeatedObjectiveRuntime(npcIdentity.Id, context);
        }

        /// <summary>校验目标基础字段及稳定 NPC 身份配置。</summary>
        public override void Validate()
        {
            base.Validate();
            if (npcIdentity == null)
                throw new InvalidOperationException("击败 NPC 目标必须配置 NPCIdentityDefinition。");
            npcIdentity.Validate();
        }

        #endregion
    }
}
