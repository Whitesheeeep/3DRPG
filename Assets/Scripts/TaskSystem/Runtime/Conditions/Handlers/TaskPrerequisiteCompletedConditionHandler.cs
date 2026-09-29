using System;
using UnityEngine;

namespace RPG.TaskSystem
{
    /// <summary>
    /// 评估前置任务是否已完成。
    /// </summary>
    public sealed class TaskPrerequisiteCompletedConditionHandler : ITaskConditionHandler
    {
        /// <summary>
        /// 获取支持的条件定义类型。
        /// </summary>
        public Type DefinitionType => typeof(TaskPrerequisiteCompletedConditionDefinition);

        /// <summary>
        /// 检查已完成任务集合并生成可供界面解释的原因。
        /// </summary>
        /// <param name="definition">前置任务条件。</param>
        /// <param name="taskManager">当前任务事实。</param>
        /// <returns>前置任务未完成时返回原因，否则返回 null。</returns>
        /// <exception cref="ArgumentException">收到不匹配的条件定义时抛出。</exception>
        public TaskAvailabilityReason Evaluate(TaskConditionDefinition definition, TaskManager taskManager)
        {
            if (!(definition is TaskPrerequisiteCompletedConditionDefinition prerequisite))
            {
                const string message = "前置任务 Handler 收到不匹配的条件定义。";
                Debug.LogError($"[TaskPrerequisiteCompletedConditionHandler] {message}");
                throw new ArgumentException(message, nameof(definition));
            }

            // 资格只读取 Manager 保存的已完成事实，不创建或修改任何任务状态。
            if (taskManager.IsTaskCompleted(prerequisite.PrerequisiteTaskId))
            {
                return null;
            }

            return new TaskAvailabilityReason(
                TaskAvailabilityReasonKind.PrerequisiteNotCompleted,
                prerequisite.PrerequisiteTaskId,
                $"需要先完成任务 {prerequisite.PrerequisiteTaskId}。");
        }
    }
}
