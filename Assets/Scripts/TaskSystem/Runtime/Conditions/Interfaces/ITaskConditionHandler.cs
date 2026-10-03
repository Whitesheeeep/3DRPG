using System;

namespace RPG.TaskSystemNS
{
    /// <summary>
    /// 将接取条件定义解析为资格原因的 Handler 契约。
    /// </summary>
    public interface ITaskConditionHandler
    {
        /// <summary>
        /// 获取该 Handler 支持的定义 CLR 类型。
        /// </summary>
        Type DefinitionType { get; }

        /// <summary>
        /// 评估条件；返回 null 表示满足，返回原因表示暂不可接取。
        /// </summary>
        /// <param name="definition">待评估条件。</param>
        /// <param name="taskSystem">当前玩家任务集合事实。</param>
        /// <returns>未满足原因；满足时为 null。</returns>
        TaskAvailabilityReason Evaluate(TaskConditionDefinition definition, TaskSystem taskSystem);
    }
}
