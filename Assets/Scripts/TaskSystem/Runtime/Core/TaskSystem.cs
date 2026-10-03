using System;
using System.Collections.Generic;
using RPG.RedDotSystemNS;
using RPG.RewardSystemNS;
using RPG.SaveSystem;
using UnityEngine;
using WS_Modules.BusinessArchitecture;
using WS_Modules.CustomEventSystem;

namespace RPG.TaskSystemNS
{
    /// <summary>
    /// 在 Unity 主线程管理玩家任务集合，并统一编排接取、领奖、存档和跨任务事实。
    /// 网络或后台线程收到的任务请求必须先派发到主线程再调用本系统。
    /// </summary>
    public sealed class TaskSystem : AbstractSystem
    {
        #region 依赖字段
        // 依赖字段：注册表解释多态定义；RewardSystem 原子准备并提交跨领域奖励；SaveManager 管理任务快照。
        private readonly TaskObjectiveHandlerRegistry objectiveHandlerRegistry;
        private readonly TaskConditionHandlerRegistry conditionHandlerRegistry;
        private readonly RewardSystem rewardSystem;
        private readonly RedDotSystem redDotSystem;
        private readonly TaskRedDotConfig taskRedDotConfig;
        private SaveManager saveManager;
        #endregion

        #region 玩家任务状态
        // 活动任务与跨任务集合分别保存单任务实例和玩家级事实。
        // key：活动 TaskId；value：持有该任务权威 Record 和当前阶段运行时的完整任务实例。
        private readonly Dictionary<TaskId, TaskRuntime> taskRuntimeByTaskIdMap =
            new Dictionary<TaskId, TaskRuntime>();
        private readonly HashSet<TaskId> completedTaskIds = new HashSet<TaskId>();
        private readonly HashSet<TaskId> unreadTaskIds = new HashSet<TaskId>();
        // 领奖保护集合用于拒绝同一任务的重复领奖请求，避免 RewardSystem 重入或多次提交。
        private readonly HashSet<TaskId> rewardClaimInProgressTaskIds = new HashSet<TaskId>();
        private TaskId trackedTaskId;
        private bool initializedForTests;
        private bool initialized;
        #endregion

        #region 构造与生命周期
        /// <summary>创建任务业务系统及其显式 Handler 注册表和奖励入口。</summary>
        /// <param name="objectiveHandlerRegistry">目标定义到目标 Handler 的注册表。</param>
        /// <param name="conditionHandlerRegistry">接取条件定义到条件 Handler 的注册表。</param>
        /// <param name="rewardSystem">负责准备并提交通用奖励的系统。</param>
        /// <param name="redDotSystem">负责任务未读红点的统一运行时系统。</param>
        /// <param name="taskRedDotConfig">主线与支线未读红点节点配置。</param>
        /// <exception cref="ArgumentNullException">任一依赖为空时抛出。</exception>
        public TaskSystem(
            TaskObjectiveHandlerRegistry objectiveHandlerRegistry,
            TaskConditionHandlerRegistry conditionHandlerRegistry,
            RewardSystem rewardSystem,
            RedDotSystem redDotSystem,
            TaskRedDotConfig taskRedDotConfig)
        {
            this.objectiveHandlerRegistry =
                objectiveHandlerRegistry ?? throw new ArgumentNullException(nameof(objectiveHandlerRegistry));
            this.conditionHandlerRegistry =
                conditionHandlerRegistry ?? throw new ArgumentNullException(nameof(conditionHandlerRegistry));
            this.rewardSystem = rewardSystem ?? throw new ArgumentNullException(nameof(rewardSystem));
            this.redDotSystem = redDotSystem ?? throw new ArgumentNullException(nameof(redDotSystem));
            this.taskRedDotConfig = taskRedDotConfig ?? throw new ArgumentNullException(nameof(taskRedDotConfig));
        }

        /// <summary>获取目标 Handler 注册表，供架构装配阶段扩展默认玩法。</summary>
        public TaskObjectiveHandlerRegistry ObjectiveHandlerRegistry => objectiveHandlerRegistry;

        /// <summary>获取 TaskSystem 是否已初始化。</summary>
        internal bool IsInitialized => initialized;

        /// <summary>获取当前活动任务记录的稳定排序副本。</summary>
        public IReadOnlyList<TaskRecord> ActiveRecords
        {
            get
            {
                var recordList = new List<TaskRecord>(taskRuntimeByTaskIdMap.Count);
                foreach (TaskRuntime taskRuntime in taskRuntimeByTaskIdMap.Values)
                {
                    recordList.Add(taskRuntime.Record);
                }

                recordList.Sort((left, right) => left.TaskId.CompareTo(right.TaskId));
                return recordList.AsReadOnly();

            }
        }

