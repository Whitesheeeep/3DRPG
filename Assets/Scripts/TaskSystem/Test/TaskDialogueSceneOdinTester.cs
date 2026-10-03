#if UNITY_EDITOR
using System.Text;
using RPG.Game;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.TaskSystemNS.Tests
{
    /// <summary>
    /// 为 TestInteractableScene 中的 Cube (1) 提供真实任务接取、状态查询和领奖入口。
    /// </summary>
    [InfoBox("仅在 Play Mode 使用。先点击接取，再实际与场景中的对话对象交互；本组件不会模拟对话或清空玩家任务数据。")]
    public sealed class TaskDialogueSceneOdinTester : MonoBehaviour
    {
        #region 测试配置与状态

        [SerializeField, Required, LabelText("测试任务配置")]
        private TaskDefinition taskDefinition;

        [ShowInInspector, ReadOnly, LabelText("资格状态")]
        private string availabilityStatus = "尚未查询";

        [ShowInInspector, ReadOnly, LabelText("当前进度")]
        private string progressStatus = "尚未查询";

        [ShowInInspector, ReadOnly, LabelText("最近操作")]
        private string lastOperation = "未操作";

        #endregion

        #region 测试操作

        /// <summary>
        /// 通过任务系统的统一入口接取场景测试任务。
        /// </summary>
        [Button("接取测试任务", ButtonSizes.Large)]
        public void AcceptTestTask()
        {
            if (!TryGetTaskSystem(out TaskSystem taskSystem))
            {
                return;
            }

            ValidateConfiguredTask();
            TaskAcceptResult result = taskSystem.TryAcceptTask(
                taskDefinition.TaskId,
                E_TaskAcceptSource.Test);
            lastOperation = result.Succeeded
                ? $"接取成功：{taskDefinition.TaskId}"
                : $"接取拒绝：{result.Failure}，资格={result.Availability?.Status}";
            Debug.Log($"[TaskDialogueSceneOdinTester] {lastOperation}", this);
            RefreshTaskStatus(taskSystem);
        }

        /// <summary>
        /// 查询场景任务的接取资格、当前阶段和目标进度。
        /// </summary>
        [Button("刷新测试任务状态")]
        public void RefreshTaskStatus()
        {
            if (!TryGetTaskSystem(out TaskSystem taskSystem))
            {
                return;
            }

            ValidateConfiguredTask();
            RefreshTaskStatus(taskSystem);
            Debug.Log(
                $"[TaskDialogueSceneOdinTester] 查询任务 {taskDefinition.TaskId}：{progressStatus}",
                this);
        }

        /// <summary>
        /// 通过任务系统提交待领奖任务并刷新 Inspector 状态。
        /// </summary>
        [Button("领取测试任务奖励", ButtonSizes.Large)]
        public void ClaimTestReward()
        {
            if (!TryGetTaskSystem(out TaskSystem taskSystem))
            {
                return;
            }

            ValidateConfiguredTask();
            TaskClaimResult result = taskSystem.TryClaimReward(taskDefinition.TaskId);
            lastOperation = result.Succeeded
                ? $"领奖成功：{taskDefinition.TaskId}"
                : $"领奖拒绝：{result.Failure}";
            Debug.Log($"[TaskDialogueSceneOdinTester] {lastOperation}", this);
            RefreshTaskStatus(taskSystem);
        }

        #endregion

        #region 状态刷新与校验

        /// <summary>
        /// 从真实任务集合生成 Inspector 展示文本。
        /// </summary>
        /// <param name="taskSystem">已初始化的任务系统。</param>
        private void RefreshTaskStatus(TaskSystem taskSystem)
        {
            TaskAvailabilityResult availability = taskSystem.GetAvailability(taskDefinition.TaskId);
            availabilityStatus = availability.Status.ToString();

            if (taskSystem.IsTaskCompleted(taskDefinition.TaskId))
            {
                progressStatus = "Completed";
                return;
            }

            if (!taskSystem.TryGetActiveRecord(taskDefinition.TaskId, out TaskRecord record))
            {
                progressStatus = availability.Status == TaskAvailabilityStatus.Locked && availability.Reasons.Count > 0
                    ? $"未接取；{availability.Reasons[0].Message}"
                    : "未接取";
                return;
            }

            var statusBuilder = new StringBuilder();
            statusBuilder.Append(record.State);
            statusBuilder.Append(" / stage=");
            statusBuilder.Append(record.CurrentStageId);
            statusBuilder.Append(" / ");

            TaskStageDefinition currentStage = taskDefinition.Stages[0];
            for (int index = 0; index < taskDefinition.Stages.Count; index++)
            {
                if (taskDefinition.Stages[index].StageId == record.CurrentStageId)
                {
                    currentStage = taskDefinition.Stages[index];
                    break;
                }
            }

            for (int index = 0; index < currentStage.Objectives.Count; index++)
            {
                TaskObjectiveDefinition objective = currentStage.Objectives[index];
                if (record.TryGetProgress(objective.ObjectiveId, out TaskObjectiveProgress progress))
                {
                    if (index > 0)
                    {
                        statusBuilder.Append(", ");
                    }

                    statusBuilder.Append(objective.ObjectiveId);
                    statusBuilder.Append('=');
                    statusBuilder.Append(progress.Current);
                    statusBuilder.Append('/');
                    statusBuilder.Append(progress.Required);
                }
            }

            progressStatus = statusBuilder.ToString();
        }

        /// <summary>
        /// 检查当前处于 Play Mode 且任务系统已由 GameArchitecture 初始化。
        /// </summary>
        /// <param name="taskSystem">找到的任务系统。</param>
        /// <returns>任务系统可用时返回 true。</returns>
        private bool TryGetTaskSystem(out TaskSystem taskSystem)
        {
            taskSystem = null;
            if (!Application.isPlaying)
            {
                lastOperation = "请先进入 Play Mode，再使用场景任务测试按钮。";
                Debug.LogWarning($"[TaskDialogueSceneOdinTester] {lastOperation}", this);
                return false;
            }

            taskSystem = GameArchitecture.Interface.GetSystem<TaskSystem>();
            return true;
        }

        /// <summary>
        /// 确认测试资产引用的任务已加入 ConfigInstaller 注入的数据库。
        /// </summary>
        /// <exception cref="System.InvalidOperationException">资产未配置或数据库未引用该任务时抛出。</exception>
        private void ValidateConfiguredTask()
        {
            if (taskDefinition == null)
            {
                throw new System.InvalidOperationException(
                    "[TaskDialogueSceneOdinTester] 请在 Inspector 配置测试 TaskDefinition。");
            }

            if (!TaskConfigManager.Instance.TryGetDefinition(
                    taskDefinition.TaskId,
                    out TaskDefinition registeredDefinition) ||
                !ReferenceEquals(registeredDefinition, taskDefinition))
            {
                throw new System.InvalidOperationException(
                    $"[TaskDialogueSceneOdinTester] 任务 {taskDefinition.TaskId} 未加入 ConfigInstaller 使用的 TaskDatabase。");
            }
        }

        #endregion
    }
}
#endif
