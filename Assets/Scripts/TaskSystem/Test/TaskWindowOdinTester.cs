#if UNITY_EDITOR
using RPG.Game;
using RPG.Game.UI.Task;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.CustomEventSystem;

namespace RPG.TaskSystemNS
{
    /// <summary>为任务测试场景提供通过正式窗口流程打开面板和检查玩家任务快照的 Inspector 入口。</summary>
    [DisallowMultipleComponent]
    [InfoBox("该测试组件只在 Unity Editor 编译；窗口请求复用 HUD 与 J 快捷键使用的同一事件流程。")]
    public sealed class TaskWindowOdinTester : MonoBehaviour
    {
        #region 测试操作

        /// <summary>通过正式 UI 意图请求打开或关闭任务窗口。</summary>
        [Button("切换任务面板")]
        public void ToggleTaskWindow()
        {
            Debug.Log("[TaskWindowOdinTester] 通过任务窗口统一意图请求切换面板。", this);
            EventSystem.EventTrigger_Type(
                typeof(TaskWindowOpenRequestedEventArgs), new TaskWindowOpenRequestedEventArgs());
        }

        /// <summary>输出当前活动任务、阶段、目标进度、追踪和未读快照。</summary>
        [Button("输出任务面板数据")]
        public void LogTaskWindowSnapshot()
        {
            TaskSystem taskSystem = GameArchitecture.Interface.GetSystem<TaskSystem>();
            Debug.Log(
                $"[TaskWindowOdinTester] 活动任务={taskSystem.ActiveRecords.Count}, " +
                $"追踪={taskSystem.TrackedTaskId}, 未读={taskSystem.UnreadTaskIds.Count}。", this);

            foreach (TaskRecord record in taskSystem.ActiveRecords)
            {
                TaskDefinition definition = TaskConfigManager.Instance.GetRequiredDefinition(record.TaskId);
                if (!definition.TryGetStage(record.CurrentStageId, out TaskStageDefinition stage, out _))
                    throw new System.InvalidOperationException(
                        $"[TaskWindowOdinTester] 任务 {record.TaskId} 缺少当前阶段配置。");

                Debug.Log(
                    $"[TaskWindowOdinTester] task={record.TaskId}, category={definition.CategoryId.Value}, " +
                    $"state={record.State}, stage={stage.StageId} ({stage.Title})。", this);
                for (int objectiveIndex = 0; objectiveIndex < stage.Objectives.Count; objectiveIndex++)
                {
                    TaskObjectiveDefinition objective = stage.Objectives[objectiveIndex];
                    if (!record.TryGetProgress(objective.ObjectiveId, out TaskObjectiveProgress progress))
                        throw new System.InvalidOperationException(
                            $"[TaskWindowOdinTester] 任务 {record.TaskId} 缺少目标进度 {objective.ObjectiveId}。");
                    Debug.Log(
                        $"[TaskWindowOdinTester] objective={objective.ObjectiveId}, " +
                        $"description={objective.DisplayDescription}, progress={progress.Current}/{progress.Required}。", this);
                }
            }
        }

        #endregion
    }
}
#endif
