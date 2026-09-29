using System;
using System.Collections.Generic;
using RPG.CurrencySystemNS;
using RPG.SaveSystem;
using UnityEngine;
using WS_Modules.BusinessArchitecture;
using WS_Modules.CustomEventSystem;

namespace RPG.TaskSystem
{
    /// <summary>
    /// 统一编排任务资格查询、接取、阶段运行时、领奖和存档恢复的业务 System。
    /// </summary>
    public sealed class TaskProgressSystem : AbstractSystem
    {
        #region 依赖字段

        // 依赖字段：显式 Handler 注册表解释配置；奖励 Handler 通过钱包执行原子货币发放。
        private readonly TaskObjectiveHandlerRegistry objectiveHandlerRegistry;
        private readonly TaskConditionHandlerRegistry conditionHandlerRegistry;
        private readonly TaskCurrencyRewardHandler currencyRewardHandler;

        #endregion

        #region 运行时状态

        // key：活动 TaskId；value：该任务当前阶段的监听运行时。
        private readonly Dictionary<TaskId, TaskRuntime> runtimeByTaskIdMap =
            new Dictionary<TaskId, TaskRuntime>();
        private readonly Queue<TaskId> pendingStageCheckQueue = new Queue<TaskId>();
        private readonly HashSet<TaskId> pendingStageCheckTaskIds = new HashSet<TaskId>();
        private readonly object rewardClaimGate = new object();
        private readonly HashSet<TaskId> rewardClaimInProgressTaskIds = new HashSet<TaskId>();

        private TaskManager taskManager;
        private SaveManager saveManager;
        private bool creatingRuntime;
        private bool processingStageChecks;
        private bool initializedForTests;

        #endregion

        #region 构造与生命周期

        /// <summary>
        /// 创建任务生命周期编排 System。
        /// </summary>
        /// <param name="objectiveHandlerRegistry">目标定义到监听 Handler 的注册表。</param>
        /// <param name="conditionHandlerRegistry">接取条件定义到评估 Handler 的注册表。</param>
        /// <param name="currencyRewardHandler">负责预检并原子发放货币奖励的 Handler。</param>
        /// <exception cref="ArgumentNullException">任一依赖为空时抛出。</exception>
        public TaskProgressSystem(
            TaskObjectiveHandlerRegistry objectiveHandlerRegistry,
            TaskConditionHandlerRegistry conditionHandlerRegistry,
            TaskCurrencyRewardHandler currencyRewardHandler)
        {
            this.objectiveHandlerRegistry =
                objectiveHandlerRegistry ?? throw new ArgumentNullException(nameof(objectiveHandlerRegistry));
            this.conditionHandlerRegistry =
                conditionHandlerRegistry ?? throw new ArgumentNullException(nameof(conditionHandlerRegistry));
            this.currencyRewardHandler =
                currencyRewardHandler ?? throw new ArgumentNullException(nameof(currencyRewardHandler));
        }

        /// <summary>
        /// 获取目标 Handler 注册表，供配置组合根在架构启动前注册玩法 Handler。
        /// </summary>
        public TaskObjectiveHandlerRegistry ObjectiveHandlerRegistry => objectiveHandlerRegistry;

        /// <summary>
        /// 获取 System 是否仍持有活动 Manager，会话销毁后为 false。
        /// </summary>
        internal bool IsInitialized => taskManager != null;

        /// <summary>
        /// 初始化任务流程、存档模块和活动任务监听。
        /// </summary>
        protected override void OnInit()
        {
            taskManager = TaskManager.Instance;
            saveManager = this.GetManager<SaveManager>();
            saveManager.RegisterModule(new TaskSaveModule(taskManager));
            saveManager.OperationCompleted += OnSaveOperationCompleted;
            Debug.Log("[TaskProgressSystem] 已初始化任务生命周期和存档模块。");
            RebuildRuntimes();
        }

        /// <summary>
        /// 注销存档通知、停止阶段监听并清理本次业务会话状态。
        /// </summary>
        protected override void OnDeinit()
        {
            if (saveManager != null)
            {
                saveManager.OperationCompleted -= OnSaveOperationCompleted;
            }

            StopAllRuntimes();
            taskManager?.ClearPlayerTaskState();
            saveManager = null;
            taskManager = null;
            initializedForTests = false;
            Debug.Log("[TaskProgressSystem] 已停止任务监听并清理会话状态。");
        }

