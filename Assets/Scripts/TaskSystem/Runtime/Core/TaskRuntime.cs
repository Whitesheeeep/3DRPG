using System;
using System.Collections.Generic;
using UnityEngine;

namespace RPG.TaskSystem
{
    /// <summary>
    /// 表示一个活动任务当前阶段的目标运行时和事件订阅生命周期。
    /// </summary>
    public sealed class TaskRuntime
    {
        #region 依赖字段

        // 依赖字段：TaskManager 保存事实；目标注册表创建阶段 Handler；回调由 TaskProgressSystem 串行推进阶段。
        private readonly TaskManager taskManager;
        private readonly TaskDefinition definition;
        private readonly TaskStageDefinition stage;
        private readonly TaskRecord record;
        private readonly TaskObjectiveHandlerRegistry handlerRegistry;
        private readonly Action<TaskId, TaskStageId> onProgressChanged;

        #endregion

        #region 运行时状态

        private readonly List<ITaskObjectiveRuntime> objectiveRuntimes =
            new List<ITaskObjectiveRuntime>();
        private bool listening;

        #endregion

        #region 生命周期与查询

        /// <summary>
        /// 创建指定任务当前阶段的运行时。
        /// </summary>
        /// <param name="taskManager">任务事实 Manager。</param>
        /// <param name="definition">静态任务定义。</param>
        /// <param name="stage">当前阶段定义。</param>
        /// <param name="record">活动任务记录。</param>
        /// <param name="handlerRegistry">目标 Handler 注册表。</param>
        /// <param name="onProgressChanged">目标进度实际变化后的系统回调。</param>
        /// <exception cref="ArgumentNullException">必要依赖为空时抛出。</exception>
        /// <exception cref="ArgumentException">阶段、任务和运行时记录不匹配时抛出。</exception>
        public TaskRuntime(
            TaskManager taskManager,
            TaskDefinition definition,
            TaskStageDefinition stage,
            TaskRecord record,
            TaskObjectiveHandlerRegistry handlerRegistry,
            Action<TaskId, TaskStageId> onProgressChanged)
        {
            this.taskManager = taskManager ?? throw new ArgumentNullException(nameof(taskManager));
            this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
            this.stage = stage ?? throw new ArgumentNullException(nameof(stage));
            this.record = record ?? throw new ArgumentNullException(nameof(record));
            this.handlerRegistry = handlerRegistry ?? throw new ArgumentNullException(nameof(handlerRegistry));
            this.onProgressChanged = onProgressChanged ?? throw new ArgumentNullException(nameof(onProgressChanged));

            if (record.TaskId != definition.TaskId || record.CurrentStageId != stage.StageId)
            {
                throw new ArgumentException("任务运行时的定义、阶段和活动记录必须互相匹配。", nameof(record));
            }
        }

        /// <summary>
        /// 获取当前任务标识。
        /// </summary>
        public TaskId TaskId => record.TaskId;

        /// <summary>
        /// 获取当前阶段标识。
        /// </summary>
        public TaskStageId StageId => stage.StageId;

        /// <summary>
        /// 获取当前任务运行时记录。
        /// </summary>
        public TaskRecord Record => record;

        /// <summary>
        /// 获取当前阶段是否已启动目标事件监听。
        /// </summary>
        public bool IsListening => listening;

        /// <summary>
        /// 创建并启动当前阶段所有目标的监听；失败时撤销本阶段已经建立的订阅。
        /// </summary>
        /// <exception cref="InvalidOperationException">缺少 Handler 或 Handler 创建失败时抛出。</exception>
        public void StartListening()
        {
            if (listening)
            {
                return;
            }

            try
            {
                for (int index = 0; index < stage.Objectives.Count; index++)
                {
                    TaskObjectiveDefinition objective = stage.Objectives[index];
                    if (!record.TryGetProgress(objective.ObjectiveId, out TaskObjectiveProgress progress))
                    {
                        throw new InvalidOperationException(
                            $"任务 {TaskId} 阶段 {StageId} 缺少目标进度 {objective.ObjectiveId}。 ");
                    }

                    ITaskObjectiveHandler handler = handlerRegistry.Resolve(objective);
                    ITaskObjectiveRuntime runtime = handler.CreateRuntime(
                        objective,
                        new RuntimeContext(
                            taskManager,
                            TaskId,
                            StageId,
                            objective.ObjectiveId,
                            progress,
                            onProgressChanged));
                    if (runtime == null)
                    {
                        throw new InvalidOperationException(
                            $"目标 Handler {handler.GetType().FullName} 返回了空运行时。 ");
                    }

                    objectiveRuntimes.Add(runtime);
                    runtime.StartListening();
                }

                listening = true;
                Debug.Log($"[TaskRuntime] 任务 {TaskId} 阶段 {StageId} 已启动目标监听，objectiveCount={objectiveRuntimes.Count}。");
            }
            catch
            {
                StopListening();
                throw;
            }
        }

