using System;
using System.Collections.Generic;
using UnityEngine;

namespace RPG.TaskSystem
{
    #region 奖励配置

    /// <summary>
    /// 描述任务完成后发放的多态奖励配置。
    /// </summary>
    [Serializable]
    public abstract class TaskRewardDefinition
    {
    }

    #endregion

    #region 阶段配置

    /// <summary>
    /// 描述任务线性流程中的一个阶段；阶段内目标全部完成后才进入下一阶段。
    /// </summary>
    [Serializable]
    public sealed class TaskStageDefinition
    {
        [SerializeField] private string stageId = string.Empty;
        [SerializeField] private string title = string.Empty;
        [SerializeField, TextArea] private string description = string.Empty;
        [SerializeReference] private List<TaskObjectiveDefinition> objectives =
            new List<TaskObjectiveDefinition>();

        /// <summary>
        /// 创建供 Unity 序列化使用的空阶段。
        /// </summary>
        public TaskStageDefinition()
        {
        }

        /// <summary>
        /// 创建可供代码配置和手动测试使用的阶段。
        /// </summary>
        /// <param name="stageId">所属任务内稳定的阶段标识。</param>
        /// <param name="title">阶段标题。</param>
        /// <param name="description">阶段说明。</param>
        /// <param name="objectives">阶段目标集合。</param>
        public TaskStageDefinition(
            string stageId,
            string title,
            string description,
            IEnumerable<TaskObjectiveDefinition> objectives)
        {
            this.stageId = stageId ?? string.Empty;
            this.title = title ?? string.Empty;
            this.description = description ?? string.Empty;
            this.objectives = objectives == null
                ? new List<TaskObjectiveDefinition>()
                : new List<TaskObjectiveDefinition>(objectives);
        }

        /// <summary>
        /// 获取阶段稳定标识。
        /// </summary>
        public TaskStageId StageId => new TaskStageId(stageId);

        /// <summary>
        /// 获取阶段标题。
        /// </summary>
        public string Title => title ?? string.Empty;

        /// <summary>
        /// 获取阶段说明。
        /// </summary>
        public string Description => description ?? string.Empty;

        /// <summary>
        /// 获取阶段目标只读列表。
        /// </summary>
        public IReadOnlyList<TaskObjectiveDefinition> Objectives => objectives;

        /// <summary>
        /// 校验阶段 ID、目标集合及阶段内目标唯一性。
        /// </summary>
        /// <exception cref="ArgumentException">阶段或目标配置非法时抛出。</exception>
        public void Validate()
        {
            objectives ??= new List<TaskObjectiveDefinition>();
            if (!TaskIdentifierRules.IsValid(stageId))
            {
                throw new ArgumentException("任务阶段 ID 无效。", nameof(stageId));
            }

            if (objectives.Count == 0)
            {
                throw new ArgumentException($"任务阶段 {stageId} 至少需要一个目标。", nameof(objectives));
            }

            var objectiveIds = new HashSet<ObjectiveId>();
            for (int index = 0; index < objectives.Count; index++)
            {
                TaskObjectiveDefinition objective = objectives[index];
                if (objective == null)
                {
                    throw new ArgumentException($"任务阶段 {stageId} 包含空目标。", nameof(objectives));
                }

                objective.Validate();
                if (!objectiveIds.Add(objective.ObjectiveId))
                {
                    throw new ArgumentException(
                        $"任务阶段 {stageId} 包含重复目标 ID：{objective.ObjectiveId}。",
                        nameof(objectives));
                }
            }
        }
    }

    #endregion

}
