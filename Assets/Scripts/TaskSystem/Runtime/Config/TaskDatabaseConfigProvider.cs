using UnityEngine;
using WS_Modules.ConfigInstaller;

namespace RPG.TaskSystemNS
{
    /// <summary>
    /// 将任务数据库配置注入静态 TaskConfigManager 的 ConfigInstaller 叶节点。
    /// </summary>
    [CreateAssetMenu(
        fileName = "TaskDatabaseConfigProvider",
        menuName = "RPG/TaskSystem/Task Database Config Provider",
        order = 1)]
    public sealed class TaskDatabaseConfigProvider : ConfigRegisterNodeBase
    {
        [SerializeField] private TaskDatabase database;

        /// <summary>
        /// 将配置资产注入 TaskConfigManager；配置校验必须在任务业务系统启动前完成。
        /// </summary>
        /// <exception cref="System.InvalidOperationException">未配置数据库资产时抛出。</exception>
        public override void Register()
        {
            if (database == null)
            {
                throw new System.InvalidOperationException("TaskDatabaseConfigProvider 未配置 TaskDatabase。 ");
            }

            TaskConfigManager.Initialize(database);
        }
    }
}