        /// <summary>获取已完成任务 ID 的副本。</summary>
        public IReadOnlyCollection<TaskId> CompletedTaskIds
        {
            get
            {
                return new List<TaskId>(completedTaskIds).AsReadOnly();

            }
        }

        /// <summary>获取尚未确认查看的活动任务 ID 副本。</summary>
        public IReadOnlyCollection<TaskId> UnreadTaskIds
        {
            get
            {
                return new List<TaskId>(unreadTaskIds).AsReadOnly();

            }
        }

        /// <summary>获取当前追踪任务；没有追踪任务时为无效 ID。</summary>
        public TaskId TrackedTaskId
        {
            get
            {
                return trackedTaskId;

            }
        }

        #region 生命周期
        /// <summary>注册任务存档模块并在加载完成后重建阶段监听。</summary>
        protected override void OnInit()
        {
            taskRedDotConfig.Validate();
            saveManager = this.GetManager<SaveManager>();
            saveManager.RegisterModule(new TaskSaveModule(this));
            saveManager.OperationCompleted += OnSaveOperationCompleted;
            initialized = true;
            Debug.Log("[TaskSystem] 已初始化任务集合与存档模块。");
            RefreshUnreadRedDots();
            RebuildRuntimes();
        }

        /// <summary>停止任务监听、注销存档通知并清空本会话玩家任务状态。</summary>
        protected override void OnDeinit()
        {
            if (saveManager != null)
            {
                saveManager.OperationCompleted -= OnSaveOperationCompleted;
            }

            // 架构销毁时 RedDotSystem 可能已经先释放运行时节点，因此这里只清理任务监听和内存状态。
            ClearPlayerTaskStateInternal(false);
            saveManager = null;
            initialized = false;
            initializedForTests = false;
            Debug.Log("[TaskSystem] 已停止全部任务运行时并清理会话状态。");
        }
        #endregion

        #endregion

        #region 查询与跨任务状态
        // 资格查询与活动任务实例读取。
        /// <summary>按 TaskId 查询资格状态并返回未满足条件的结构化原因。</summary>
        /// <param name="taskId">任务标识。</param>
        /// <returns>当前资格和原因列表。</returns>
        public TaskAvailabilityResult GetAvailability(TaskId taskId)
        {
            EnsureInitialized();
            if (!TaskConfigManager.Instance.TryGetDefinition(taskId, out TaskDefinition definition))
            {
                return new TaskAvailabilityResult(TaskAvailabilityStatus.NotFound, null);
            }

            if (IsTaskCompleted(taskId))
            {
                return new TaskAvailabilityResult(TaskAvailabilityStatus.Completed, null);
            }

            if (TryGetRuntime(taskId, out _))
            {
                return new TaskAvailabilityResult(TaskAvailabilityStatus.Active, null);
            }

            var reasonList = new List<TaskAvailabilityReason>();
            for (int index = 0; index < definition.UnlockConditions.Count; index++)
            {
                TaskConditionDefinition condition = definition.UnlockConditions[index];
                ITaskConditionHandler handler = conditionHandlerRegistry.Resolve(condition);
                TaskAvailabilityReason reason = handler.Evaluate(condition, this);
                if (reason != null)
                {
                    reasonList.Add(reason);
                }
            }

            return reasonList.Count == 0
                ? new TaskAvailabilityResult(TaskAvailabilityStatus.Available, null)
                : new TaskAvailabilityResult(TaskAvailabilityStatus.Locked, reasonList.AsReadOnly());
        }

        /// <summary>尝试读取指定活动任务实例。</summary>
        /// <param name="taskId">任务标识。</param>
        /// <param name="taskRuntime">找到的整条任务实例。</param>
        /// <returns>活动实例存在时返回 true。</returns>
        public bool TryGetRuntime(TaskId taskId, out TaskRuntime taskRuntime)
        {
            return taskRuntimeByTaskIdMap.TryGetValue(taskId, out taskRuntime);

        }

        /// <summary>尝试读取活动任务当前权威 Record。</summary>
        /// <param name="taskId">任务标识。</param>
        /// <param name="record">找到的活动任务 Record。</param>
        /// <returns>活动任务存在时返回 true。</returns>
        public bool TryGetActiveRecord(TaskId taskId, out TaskRecord record)
        {
            if (TryGetRuntime(taskId, out TaskRuntime taskRuntime))
            {
                record = taskRuntime.Record;
                return true;
            }

            record = null;
            return false;
        }