        #endregion

        #region 任务资格与命令

        /// <summary>
        /// 查询任务当前能否接取并返回所有未满足条件的结构化原因。
        /// </summary>
        /// <param name="taskId">任务标识。</param>
        /// <returns>任务资格与阻塞原因。</returns>
        public TaskAvailabilityResult GetAvailability(TaskId taskId)
        {
            EnsureInitialized();
            if (!taskManager.TryGetDefinition(taskId, out TaskDefinition definition))
            {
                return new TaskAvailabilityResult(TaskAvailabilityStatus.NotFound, null);
            }

            if (taskManager.IsTaskCompleted(taskId))
            {
                return new TaskAvailabilityResult(TaskAvailabilityStatus.Completed, null);
            }

            // 如果已经有活动记录，说明任务正在进行中，直接返回 Active 状态。
            if (taskManager.TryGetActiveRecord(taskId, out _))
            {
                return new TaskAvailabilityResult(TaskAvailabilityStatus.Active, null);
            }

            var reasons = new List<TaskAvailabilityReason>();
            for (int index = 0; index < definition.UnlockConditions.Count; index++)
            {
                TaskConditionDefinition condition = definition.UnlockConditions[index];
                ITaskConditionHandler handler = conditionHandlerRegistry.Resolve(condition);
                TaskAvailabilityReason reason = handler.Evaluate(condition, taskManager);
                if (reason != null)
                {
                    reasons.Add(reason);
                }
            }

            return reasons.Count == 0
                ? new TaskAvailabilityResult(TaskAvailabilityStatus.Available, null)
                : new TaskAvailabilityResult(TaskAvailabilityStatus.Locked, reasons.AsReadOnly());
        }

        /// <summary>
        /// 通过统一入口校验条件、创建活动记录并启动首阶段监听。
        /// </summary>
        /// <param name="taskId">待接取任务。</param>
        /// <param name="source">发起接取的调用来源，仅用于事实事件诊断。</param>
        /// <returns>接取结果及拒绝原因。</returns>
        public TaskAcceptResult TryAcceptTask(TaskId taskId, TaskAcceptSource source)
        {
            EnsureInitialized();
            TaskAvailabilityResult availability = GetAvailability(taskId);
            if (availability.Status != TaskAvailabilityStatus.Available)
            {
                return new TaskAcceptResult(MapAvailabilityFailure(availability.Status), availability);
            }

            TaskManager currentTaskManager = taskManager;
            currentTaskManager.TryGetDefinition(taskId, out TaskDefinition definition);
            ValidateHandlerRegistrations(definition);

            // 如果没有成功创建活动记录，说明在多线程或多调用源的情况下已经有其他接取流程抢先创建了记录。
            if (!currentTaskManager.TryCreateActiveRecord(taskId, out _))
            {
                TaskAvailabilityResult latestAvailability = GetAvailability(taskId);
                return new TaskAcceptResult(MapAvailabilityFailure(latestAvailability.Status), latestAvailability);
            }

            try
            {
                if (!TryCreateRuntimeInternal(taskId, false))
                {
                    throw new InvalidOperationException($"任务 {taskId} 已创建记录，但首阶段运行时未能建立。 ");
                }

                if (!currentTaskManager.CommitAccepted(taskId, source))
                {
                    throw new InvalidOperationException($"任务 {taskId} 首阶段监听建立后无法提交接取事实。 ");
                }
            }
            catch
            {
                RemoveRuntime(taskId);
                currentTaskManager.RollbackUncommittedTask(taskId);
                Debug.LogError($"[TaskProgressSystem] 任务 {taskId} 接取失败，已撤销临时活动记录。");
                throw;
            }

            Debug.Log($"[TaskProgressSystem] 任务 {taskId} 接取成功，source={source}。");
            ProcessPendingStageChecks();
            return new TaskAcceptResult(TaskCommandFailure.None, availability);
        }

