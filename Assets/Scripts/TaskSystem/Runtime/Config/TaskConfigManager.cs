using System;
using UnityEngine;
using WS_Modules.Singleton;

namespace RPG.TaskSystemNS
{
    /// <summary>
    /// 在 Unity 主线程持有经过校验的任务静态配置，并提供按 TaskId 查询定义的入口。
    /// </summary>
    public sealed class TaskConfigManager : SingletonBase<TaskConfigManager>
    {
        #region 配置字段

        private static TaskDatabase database;

        #endregion

        #region 构造与配置注入

        /// <summary>创建由 SingletonBase 管理的任务配置门面。</summary>
        private TaskConfigManager()
        {
        }

        /// <summary>获取任务数据库是否已注入。</summary>
        public bool IsConfigured => database != null;

        /// <summary>获取已注入的任务数据库。</summary>
        public TaskDatabase Database
        {
            get
            {
                EnsureDatabase();
                return database;
            }
        }

        /// <summary>
        /// 注入唯一任务数据库并校验全部任务定义。
        /// </summary>
        /// <param name="taskDatabase">集中引用任务定义资产的数据库。</param>
        /// <exception cref="ArgumentNullException">数据库为空时抛出。</exception>
        /// <exception cref="InvalidOperationException">尝试替换已注入的数据库时抛出。</exception>
        public static void Initialize(TaskDatabase taskDatabase)
        {
            if (taskDatabase == null)
            {
                throw new ArgumentNullException(nameof(taskDatabase));
            }

            if (database != null)
            {
                if (!ReferenceEquals(database, taskDatabase))
                {
                    throw new InvalidOperationException(
                        "[TaskConfigManager] 不允许替换已经注入的 TaskDatabase。");
                }

                return;
            }

            taskDatabase.ValidateAndBuildIndex();
            database = taskDatabase;

            Debug.Log($"[TaskConfigManager] 已注入任务数据库，definitionCount={taskDatabase.Definitions.Count}。");
        }

        #endregion

        #region 配置查询

        /// <summary>尝试按稳定任务标识获取静态定义。</summary>
        /// <param name="taskId">任务标识。</param>
        /// <param name="definition">找到的任务定义。</param>
        /// <returns>找到定义时返回 true。</returns>
        public bool TryGetDefinition(TaskId taskId, out TaskDefinition definition)
        {
            EnsureDatabase();
            return database.TryGetDefinition(taskId, out definition);
        }

        /// <summary>读取指定任务的必需静态定义。</summary>
        /// <param name="taskId">任务标识。</param>
        /// <returns>对应任务定义。</returns>
        /// <exception cref="InvalidOperationException">任务 ID 不存在时抛出。</exception>
        public TaskDefinition GetRequiredDefinition(TaskId taskId)
        {
            if (TryGetDefinition(taskId, out TaskDefinition definition))
            {
                return definition;
            }

            throw new InvalidOperationException($"[TaskConfigManager] 找不到任务定义：{taskId}。");
        }

        #endregion

        #region 测试配置替换

#if UNITY_EDITOR
        /// <summary>替换测试期间使用的任务数据库。</summary>
        /// <param name="taskDatabase">测试数据库。</param>
        internal static void ResetForTests(TaskDatabase taskDatabase)
        {
            if (taskDatabase == null)
            {
                throw new ArgumentNullException(nameof(taskDatabase));
            }

            taskDatabase.ValidateAndBuildIndex();
            database = taskDatabase;

            Debug.Log($"[TaskConfigManager] 已切换测试数据库，definitionCount={taskDatabase.Definitions.Count}。");
        }

        /// <summary>恢复测试开始前注入的任务数据库。</summary>
        /// <param name="previousDatabase">先前数据库；原先未配置时为空。</param>
        internal static void RestoreAfterTests(TaskDatabase previousDatabase)
        {
            database = previousDatabase;

            Debug.Log($"[TaskConfigManager] 已恢复测试前配置，configured={database != null}。");
        }
#endif

        #endregion

        #region 内部校验

        /// <summary>确保查询前已注入任务数据库。</summary>
        /// <exception cref="InvalidOperationException">数据库尚未配置时抛出。</exception>
        private static void EnsureDatabase()
        {
            if (database == null)
            {
                throw new InvalidOperationException(
                    "[TaskConfigManager] 尚未通过 ConfigInstaller 注入 TaskDatabase。");
            }
        }

        #endregion
    }
}