        // 跨任务完成、追踪与未读事实。
        /// <summary>判断任务是否已记录为完成。</summary>
        /// <param name="taskId">任务标识。</param>
        /// <returns>任务存在于完成集合时返回 true。</returns>
        public bool IsTaskCompleted(TaskId taskId)
        {
            return completedTaskIds.Contains(taskId);

        }

        /// <summary>将指定活动任务设为追踪任务并发送变化事件。</summary>
        /// <param name="taskId">活动任务标识。</param>
        /// <returns>任务活动且追踪状态更新成功时返回 true。</returns>
        public bool TrySetTrackedTask(TaskId taskId)
        {
            TaskId previousTaskId;
            if (!taskRuntimeByTaskIdMap.ContainsKey(taskId))
            {
                return false;
            }

            if (trackedTaskId == taskId)
            {
                return true;
            }

            previousTaskId = trackedTaskId;
            trackedTaskId = taskId;


            Debug.Log("[TaskSystem] 触发 TaskTrackedChangedEventArgs 事件。");
            EventSystem.EventTrigger_Type(
                typeof(TaskTrackedChangedEventArgs),
                new TaskTrackedChangedEventArgs(previousTaskId, taskId));
            return true;
        }

        /// <summary>清除当前追踪任务并发送变化事件。</summary>
        /// <returns>追踪状态已清除时返回 true。</returns>
        public bool ClearTrackedTask()
        {
            TaskId previousTaskId;
            if (!trackedTaskId.IsValid)
            {
                return false;
            }

            previousTaskId = trackedTaskId;
            trackedTaskId = default;


            Debug.Log("[TaskSystem] 触发 TaskTrackedChangedEventArgs 事件。");
            EventSystem.EventTrigger_Type(
                typeof(TaskTrackedChangedEventArgs),
                new TaskTrackedChangedEventArgs(previousTaskId, default));
            return true;
        }

        /// <summary>确认查看任务并清除其未读状态。</summary>
        /// <param name="taskId">活动任务标识。</param>
        /// <returns>存在未读状态且已清除时返回 true。</returns>
        public bool AcknowledgeTask(TaskId taskId)
        {
            if (!unreadTaskIds.Remove(taskId))
            {
                return false;
            }

            RefreshUnreadRedDots();

            Debug.Log("[TaskSystem] 触发 TaskAcknowledgedEventArgs 事件。");
            EventSystem.EventTrigger_Type(typeof(TaskAcknowledgedEventArgs), new TaskAcknowledgedEventArgs(taskId));
            return true;
        }
        #endregion

        #region 接取与领奖
        // 接取统一创建 TaskRuntime，并在首阶段监听建立后提交活动事实。
        /// <summary>统一校验资格、建立整条任务实例并启动首阶段。</summary>
        /// <param name="taskId">待接取任务标识。</param>
        /// <param name="source">调用接取入口的来源。</param>
        /// <returns>接取结果及拒绝原因。</returns>
        public TaskAcceptResult TryAcceptTask(TaskId taskId, E_TaskAcceptSource source)
        {
            EnsureInitialized();
            TaskAvailabilityResult availability = GetAvailability(taskId);
            if (availability.Status != TaskAvailabilityStatus.Available)
            {
                return new TaskAcceptResult(MapAvailabilityFailure(availability.Status), availability);
            }

            TaskDefinition definition = TaskConfigManager.Instance.GetRequiredDefinition(taskId);
            ValidateHandlerRegistrations(definition);
            var record = new TaskRecord(definition);
            var taskRuntime = new TaskRuntime(definition, record, objectiveHandlerRegistry);
            if (taskRuntimeByTaskIdMap.ContainsKey(taskId) || completedTaskIds.Contains(taskId))
            {
                TaskAvailabilityResult latestAvailability = GetAvailability(taskId);
                return new TaskAcceptResult(MapAvailabilityFailure(latestAvailability.Status), latestAvailability);
            }

            taskRuntimeByTaskIdMap.Add(taskId, taskRuntime);


            try
            {
                taskRuntime.Start(true, false);
            }
            catch
            {
                taskRuntime.Stop();
                taskRuntimeByTaskIdMap.Remove(taskId);


                Debug.LogError($"[TaskSystem] 任务 {taskId} 首阶段启动失败，已撤销临时任务实例。");
                throw;
            }

            unreadTaskIds.Add(taskId);

            RefreshUnreadRedDots();

            Debug.Log("[TaskSystem] 触发 TaskAcceptedEventArgs 事件。");
            try
            {
                EventSystem.EventTrigger_Type(
                    typeof(TaskAcceptedEventArgs),
                    new TaskAcceptedEventArgs(taskId, source));
            }
            finally
            {
                taskRuntime.ProcessDeferredStageCompletion();
            }

            Debug.Log($"[TaskSystem] 任务 {taskId} 接取成功，source={source}。");
            return new TaskAcceptResult(TaskCommandFailure.None, availability);
        }

