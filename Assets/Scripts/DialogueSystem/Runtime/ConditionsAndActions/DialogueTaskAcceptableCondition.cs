using System;
using RPG.TaskSystemNS;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.DialogueSystemModule
{
    /// <summary>仅在指定任务当前可接取时启用对应对话选项。</summary>
    [Serializable]
    public sealed class DialogueTaskAcceptableCondition : DialogueCondition
    {
        #region 配置字段

        [SerializeField, TaskIdDropdown, LabelText("任务")]
        private string taskId = string.Empty;

        #endregion

        #region 构造

        /// <summary>创建可由 SerializeReference 命令抽屉实例化的任务资格条件。</summary>
        public DialogueTaskAcceptableCondition()
        {
        }

        #endregion

        #region 配置校验与判断

        /// <summary>校验任务标识格式。</summary>
        /// <exception cref="ArgumentException">任务标识为空或非法时抛出。</exception>
        public override void Validate()
        {
            _ = new TaskId(taskId);
        }

        /// <summary>查询 TaskSystem 的接取资格，并返回明确的选项置灰原因。</summary>
        /// <param name="context">当前对话命令上下文。</param>
        /// <returns>任务可接取时返回满足结果，其余状态返回诊断原因。</returns>
        public override DialogueConditionResult Evaluate(DialogueCommandContext context)
        {
            TaskId configuredTaskId = new TaskId(taskId);
            TaskAvailabilityResult availability = context.Architecture
                .GetSystem<TaskSystem>()
                .GetAvailability(configuredTaskId);

            switch (availability.Status)
            {
                case TaskAvailabilityStatus.Available:
                    return DialogueConditionResult.Met();
                case TaskAvailabilityStatus.Locked:
                    return DialogueConditionResult.NotMet(availability.Reasons.Count > 0
                        ? availability.Reasons[0].Message
                        : "任务尚未解锁");
                case TaskAvailabilityStatus.Active:
                    return DialogueConditionResult.NotMet("任务已接取");
                case TaskAvailabilityStatus.Completed:
                    return DialogueConditionResult.NotMet("任务已完成");
                default:
                    return DialogueConditionResult.NotMet("任务不存在");
            }
        }

        #endregion
    }
}
