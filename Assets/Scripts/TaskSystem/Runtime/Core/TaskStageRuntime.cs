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

        // 依赖字段：所属 TaskRuntime 提供权威 Record；注册表根据每个目标定义创建 ObjectiveRuntime。
        private readonly TaskRuntime taskRuntime;
        private readonly TaskStageDefinition stageDefinition;
        private readonly TaskObjectiveHandlerRegistry objectiveHandlerRegistry;

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
        /// <param name="objectiveHandlerRegistry">目标 Handler 注册表。</param>
        /// <exception cref="ArgumentNullException">必需依赖为空时抛出。</exception>
        /// <exception cref="InvalidOperationException">Record 缺少当前阶段目标进度时抛出。</exception>
        public TaskStageRuntime(
            TaskRuntime taskRuntime,
            TaskStageDefinition stageDefinition,
            TaskObjectiveHandlerRegistry objectiveHandlerRegistry)
        {
            this.taskRuntime = taskRuntime ?? throw new ArgumentNullException(nameof(taskRuntime));
            this.stageDefinition = stageDefinition ?? throw new ArgumentNullException(nameof(stageDefinition));
            this.objectiveHandlerRegistry =
                objectiveHandlerRegistry ?? throw new ArgumentNullException(nameof(objectiveHandlerRegistry));

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

        #endregion

        #region 监听生命周期

        /// <summary>创建并启动阶段所有 ObjectiveRuntime；失败时释放已启动订阅。</summary>
        /// <exception cref="InvalidOperationException">缺少 Handler 或创建目标运行时失败时抛出。</exception>
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
                    ITaskObjectiveHandler handler = objectiveHandlerRegistry.Resolve(objective);
                    ITaskObjectiveRuntime objectiveRuntime = handler.CreateRuntime(
                        objective,
                        taskRuntime.CreateObjectiveContext(this, objective.ObjectiveId));
                    if (objectiveRuntime == null)
                    {
                        throw new InvalidOperationException(
                            $"目标 Handler {handler.GetType().FullName} 返回了空运行时。");
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
