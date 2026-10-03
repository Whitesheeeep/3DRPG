using System;
using UnityEngine;
using WS_Modules.CustomEventSystem;

namespace RPG.TaskSystemNS
{
    /// <summary>
    /// 表示从接取到领奖完成的整条任务实例，并持有唯一可存档记录。
    /// </summary>
    public sealed class TaskRuntime
    {
        #region 依赖字段

        // 依赖字段：静态 Definition 描述玩法；Record 保存唯一玩家事实；注册表按目标类型创建 ObjectiveRuntime。
        private readonly TaskDefinition definition;
        private readonly TaskRecord record;
        private readonly TaskObjectiveHandlerRegistry objectiveHandlerRegistry;

        #endregion

        #region 运行时状态

        // 当前阶段引用与避免目标回调重入阶段切换的同步标记。
        private TaskStageRuntime currentStageRuntime;
        private bool startingStageRuntime;
        private bool processingStageCompletion;
        private bool pendingStageCompletion;
        private bool suppressObjectiveProgressDuringStart;

        #endregion

        #region 构造与查询

        /// <summary>创建由任务系统持有的完整任务运行实例。</summary>
        /// <param name="definition">静态任务定义。</param>
        /// <param name="record">该任务唯一的可存档状态。</param>
        /// <param name="objectiveHandlerRegistry">创建目标运行时的注册表。</param>
        /// <exception cref="ArgumentNullException">必需依赖为空时抛出。</exception>
        /// <exception cref="ArgumentException">定义与记录不匹配时抛出。</exception>
        public TaskRuntime(
            TaskDefinition definition,
            TaskRecord record,
            TaskObjectiveHandlerRegistry objectiveHandlerRegistry)
        {
            this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
            this.record = record ?? throw new ArgumentNullException(nameof(record));
            this.objectiveHandlerRegistry =
                objectiveHandlerRegistry ?? throw new ArgumentNullException(nameof(objectiveHandlerRegistry));

            if (record.TaskId != definition.TaskId ||
                !definition.TryGetStage(record.CurrentStageId, out _, out _))
            {
                throw new ArgumentException("任务定义与记录必须属于同一任务，且记录阶段必须存在。", nameof(record));
            }
        }

        /// <summary>获取稳定任务标识。</summary>
        public TaskId TaskId => record.TaskId;

        /// <summary>获取当前可存档状态。</summary>
        public TaskRecord Record => record;

        /// <summary>获取当前阶段运行时；待领奖任务没有活动阶段运行时。</summary>
        public TaskStageRuntime CurrentStageRuntime => currentStageRuntime;

        /// <summary>获取当前阶段是否正在接收目标事件。</summary>
        public bool IsListening => currentStageRuntime != null && currentStageRuntime.IsListening;

        #endregion

        #region 生命周期

        /// <summary>
        /// 启动当前 InProgress 阶段的目标监听；恢复 Claimable 记录时保持无监听状态。
        /// </summary>
        /// <exception cref="InvalidOperationException">目标 Handler 缺失或监听创建失败时抛出。</exception>
        public void Start()
        {
            Start(false, false);
        }

        /// <summary>启动阶段监听，并按需延后阶段检查或忽略订阅建立期间的同步进度回调。</summary>
        /// <param name="deferStageCompletion">是否将阶段完成检查交给接取流程在接取事件后处理。</param>
        /// <param name="suppressProgressDuringStart">是否丢弃恢复监听期间同步触发的目标进度写入。</param>
        internal void Start(bool deferStageCompletion, bool suppressProgressDuringStart)
        {
            if (record.State == E_TaskLifecycleState.Claimable || currentStageRuntime != null)
            {
                return;
            }

            if (!definition.TryGetStage(record.CurrentStageId, out TaskStageDefinition stage, out _))
            {
                throw new InvalidOperationException($"任务 {TaskId} 当前阶段不存在：{record.CurrentStageId}。");
            }

            StartStageRuntime(stage, suppressProgressDuringStart);
            if (!deferStageCompletion)
            {
                ProcessPendingStageCompletion();
            }
        }

        /// <summary>处理阶段监听建立期间排队的同步完成信号。</summary>
        internal void ProcessDeferredStageCompletion() => ProcessPendingStageCompletion();

