using System;
using System.Collections.Generic;
using RPG.SaveSystem;
using UnityEngine;
using WS_Modules.CustomEventSystem;
using WS_Modules.Singleton;

namespace RPG.TaskSystem
{
    /// <summary>
    /// 持有任务配置和玩家任务事实的纯 C# 单例 Manager。
    /// </summary>
    public sealed class TaskManager : SingletonBase<TaskManager>
    {
        #region 配置与状态字段

        // 配置锁独立于实例状态锁，使 ConfigInstaller 注入数据库时不创建 Manager 实例。
        private static readonly object configurationGate = new object();
        private readonly object stateGate = new object();

        // 活动任务表以 TaskId 为键，每个值仅保存当前阶段的运行时进度。
        private readonly Dictionary<TaskId, TaskRecord> taskRecordByIdMap =
            new Dictionary<TaskId, TaskRecord>();
        private readonly HashSet<TaskId> completedTaskIds = new HashSet<TaskId>();
        private readonly HashSet<TaskId> unreadTaskIds = new HashSet<TaskId>();

        private static TaskDatabase database;
        private static bool configured;
        private TaskId trackedTaskId;

        #endregion

        #region 生命周期与查询

        /// <summary>
        /// 创建任务 Manager；实例由 SingletonBase 通过私有无参构造函数创建。
        /// </summary>
        private TaskManager()
        {
        }

        /// <summary>
        /// 获取当前是否已经完成配置注入。
        /// </summary>
        public bool IsConfigured => configured;

        /// <summary>
        /// 获取当前任务数据库。
        /// </summary>
        public TaskDatabase Database
        {
            get
            {
                EnsureConfigured();
                return database;
            }
        }

        /// <summary>
        /// 获取当前活动任务的稳定列表副本。
        /// </summary>
        public IReadOnlyList<TaskRecord> ActiveRecords
        {
            get
            {
                lock (stateGate)
                {
                    var records = new List<TaskRecord>(taskRecordByIdMap.Values);
                    records.Sort((left, right) => left.TaskId.CompareTo(right.TaskId));
                    return records.AsReadOnly();
                }
            }
        }

        /// <summary>
        /// 获取已经完成的一次性任务标识副本。
        /// </summary>
        public IReadOnlyCollection<TaskId> CompletedTaskIds
        {
            get
            {
                lock (stateGate)
                {
                    return new List<TaskId>(completedTaskIds).AsReadOnly();
                }
            }
        }

        /// <summary>
        /// 获取当前尚未确认的任务标识副本。
        /// </summary>
        public IReadOnlyCollection<TaskId> UnreadTaskIds
        {
            get
            {
                lock (stateGate)
                {
                    return new List<TaskId>(unreadTaskIds).AsReadOnly();
                }
            }
        }

        /// <summary>
        /// 获取当前追踪任务；没有追踪任务时返回无效值。
        /// </summary>
        public TaskId TrackedTaskId
        {
            get
            {
                lock (stateGate)
                {
                    return trackedTaskId;
                }
            }
        }

        /// <summary>
        /// 注入任务数据库并在业务运行前校验全部独立任务资产。
        /// </summary>
        /// <param name="taskDatabase">集中引用任务定义的数据库资产。</param>
        /// <exception cref="ArgumentNullException">数据库为空时抛出。</exception>
        /// <exception cref="InvalidOperationException">重复注入不同数据库时抛出。</exception>
        public static void Initialize(TaskDatabase taskDatabase)
        {
            if (taskDatabase == null)
            {
                throw new ArgumentNullException(nameof(taskDatabase));
            }

            lock (configurationGate)
            {
                if (configured)
                {
                    if (!ReferenceEquals(database, taskDatabase))
                    {
                        throw new InvalidOperationException("TaskManager 已经注入其他 TaskDatabase。 ");
                    }

                    return;
                }

                taskDatabase.ValidateAndBuildIndex();
                database = taskDatabase;
                configured = true;
            }

            Debug.Log($"[TaskManager] 已注入任务数据库，definitionCount={taskDatabase.Definitions.Count}。");
        }