        // 奖励领取由系统跨越 RewardSystem 和玩家任务集合完成提交。
        /// <summary>通过 RewardSystem 提交待领奖任务的全部奖励并完成任务事实。</summary>
        /// <param name="taskId">待领奖任务标识。</param>
        /// <returns>领奖成功或结构化拒绝结果。</returns>
        public TaskClaimResult TryClaimReward(TaskId taskId)
        {
            EnsureInitialized();
            if (!rewardClaimInProgressTaskIds.Add(taskId))
            {
                Debug.LogWarning($"[TaskSystem] 任务 {taskId} 已有领奖流程执行中，拒绝重入领奖请求。");
                return new TaskClaimResult(TaskCommandFailure.RewardClaimInProgress, null);
            }


            try
            {
                return TryClaimRewardInternal(taskId);
            }
            finally
            {
                rewardClaimInProgressTaskIds.Remove(taskId);

            }
        }

        /// <summary>准备奖励、提交奖励和完成事实，再安全发布提交后的通知。</summary>
        /// <param name="taskId">待领奖任务标识。</param>
        /// <returns>领奖成功或结构化拒绝结果。</returns>
        private TaskClaimResult TryClaimRewardInternal(TaskId taskId)
        {
            if (!TaskConfigManager.Instance.TryGetDefinition(taskId, out TaskDefinition definition))
            {
                Debug.LogWarning($"[TaskSystem] 任务 {taskId} 不存在，拒绝领奖。");
                return new TaskClaimResult(TaskCommandFailure.TaskNotFound, null);
            }

            if (!TryGetRuntime(taskId, out TaskRuntime taskRuntime) ||
                taskRuntime.Record.State != E_TaskLifecycleState.Claimable)
            {
                Debug.LogWarning($"[TaskSystem] 任务 {taskId} 当前不处于 Claimable，拒绝领奖。");
                return new TaskClaimResult(TaskCommandFailure.NotClaimable, null);
            }

            if (!rewardSystem.TryPrepareGrant(
                    definition.Rewards,
                    out PreparedRewardGrant preparedGrant,
                    out RewardGrantResult preparationResult))
            {
                Debug.LogWarning(
                    $"[TaskSystem] 任务 {taskId} 奖励预检未通过，domain={preparationResult.FailureDomain}, currency={preparationResult.CurrencyStatus}, inventory={preparationResult.InventoryStatus}；保留 Claimable 状态。");
                return new TaskClaimResult(TaskCommandFailure.RewardRejected, preparationResult);
            }

            if (!preparedGrant.CanCommit)
            {
                Debug.LogWarning($"[TaskSystem] 任务 {taskId} 奖励准备状态已变化，保留 Claimable 并允许重试。");
                return new TaskClaimResult(
                    TaskCommandFailure.RewardRejected,
                    new RewardGrantResult(RewardGrantFailureDomain.StateChanged));
            }

            // 奖励和完成集合先提交，后续事件回调只能观察到一致的已完成状态。
            preparedGrant.CommitState();
            TaskId previousTrackedTaskId = default;
            bool trackingChanged;
            taskRuntime.Stop();
            if (!taskRuntimeByTaskIdMap.Remove(taskId) || taskRuntime.Record.State != E_TaskLifecycleState.Claimable)
            {
                throw new InvalidOperationException($"任务 {taskId} 奖励已提交，但完成状态无法提交。");
            }

            completedTaskIds.Add(taskId);
            unreadTaskIds.Remove(taskId);
            trackingChanged = trackedTaskId == taskId;
            if (trackingChanged)
            {
                previousTrackedTaskId = trackedTaskId;
                trackedTaskId = default;
            }

            RefreshUnreadRedDots();

            if (trackingChanged)
            {
                Debug.Log("[TaskSystem] 触发 TaskTrackedChangedEventArgs 事件。");
                try
                {
                    EventSystem.EventTrigger_Type(
                        typeof(TaskTrackedChangedEventArgs),
                        new TaskTrackedChangedEventArgs(previousTrackedTaskId, default));
                }
                catch (Exception exception)
                {
                    Debug.LogError(
                        $"[TaskSystem] 完成事实已提交，但追踪事件通知异常，taskId={taskId}。\n{exception}");
                }
            }

            Debug.Log("[TaskSystem] 触发 TaskCompletedEventArgs 事件。");
            try
            {
                EventSystem.EventTrigger_Type(
                    typeof(TaskCompletedEventArgs),
                    new TaskCompletedEventArgs(taskId));
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[TaskSystem] 完成事实已提交，但任务完成事件通知异常，taskId={taskId}。\n{exception}");
            }

            try
            {
                preparedGrant.PublishNotifications();
            }
            catch (Exception exception)
            {
                Debug.LogError($"[TaskSystem] 任务奖励已提交，但奖励通知失败，taskId={taskId}。\n{exception}");
            }

            Debug.Log($"[TaskSystem] 任务 {taskId} 领奖成功并记为完成。");
            return new TaskClaimResult(TaskCommandFailure.None, RewardGrantResult.Success());
        }
        #endregion

