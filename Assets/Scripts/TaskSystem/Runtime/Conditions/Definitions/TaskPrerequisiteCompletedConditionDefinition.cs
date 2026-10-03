using System;
using UnityEngine;

namespace RPG.TaskSystemNS
{
    /// <summary>
    /// 描述接取任务所需完成的前置任务。
    /// </summary>
    [Serializable]
    public sealed class TaskPrerequisiteCompletedConditionDefinition : TaskConditionDefinition
    {
        [SerializeField, TaskIdDropdown] private string prerequisiteTaskId = string.Empty;

        /// <summary>
        /// 创建供 Unity 序列化使用的空前置条件。
        /// </summary>
        public TaskPrerequisiteCompletedConditionDefinition()
        {
        }

        /// <summary>
        /// 创建引用指定前置任务的条件。
        /// </summary>
        /// <param name="taskId">必须已完成的任务标识。</param>
        public TaskPrerequisiteCompletedConditionDefinition(string taskId)
        {
            prerequisiteTaskId = taskId ?? string.Empty;
        }

        /// <summary>
        /// 获取前置任务标识。
        /// </summary>
        public TaskId PrerequisiteTaskId => new TaskId(prerequisiteTaskId);

        /// <summary>
        /// 校验前置任务标识。
        /// </summary>
        /// <exception cref="ArgumentException">前置任务 ID 非法时抛出。</exception>
        public override void Validate()
        {
            _ = new TaskId(prerequisiteTaskId);
        }
    }
}
