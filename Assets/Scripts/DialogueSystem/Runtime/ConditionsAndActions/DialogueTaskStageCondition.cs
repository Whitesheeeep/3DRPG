using System;
using RPG.TaskSystemNS;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.DialogueSystemModule
{
    /// <summary>检查任务正处于指定活动阶段，并为未到达阶段的选项提供原因。</summary>
    [Serializable]
    public sealed class DialogueTaskStageCondition : DialogueCondition
    {
        #region 配置字段

        [SerializeField, TaskIdDropdown, LabelText("任务")]
        private string taskId = string.Empty;
        [SerializeField, LabelText("目标阶段 ID")]
        private string stageId = string.Empty;
        [SerializeField, LabelText("尚未到达阶段时的提示")]
        private string unavailableReason = string.Empty;

        #endregion

        #region 构造

        /// <summary>创建可由 SerializeReference 命令抽屉实例化的任务阶段条件。</summary>
        public DialogueTaskStageCondition()
        {
        }

        #endregion

        #region 配置校验与判断

        /// <summary>校验任务、阶段标识以及未到达阶段时的提示。</summary>
        /// <exception cref="ArgumentException">任一必填配置非法时抛出。</exception>
        public override void Validate()
        {
            _ = new TaskId(taskId);
            _ = new TaskStageId(stageId);
            if (string.IsNullOrWhiteSpace(unavailableReason))
                throw new ArgumentException("任务阶段条件必须配置未到达阶段时的提示。", nameof(unavailableReason));
        }

        /// <summary>按任务生命周期和当前阶段返回可用于置灰选项的具体原因。</summary>
        /// <param name="context">当前对话命令上下文。</param>
        /// <returns>任务处于指定活动阶段时满足，否则返回对应状态说明。</returns>
        public override DialogueConditionResult Evaluate(DialogueCommandContext context)
        {
            TaskSystem taskSystem = context.Architecture.GetSystem<TaskSystem>();
            TaskId configuredTaskId = new TaskId(taskId);
            TaskAvailabilityResult availability = taskSystem.GetAvailability(configuredTaskId);

            switch (availability.Status)
            {
                case TaskAvailabilityStatus.NotFound:
                    return DialogueConditionResult.NotMet("任务不存在");
                case TaskAvailabilityStatus.Locked:
                    return DialogueConditionResult.NotMet("任务尚未解锁");
                case TaskAvailabilityStatus.Available:
                    return DialogueConditionResult.NotMet("需要先接取讨伐任务");
                case TaskAvailabilityStatus.Completed:
                    return DialogueConditionResult.NotMet("委托已完成");
                case TaskAvailabilityStatus.Active:
                    if (!taskSystem.TryGetActiveRecord(configuredTaskId, out TaskRecord record))
                        throw new InvalidOperationException(
                            $"[DialogueTaskStageCondition] TaskSystem 报告任务活动但未提供 Record，taskId={configuredTaskId}。");
                    if (record.State == E_TaskLifecycleState.Claimable)
                        return DialogueConditionResult.NotMet("已汇报，等待领取奖励");
                    return record.CurrentStageId == new TaskStageId(stageId)
                        ? DialogueConditionResult.Met()
                        : DialogueConditionResult.NotMet(unavailableReason.Trim());
                default:
                    throw new ArgumentOutOfRangeException(nameof(availability.Status), availability.Status,
                        "未知的任务资格状态。");
            }
        }

        #endregion
    }
}