        /// <summary>
        /// 尝试按稳定标识获取任务定义资产。
        /// </summary>
        /// <param name="taskId">任务标识。</param>
        /// <param name="definition">找到的任务定义。</param>
        /// <returns>找到定义时返回 true。</returns>
        public bool TryGetDefinition(TaskId taskId, out TaskDefinition definition)
        {
            EnsureConfigured();
            return database.TryGetDefinition(taskId, out definition);
        }

        /// <summary>
        /// 尝试获取指定活动任务记录。
        /// </summary>
        /// <param name="taskId">任务标识。</param>
        /// <param name="record">找到的活动任务记录。</param>
        /// <returns>找到记录时返回 true。</returns>
        public bool TryGetActiveRecord(TaskId taskId, out TaskRecord record)
        {
            lock (stateGate)
            {
                return taskRecordByIdMap.TryGetValue(taskId, out record);
            }
        }

        /// <summary>
        /// 判断指定任务是否已经完成。
        /// </summary>
        /// <param name="taskId">任务标识。</param>
        /// <returns>任务存在于已完成集合时返回 true。</returns>
        public bool IsTaskCompleted(TaskId taskId)
        {
            lock (stateGate)
            {
                return completedTaskIds.Contains(taskId);
            }
        }

        #endregion

        #region 追踪与未读状态

        /// <summary>
        /// 将指定活动任务设置为当前追踪任务。
        /// </summary>
        /// <param name="taskId">待追踪任务标识。</param>
        /// <returns>任务活动且设置成功时返回 true。</returns>
        public bool TrySetTrackedTask(TaskId taskId)
        {
            TaskId previousTaskId;
            lock (stateGate)
            {
                if (!taskRecordByIdMap.ContainsKey(taskId))
                {
                    return false;
                }

                if (trackedTaskId == taskId)
                {
                    return true;
                }

                previousTaskId = trackedTaskId;
                trackedTaskId = taskId;
            }

            Publish(new TaskTrackedChangedEventArgs(previousTaskId, taskId));
            return true;
        }

        /// <summary>
        /// 清除当前追踪任务并发布追踪变化事件。
        /// </summary>
        /// <returns>存在追踪任务且已清除时返回 true。</returns>
        public bool ClearTrackedTask()
        {
            TaskId previousTaskId;
            lock (stateGate)
            {
                if (!trackedTaskId.IsValid)
                {
                    return false;
                }

                previousTaskId = trackedTaskId;
                trackedTaskId = default;
            }

            Publish(new TaskTrackedChangedEventArgs(previousTaskId, default));
            return true;
        }

        /// <summary>
        /// 确认查看具体活动任务并清除其未读事实。
        /// </summary>
        /// <param name="taskId">待确认任务标识。</param>
        /// <returns>任务存在未读事实并已清除时返回 true。</returns>
        public bool AcknowledgeTask(TaskId taskId)
        {
            lock (stateGate)
            {
                if (!unreadTaskIds.Remove(taskId))
                {
                    return false;
                }
            }

            Publish(new TaskAcknowledgedEventArgs(taskId));
            return true;
        }

        #endregion

        #region 生命周期状态变更

        /// <summary>
        /// 为统一接取流程创建尚未对外发布的活动记录。
        /// </summary>
        /// <param name="taskId">待接取任务标识。</param>
        /// <param name="record">创建的活动记录。</param>
        /// <returns>任务定义存在且未活动、未完成时返回 true。</returns>
        internal bool TryCreateActiveRecord(TaskId taskId, out TaskRecord record)
        {
            EnsureConfigured();
            lock (stateGate)
            {
                record = null;
                if (!database.TryGetDefinition(taskId, out TaskDefinition definition) ||
                    taskRecordByIdMap.ContainsKey(taskId) ||
                    completedTaskIds.Contains(taskId))
                {
                    return false;
                }

                record = new TaskRecord(definition);
                taskRecordByIdMap.Add(taskId, record);
                return true;
            }
        }

        /// <summary>
        /// 在首阶段目标监听建立后提交接取事实并加入未读集合。
        /// </summary>
        /// <param name="taskId">已经创建运行时的任务。</param>
        /// <param name="source">调用方来源。</param>
        /// <returns>活动记录存在时返回 true。</returns>
        internal bool CommitAccepted(TaskId taskId, TaskAcceptSource source)
        {
            lock (stateGate)
            {
                if (!taskRecordByIdMap.ContainsKey(taskId))
                {
                    return false;
                }

                unreadTaskIds.Add(taskId);
            }

            Publish(new TaskAcceptedEventArgs(taskId, source));
            return true;
        }

