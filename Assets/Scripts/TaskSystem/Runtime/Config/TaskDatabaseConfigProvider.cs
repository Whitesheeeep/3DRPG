using UnityEngine;
using WS_Modules.ConfigInstaller;

namespace RPG.TaskSystem
{
    /// <summary>
    /// 将任务数据库配置注入纯 C# TaskManager 的 ConfigInstaller 叶节点。
    /// </summary>
    [CreateAssetMenu(
        fileName = "TaskDatabaseConfigProvider",
        menuName = "RPG/TaskSystem/Task Database Config Provider",
        order = 1)]
    public sealed class TaskDatabaseConfigProvider : ConfigRegisterNodeBase
    {
        [SerializeField] private TaskDatabase database;

        /// <summary>
        /// 将配置资产显式初始化到 TaskManager；此步骤必须发生在业务架构启动前。
        /// </summary>
        /// <exception cref="System.InvalidOperationException">未配置数据库资产时抛出。</exception>
        public override void Register()
        {
            if (database == null)
            {
                throw new System.InvalidOperationException("TaskDatabaseConfigProvider 未配置 TaskDatabase。 ");
            }

            TaskManager.Instance.Initialize(database);
        }
    }
}