        /// <summary>
        /// 在货币奖励预检成功并原子发放后，将待提交任务记为已完成。
        /// </summary>
        /// <param name="taskId">待领奖任务。</param>
        /// <returns>领奖成功或失败原因。</returns>
        public TaskClaimResult TryClaimReward(TaskId taskId)
        {
            EnsureInitialized();
            lock (rewardClaimGate)
            {
                if (!rewardClaimInProgressTaskIds.Add(taskId))
                {
                    Debug.LogWarning($"[TaskProgressSystem] 任务 {taskId} 已有领奖流程执行中，拒绝重入领奖请求。");
                    return new TaskClaimResult(TaskCommandFailure.RewardClaimInProgress, null);
                }
            }

            try
            {
                return TryClaimRewardInternal(taskId);
            }
            finally
            {
                lock (rewardClaimGate)
                {
                    rewardClaimInProgressTaskIds.Remove(taskId);
                }
            }
        }

        /// <summary>
        /// 预检并发放奖励，再提交任务完成事实。
        /// </summary>
        /// <param name="taskId">待领奖任务。</param>
        /// <returns>领奖成功或结构化业务拒绝。</returns>
        private TaskClaimResult TryClaimRewardInternal(TaskId taskId)
        {
            if (!taskManager.TryGetDefinition(taskId, out TaskDefinition definition))
            {
                Debug.LogWarning($"[TaskProgressSystem] 任务 {taskId} 不存在，拒绝领奖。");
                return new TaskClaimResult(TaskCommandFailure.TaskNotFound, null);
            }

            if (!taskManager.TryGetActiveRecord(taskId, out TaskRecord record) ||
                record.State != TaskLifecycleState.Claimable)
            {
                Debug.LogWarning($"[TaskProgressSystem] 任务 {taskId} 当前不处于 Claimable，拒绝领奖。");
                return new TaskClaimResult(TaskCommandFailure.NotClaimable, null);
            }

            CurrencyOperationResult preflight = currencyRewardHandler.CanGrant(definition.Rewards);
            if (!preflight.Succeeded)
            {
                Debug.LogWarning(
                    $"[TaskProgressSystem] 任务 {taskId} 领奖预检未通过，currencyStatus={preflight.Status}；保留 Claimable 状态。");
                return new TaskClaimResult(TaskCommandFailure.RewardRejected, preflight.Status);
            }

            CurrencyOperationResult grant = currencyRewardHandler.Grant(definition.Rewards);
            if (!grant.Succeeded)
            {
                Debug.LogWarning(
                    $"[TaskProgressSystem] 任务 {taskId} 货币发放被钱包拒绝，currencyStatus={grant.Status}；保留 Claimable 状态。");
                return new TaskClaimResult(TaskCommandFailure.RewardRejected, grant.Status);
            }

            RemoveRuntime(taskId);
            if (!taskManager.TryMarkCompleted(taskId))
            {
                throw new InvalidOperationException(
                    $"任务 {taskId} 货币已发放，但 Claimable 任务状态无法完成提交。");
            }

            Debug.Log($"[TaskProgressSystem] 任务 {taskId} 领奖成功并记为完成。");
            return new TaskClaimResult(TaskCommandFailure.None, grant.Status);
        }

        #endregion

        #region 运行时监听与阶段推进

        /// <summary>
        /// 为指定活动任务创建并启动当前阶段的运行时。
        /// </summary>
        /// <param name="taskId">活动任务标识。</param>
        /// <returns>成功创建时返回 true；运行时已存在或任务记录不存在时返回 false。</returns>
        public bool TryCreateRuntime(TaskId taskId)
        {
            EnsureInitialized();
            return TryCreateRuntimeInternal(taskId, true);
        }

        /// <summary>
        /// 停止并移除指定活动任务的阶段运行时。
        /// </summary>
        /// <param name="taskId">任务标识。</param>
        /// <returns>找到并移除运行时时返回 true。</returns>
        public bool RemoveRuntime(TaskId taskId)
        {
            if (!runtimeByTaskIdMap.TryGetValue(taskId, out TaskRuntime runtime))
            {
                return false;
            }

            runtime.StopListening();
            runtimeByTaskIdMap.Remove(taskId);
            return true;
        }

        /// <summary>
        /// 根据 Manager 中的活动阶段重建全部目标运行时。
        /// </summary>
        public void RebuildRuntimes()
        {
            EnsureInitialized();
            StopAllRuntimes();
            IReadOnlyList<TaskRecord> activeRecords = taskManager.ActiveRecords;
            for (int index = 0; index < activeRecords.Count; index++)
            {
                if (activeRecords[index].State == TaskLifecycleState.InProgress)
                {
                    TryCreateRuntimeInternal(activeRecords[index].TaskId, false);
                }
            }

            ProcessPendingStageChecks();
            Debug.Log($"[TaskProgressSystem] 已重建活动任务阶段监听，active={activeRecords.Count}。");
        }