        #region 运行时集合
        /// <summary>启动已存在任务实例的当前阶段监听。</summary>
        /// <param name="taskId">活动任务标识。</param>
        /// <returns>找到任务实例时返回 true。</returns>
        public bool TryCreateRuntime(TaskId taskId)
        {
            EnsureInitialized();
            if (!TryGetRuntime(taskId, out TaskRuntime taskRuntime))
            {
                return false;
            }

            taskRuntime.Start();
            return true;
        }

        /// <summary>停止全部阶段监听后依据活动 Record 重建监听。</summary>
        public void RebuildRuntimes()
        {
            EnsureInitialized();
            TaskRuntime[] taskRuntimeArray;
            taskRuntimeArray = new TaskRuntime[taskRuntimeByTaskIdMap.Count];
            taskRuntimeByTaskIdMap.Values.CopyTo(taskRuntimeArray, 0);


            for (int index = 0; index < taskRuntimeArray.Length; index++)
            {
                taskRuntimeArray[index].Stop();
            }

            for (int index = 0; index < taskRuntimeArray.Length; index++)
            {
                if (taskRuntimeArray[index].Record.State == E_TaskLifecycleState.InProgress)
                {
                    taskRuntimeArray[index].Start(false, true);
                }
            }

            Debug.Log($"[TaskSystem] 已重建活动任务阶段监听，active={taskRuntimeArray.Length}。");
        }

        /// <summary>清空活动、完成、追踪、未读和领奖保护状态。</summary>
        public void ClearPlayerTaskState()
        {
            ClearPlayerTaskStateInternal(true);
        }

        /// <summary>清空玩家任务事实，并按调用阶段决定是否同步仍在运行的红点系统。</summary>
        /// <param name="synchronizeUnreadRedDots">业务主动清空时同步红点；架构销毁时传 false。</param>
        private void ClearPlayerTaskStateInternal(bool synchronizeUnreadRedDots)
        {
            TaskRuntime[] taskRuntimeArray;
            taskRuntimeArray = new TaskRuntime[taskRuntimeByTaskIdMap.Count];
            taskRuntimeByTaskIdMap.Values.CopyTo(taskRuntimeArray, 0);
            taskRuntimeByTaskIdMap.Clear();
            completedTaskIds.Clear();
            unreadTaskIds.Clear();
            rewardClaimInProgressTaskIds.Clear();
            trackedTaskId = default;


            for (int index = 0; index < taskRuntimeArray.Length; index++)
            {
                taskRuntimeArray[index].Stop();
            }

            if (synchronizeUnreadRedDots)
            {
                RefreshUnreadRedDots();
            }

            Debug.Log($"[TaskSystem] 已清空玩家任务数据，active={taskRuntimeArray.Length}。");
        }
        #endregion

        #region 存档快照
        /// <summary>从活动 TaskRuntime 所持 Record 采集任务存档快照。</summary>
        /// <returns>包含活动、完成、追踪和未读事实的快照。</returns>
        public TaskSaveSnapshot CaptureSnapshot()
        {
            EnsureInitialized();
            var snapshot = new TaskSaveSnapshot
            {
                ActiveTasks = new List<TaskRecordSnapshot>(),
                CompletedTaskIds = new List<string>(),
                TrackedTaskId = trackedTaskId.IsValid ? trackedTaskId.Value : string.Empty,
                UnreadTaskIds = new List<string>()
            };

            foreach (TaskRuntime taskRuntime in taskRuntimeByTaskIdMap.Values)
            {
                TaskRecord record = taskRuntime.Record;
                var recordSnapshot = new TaskRecordSnapshot
                {
                    TaskId = record.TaskId.Value,
                    CurrentStageId = record.CurrentStageId.Value,
                    State = record.State,
                    ObjectiveProgress = new List<TaskObjectiveProgressSnapshot>()
                };
                foreach (TaskObjectiveProgress progress in record.ObjectiveProgress)
                {
                    recordSnapshot.ObjectiveProgress.Add(new TaskObjectiveProgressSnapshot
                    {
                        ObjectiveId = progress.ObjectiveId.Value,
                        Current = progress.Current,
                        Required = progress.Required
                    });
                }

                snapshot.ActiveTasks.Add(recordSnapshot);
            }

            foreach (TaskId taskId in completedTaskIds)
            {
                snapshot.CompletedTaskIds.Add(taskId.Value);
            }

            foreach (TaskId taskId in unreadTaskIds)
            {
                snapshot.UnreadTaskIds.Add(taskId.Value);
            }

            return snapshot;

        }

