using System;
using System.Collections.Generic;
using UnityEngine;

namespace RPG.TaskSystem
{
    #region 多态定义

    /// <summary>
    /// 表示一个只描述接取资格的多态条件配置。
    /// </summary>
    [Serializable]
    public abstract class TaskConditionDefinition
    {
    }

    /// <summary>
    /// 表示一个需要由运行时 Handler 解释的多态目标配置。
    /// </summary>
    [Serializable]
    public abstract class TaskObjectiveDefinition
    {
        [SerializeField] private string objectiveId = string.Empty;
        [SerializeField, Min(1)] private int required = 1;

        /// <summary>
        /// 创建目标定义。
        /// </summary>
        protected TaskObjectiveDefinition()
        {
        }

        /// <summary>
        /// 创建带有稳定标识和目标数量的目标定义，供代码和测试构造配置。
        /// </summary>
        /// <param name="objectiveId">任务内部唯一目标标识。</param>
        /// <param name="required">目标所需的非负整数数量。</param>
        /// <exception cref="ArgumentException">目标标识无效时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException">目标数量不是正数时抛出。</exception>
        protected TaskObjectiveDefinition(string objectiveId, int required)
        {
            if (!TaskIdentifierRules.IsValid(objectiveId))
            {
                throw new ArgumentException("目标 ID 无效。", nameof(objectiveId));
            }

            if (required <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(required), "目标所需数量必须大于零。");
            }

            this.objectiveId = objectiveId;
            this.required = required;
        }

        /// <summary>
        /// 获取任务内部稳定目标标识。
        /// </summary>
        public ObjectiveId ObjectiveId => new ObjectiveId(objectiveId);

        /// <summary>
        /// 获取目标完成所需数量。
        /// </summary>
        public int Required => required;

