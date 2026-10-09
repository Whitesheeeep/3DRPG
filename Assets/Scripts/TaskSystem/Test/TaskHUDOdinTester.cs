#if UNITY_EDITOR
using RPG.Game;
using RPG.TaskSystemNS;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.TaskSystemNS.Test
{
    /// <summary>提供追踪和接取测试任务的 Inspector 操作。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖 Inspector 显式指定两阶段任务和多目标任务；导航目标由任务目标配置的 NPCId 经 TaskSystem 自动解析。")]
    public sealed class TaskHUDOdinTester : MonoBehaviour
    {
        #region 依赖字段
        [SerializeField, Required] private TaskDefinition testTaskDefinition;
        [SerializeField, Required] private TaskDefinition multiObjectiveTaskDefinition;
        // 依赖字段：Odin 操作仅调用任务业务 API，不注入 HUD 导航状态。
        private TaskSystem taskSystem;
        #endregion

        #region Odin 测试操作
        /// <summary>将已接取的两阶段 NPC 对话测试任务设为追踪任务。</summary>
        [Button("追踪两阶段测试任务")]
        private void TrackTestTask()
        {
            if (!TryGetTaskSystem(out TaskSystem system) || !ValidateTaskReference(testTaskDefinition, "两阶段"))
                return;

            if (!system.TrySetTrackedTask(testTaskDefinition.TaskId))
            {
                Debug.LogWarning(
                    $"[TaskHUDOdinTester] 两阶段测试任务尚未接取，无法追踪；taskId={testTaskDefinition.TaskId}。",
                    this);
                return;
            }

            Debug.Log($"[TaskHUDOdinTester] 已追踪两阶段测试任务，taskId={testTaskDefinition.TaskId}。", this);
        }

        /// <summary>通过真实任务 API 接取未完成的多目标任务并设置为追踪。</summary>
        [Button("接取并追踪多目标任务", ButtonSizes.Large)]
        private void AcceptAndTrackMultiObjectiveTask()
        {
            if (!TryGetTaskSystem(out TaskSystem system) ||
                !ValidateTaskReference(multiObjectiveTaskDefinition, "多目标"))
                return;

            TaskId taskId = multiObjectiveTaskDefinition.TaskId;
            if (system.IsTaskCompleted(taskId))
            {
                Debug.LogWarning(
                    $"[TaskHUDOdinTester] 多目标任务已完成；测试入口不会重置玩家任务数据，taskId={taskId}。",
                    this);
                return;
            }

            if (!system.TryGetActiveRecord(taskId, out _))
            {
                TaskAvailabilityResult availability = system.GetAvailability(taskId);
                if (availability.Status != TaskAvailabilityStatus.Available)
                {
                    string reason = availability.Reasons.Count > 0
                        ? availability.Reasons[0].Message
                        : availability.Status.ToString();
                    Debug.LogWarning(
                        $"[TaskHUDOdinTester] 多目标任务当前不可接取，taskId={taskId}, reason={reason}。",
                        this);
                    return;
                }

                TaskAcceptResult acceptResult = system.TryAcceptTask(taskId, E_TaskAcceptSource.Test);
                if (!acceptResult.Succeeded)
                {
                    Debug.LogWarning(
                        $"[TaskHUDOdinTester] 多目标任务接取失败，taskId={taskId}, failure={acceptResult.Failure}。",
                        this);
                    return;
                }
            }

            if (!system.TrySetTrackedTask(taskId))
                throw new System.InvalidOperationException($"[TaskHUDOdinTester] 已接取任务无法设置追踪：{taskId}。");

            Debug.Log($"[TaskHUDOdinTester] 多目标任务已接取并追踪，taskId={taskId}。", this);
        }
        #endregion

        #region 测试任务配置校验
        /// <summary>校验测试任务资产是否已在当前 TaskDatabase 中注册。</summary>
        /// <param name="definition">待验证任务资产。</param>
        /// <param name="label">日志中的测试任务类型说明。</param>
        /// <returns>资产已由当前配置管理器注册时返回 true。</returns>
        private bool ValidateTaskReference(TaskDefinition definition, string label)
        {
            if (definition == null)
                throw new System.InvalidOperationException($"[TaskHUDOdinTester] Inspector 未配置{label}任务资产。");

            if (TaskConfigManager.Instance.TryGetDefinition(definition.TaskId, out TaskDefinition registered) &&
                ReferenceEquals(registered, definition))
                return true;

            Debug.LogError(
                $"[TaskHUDOdinTester] {label}任务未加入当前 TaskDatabase，taskId={definition.TaskId}。",
                this);
            return false;
        }

        /// <summary>确认调用 Odin 按钮时处于 Play Mode 且任务系统已初始化。</summary>
        /// <param name="system">已注册的任务系统。</param>
        /// <returns>可以调用任务 API 时返回 true。</returns>
        private bool TryGetTaskSystem(out TaskSystem system)
        {
            system = taskSystem;
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[TaskHUDOdinTester] 请先进入 Play Mode，再使用任务测试按钮。", this);
                return false;
            }

            if (system == null)
                system = GameArchitecture.Interface.GetSystem<TaskSystem>();
            return true;
        }
        #endregion
    }
}
#endif
