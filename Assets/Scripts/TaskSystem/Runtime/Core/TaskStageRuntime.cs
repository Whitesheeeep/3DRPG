using System;
using System.Collections.Generic;
using UnityEngine;

namespace RPG.TaskSystemNS
{
    /// <summary>
    /// 管理任务当前阶段中的 ObjectiveRuntime 创建、进度引用和监听生命周期。
    /// </summary>
    public sealed class TaskStageRuntime
    {
        #region 依赖字段

        // 依赖字段：所属 TaskRuntime 提供权威 Record 和 NPC 查询依赖；Definition 直接创建目标 Runtime。
        private readonly TaskRuntime taskRuntime;
        private readonly TaskStageDefinition stageDefinition;

        #endregion

        #region 运行时状态

        // 阶段运行时不复制目标进度，而是直接绑定 Record 中的进度对象，确保阶段状态实时派生。
        private readonly List<TaskObjectiveProgress> progressList = new List<TaskObjectiveProgress>();
        private readonly List<ITaskObjectiveRuntime> objectiveRuntimeList = new List<ITaskObjectiveRuntime>();
        private bool listening;

        #endregion

        #region 构造与查询

        // 构造期间绑定当前 Record 中的进度对象，确保阶段运行时不复制状态。
        /// <summary>创建任务单个阶段的执行上下文。</summary>
        /// <param name="taskRuntime">拥有任务 Record 的实例。</param>
        /// <param name="stageDefinition">静态阶段定义。</param>
        /// <exception cref="ArgumentNullException">必需依赖为空时抛出。</exception>
        /// <exception cref="InvalidOperationException">Record 缺少当前阶段目标进度时抛出。</exception>
        public TaskStageRuntime(
            TaskRuntime taskRuntime,
            TaskStageDefinition stageDefinition)
        {
            this.taskRuntime = taskRuntime ?? throw new ArgumentNullException(nameof(taskRuntime));
            this.stageDefinition = stageDefinition ?? throw new ArgumentNullException(nameof(stageDefinition));

            for (int index = 0; index < stageDefinition.Objectives.Count; index++)
            {
                TaskObjectiveDefinition objective = stageDefinition.Objectives[index];
                if (!taskRuntime.Record.TryGetProgress(objective.ObjectiveId, out TaskObjectiveProgress progress))
                {
                    throw new InvalidOperationException(
                        $"任务 {taskRuntime.TaskId} 阶段 {stageDefinition.StageId} 缺少目标进度 {objective.ObjectiveId}。");
                }

                progressList.Add(progress);
            }
        }

        // 阶段状态由绑定的目标进度实时派生。
        /// <summary>获取当前阶段标识。</summary>
        public TaskStageId StageId => stageDefinition.StageId;

        /// <summary>获取当前阶段是否已启动全部目标监听。</summary>
        public bool IsListening => listening;

        /// <summary>判断阶段目标是否全部完成。</summary>
        public bool IsComplete
        {
            get
            {
                if (progressList.Count == 0)
                {
                    return false;
                }

                for (int index = 0; index < progressList.Count; index++)
                {
                    if (!progressList[index].IsComplete)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>按配置顺序选择首个未完成且声明导航的目标，并解析其 Transform。</summary>
        /// <param name="target">所选目标的世界导航 Transform。</param>
        /// <returns>当前阶段存在可用导航 Transform 时返回 true。</returns>
        /// <exception cref="Exception">目标 Runtime 的导航解析发生错误时继续传播。</exception>
        public bool TryGetNavigationTarget(out Transform target, out Vector3 offset)
        {
            for (int index = 0; index < objectiveRuntimeList.Count; index++)
            {
                if (progressList[index].IsComplete ||
                    !(objectiveRuntimeList[index] is ITaskObjectiveNavigationProvider navigationProvider) ||
                    !navigationProvider.HasNavigationTarget)
                    continue;

                // 已选中的 NPC 暂不可用时不跳到后续目标，避免场景加载状态改变任务导航顺序。
                return navigationProvider.TryGetNavigationTarget(out target, out offset);
            }

            offset = Vector3.zero;
            target = null;
            return false;
        }

        #endregion

        #region 监听生命周期

        /// <summary>创建并启动阶段所有 ObjectiveRuntime；失败时释放已启动订阅。</summary>
        /// <exception cref="InvalidOperationException">Definition 创建目标运行时失败时抛出。</exception>
        public void StartListening()
        {
            if (listening)
            {
                return;
            }

            try
            {
                for (int index = 0; index < stageDefinition.Objectives.Count; index++)
                {
                    TaskObjectiveDefinition objective = stageDefinition.Objectives[index];
                    ITaskObjectiveRuntime objectiveRuntime = objective.CreateRuntime(
                        taskRuntime.CreateObjectiveContext(this, objective.ObjectiveId));
                    if (objectiveRuntime == null)
                    {
                        throw new InvalidOperationException(
                            $"目标 Definition {objective.GetType().FullName} 返回了空运行时。");
                    }

                    objectiveRuntimeList.Add(objectiveRuntime);
                    objectiveRuntime.StartListening();
                }

                listening = true;
                Debug.Log(
                    $"[TaskStageRuntime] 任务 {taskRuntime.TaskId} 阶段 {StageId} 监听已启动，objectiveCount={objectiveRuntimeList.Count}。");
            }
            catch
            {
                StopListening();
                throw;
            }
        }

        /// <summary>反向停止并清理本阶段所有 ObjectiveRuntime 订阅。</summary>
        public void StopListening()
        {
            if (!listening && objectiveRuntimeList.Count == 0)
            {
                return;
            }

            for (int index = objectiveRuntimeList.Count - 1; index >= 0; index--)
            {
                objectiveRuntimeList[index].StopListening();
            }

            objectiveRuntimeList.Clear();
            listening = false;
            Debug.Log($"[TaskStageRuntime] 任务 {taskRuntime.TaskId} 阶段 {StageId} 监听已停止。");
        }

        #endregion
    }
}