        /// <summary>停止并释放当前阶段全部目标监听，任务记录仍由本实例持有。</summary>
        public void Stop()
        {
            TaskStageRuntime previousStageRuntime = currentStageRuntime;
            currentStageRuntime = null;
            previousStageRuntime?.StopListening();
        }

        #endregion

        #region 阶段与目标进度

        // 目标写入入口与阶段推进由同一 TaskRuntime 串行处理。
        /// <summary>向目标运行时提供与当前阶段绑定的进度上下文。</summary>
        /// <param name="stageRuntime">上下文所属阶段实例。</param>
        /// <param name="objectiveId">目标标识。</param>
        /// <returns>目标受限写入上下文。</returns>
        internal ITaskObjectiveRuntimeContext CreateObjectiveContext(
            TaskStageRuntime stageRuntime,
            ObjectiveId objectiveId)
        {
            if (!record.TryGetProgress(objectiveId, out TaskObjectiveProgress progress))
            {
                throw new InvalidOperationException(
                    $"任务 {TaskId} 阶段 {record.CurrentStageId} 缺少目标进度 {objectiveId}。");
            }

            return new ObjectiveRuntimeContext(this, stageRuntime, progress);
        }

        /// <summary>
        /// 校验目标回调仍属于当前阶段后更新唯一记录，并排队检查阶段完成。
        /// </summary>
        /// <param name="stageRuntime">回调所属阶段实例。</param>
        /// <param name="progress">当前目标进度对象。</param>
        /// <param name="value">增量或新的绝对进度。</param>
        /// <param name="absolute">是否覆盖为绝对值。</param>
        internal void ApplyObjectiveProgress(
            TaskStageRuntime stageRuntime,
            TaskObjectiveProgress progress,
            int value,
            bool absolute)
        {
            if (!ReferenceEquals(currentStageRuntime, stageRuntime) ||
                suppressObjectiveProgressDuringStart ||
                record.State != E_TaskLifecycleState.InProgress)
            {
                return;
            }

            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "目标进度值不能为负数。");
            }

            int previousValue = progress.Current;
            bool changed = absolute
                ? record.SetObjectiveProgress(progress.ObjectiveId, value)
                : record.AddObjectiveProgress(progress.ObjectiveId, value);
            if (!changed)
            {
                return;
            }

            if (stageRuntime.IsComplete)
            {
                pendingStageCompletion = true;
            }

