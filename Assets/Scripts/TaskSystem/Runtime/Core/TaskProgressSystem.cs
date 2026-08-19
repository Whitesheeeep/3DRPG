using System;
using System.Collections.Generic;
using RPG.SaveSystem;
using WS_Modules.BusinessArchitecture;

namespace RPG.TaskSystem
{
    /// <summary>
    /// 协调活动任务运行时创建、恢复、启动监听和销毁的业务 System。
    /// </summary>
    public sealed class TaskProgressSystem : AbstractSystem
    {
        private readonly TaskObjectiveHandlerRegistry handlerRegistry;
        private readonly Dictionary<TaskId, TaskRuntime> runtimes =
            new Dictionary<TaskId, TaskRuntime>();

        private TaskManager taskManager;
        private SaveManager saveManager;

        /// <summary>
        /// 创建任务运行时生命周期协调 System。
        /// </summary>
        /// <param name="handlerRegistry">任务实例创建目标 Handler 所需的注册表。</param>
        /// <exception cref="ArgumentNullException">注册表为空时抛出。</exception>
        public TaskProgressSystem(TaskObjectiveHandlerRegistry handlerRegistry)
        {
            this.handlerRegistry = handlerRegistry ?? throw new ArgumentNullException(nameof(handlerRegistry));
        }

        /// <summary>
        /// 获取目标 Handler 注册表，供配置组合根在架构启动前注册具体实现。
        /// </summary>
        public TaskObjectiveHandlerRegistry HandlerRegistry => handlerRegistry;

        /// <summary>
        /// 初始化任务运行时生命周期和任务存档模块。
        /// </summary>
        protected override void OnInit()
        {
            taskManager = TaskManager.Instance;
            saveManager = this.GetManager<SaveManager>();
            saveManager.RegisterModule(new TaskSaveModule(taskManager));
            saveManager.OperationCompleted += OnSaveOperationCompleted;
            RebuildRuntimes();
        }

        /// <summary>
        /// 注销存档完成通知并停止所有活动任务的事件监听。
        /// </summary>
        protected override void OnDeinit()
        {
            if (saveManager != null)
            {
                saveManager.OperationCompleted -= OnSaveOperationCompleted;
            }

            StopAllRuntimes();
            taskManager?.ClearRuntimeState();
            saveManager = null;
            taskManager = null;
        }

        /// <summary>
        /// 为指定活动任务创建并启动一个 TaskRuntime。
        /// </summary>
        /// <param name="taskId">活动任务标识。</param>
        /// <returns>成功创建时返回 true；运行时已经存在或任务记录不存在时返回 false。</returns>
        public bool TryCreateRuntime(TaskId taskId)
        {
            EnsureInitialized();
            if (runtimes.ContainsKey(taskId) ||
                !taskManager.TryGetActiveRecord(taskId, out TaskRecord record) ||
                !taskManager.TryGetDefinition(taskId, out TaskDefinition definition))
            {
                return false;
            }

            var runtime = new TaskRuntime(taskManager, definition, record, handlerRegistry);
            runtime.StartListening();
            runtimes.Add(taskId, runtime);
            return true;
        }

        /// <summary>
        /// 停止并移除指定任务运行时。
        /// </summary>
        /// <param name="taskId">任务标识。</param>
        /// <returns>找到并移除运行时时返回 true。</returns>
        public bool RemoveRuntime(TaskId taskId)
        {
            if (!runtimes.TryGetValue(taskId, out TaskRuntime runtime))
            {
                return false;
            }

            runtime.StopListening();
            runtimes.Remove(taskId);
            return true;
        }

        /// <summary>
        /// 根据 TaskManager 当前活动记录重建全部任务运行时和目标订阅。
        /// </summary>
        public void RebuildRuntimes()
        {
            EnsureInitialized();
            StopAllRuntimes();

            IReadOnlyList<TaskRecord> activeRecords = taskManager.ActiveRecords;
            for (int index = 0; index < activeRecords.Count; index++)
            {
                TryCreateRuntime(activeRecords[index].TaskId);
            }
        }

        /// <summary>
        /// 获取指定任务当前运行时实例。
        /// </summary>
        /// <param name="taskId">任务标识。</param>
        /// <param name="runtime">找到的任务运行时。</param>
        /// <returns>找到运行时时返回 true。</returns>
        public bool TryGetRuntime(TaskId taskId, out TaskRuntime runtime) =>
            runtimes.TryGetValue(taskId, out runtime);

        /// <summary>
        /// 在存档加载成功后重建任务运行时，避免恢复过程重放普通任务事件。
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
        /// 停止全部活动任务运行时并清空实例表。
        /// </summary>
        private void StopAllRuntimes()
        {
            foreach (TaskRuntime runtime in runtimes.Values)
            {
                runtime.StopListening();
            }

            runtimes.Clear();
        }

        /// <summary>
        /// 确保 System 已经完成 BusinessArchitecture 初始化。
        /// </summary>
        private void EnsureInitialized()
        {
            if (taskManager == null)
            {
                throw new InvalidOperationException("TaskProgressSystem 尚未完成初始化。 ");
            }
        }
    }
}