        /// <summary>
        /// 尝试获取指定任务当前阶段运行时。
        /// </summary>
        /// <param name="taskId">任务标识。</param>
        /// <param name="runtime">找到的运行时。</param>
        /// <returns>找到运行时时返回 true。</returns>
        public bool TryGetRuntime(TaskId taskId, out TaskRuntime runtime) =>
            runtimeByTaskIdMap.TryGetValue(taskId, out runtime);

        /// <summary>
        /// 创建运行时并按需要在订阅完成后处理同步到达的进度。
        /// </summary>
        /// <param name="taskId">活动任务。</param>
        /// <param name="processPending">运行时加入集合后是否处理阶段完成队列。</param>
        /// <returns>成功创建时返回 true。</returns>
        private bool TryCreateRuntimeInternal(TaskId taskId, bool processPending)
        {
            if (runtimeByTaskIdMap.ContainsKey(taskId) ||
                !taskManager.TryGetActiveRecord(taskId, out TaskRecord record) ||
                record.State != TaskLifecycleState.InProgress ||
                !taskManager.TryGetDefinition(taskId, out TaskDefinition definition) ||
                !definition.TryGetStage(record.CurrentStageId, out TaskStageDefinition stage, out _))
            {
                return false;
            }

            var runtime = new TaskRuntime(
                taskManager,
                definition,
                stage,
                record,
                objectiveHandlerRegistry,
                OnObjectiveProgressChanged);

            bool wasCreatingRuntime = creatingRuntime;
            creatingRuntime = true;
            try
            {
                runtime.StartListening();
                runtimeByTaskIdMap.Add(taskId, runtime);
            }
            catch
            {
                runtime.StopListening();
                throw;
            }
            finally
            {
                creatingRuntime = wasCreatingRuntime;
            }

            if (processPending && !creatingRuntime && !processingStageChecks)
            {
                ProcessPendingStageChecks();
            }

            return true;
        }

        /// <summary>
        /// 将目标进度变化排入阶段检查队列，避免在目标事件回调中重入阶段切换。
        /// </summary>
        /// <param name="taskId">变化所属任务。</param>
        /// <param name="stageId">变化所属阶段。</param>
        private void OnObjectiveProgressChanged(TaskId taskId, TaskStageId stageId)
        {
            if (!pendingStageCheckTaskIds.Add(taskId))
            {
                return;
            }

            pendingStageCheckQueue.Enqueue(taskId);
            if (!creatingRuntime && !processingStageChecks)
            {
                ProcessPendingStageChecks();
            }
        }

        /// <summary>
        /// 顺序停止旧阶段、切换记录并建立新监听；最后阶段完成时转为待提交。
        /// </summary>
        private void ProcessPendingStageChecks()
        {
            if (creatingRuntime || processingStageChecks)
            {
                return;
            }

            processingStageChecks = true;
            try
            {
                while (pendingStageCheckQueue.Count > 0)
                {
                    TaskId taskId = pendingStageCheckQueue.Dequeue();
                    pendingStageCheckTaskIds.Remove(taskId);
                    if (!taskManager.IsCurrentStageComplete(taskId) ||
                        !taskManager.TryGetActiveRecord(taskId, out TaskRecord record) ||
                        !taskManager.TryGetDefinition(taskId, out TaskDefinition definition) ||
                        !definition.TryGetStage(record.CurrentStageId, out _, out int stageIndex))
                    {
                        continue;
                    }

                    RemoveRuntime(taskId);
                    if (stageIndex + 1 < definition.Stages.Count)
                    {
                        TaskStageDefinition nextStage = definition.Stages[stageIndex + 1];
                        if (!taskManager.TryAdvanceStage(taskId, nextStage, out TaskStageId previousStageId))
                        {
                            throw new InvalidOperationException($"任务 {taskId} 当前阶段完成后无法进入下一阶段。");
                        }

                        if (!TryCreateRuntimeInternal(taskId, false))
                        {
                            throw new InvalidOperationException($"任务 {taskId} 新阶段 {nextStage.StageId} 监听无法建立。");
                        }

                        // 新阶段监听已经可用后再发布事实，避免接收方观察到尚未运行的阶段。
                        EventSystem.EventTrigger_Type(
                            typeof(TaskStageChangedEventArgs),
                            new TaskStageChangedEventArgs(taskId, previousStageId, nextStage.StageId));
                        Debug.Log($"[TaskProgressSystem] 任务 {taskId} 已切换阶段，stage={nextStage.StageId}。");
                    }
                    else if (taskManager.TryMarkClaimable(taskId))
                    {
                        Debug.Log($"[TaskProgressSystem] 任务 {taskId} 最后阶段完成，状态转为 Claimable。");
                    }
                }
            }
            finally
            {
                processingStageChecks = false;
            }
        }

