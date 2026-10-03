using System;
using System.Collections.Generic;
using RPG.RewardSystemNS;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.TaskSystemNS
{
    #region 每任务配置资产

    /// <summary>
    /// 描述一个独立 ScriptableObject 任务资产中的静态任务配置。
    /// </summary>
    [CreateAssetMenu(fileName = "TaskDefinition", menuName = "RPG/TaskSystem/Task Definition", order = 1)]
    public sealed class TaskDefinition : ScriptableObject
    {
        [SerializeField] private string taskId = string.Empty;
        [SerializeField, TaskCategoryDropdown] private string categoryId = string.Empty;
        [SerializeField] private string title = string.Empty;
        [SerializeField, TextArea] private string description = string.Empty;
        [SerializeReference] private List<TaskConditionDefinition> unlockConditions =
            new List<TaskConditionDefinition>();
        [SerializeField] private List<TaskStageDefinition> stages = new List<TaskStageDefinition>();
        [SerializeReference] private List<RewardDefinition> rewards = new List<RewardDefinition>();

        /// <summary>
        /// 获取任务稳定标识。
        /// </summary>
        public TaskId TaskId => new TaskId(taskId);

        /// <summary>
        /// 获取任务分类标识。
        /// </summary>
        public TaskCategoryId CategoryId => new TaskCategoryId(categoryId);

        /// <summary>
        /// 获取任务标题。
        /// </summary>
        public string Title => title ?? string.Empty;

        /// <summary>
        /// 获取任务说明。
        /// </summary>
        public string Description => description ?? string.Empty;

        /// <summary>
        /// 获取接取条件只读列表。
        /// </summary>
        public IReadOnlyList<TaskConditionDefinition> UnlockConditions => unlockConditions;

        /// <summary>
        /// 获取按执行顺序排列的阶段只读列表。
        /// </summary>
        public IReadOnlyList<TaskStageDefinition> Stages => stages;

        /// <summary>
        /// 获取任务奖励只读列表。
        /// </summary>
        public IReadOnlyList<RewardDefinition> Rewards => rewards;

        /// <summary>
        /// 尝试按稳定阶段 ID 查找阶段及其顺序位置。
        /// </summary>
        /// <param name="stageId">待查找阶段标识。</param>
        /// <param name="stage">找到的阶段。</param>
        /// <param name="index">找到阶段的顺序位置。</param>
        /// <returns>找到时返回 true。</returns>
        public bool TryGetStage(TaskStageId stageId, out TaskStageDefinition stage, out int index)
        {
            for (int stageIndex = 0; stageIndex < stages.Count; stageIndex++)
            {
                if (stages[stageIndex].StageId == stageId)
                {
                    stage = stages[stageIndex];
                    index = stageIndex;
                    return true;
                }
            }

            stage = null;
            index = -1;
            return false;
        }

        /// <summary>
        /// 校验任务资产及阶段、条件和通用奖励配置。
        /// </summary>
        /// <exception cref="ArgumentException">任务、阶段、条件或奖励配置非法时抛出。</exception>
        public void Validate()
        {
            unlockConditions ??= new List<TaskConditionDefinition>();
            stages ??= new List<TaskStageDefinition>();
            rewards ??= new List<RewardDefinition>();

            if (!TaskIdentifierRules.IsValid(taskId))
            {
                throw new ArgumentException("任务 ID 无效。", nameof(taskId));
            }

            if (!TaskIdentifierRules.IsValid(categoryId))
            {
                throw new ArgumentException("任务分类 ID 无效。", nameof(categoryId));
            }

            if (!TaskCategoryCatalog.IsDefined(categoryId))
            {
                throw new ArgumentException(
                    $"任务 {taskId} 使用了未登记的任务分类 ID：{categoryId}。",
                    nameof(categoryId));
            }

            if (stages.Count == 0)
            {
                throw new ArgumentException($"任务 {taskId} 至少需要一个阶段。", nameof(stages));
            }

            if (rewards.Count == 0)
            {
                throw new ArgumentException($"任务 {taskId} 至少需要一个奖励。", nameof(rewards));
            }

            var stageIds = new HashSet<TaskStageId>();
            for (int index = 0; index < stages.Count; index++)
            {
                TaskStageDefinition stage = stages[index];
                if (stage == null)
                {
                    throw new ArgumentException($"任务 {taskId} 包含空阶段。", nameof(stages));
                }

                stage.Validate();
                if (!stageIds.Add(stage.StageId))
                {
                    throw new ArgumentException($"任务 {taskId} 包含重复阶段 ID：{stage.StageId}。", nameof(stages));
                }
            }

            for (int index = 0; index < unlockConditions.Count; index++)
            {
                if (unlockConditions[index] == null)
                {
                    throw new ArgumentException($"任务 {taskId} 包含空接取条件。", nameof(unlockConditions));
                }

                unlockConditions[index].Validate();
            }

            for (int index = 0; index < rewards.Count; index++)
            {
                if (rewards[index] == null)
                {
                    throw new ArgumentException(
                        $"任务 {taskId} 包含空奖励定义，index={index}。",
                        nameof(rewards));
                }

                rewards[index].Validate();
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// 创建 Odin 手动测试使用的临时任务资产，不写入项目资源。
        /// </summary>
        /// <param name="taskId">稳定任务标识。</param>
        /// <param name="categoryId">分类稳定标识。</param>
        /// <param name="title">任务标题。</param>
        /// <param name="description">任务说明。</param>
        /// <param name="conditions">接取条件。</param>
        /// <param name="taskStages">有序阶段。</param>
        /// <param name="taskRewards">任务奖励。</param>
        /// <returns>已经校验的临时任务资产。</returns>
        internal static TaskDefinition CreateRuntime(
            string taskId,
            string categoryId,
            string title,
            string description,
            IEnumerable<TaskConditionDefinition> conditions,
            IEnumerable<TaskStageDefinition> taskStages,
            IEnumerable<RewardDefinition> taskRewards)
        {
            TaskDefinition definition = CreateInstance<TaskDefinition>();
            definition.hideFlags = HideFlags.DontSave;
            definition.taskId = taskId;
            definition.categoryId = categoryId;
            definition.title = title;
            definition.description = description;
            definition.unlockConditions = conditions == null
                ? new List<TaskConditionDefinition>()
                : new List<TaskConditionDefinition>(conditions);
            definition.stages = taskStages == null
                ? new List<TaskStageDefinition>()
                : new List<TaskStageDefinition>(taskStages);
            definition.rewards = taskRewards == null
                ? new List<RewardDefinition>()
                : new List<RewardDefinition>(taskRewards);
            definition.Validate();
            return definition;
        }
#endif
    }

    #endregion
}