        /// <summary>
        /// 校验目标定义的稳定标识和数量。
        /// </summary>
        /// <exception cref="ArgumentException">目标定义非法时抛出。</exception>
        public virtual void Validate()
        {
            if (!TaskIdentifierRules.IsValid(objectiveId))
            {
                throw new ArgumentException("目标 ID 无效。", nameof(objectiveId));
            }

            if (required <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(required), "目标所需数量必须大于零。");
            }
        }
    }

    /// <summary>
    /// 表示一个只描述奖励内容的多态配置。
    /// </summary>
    [Serializable]
    public abstract class TaskRewardDefinition
    {
    }

    #endregion

    #region 任务定义

    /// <summary>
    /// 描述一个集中式任务数据库中的静态任务配置。
    /// </summary>
    [Serializable]
    public sealed class TaskDefinition
    {
        [SerializeField] private string taskId = string.Empty;
        [SerializeField] private string categoryId = string.Empty;
        [SerializeField] private string title = string.Empty;
        [SerializeField, TextArea] private string description = string.Empty;
        /// <summary>
        /// 任务接取条件列表，允许为空。
        /// </summary>
        [SerializeReference] private List<TaskConditionDefinition> unlockConditions =
            new List<TaskConditionDefinition>();
        /// <summary>
        /// 任务需要达到的目标列表，至少一个目标。
        /// </summary>
        [SerializeReference] private List<TaskObjectiveDefinition> objectives =
            new List<TaskObjectiveDefinition>();
        /// <summary>
        /// 任务完成后给予的奖励列表，至少一个奖励。
        /// </summary>
        [SerializeReference] private List<TaskRewardDefinition> rewards =
            new List<TaskRewardDefinition>();

        /// <summary>
        /// 创建供 Unity 序列化使用的空任务定义。
        /// </summary>
        public TaskDefinition()
        {
        }

        /// <summary>
        /// 创建一个可直接用于运行时测试和代码配置的任务定义。
        /// </summary>
        /// <param name="taskId">全局稳定任务标识。</param>
        /// <param name="categoryId">任务分类标识。</param>
        /// <param name="title">任务展示标题。</param>
        /// <param name="description">任务展示描述。</param>
        /// <param name="unlockConditions">接取条件配置。</param>
        /// <param name="objectives">目标配置。</param>
        /// <param name="rewards">奖励配置。</param>
        public TaskDefinition(
            string taskId,
            string categoryId,
            string title,
            string description,
            IEnumerable<TaskConditionDefinition> unlockConditions,
            IEnumerable<TaskObjectiveDefinition> objectives,
            IEnumerable<TaskRewardDefinition> rewards)
        {
            this.taskId = taskId ?? string.Empty;
            this.categoryId = categoryId ?? string.Empty;
            this.title = title ?? string.Empty;
            this.description = description ?? string.Empty;
            this.unlockConditions = CopyList(unlockConditions);
            this.objectives = CopyList(objectives);
            this.rewards = CopyList(rewards);
        }

        /// <summary>
        /// 获取任务稳定标识。
        /// </summary>
        public TaskId TaskId => new TaskId(taskId);

        /// <summary>
        /// 获取任务分类标识。
        /// </summary>
        public TaskCategoryId CategoryId => new TaskCategoryId(categoryId);

        /// <summary>
        /// 获取展示标题。
        /// </summary>
        public string Title => title ?? string.Empty;

        /// <summary>
        /// 获取展示描述。
        /// </summary>
        public string Description => description ?? string.Empty;

        /// <summary>
        /// 获取接取条件配置的只读视图。
        /// </summary>
        public IReadOnlyList<TaskConditionDefinition> UnlockConditions => unlockConditions;

        /// <summary>
        /// 获取目标配置的只读视图。
        /// </summary>
        public IReadOnlyList<TaskObjectiveDefinition> Objectives => objectives;

        /// <summary>
        /// 获取奖励配置的只读视图。
        /// </summary>
        public IReadOnlyList<TaskRewardDefinition> Rewards => rewards;

        /// <summary>
        /// 校验任务定义及其目标内部标识，确保运行时可建立稳定索引。
        /// </summary>
        /// <exception cref="ArgumentException">任务标识、分类、目标或奖励配置非法时抛出。</exception>
        public void Validate()
        {
            unlockConditions ??= new List<TaskConditionDefinition>();
            objectives ??= new List<TaskObjectiveDefinition>();
            rewards ??= new List<TaskRewardDefinition>();

            if (!TaskIdentifierRules.IsValid(taskId))
            {
                throw new ArgumentException("任务 ID 无效。", nameof(taskId));
            }

            if (!TaskIdentifierRules.IsValid(categoryId))
            {
                throw new ArgumentException("任务分类 ID 无效。", nameof(categoryId));
            }

            if (objectives == null || objectives.Count == 0)
            {
                throw new ArgumentException("任务至少需要一个目标。", nameof(objectives));
            }

            if (rewards == null || rewards.Count == 0)
            {
                throw new ArgumentException("任务至少需要一个奖励。", nameof(rewards));
            }

            var objectiveIds = new HashSet<ObjectiveId>();
            for (int index = 0; index < objectives.Count; index++)
            {
                TaskObjectiveDefinition objective = objectives[index];
                if (objective == null)
                {
                    throw new ArgumentException($"任务 {taskId} 的目标列表包含空引用。", nameof(objectives));
                }

                objective.Validate();
                if (!objectiveIds.Add(objective.ObjectiveId))
                {
                    throw new ArgumentException(
                        $"任务 {taskId} 包含重复目标 ID：{objective.ObjectiveId}。",
                        nameof(objectives));
                }
            }

            for (int index = 0; index < unlockConditions.Count; index++)
            {
                if (unlockConditions[index] == null)
                {
                    throw new ArgumentException($"任务 {taskId} 的接取条件列表包含空引用。", nameof(unlockConditions));
                }
            }

            for (int index = 0; index < rewards.Count; index++)
            {
                if (rewards[index] == null)
                {
                    throw new ArgumentException($"任务 {taskId} 的奖励列表包含空引用。", nameof(rewards));
                }
            }
        }

        /// <summary>
        /// 复制序列化列表，避免代码构造任务后继续修改外部集合。
        /// </summary>
        /// <typeparam name="T">列表元素类型。</typeparam>
        /// <param name="source">待复制集合。</param>
        /// <returns>独立可变列表。</returns>
        private static List<T> CopyList<T>(IEnumerable<T> source)
        {
            return source == null ? new List<T>() : new List<T>(source);
        }
    }

    #endregion
}