        /// <summary>
        /// 预解析任务所有阶段的目标 Handler，避免接取后才发现配置缺失。
        /// </summary>
        /// <param name="definition">任务定义。</param>
        private void ValidateHandlerRegistrations(TaskDefinition definition)
        {
            for (int stageIndex = 0; stageIndex < definition.Stages.Count; stageIndex++)
            {
                TaskStageDefinition stage = definition.Stages[stageIndex];
                for (int objectiveIndex = 0; objectiveIndex < stage.Objectives.Count; objectiveIndex++)
                {
                    objectiveHandlerRegistry.Resolve(stage.Objectives[objectiveIndex]);
                }
            }
        }

        /// <summary>
        /// 停止所有任务阶段监听并清空运行时和待处理队列。
        /// </summary>
        private void StopAllRuntimes()
        {
            foreach (TaskRuntime runtime in runtimeByTaskIdMap.Values)
            {
                runtime.StopListening();
            }

            runtimeByTaskIdMap.Clear();
            pendingStageCheckQueue.Clear();
            pendingStageCheckTaskIds.Clear();
        }

        #endregion

        #region 存档与测试初始化

        /// <summary>
        /// 在任务模块成功加载后恢复当前阶段目标订阅。
        /// </summary>
        /// <param name="completion">存档操作完成通知。</param>
        private void OnSaveOperationCompleted(SaveOperationCompleted completion)
        {
            if (completion != null &&
                completion.Kind == SaveOperationKind.Load &&
                completion.IsSuccess)
            {
                RebuildRuntimes();
            }
        }

        /// <summary>
        /// 初始化不依赖 BusinessArchitecture 生命周期的 Odin 手动测试实例。
        /// </summary>
        internal void InitializeForTests()
        {
            if (taskManager != null)
            {
                throw new InvalidOperationException("TaskProgressSystem 已经初始化，不能重复进入测试初始化路径。");
            }

            taskManager = TaskManager.Instance;
            initializedForTests = true;
        }

        /// <summary>
        /// 停止测试监听并清理测试期间写入的任务事实。
        /// </summary>
        internal void DeinitializeForTests()
        {
            if (!initializedForTests)
            {
                return;
            }

            StopAllRuntimes();
            taskManager.ClearPlayerTaskState();
            taskManager = null;
            initializedForTests = false;
        }

        /// <summary>
        /// 确认 System 已经完成架构初始化或测试初始化。
        /// </summary>
        /// <exception cref="InvalidOperationException">System 尚未初始化时抛出。</exception>
        private void EnsureInitialized()
        {
            if (taskManager == null)
            {
                throw new InvalidOperationException("TaskProgressSystem 尚未完成初始化。 ");
            }
        }

        /// <summary>
        /// 将资格查询状态转换为命令拒绝原因。
        /// </summary>
        /// <param name="status">当前接取状态。</param>
        /// <returns>对应业务拒绝原因。</returns>
        private static TaskCommandFailure MapAvailabilityFailure(TaskAvailabilityStatus status)
        {
            switch (status)
            {
                case TaskAvailabilityStatus.NotFound:
                    return TaskCommandFailure.TaskNotFound;
                case TaskAvailabilityStatus.Active:
                    return TaskCommandFailure.AlreadyActive;
                case TaskAvailabilityStatus.Completed:
                    return TaskCommandFailure.AlreadyCompleted;
                case TaskAvailabilityStatus.Locked:
                    return TaskCommandFailure.ConditionNotMet;
                default:
                    throw new ArgumentOutOfRangeException(nameof(status), status, "可接取状态不应转换为拒绝结果。");
            }
        }

        #endregion
    }
}