            try
            {
                Debug.Log("[TaskRuntime] 触发 TaskObjectiveProgressChangedEventArgs 事件。");
                EventSystem.EventTrigger_Type(
                    typeof(TaskObjectiveProgressChangedEventArgs),
                    new TaskObjectiveProgressChangedEventArgs(
                        TaskId,
                        record.CurrentStageId,
                        progress.ObjectiveId,
                        previousValue,
                        progress.Current));
            }
            finally
            {
                if (pendingStageCompletion && !startingStageRuntime && !processingStageCompletion)
                {
                    ProcessPendingStageCompletion();
                }
            }
        }

        /// <summary>启动阶段运行时并延迟处理订阅期间同步完成的目标。</summary>
        /// <param name="stage">需要运行的阶段定义。</param>
        /// <param name="suppressProgressDuringStart">是否丢弃监听建立期间同步触发的进度回调。</param>
        private void StartStageRuntime(TaskStageDefinition stage, bool suppressProgressDuringStart = false)
        {
            var nextStageRuntime = new TaskStageRuntime(this, stage, objectiveHandlerRegistry);
            currentStageRuntime = nextStageRuntime;
            startingStageRuntime = true;
            suppressObjectiveProgressDuringStart = suppressProgressDuringStart;
            try
            {
                nextStageRuntime.StartListening();
            }
            catch
            {
                currentStageRuntime = null;
                nextStageRuntime.StopListening();
                throw;
            }
            finally
            {
                startingStageRuntime = false;
                suppressObjectiveProgressDuringStart = false;
            }
        }

        /// <summary>按阶段顺序切换任务，并在最终阶段完成后保留待领奖任务实例。</summary>
        private void ProcessPendingStageCompletion()
        {
            if (!pendingStageCompletion || processingStageCompletion || currentStageRuntime == null)
            {
                return;
            }

            processingStageCompletion = true;
            try
            {
                while (pendingStageCompletion && currentStageRuntime != null)
                {
                    pendingStageCompletion = false;
                    TaskStageRuntime completedStageRuntime = currentStageRuntime;
                    if (!completedStageRuntime.IsComplete ||
                        !definition.TryGetStage(completedStageRuntime.StageId, out _, out int stageIndex))
                    {
                        continue;
                    }

                    TaskStageId previousStageId = completedStageRuntime.StageId;
                    currentStageRuntime = null;
                    completedStageRuntime.StopListening();

                    // 如果当前阶段是最后一个阶段，则将任务状态设置为 Claimable 并触发事件；否则切换到下一个阶段。
                    if (stageIndex + 1 < definition.Stages.Count)
                    {
                        TaskStageDefinition nextStage = definition.Stages[stageIndex + 1];
                        // 更新记录Data 的数据
                        record.ActivateStage(nextStage);
                        StartStageRuntime(nextStage);

                        Debug.Log("[TaskRuntime] 触发 TaskStageChangedEventArgs 事件。");
                        EventSystem.EventTrigger_Type(
                            typeof(TaskStageChangedEventArgs),
                            new TaskStageChangedEventArgs(TaskId, previousStageId, nextStage.StageId));
                        Debug.Log($"[TaskRuntime] 任务 {TaskId} 已切换到阶段 {nextStage.StageId}。");
                    }
                    else
                    {
                        record.SetState(E_TaskLifecycleState.Claimable);
                        Debug.Log("[TaskRuntime] 触发 TaskStateChangedEventArgs 事件。");
                        EventSystem.EventTrigger_Type(
                            typeof(TaskStateChangedEventArgs),
                            new TaskStateChangedEventArgs(
                                TaskId,
                                E_TaskLifecycleState.InProgress,
                                E_TaskLifecycleState.Claimable));
                        Debug.Log("[TaskRuntime] 触发 TaskRewardClaimableEventArgs 事件。");
                        EventSystem.EventTrigger_Type(
                            typeof(TaskRewardClaimableEventArgs),
                            new TaskRewardClaimableEventArgs(TaskId));
                        Debug.Log($"[TaskRuntime] 任务 {TaskId} 已完成最后阶段并进入 Claimable。");
                    }
                }
            }
            finally
            {
                processingStageCompletion = false;
            }
        }

        #endregion

        #region 目标进度上下文

        /// <summary>把目标写入绑定到任务和阶段实例的受限上下文。</summary>
        private sealed class ObjectiveRuntimeContext : ITaskObjectiveRuntimeContext
        {
            #region 依赖字段

            // 依赖字段：TaskRuntime 负责校验阶段有效性并提交进度；目标对象只持有自身进度引用。
            private readonly TaskRuntime taskRuntime;
            private readonly TaskStageRuntime stageRuntime;
            private readonly TaskObjectiveProgress progress;

            #endregion

            /// <summary>创建限定在所属任务和阶段的目标上下文。</summary>
            /// <param name="taskRuntime">任务运行实例。</param>
            /// <param name="stageRuntime">阶段运行实例。</param>
            /// <param name="progress">Record 中的目标进度对象。</param>
            internal ObjectiveRuntimeContext(
                TaskRuntime taskRuntime,
                TaskStageRuntime stageRuntime,
                TaskObjectiveProgress progress)
            {
                this.taskRuntime = taskRuntime;
                this.stageRuntime = stageRuntime;
                this.progress = progress;
            }

            /// <summary>获取任务标识。</summary>
            public TaskId TaskId => taskRuntime.TaskId;

            /// <summary>获取所属阶段标识。</summary>
            public TaskStageId StageId => stageRuntime.StageId;

            /// <summary>获取目标标识。</summary>
            public ObjectiveId ObjectiveId => progress.ObjectiveId;

            /// <summary>获取目标需求数量。</summary>
            public int Required => progress.Required;

            /// <summary>获取目标当前进度。</summary>
            public int Current => progress.Current;

            /// <summary>累加事件型目标进度。</summary>
            /// <param name="delta">非负增加量。</param>
            public void AddProgress(int delta) =>
                taskRuntime.ApplyObjectiveProgress(stageRuntime, progress, delta, false);

            /// <summary>设置状态型目标的绝对进度。</summary>
            /// <param name="value">非负当前值。</param>
            public void SetProgress(int value) =>
                taskRuntime.ApplyObjectiveProgress(stageRuntime, progress, value, true);
        }

        #endregion
    }
}