        /// <summary>校验快照并以新建 TaskRuntime 原子替换当前玩家任务状态。</summary>
        /// <param name="snapshot">待恢复快照。</param>
        /// <exception cref="ArgumentNullException">快照为空时抛出。</exception>
        /// <exception cref="InvalidOperationException">任务定义与快照不匹配时抛出。</exception>
        public void RestoreSnapshot(TaskSaveSnapshot snapshot)
        {
            EnsureInitialized();
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            snapshot.ValidateShape();
            var restoredTaskRuntimeByTaskIdMap = new Dictionary<TaskId, TaskRuntime>();
            var restoredCompletedTaskIds = new HashSet<TaskId>();
            var restoredUnreadTaskIds = new HashSet<TaskId>();
            TaskId restoredTrackedTaskId = string.IsNullOrEmpty(snapshot.TrackedTaskId)
                ? default
                : new TaskId(snapshot.TrackedTaskId);

            RestoreActiveRuntimes(snapshot, restoredTaskRuntimeByTaskIdMap);
            RestoreCompletedIds(snapshot, restoredCompletedTaskIds, restoredTaskRuntimeByTaskIdMap);
            RestoreUnreadIds(snapshot, restoredTaskRuntimeByTaskIdMap, restoredUnreadTaskIds);
            if (restoredTrackedTaskId.IsValid && !restoredTaskRuntimeByTaskIdMap.ContainsKey(restoredTrackedTaskId))
            {
                throw new InvalidOperationException($"任务快照追踪了非活动任务：{restoredTrackedTaskId}。");
            }

            // 先同步替换所有玩家任务事实，再停止旧监听，避免注销回调观察到半更新的集合。
            TaskRuntime[] previousRuntimeArray;
            previousRuntimeArray = new TaskRuntime[taskRuntimeByTaskIdMap.Count];
            taskRuntimeByTaskIdMap.Values.CopyTo(previousRuntimeArray, 0);
            taskRuntimeByTaskIdMap.Clear();
            foreach (KeyValuePair<TaskId, TaskRuntime> restoredTaskRuntimeByTaskId in
                     restoredTaskRuntimeByTaskIdMap)
            {
                taskRuntimeByTaskIdMap.Add(restoredTaskRuntimeByTaskId.Key, restoredTaskRuntimeByTaskId.Value);
            }

            completedTaskIds.Clear();
            completedTaskIds.UnionWith(restoredCompletedTaskIds);
            unreadTaskIds.Clear();
            unreadTaskIds.UnionWith(restoredUnreadTaskIds);
            trackedTaskId = restoredTrackedTaskId;
            rewardClaimInProgressTaskIds.Clear();


            for (int index = 0; index < previousRuntimeArray.Length; index++)
            {
                previousRuntimeArray[index].Stop();
            }

            RefreshUnreadRedDots();
            Debug.Log(
                $"[TaskSystem] 已恢复任务快照，active={restoredTaskRuntimeByTaskIdMap.Count}, completed={restoredCompletedTaskIds.Count}。");
        }
        #endregion