        /// <summary>
        /// 清理尚未成功提交接取事件的新活动记录。
        /// </summary>
        /// <param name="taskId">接取流程创建的任务标识。</param>
        internal void RollbackUncommittedTask(TaskId taskId)
        {
            lock (stateGate)
            {
                taskRecordByIdMap.Remove(taskId);
                unreadTaskIds.Remove(taskId);
            }
        }

        /// <summary>
        /// 检查活动任务当前阶段是否已经全部完成。
        /// </summary>
        /// <param name="taskId">任务标识。</param>
        /// <returns>当前阶段目标全部完成时返回 true。</returns>
        internal bool IsCurrentStageComplete(TaskId taskId)
        {
            lock (stateGate)
            {
                return taskRecordByIdMap.TryGetValue(taskId, out TaskRecord record) &&
                       record.State == TaskLifecycleState.InProgress &&
                       record.IsCurrentStageComplete();
            }
        }

        /// <summary>
        /// 将活动任务移至已完成阶段后的下一阶段，事件由 System 在新监听成功后发布。
        /// </summary>
        /// <param name="taskId">任务标识。</param>
        /// <param name="nextStage">下一阶段定义。</param>
        /// <param name="previousStageId">变化前阶段标识。</param>
        /// <returns>记录仍在进行且当前阶段完整时返回 true。</returns>
        internal bool TryAdvanceStage(
            TaskId taskId,
            TaskStageDefinition nextStage,
            out TaskStageId previousStageId)
        {
            if (nextStage == null)
            {
                throw new ArgumentNullException(nameof(nextStage));
            }

            lock (stateGate)
            {
                previousStageId = default;
                if (!taskRecordByIdMap.TryGetValue(taskId, out TaskRecord record) ||
                    record.State != TaskLifecycleState.InProgress ||
                    !record.IsCurrentStageComplete())
                {
                    return false;
                }

                previousStageId = record.CurrentStageId;
                record.ActivateStage(nextStage);
                return true;
            }
        }

        /// <summary>
        /// 将最后阶段完整的任务置为待提交状态。
        /// </summary>
        /// <param name="taskId">任务标识。</param>
        /// <returns>状态实际切换为 Claimable 时返回 true。</returns>
        internal bool TryMarkClaimable(TaskId taskId)
        {
            lock (stateGate)
            {
                if (!taskRecordByIdMap.TryGetValue(taskId, out TaskRecord record) ||
                    record.State != TaskLifecycleState.InProgress ||
                    !record.IsCurrentStageComplete())
                {
                    return false;
                }

                record.SetState(TaskLifecycleState.Claimable);
            }

            Publish(new TaskStateChangedEventArgs(
                taskId,
                TaskLifecycleState.InProgress,
                TaskLifecycleState.Claimable));
            Publish(new TaskRewardClaimableEventArgs(taskId));
            return true;
        }

        /// <summary>
        /// 奖励成功发放后记录任务完成并清理追踪和未读状态。
        /// </summary>
        /// <param name="taskId">待完成任务标识。</param>
        /// <returns>任务当前处于 Claimable 时返回 true。</returns>
        internal bool TryMarkCompleted(TaskId taskId)
        {
            TaskId previousTrackedTaskId = default;
            bool trackingChanged = false;
            lock (stateGate)
            {
                if (!taskRecordByIdMap.TryGetValue(taskId, out TaskRecord record) ||
                    record.State != TaskLifecycleState.Claimable)
                {
                    return false;
                }

                taskRecordByIdMap.Remove(taskId);
                completedTaskIds.Add(taskId);
                unreadTaskIds.Remove(taskId);
                if (trackedTaskId == taskId)
                {
                    previousTrackedTaskId = trackedTaskId;
                    trackedTaskId = default;
                    trackingChanged = true;
                }
            }

            if (trackingChanged)
            {
                Publish(new TaskTrackedChangedEventArgs(previousTrackedTaskId, default));
            }

            Publish(new TaskCompletedEventArgs(taskId));
            return true;
        }

