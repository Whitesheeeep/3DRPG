using System;
using System.Collections.Generic;
using WS_Modules.CustomEventSystem;
using WS_Modules.Singleton;

namespace RPG.TaskSystem
{
    /// <summary>
    /// 持有任务配置和玩家任务事实的纯 C# 单例 Manager。
    /// </summary>
    public sealed class TaskManager : SingletonBase<TaskManager>
    {
        // 配置锁独立于实例状态锁，使 ConfigInstaller 可以在不创建 TaskManager 单例的情况下注入数据库。
        private static readonly object configurationGate = new object();

        private readonly object stateGate = new object();
        private readonly Dictionary<TaskId, TaskRecord> activeRecords =
            new Dictionary<TaskId, TaskRecord>();
        private readonly HashSet<TaskId> completedTaskIds = new HashSet<TaskId>();
        private readonly HashSet<TaskId> unreadTaskIds = new HashSet<TaskId>();

        private static TaskDatabase database;
        private static bool configured;
        private TaskId trackedTaskId;

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
        /// 获取当前活动任务的稳定快照列表。
        /// </summary>
        public IReadOnlyList<TaskRecord> ActiveRecords
        {
            get
            {
                lock (stateGate)
                {
                    return new List<TaskRecord>(activeRecords.Values).AsReadOnly();
                }
            }
        }

        /// <summary>
        /// 获取已经完成的一次性任务标识。
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
        /// 获取当前尚未确认的任务标识。
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
        public TaskId TrackedTaskId => trackedTaskId;

        /// <summary>
        /// 注入集中式任务数据库，并在运行前建立定义索引。
        /// </summary>
        /// <param name="taskDatabase">任务配置资产。</param>
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
        }

        /// <summary>
        /// 尝试按稳定标识获取任务定义。
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
        /// 尝试获取活动任务记录。
        /// </summary>
        /// <param name="taskId">任务标识。</param>
        /// <param name="record">找到的活动任务记录。</param>
        /// <returns>找到记录时返回 true。</returns>
        public bool TryGetActiveRecord(TaskId taskId, out TaskRecord record)
        {
            lock (stateGate)
            {
                return activeRecords.TryGetValue(taskId, out record);
            }
        }

        /// <summary>
        /// 将指定活动任务设置为当前追踪任务。
        /// </summary>
        /// <param name="taskId">待追踪任务标识。</param>
        /// <returns>追踪目标发生变化时返回 true。</returns>
        public bool TrySetTrackedTask(TaskId taskId)
        {
            lock (stateGate)
            {
                if (!activeRecords.ContainsKey(taskId))
                {
                    return false;
                }

                if (trackedTaskId == taskId)
                {
                    return true;
                }

                TaskId previousTaskId = trackedTaskId;
                trackedTaskId = taskId;
                Publish(new TaskTrackedChangedEventArgs(previousTaskId, trackedTaskId));
                return true;
            }
        }