        /// <summary>
        /// 反向停止并清理当前阶段的全部目标监听；重复调用安全。
        /// </summary>
        public void StopListening()
        {
            if (!listening && objectiveRuntimes.Count == 0)
            {
                return;
            }

            for (int index = objectiveRuntimes.Count - 1; index >= 0; index--)
            {
                objectiveRuntimes[index].StopListening();
            }

            objectiveRuntimes.Clear();
            listening = false;
            Debug.Log($"[TaskRuntime] 任务 {TaskId} 阶段 {StageId} 已停止目标监听。");
        }

        #endregion

        #region 目标上下文

        /// <summary>
        /// 为 Handler 提供限定在所属任务和阶段内的进度修改入口。
        /// </summary>
        private sealed class RuntimeContext : ITaskObjectiveRuntimeContext
        {
            #region 依赖字段

            // 依赖字段：Manager 校验阶段令牌并写入事实，回调通知 System 检查阶段边界。
            private readonly TaskManager taskManager;
            private readonly TaskObjectiveProgress progress;
            private readonly Action<TaskId, TaskStageId> onProgressChanged;

            #endregion

            /// <summary>
            /// 创建目标进度上下文。
            /// </summary>
            /// <param name="taskManager">任务事实 Manager。</param>
            /// <param name="taskId">任务标识。</param>
            /// <param name="stageId">当前阶段标识。</param>
            /// <param name="objectiveId">目标标识。</param>
            /// <param name="progress">目标进度对象。</param>
            /// <param name="onProgressChanged">目标变化回调。</param>
            internal RuntimeContext(
                TaskManager taskManager,
                TaskId taskId,
                TaskStageId stageId,
                ObjectiveId objectiveId,
                TaskObjectiveProgress progress,
                Action<TaskId, TaskStageId> onProgressChanged)
            {
                this.taskManager = taskManager;
                TaskId = taskId;
                StageId = stageId;
                ObjectiveId = objectiveId;
                this.progress = progress;
                this.onProgressChanged = onProgressChanged;
            }

            /// <summary>获取所属任务标识。</summary>
            public TaskId TaskId { get; }

            /// <summary>获取所属阶段标识。</summary>
            public TaskStageId StageId { get; }

            /// <summary>获取目标标识。</summary>
            public ObjectiveId ObjectiveId { get; }

            /// <summary>获取目标需求数量。</summary>
            public int Required => progress.Required;

            /// <summary>获取目标当前进度。</summary>
            public int Current => progress.Current;

            /// <summary>
            /// 将事件增量提交给 Manager，并在进度变化后请求 System 检查阶段完成。
            /// </summary>
            /// <param name="delta">非负增加量。</param>
            public void AddProgress(int delta)
            {
                if (taskManager.ApplyObjectiveProgress(TaskId, StageId, ObjectiveId, delta, false))
                {
                    onProgressChanged(TaskId, StageId);
                }
            }

            /// <summary>
            /// 将外部业务当前值提交给 Manager，并在进度变化后请求 System 检查阶段完成。
            /// </summary>
            /// <param name="value">新的非负当前值。</param>
            public void SetProgress(int value)
            {
                if (taskManager.ApplyObjectiveProgress(TaskId, StageId, ObjectiveId, value, true))
                {
                    onProgressChanged(TaskId, StageId);
                }
            }
        }

        #endregion
    }
}