        /// <summary>
        /// 应用目标进度变化并发布当前阶段的进度事实。
        /// </summary>
        /// <param name="taskId">任务标识。</param>
        /// <param name="stageId">发起变化的阶段标识，用于忽略已停止 Handler 的迟到回调。</param>
        /// <param name="objectiveId">当前阶段目标标识。</param>
        /// <param name="value">增加量或绝对进度。</param>
        /// <param name="absolute">是否按绝对进度覆盖。</param>
        /// <returns>目标进度发生变化时返回 true。</returns>
        internal bool ApplyObjectiveProgress(
            TaskId taskId,
            TaskStageId stageId,
            ObjectiveId objectiveId,
            int value,
            bool absolute)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "目标进度值不能为负数。");
            }

            int previousValue;
            int currentValue;
            lock (stateGate)
            {
                if (!taskRecordByIdMap.TryGetValue(taskId, out TaskRecord record) ||
                    record.CurrentStageId != stageId)
                {
                    return false;
                }

                if (!record.TryGetProgress(objectiveId, out TaskObjectiveProgress progress))
                {
                    throw new InvalidOperationException(
                        $"任务 {taskId} 的当前阶段 {stageId} 不包含目标 {objectiveId}。 ");
                }

                previousValue = progress.Current;
                bool changed = absolute
                    ? record.SetObjectiveProgress(objectiveId, value)
                    : record.AddObjectiveProgress(objectiveId, value);
                if (!changed)
                {
                    return false;
                }

                currentValue = progress.Current;
            }

            Publish(new TaskObjectiveProgressChangedEventArgs(
                taskId,
                stageId,
                objectiveId,
                previousValue,
                currentValue));
            return true;
        }

        #endregion

        #region 存档快照

        /// <summary>
        /// 将当前任务事实转换为不含配置引用和监听句柄的存档快照。
        /// </summary>
        /// <returns>当前任务快照。</returns>
        public TaskSaveSnapshot CaptureSnapshot()
        {
            EnsureConfigured();
            lock (stateGate)
            {
                var snapshot = new TaskSaveSnapshot
                {
                    ActiveTasks = new List<TaskRecordSnapshot>(),
                    CompletedTaskIds = new List<string>(),
                    TrackedTaskId = trackedTaskId.IsValid ? trackedTaskId.Value : string.Empty,
                    UnreadTaskIds = new List<string>()
                };

                foreach (TaskRecord record in taskRecordByIdMap.Values)
                {
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
        }

        /// <summary>
        /// 在不发送普通任务事件的前提下原子恢复已经校验的快照。
        /// </summary>
        /// <param name="snapshot">待恢复任务快照。</param>
        /// <exception cref="ArgumentNullException">快照为空时抛出。</exception>
        /// <exception cref="InvalidOperationException">快照与当前定义不匹配时抛出。</exception>
        public void RestoreSnapshot(TaskSaveSnapshot snapshot)
        {
            EnsureConfigured();
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            snapshot.ValidateShape();
            lock (stateGate)
            {
                var restoredTaskRecordByIdMap = new Dictionary<TaskId, TaskRecord>();
                var restoredCompleted = new HashSet<TaskId>();
                var restoredUnread = new HashSet<TaskId>();

                RestoreActiveRecords(snapshot, restoredTaskRecordByIdMap);
                RestoreCompletedIds(snapshot, restoredCompleted, restoredTaskRecordByIdMap);
                RestoreUnreadIds(snapshot, restoredTaskRecordByIdMap, restoredUnread);

                TaskId restoredTracked = default;
                if (!string.IsNullOrEmpty(snapshot.TrackedTaskId))
                {
                    restoredTracked = new TaskId(snapshot.TrackedTaskId);
                    if (!restoredTaskRecordByIdMap.ContainsKey(restoredTracked))
                    {
                        throw new InvalidOperationException("存档追踪任务必须存在于活动任务集合中。");
                    }
                }

                taskRecordByIdMap.Clear();
                foreach (KeyValuePair<TaskId, TaskRecord> pair in restoredTaskRecordByIdMap)
                {
                    taskRecordByIdMap.Add(pair.Key, pair.Value);
                }

                completedTaskIds.Clear();
                completedTaskIds.UnionWith(restoredCompleted);
                unreadTaskIds.Clear();
                unreadTaskIds.UnionWith(restoredUnread);
                trackedTaskId = restoredTracked;
            }

            Debug.Log($"[TaskManager] 已恢复任务状态，active={snapshot.ActiveTasks.Count}, " +
                      $"completed={snapshot.CompletedTaskIds.Count}, unread={snapshot.UnreadTaskIds.Count}。");
        }

        #endregion

        #region 测试与内部校验

        /// <summary>
        /// 清理玩家任务状态，不注销 Singleton 实例和已注入配置。
        /// </summary>
        internal void ClearPlayerTaskState()
        {
            lock (stateGate)
            {
                ClearPlayerTaskStateInternal();
            }

            Debug.Log("[TaskManager] 已清理玩家任务状态。");
        }

        /// <summary>
        /// 为 Odin 手动测试替换数据库并清理旧任务事实。
        /// </summary>
        /// <param name="taskDatabase">测试使用的临时任务数据库。</param>
        /// <exception cref="ArgumentNullException">数据库为空时抛出。</exception>
        internal void ResetForTests(TaskDatabase taskDatabase)
        {
            if (taskDatabase == null)
            {
                throw new ArgumentNullException(nameof(taskDatabase));
            }

            lock (configurationGate)
            {
                lock (stateGate)
                {
                    configured = false;
                    database = null;
                    ClearPlayerTaskStateInternal();
                }

                Initialize(taskDatabase);
            }
        }

        /// <summary>
        /// 恢复手动测试前的数据库与玩家任务快照。
        /// </summary>
        /// <param name="previousDatabase">测试前的任务数据库；原先未配置时为 null。</param>
        /// <param name="previousSnapshot">测试前的任务事实；无原数据库时必须为 null。</param>
        /// <exception cref="ArgumentException">缺少原数据库但快照非空时抛出。</exception>
        internal void RestoreAfterTests(TaskDatabase previousDatabase, TaskSaveSnapshot previousSnapshot)
        {
            if (previousDatabase == null && previousSnapshot != null)
            {
                throw new ArgumentException("恢复测试快照前必须同时提供原任务数据库。", nameof(previousSnapshot));
            }

            lock (configurationGate)
            {
                lock (stateGate)
                {
                    configured = false;
                    database = null;
                    ClearPlayerTaskStateInternal();
                }

                if (previousDatabase == null)
                {
                    return;
                }

                Initialize(previousDatabase);
                if (previousSnapshot != null)
                {
                    RestoreSnapshot(previousSnapshot);
                }
            }
        }

        /// <summary>
        /// 校验并重建存档中的活动任务记录。
        /// </summary>
        /// <param name="snapshot">任务快照。</param>
        /// <param name="restoredTaskRecordByIdMap">临时活动记录表，成功后才替换当前状态。</param>
        private void RestoreActiveRecords(
            TaskSaveSnapshot snapshot,
            Dictionary<TaskId, TaskRecord> restoredTaskRecordByIdMap)
        {
            foreach (TaskRecordSnapshot recordSnapshot in snapshot.ActiveTasks)
            {
                TaskId taskId = new TaskId(recordSnapshot.TaskId);
                if (!database.TryGetDefinition(taskId, out TaskDefinition definition))
                {
                    throw new InvalidOperationException($"任务存档引用了不存在的任务：{taskId}。 ");
                }

                if (restoredTaskRecordByIdMap.ContainsKey(taskId))
                {
                    throw new InvalidOperationException($"任务存档包含重复活动任务：{taskId}。 ");
                }

                TaskStageId stageId = new TaskStageId(recordSnapshot.CurrentStageId);
                if (!definition.TryGetStage(stageId, out TaskStageDefinition stage, out int stageIndex))
                {
                    throw new InvalidOperationException($"任务 {taskId} 存档阶段无法匹配定义：{stageId}。 ");
                }

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
                            $"任务 {taskId} 当前阶段进度无法匹配定义：{objectiveId}。 ");
                    }

                    if (progress.Required != progressSnapshot.Required ||
                        progressSnapshot.Current < 0 ||
                        progressSnapshot.Current > progress.Required)
                    {
                        throw new InvalidOperationException(
                            $"任务 {taskId} 的目标进度范围或需求不匹配：{objectiveId}。 ");
                    }

                    record.SetObjectiveProgress(objectiveId, progressSnapshot.Current);
                }

                if (restoredObjectiveIds.Count != stage.Objectives.Count)
                {
                    throw new InvalidOperationException($"任务 {taskId} 当前阶段快照缺少目标进度。");
                }

                bool stageComplete = record.IsCurrentStageComplete();
                bool isLastStage = stageIndex == definition.Stages.Count - 1;
                bool expectedClaimable = isLastStage && stageComplete;
                if ((recordSnapshot.State == TaskLifecycleState.Claimable) != expectedClaimable ||
                    (!isLastStage && stageComplete))
                {
                    throw new InvalidOperationException($"任务 {taskId} 的阶段状态与目标进度不一致。");
                }

                record.SetState(recordSnapshot.State);
                restoredTaskRecordByIdMap.Add(taskId, record);
            }
        }

        /// <summary>
        /// 校验已完成集合并拒绝与活动任务重复的 ID。
        /// </summary>
        /// <param name="snapshot">任务快照。</param>
        /// <param name="restoredCompletedTaskIds">临时完成集合。</param>
        /// <param name="activeTaskRecordByIdMap">已恢复的活动任务记录。</param>
        private void RestoreCompletedIds(
            TaskSaveSnapshot snapshot,
            HashSet<TaskId> restoredCompletedTaskIds,
            Dictionary<TaskId, TaskRecord> activeTaskRecordByIdMap)
        {
            foreach (string value in snapshot.CompletedTaskIds)
            {
                TaskId taskId = new TaskId(value);
                if (!database.TryGetDefinition(taskId, out _))
                {
                    throw new InvalidOperationException($"完成任务集合引用了不存在的任务：{taskId}。 ");
                }

                if (activeTaskRecordByIdMap.ContainsKey(taskId) || !restoredCompletedTaskIds.Add(taskId))
                {
                    throw new InvalidOperationException($"完成任务集合包含重复或活动任务：{taskId}。 ");
                }
            }
        }

        /// <summary>
        /// 校验未读集合只能引用活动且未完成的任务。
        /// </summary>
        /// <param name="snapshot">任务快照。</param>
        /// <param name="activeTaskRecordByIdMap">已恢复活动任务记录。</param>
        /// <param name="restoredUnreadTaskIds">临时未读集合。</param>
        private void RestoreUnreadIds(
            TaskSaveSnapshot snapshot,
            Dictionary<TaskId, TaskRecord> activeTaskRecordByIdMap,
            HashSet<TaskId> restoredUnreadTaskIds)
        {
            foreach (string value in snapshot.UnreadTaskIds)
            {
                TaskId taskId = new TaskId(value);
                if (!activeTaskRecordByIdMap.ContainsKey(taskId) || !restoredUnreadTaskIds.Add(taskId))
                {
                    throw new InvalidOperationException($"未读任务集合包含非法或重复任务：{taskId}。 ");
                }
            }
        }

        /// <summary>
        /// 清空活动、完成、未读和追踪任务事实。
        /// </summary>
        private void ClearPlayerTaskStateInternal()
        {
            taskRecordByIdMap.Clear();
            completedTaskIds.Clear();
            unreadTaskIds.Clear();
            trackedTaskId = default;
        }

        /// <summary>
        /// 确认调用方已经通过 ConfigInstaller 注入任务数据库。
        /// </summary>
        /// <exception cref="InvalidOperationException">数据库尚未配置时抛出。</exception>
        private void EnsureConfigured()
        {
            if (!configured || database == null)
            {
                throw new InvalidOperationException("TaskManager 尚未通过 ConfigInstaller 注入 TaskDatabase。 ");
            }
        }

        /// <summary>
        /// 通过 WSFrame 类型事件中心发布已经提交的任务事实。
        /// </summary>
        /// <typeparam name="TEvent">事件类型。</typeparam>
        /// <param name="eventArgs">事件数据。</param>
        private static void Publish<TEvent>(TEvent eventArgs)
        {
            EventSystem.EventTrigger_Type(typeof(TEvent), eventArgs);
        }

        #endregion
    }
}