        /// <summary>
        /// 清除当前追踪任务并发布追踪变化事件。
        /// </summary>
        /// <returns>存在追踪任务且已清除时返回 true。</returns>
        public bool ClearTrackedTask()
        {
            lock (stateGate)
            {
                if (!trackedTaskId.IsValid)
                {
                    return false;
                }

                TaskId previousTaskId = trackedTaskId;
                trackedTaskId = default;
                Publish(new TaskTrackedChangedEventArgs(previousTaskId, trackedTaskId));
                return true;
            }
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

                Publish(new TaskAcknowledgedEventArgs(taskId));
                return true;
            }
        }

        /// <summary>
        /// 创建供任务流程和手动测试使用的活动任务记录。
        /// </summary>
        /// <param name="taskId">待接取任务标识。</param>
        /// <param name="record">创建的任务记录。</param>
        /// <returns>成功创建时返回 true；任务不存在、已活动或已完成时返回 false。</returns>
        internal bool TryCreateActiveRecord(TaskId taskId, out TaskRecord record)
        {
            EnsureConfigured();
            lock (stateGate)
            {
                record = null;
                if (!database.TryGetDefinition(taskId, out TaskDefinition definition) ||
                    activeRecords.ContainsKey(taskId) ||
                    completedTaskIds.Contains(taskId))
                {
                    return false;
                }

                record = new TaskRecord(definition);
                activeRecords.Add(taskId, record);
                unreadTaskIds.Add(taskId);
                Publish(new TaskAcceptedEventArgs(taskId));
                return true;
            }
        }

        /// <summary>
        /// 将可领取任务转为已完成事实并清理活动运行时数据。
        /// </summary>
        /// <param name="taskId">待完成任务标识。</param>
        /// <returns>任务处于可领取并成功完成时返回 true。</returns>
        internal bool TryMarkCompleted(TaskId taskId)
        {
            lock (stateGate)
            {
                if (!activeRecords.TryGetValue(taskId, out TaskRecord record) ||
                    record.State != TaskLifecycleState.Claimable)
                {
                    return false;
                }

                activeRecords.Remove(taskId);
                completedTaskIds.Add(taskId);
                unreadTaskIds.Remove(taskId);
                TaskId previousTrackedTaskId = trackedTaskId;
                if (trackedTaskId == taskId)
                {
                    trackedTaskId = default;
                }

                if (previousTrackedTaskId != trackedTaskId)
                {
                    Publish(new TaskTrackedChangedEventArgs(previousTrackedTaskId, trackedTaskId));
                }

                Publish(new TaskCompletedEventArgs(taskId));
                return true;
            }
        }

        /// <summary>
        /// 按事件累计量更新活动任务目标进度。
        /// </summary>
        /// <param name="taskId">任务标识。</param>
        /// <param name="objectiveId">目标标识。</param>
        /// <param name="delta">非负增加量。</param>
        /// <returns>目标或任务状态变化时返回 true。</returns>
        internal bool ApplyObjectiveDelta(TaskId taskId, ObjectiveId objectiveId, int delta)
        {
            if (delta < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(delta), "目标累计增加量不能为负数。");
            }

            lock (stateGate)
            {
                return ApplyObjectiveUpdateInternal(taskId, objectiveId, delta, false);
            }
        }

        /// <summary>
        /// 按外部业务当前状态覆盖活动任务目标进度。
        /// </summary>
        /// <param name="taskId">任务标识。</param>
        /// <param name="objectiveId">目标标识。</param>
        /// <param name="value">非负当前值。</param>
        /// <returns>目标或任务状态变化时返回 true。</returns>
        internal bool SetObjectiveProgress(TaskId taskId, ObjectiveId objectiveId, int value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "目标当前进度不能为负数。");
            }

            lock (stateGate)
            {
                return ApplyObjectiveUpdateInternal(taskId, objectiveId, value, true);
            }
        }

        /// <summary>
        /// 将当前任务状态转换为存档快照；不包含运行时订阅和 Handler 引用。
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

                foreach (TaskRecord record in activeRecords.Values)
                {
                    var recordSnapshot = new TaskRecordSnapshot
                    {
                        TaskId = record.TaskId.Value,
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
        /// 在不发送普通任务事件的前提下恢复已验证的任务快照。
        /// </summary>
        /// <param name="snapshot">待恢复任务快照。</param>
        /// <exception cref="ArgumentNullException">快照为空时抛出。</exception>
        /// <exception cref="InvalidOperationException">快照引用未知任务、重复数据或非法进度时抛出。</exception>
        public void RestoreSnapshot(TaskSaveSnapshot snapshot)
        {
            EnsureConfigured();
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            lock (stateGate)
            {
                var restoredRecords = new Dictionary<TaskId, TaskRecord>();
                var restoredCompleted = new HashSet<TaskId>();
                var restoredUnread = new HashSet<TaskId>();

                RestoreActiveRecords(snapshot, restoredRecords);
                RestoreCompletedIds(snapshot, restoredCompleted);
                RestoreUnreadIds(snapshot, restoredRecords, restoredCompleted, restoredUnread);

                TaskId restoredTracked = default;
                if (!string.IsNullOrEmpty(snapshot.TrackedTaskId))
                {
                    restoredTracked = ParseTaskId(snapshot.TrackedTaskId);
                    if (!restoredRecords.ContainsKey(restoredTracked))
                    {
                        throw new InvalidOperationException("存档追踪任务必须存在于活动任务集合中。");
                    }
                }

                activeRecords.Clear();
                completedTaskIds.Clear();
                unreadTaskIds.Clear();
                foreach (KeyValuePair<TaskId, TaskRecord> pair in restoredRecords)
                {
                    activeRecords.Add(pair.Key, pair.Value);
                }

                completedTaskIds.UnionWith(restoredCompleted);
                unreadTaskIds.UnionWith(restoredUnread);
                trackedTaskId = restoredTracked;
            }
        }

        /// <summary>
        /// 清理当前运行时任务事实，不注销 Singleton 实例和已注入配置。
        /// </summary>
        internal void ClearRuntimeState()
        {
            lock (stateGate)
            {
                ClearRuntimeStateInternal();
            }
        }

        /// <summary>
        /// 为 Odin 手动测试替换配置数据库并清空旧运行时状态。
        /// </summary>
        /// <param name="taskDatabase">测试使用的任务数据库。</param>
        /// <exception cref="ArgumentNullException">数据库为空时抛出。</exception>
        internal void ResetForTests(TaskDatabase taskDatabase)
        {
            if (taskDatabase == null)
            {
                throw new ArgumentNullException(nameof(taskDatabase));
            }

            lock (configurationGate)
            {
                // 测试需要同时清理静态配置和当前实例状态，避免不同测试之间共享数据库或任务事实。
                lock (stateGate)
                {
                    configured = false;
                    database = null;
                    ClearRuntimeStateInternal();
                }

                Initialize(taskDatabase);
            }
        }

        /// <summary>
        /// 应用一次目标变化并发布进度和状态事实事件。
        /// </summary>
        /// <param name="taskId">任务标识。</param>
        /// <param name="objectiveId">目标标识。</param>
        /// <param name="value">增量或当前值。</param>
        /// <param name="absolute">是否覆盖为当前值。</param>
        /// <returns>目标或任务状态变化时返回 true。</returns>
        private bool ApplyObjectiveUpdateInternal(
            TaskId taskId,
            ObjectiveId objectiveId,
            int value,
            bool absolute)
        {
            if (!activeRecords.TryGetValue(taskId, out TaskRecord record))
            {
                return false;
            }

            if (!record.TryGetProgress(objectiveId, out TaskObjectiveProgress progress))
            {
                throw new InvalidOperationException($"任务 {taskId} 不包含目标 {objectiveId}。 ");
            }

            int previousValue = progress.Current;
            TaskLifecycleState previousState = record.State;
            bool changed = absolute
                ? record.SetObjectiveProgress(objectiveId, value)
                : record.AddObjectiveProgress(objectiveId, value);

            if (!changed)
            {
                return false;
            }

            if (previousValue != progress.Current)
            {
                Publish(new TaskObjectiveProgressChangedEventArgs(
                    taskId,
                    objectiveId,
                    previousValue,
                    progress.Current));
            }

            if (previousState != record.State)
            {
                Publish(new TaskStateChangedEventArgs(taskId, previousState, record.State));
            }

            return true;
        }

        /// <summary>
        /// 恢复全部活动任务记录并校验目标 ID 与版本需求。
        /// </summary>
        /// <param name="snapshot">任务存档快照。</param>
        /// <param name="target">待写入的临时活动任务表。</param>
        private void RestoreActiveRecords(TaskSaveSnapshot snapshot, Dictionary<TaskId, TaskRecord> target)
        {
            if (snapshot.ActiveTasks == null)
            {
                return;
            }

            foreach (TaskRecordSnapshot recordSnapshot in snapshot.ActiveTasks)
            {
                if (recordSnapshot == null)
                {
                    throw new InvalidOperationException("任务存档包含空活动任务记录。");
                }

                TaskId taskId = ParseTaskId(recordSnapshot.TaskId);
                if (!database.TryGetDefinition(taskId, out TaskDefinition definition))
                {
                    throw new InvalidOperationException($"任务存档引用了不存在的任务：{taskId}。 ");
                }

                if (target.ContainsKey(taskId))
                {
                    throw new InvalidOperationException($"任务存档包含重复活动任务：{taskId}。 ");
                }

                var restoredRecord = new TaskRecord(definition);
                target.Add(taskId, restoredRecord);
                TaskRecord record = restoredRecord;
                var restoredObjectiveIds = new HashSet<ObjectiveId>();
                if (recordSnapshot.ObjectiveProgress == null)
                {
                    throw new InvalidOperationException($"任务 {taskId} 缺少目标进度列表。 ");
                }

                foreach (TaskObjectiveProgressSnapshot progressSnapshot in recordSnapshot.ObjectiveProgress)
                {
                    if (progressSnapshot == null)
                    {
                        throw new InvalidOperationException($"任务 {taskId} 包含空目标进度。 ");
                    }

                    ObjectiveId objectiveId = ParseObjectiveId(progressSnapshot.ObjectiveId);
                    if (!restoredObjectiveIds.Add(objectiveId) ||
                        !record.TryGetProgress(objectiveId, out TaskObjectiveProgress progress))
                    {
                        throw new InvalidOperationException($"任务 {taskId} 的目标进度无法匹配定义：{objectiveId}。 ");
                    }

                    if (progress.Required != progressSnapshot.Required ||
                        progressSnapshot.Current < 0 ||
                        progressSnapshot.Current > progress.Required)
                    {
                        throw new InvalidOperationException($"任务 {taskId} 的目标进度范围或需求不匹配：{objectiveId}。 ");
                    }

                    record.SetObjectiveProgress(objectiveId, progressSnapshot.Current);
                }

                if (recordSnapshot.State != TaskLifecycleState.InProgress &&
                    recordSnapshot.State != TaskLifecycleState.Claimable)
                {
                    throw new InvalidOperationException($"任务 {taskId} 的生命周期状态无效。 ");
                }

                TaskLifecycleState expectedState = TaskLifecycleState.Claimable;
                foreach (TaskObjectiveProgress progress in record.ObjectiveProgress)
                {
                    if (!progress.IsComplete)
                    {
                        expectedState = TaskLifecycleState.InProgress;
                        break;
                    }
                }

                if (recordSnapshot.State != expectedState)
                {
                    throw new InvalidOperationException($"任务 {taskId} 的状态与目标进度不一致。 ");
                }

                record.SetState(recordSnapshot.State);
            }
        }

        /// <summary>
        /// 恢复完成任务集合并校验任务标识存在且不重复。
        /// </summary>
        /// <param name="snapshot">任务存档快照。</param>
        /// <param name="target">待写入的临时完成集合。</param>
        private void RestoreCompletedIds(TaskSaveSnapshot snapshot, HashSet<TaskId> target)
        {
            if (snapshot.CompletedTaskIds == null)
            {
                return;
            }

            foreach (string value in snapshot.CompletedTaskIds)
            {
                TaskId taskId = ParseTaskId(value);
                if (!database.TryGetDefinition(taskId, out _))
                {
                    throw new InvalidOperationException($"完成任务集合引用了不存在的任务：{taskId}。 ");
                }

                if (!target.Add(taskId))
                {
                    throw new InvalidOperationException($"完成任务集合包含重复任务：{taskId}。 ");
                }
            }
        }

        /// <summary>
        /// 恢复未读集合并确保其只引用活动且未完成的任务。
        /// </summary>
        /// <param name="snapshot">任务存档快照。</param>
        /// <param name="active">已恢复活动任务集合。</param>
        /// <param name="completed">已恢复完成任务集合。</param>
        /// <param name="target">待写入的临时未读集合。</param>
        private void RestoreUnreadIds(
            TaskSaveSnapshot snapshot,
            Dictionary<TaskId, TaskRecord> active,
            HashSet<TaskId> completed,
            HashSet<TaskId> target)
        {
            if (snapshot.UnreadTaskIds == null)
            {
                return;
            }

            foreach (string value in snapshot.UnreadTaskIds)
            {
                TaskId taskId = ParseTaskId(value);
                if (!active.ContainsKey(taskId) || completed.Contains(taskId) || !target.Add(taskId))
                {
                    throw new InvalidOperationException($"未读任务集合包含非法或重复任务：{taskId}。 ");
                }
            }
        }

        /// <summary>
        /// 将字符串解析为严格任务标识。
        /// </summary>
        /// <param name="value">待解析字符串。</param>
        /// <returns>任务标识。</returns>
        private static TaskId ParseTaskId(string value)
        {
            return new TaskId(value);
        }

        /// <summary>
        /// 将字符串解析为严格目标标识。
        /// </summary>
        /// <param name="value">待解析字符串。</param>
        /// <returns>目标标识。</returns>
        private static ObjectiveId ParseObjectiveId(string value)
        {
            return new ObjectiveId(value);
        }

        /// <summary>
        /// 清空任务运行时状态集合。
        /// </summary>
        private void ClearRuntimeStateInternal()
        {
            activeRecords.Clear();
            completedTaskIds.Clear();
            unreadTaskIds.Clear();
            trackedTaskId = default;
        }

        /// <summary>
        /// 确保调用方已经通过 ConfigInstaller 注入任务数据库。
        /// </summary>
        private void EnsureConfigured()
        {
            if (!configured || database == null)
            {
                throw new InvalidOperationException("TaskManager 尚未通过 ConfigInstaller 注入 TaskDatabase。 ");
            }
        }

        /// <summary>
        /// 通过 WSFrame 类型事件中心发布任务事实事件。
        /// </summary>
        /// <typeparam name="TEvent">事件类型。</typeparam>
        /// <param name="eventArgs">事件数据。</param>
        private static void Publish<TEvent>(TEvent eventArgs)
        {
            EventSystem.EventTrigger_Type(typeof(TEvent), eventArgs);
        }
    }
}