        #region 恢复校验与测试初始化
        // 先构造临时任务实例和全局事实，校验通过后再替换当前状态。
        /// <summary>根据当前配置重建快照中的活动任务实例和权威 Record。</summary>
        /// <param name="snapshot">任务快照。</param>
        /// <param name="restoredTaskRuntimeByTaskIdMap">临时任务实例映射。</param>
        private void RestoreActiveRuntimes(
            TaskSaveSnapshot snapshot,
            Dictionary<TaskId, TaskRuntime> restoredTaskRuntimeByTaskIdMap)
        {
            foreach (TaskRecordSnapshot recordSnapshot in snapshot.ActiveTasks)
            {
                TaskId taskId = new TaskId(recordSnapshot.TaskId);
                TaskDefinition definition = TaskConfigManager.Instance.GetRequiredDefinition(taskId);
                ValidateHandlerRegistrations(definition);
                if (restoredTaskRuntimeByTaskIdMap.ContainsKey(taskId))
                {
                    throw new InvalidOperationException($"任务快照包含重复活动任务：{taskId}。");
                }

                TaskStageId stageId = new TaskStageId(recordSnapshot.CurrentStageId);
                if (!definition.TryGetStage(stageId, out TaskStageDefinition stage, out int stageIndex))
                {
                    throw new InvalidOperationException($"任务 {taskId} 存档阶段无法匹配定义：{stageId}。");
                }

                // 先构造 Record 并恢复目标进度，再校验阶段完成状态与生命周期状态是否一致。
                var record = new TaskRecord(definition);
                record.ActivateStage(stage);
                var restoredObjectiveIds = new HashSet<ObjectiveId>();
                foreach (TaskObjectiveProgressSnapshot progressSnapshot in recordSnapshot.ObjectiveProgress)
                {
                    ObjectiveId objectiveId = new ObjectiveId(progressSnapshot.ObjectiveId);
                    if (!restoredObjectiveIds.Add(objectiveId) ||
                        !record.TryGetProgress(objectiveId, out TaskObjectiveProgress progress))
                    {
                        throw new InvalidOperationException(
                            $"任务 {taskId} 当前阶段进度无法匹配定义：{objectiveId}。");
                    }

                    if (progress.Required != progressSnapshot.Required ||
                        progressSnapshot.Current < 0 ||
                        progressSnapshot.Current > progress.Required)
                    {
                        throw new InvalidOperationException(
                            $"任务 {taskId} 的目标进度范围或需求不匹配：{objectiveId}。");
                    }

                    record.SetObjectiveProgress(objectiveId, progressSnapshot.Current);
                }

                if (restoredObjectiveIds.Count != stage.Objectives.Count)
                {
                    throw new InvalidOperationException($"任务 {taskId} 当前阶段快照缺少目标进度。");
                }

                // 当前阶段完成时，生命周期状态必须为 Claimable；非最后阶段完成时，生命周期状态必须为 InProgress。
                bool stageComplete = record.IsCurrentStageComplete();
                bool isLastStage = stageIndex == definition.Stages.Count - 1;
                bool expectedClaimable = isLastStage && stageComplete;
                if ((recordSnapshot.State == E_TaskLifecycleState.Claimable) != expectedClaimable ||
                    (!isLastStage && stageComplete))
                {
                    throw new InvalidOperationException($"任务 {taskId} 的阶段状态与目标进度不一致。");
                }

                record.SetState(recordSnapshot.State);
                restoredTaskRuntimeByTaskIdMap.Add(
                    taskId,
                    new TaskRuntime(definition, record, objectiveHandlerRegistry));
            }
        }

        /// <summary>校验完成集合中的任务有效且不与活动集合重叠。</summary>
        /// <param name="snapshot">任务快照。</param>
        /// <param name="restoredCompletedTaskIds">临时已完成集合。</param>
        /// <param name="restoredTaskRuntimeByTaskIdMap">临时活动任务集合。</param>
        private static void RestoreCompletedIds(
            TaskSaveSnapshot snapshot,
            HashSet<TaskId> restoredCompletedTaskIds,
            Dictionary<TaskId, TaskRuntime> restoredTaskRuntimeByTaskIdMap)
        {
            foreach (string value in snapshot.CompletedTaskIds)
            {
                TaskId taskId = new TaskId(value);
                if (!TaskConfigManager.Instance.TryGetDefinition(taskId, out _) ||
                    restoredTaskRuntimeByTaskIdMap.ContainsKey(taskId) ||
                    !restoredCompletedTaskIds.Add(taskId))
                {
                    throw new InvalidOperationException($"已完成任务集合包含非法、重复或活动任务：{taskId}。");
                }
            }
        }

        /// <summary>校验未读集合只引用活动任务且没有重复 ID。</summary>
        /// <param name="snapshot">任务快照。</param>
        /// <param name="restoredTaskRuntimeByTaskIdMap">临时活动任务集合。</param>
        /// <param name="restoredUnreadTaskIds">临时未读集合。</param>
        private static void RestoreUnreadIds(
            TaskSaveSnapshot snapshot,
            Dictionary<TaskId, TaskRuntime> restoredTaskRuntimeByTaskIdMap,
            HashSet<TaskId> restoredUnreadTaskIds)
        {
            foreach (string value in snapshot.UnreadTaskIds)
            {
                TaskId taskId = new TaskId(value);
                if (!restoredTaskRuntimeByTaskIdMap.ContainsKey(taskId) || !restoredUnreadTaskIds.Add(taskId))
                {
                    throw new InvalidOperationException($"未读任务集合包含非法或重复任务：{taskId}。");
                }
            }
        }

