using System;
using System.Collections.Generic;

namespace RPG.TaskSystem
{
    /// <summary>
    /// 表示一个活动任务的运行时实例，封装目标 Handler 和事件订阅生命周期。
    /// </summary>
    public sealed class TaskRuntime
    {
        private readonly TaskManager taskManager;
        private readonly TaskDefinition definition;
        private readonly TaskRecord record;
        private readonly TaskObjectiveHandlerRegistry handlerRegistry;
        private readonly List<ITaskObjectiveRuntime> objectiveRuntimes =
            new List<ITaskObjectiveRuntime>();

        private bool listening;

        /// <summary>
        /// 创建一个活动任务运行时。
        /// </summary>
        /// <param name="taskManager">任务状态 Manager。</param>
        /// <param name="definition">任务静态定义。</param>
        /// <param name="record">任务运行时记录。</param>
        /// <param name="handlerRegistry">目标 Handler 注册表。</param>
        /// <exception cref="ArgumentNullException">任一依赖为空时抛出。</exception>
        public TaskRuntime(
            TaskManager taskManager,
            TaskDefinition definition,
            TaskRecord record,
            TaskObjectiveHandlerRegistry handlerRegistry)
        {
            this.taskManager = taskManager ?? throw new ArgumentNullException(nameof(taskManager));
            this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
            this.record = record ?? throw new ArgumentNullException(nameof(record));
            this.handlerRegistry = handlerRegistry ?? throw new ArgumentNullException(nameof(handlerRegistry));

            if (record.TaskId != definition.TaskId)
            {
                throw new ArgumentException("任务运行时记录与任务定义的 TaskId 不一致。", nameof(record));
            }
        }

        /// <summary>
        /// 获取当前任务标识。
        /// </summary>
        public TaskId TaskId => record.TaskId;

        /// <summary>
        /// 获取当前任务运行时记录。
        /// </summary>
        public TaskRecord Record => record;

        /// <summary>
        /// 获取当前任务是否已经启动目标事件监听。
        /// </summary>
        public bool IsListening => listening;

        /// <summary>
        /// 为该任务的全部目标创建并启动事件订阅；重复调用没有副作用。
        /// </summary>
        /// <exception cref="InvalidOperationException">目标 Handler 缺失或创建运行时失败时抛出。</exception>
        public void StartListening()
        {
            if (listening)
            {
                return;
            }

            try
            {
                for (int index = 0; index < definition.Objectives.Count; index++)
                {
                    TaskObjectiveDefinition objective = definition.Objectives[index];
                    if (!record.TryGetProgress(objective.ObjectiveId, out TaskObjectiveProgress progress))
                    {
                        throw new InvalidOperationException(
                            $"任务 {TaskId} 的目标进度缺少定义 {objective.ObjectiveId}。 ");
                    }

                    ITaskObjectiveHandler handler = handlerRegistry.Resolve(objective);
                    ITaskObjectiveRuntime runtime = handler.CreateRuntime(
                        objective,
                        new RuntimeContext(taskManager, TaskId, objective.ObjectiveId, progress));
                    if (runtime == null)
                    {
                        throw new InvalidOperationException(
                            $"目标 Handler {handler.GetType().FullName} 返回了空运行时。 ");
                    }

                    objectiveRuntimes.Add(runtime);
                    runtime.StartListening();
                }

                listening = true;
            }
            catch
            {
                StopListening();
                throw;
            }
        }

        /// <summary>
        /// 停止并清理该任务的全部目标事件订阅；重复调用安全。
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
        }

        /// <summary>
        /// 为 Handler 提供受限的任务目标进度上下文。
        /// </summary>
        private sealed class RuntimeContext : ITaskObjectiveRuntimeContext
        {
            private readonly TaskManager taskManager;
            private readonly TaskObjectiveProgress progress;

            /// <summary>
            /// 创建目标运行时上下文。
            /// </summary>
            /// <param name="taskManager">任务状态 Manager。</param>
            /// <param name="taskId">任务标识。</param>
            /// <param name="objectiveId">目标标识。</param>
            /// <param name="progress">目标当前进度。</param>
            internal RuntimeContext(
                TaskManager taskManager,
                TaskId taskId,
                ObjectiveId objectiveId,
                TaskObjectiveProgress progress)
            {
                this.taskManager = taskManager;
                TaskId = taskId;
                ObjectiveId = objectiveId;
                this.progress = progress;
            }

            /// <summary>
            /// 获取所属任务标识。
            /// </summary>
            public TaskId TaskId { get; }

            /// <summary>
            /// 获取目标标识。
            /// </summary>
            public ObjectiveId ObjectiveId { get; }

            /// <summary>
            /// 获取目标需求数量。
            /// </summary>
            public int Required => progress.Required;

            /// <summary>
            /// 获取目标当前进度。
            /// </summary>
            public int Current => progress.Current;

            /// <summary>
            /// 将事件增量提交给 TaskManager。
            /// </summary>
            /// <param name="delta">非负增加量。</param>
            public void AddProgress(int delta)
            {
                taskManager.ApplyObjectiveDelta(TaskId, ObjectiveId, delta);
            }

            /// <summary>
            /// 将外部业务当前值提交给 TaskManager。
            /// </summary>
            /// <param name="value">非负当前值。</param>
            public void SetProgress(int value)
            {
                taskManager.SetObjectiveProgress(TaskId, ObjectiveId, value);
            }
        }
    }
}