        // 架构存档回调与 Odin 测试生命周期。
        /// <summary>在任务模块加载成功后重建进行中任务的阶段监听。</summary>
        /// <param name="completion">存档操作结果。</param>
        private void OnSaveOperationCompleted(SaveOperationCompleted completion)
        {
            // 存档加载成功后重建活动任务阶段监听，避免在加载前就触发阶段事件。
            if (completion is { Kind: SaveOperationKind.Load, IsSuccess: true })
            {
                RefreshUnreadRedDots();
                RebuildRuntimes();
            }
        }

        /// <summary>初始化独立 Odin 测试使用的任务系统实例。</summary>
        internal void InitializeForTests()
        {
            if (initialized)
            {
                throw new InvalidOperationException("TaskSystem 已初始化，不能重复初始化测试路径。");
            }

            initializedForTests = true;
            initialized = true;
            taskRedDotConfig.Validate();
            RefreshUnreadRedDots();
            Debug.Log("[TaskSystem] 已初始化 Odin 测试路径。");
        }

        /// <summary>停止测试监听并清理本轮玩家任务状态。</summary>
        internal void DeinitializeForTests()
        {
            if (!initializedForTests)
            {
                return;
            }

            ClearPlayerTaskState();
            initializedForTests = false;
            initialized = false;
            Debug.Log("[TaskSystem] 已结束 Odin 测试路径。");
        }

        /// <summary>确保系统已由 Architecture 或 Odin 测试初始化。</summary>
        /// <exception cref="InvalidOperationException">系统尚未初始化时抛出。</exception>
        private void EnsureInitialized()
        {
            if (!initialized)
            {
                throw new InvalidOperationException("[TaskSystem] 尚未完成系统初始化。");
            }
        }

        /// <summary>按当前未读任务集合更新主线与支线红点自身值。</summary>
        private void RefreshUnreadRedDots()
        {
            int mainCount = 0;
            int sideCount = 0;
            foreach (TaskId unreadTaskId in unreadTaskIds)
            {
                TaskDefinition definition = TaskConfigManager.Instance.GetRequiredDefinition(unreadTaskId);
                switch (definition.CategoryId.Value)
                {
                    case "main":
                        mainCount++;
                        break;
                    case "side":
                        sideCount++;
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"[TaskSystem] 未读任务 {unreadTaskId} 使用了不支持的红点分类：{definition.CategoryId.Value}。");
                }
            }

            // 先更新两个叶节点，RedDotSystem 会在现有帧末统一聚合并通知徽标。
            int previousMainCount = redDotSystem.GetSelfValue(taskRedDotConfig.MainUnreadKey);
            int previousSideCount = redDotSystem.GetSelfValue(taskRedDotConfig.SideUnreadKey);
            redDotSystem.SetSelfValue(taskRedDotConfig.MainUnreadKey, mainCount);
            redDotSystem.SetSelfValue(taskRedDotConfig.SideUnreadKey, sideCount);
            if (previousMainCount != mainCount || previousSideCount != sideCount)
            {
                Debug.Log(
                    $"[TaskSystem] 已同步未读任务红点自身值，main={previousMainCount}->{mainCount}, side={previousSideCount}->{sideCount}。");
            }
        }

        // 配置注册完整性和失败状态转换。
        /// <summary>确认活动任务全部目标都有已注册 Handler。</summary>
        /// <param name="definition">任务静态定义。</param>
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

        /// <summary>将资格查询状态转换为接取命令失败原因。</summary>
        /// <param name="status">资格查询状态。</param>
        /// <returns>接取业务结果枚举。</returns>
        private static TaskCommandFailure MapAvailabilityFailure(TaskAvailabilityStatus status)
        {
            switch (status)
            {
                case TaskAvailabilityStatus.NotFound:
                    return TaskCommandFailure.TaskNotFound;
                case TaskAvailabilityStatus.Locked:
                    return TaskCommandFailure.ConditionNotMet;
                case TaskAvailabilityStatus.Active:
                    return TaskCommandFailure.AlreadyActive;
                case TaskAvailabilityStatus.Completed:
                    return TaskCommandFailure.AlreadyCompleted;
                default:
                    return TaskCommandFailure.None;
            }
        }
        #endregion
    }
}
