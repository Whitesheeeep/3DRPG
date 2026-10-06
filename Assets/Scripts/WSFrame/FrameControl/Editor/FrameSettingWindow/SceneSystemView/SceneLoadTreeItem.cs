using WS_Modules.SceneModule;

namespace WS_Modules
{
    /// <summary>描述配置树内某一个引用位置；同一任务资产的不同树路径使用不同实例。</summary>
    internal sealed class SceneLoadTreeItem
    {
        #region 树行数据

        /// <summary>获取本次树重建分配的唯一行 ID。</summary>
        public int Id { get; }
        /// <summary>获取此行所属的场景配置；任务行也保留所属配置。</summary>
        public SceneLoadConfig Config { get; }
        /// <summary>获取此行代表的任务资产；配置行为空。</summary>
        public SceneLoadTask Task { get; }
        /// <summary>获取当前任务引用所在的父组合；根任务为空。</summary>
        public SceneLoadTask ParentTask { get; }
        /// <summary>获取当前配置在数据库中的顺序。</summary>
        public int ConfigIndex { get; }
        /// <summary>获取当前任务在父组合中的真实顺序。</summary>
        public int SiblingIndex { get; }
        /// <summary>获取可以区分共享资产不同引用位置的路径。</summary>
        public string ReferencePath { get; }
        /// <summary>获取此行的层级深度。</summary>
        public int Depth { get; }
        /// <summary>获取配置行或任务类型的可读标识。</summary>
        public string TypeLabel { get; }
        /// <summary>获取创建此行时记录的节点种类。</summary>
        public bool IsConfiguration { get; }

        #endregion

        #region 生命周期

        /// <summary>创建本次树渲染使用的不可变行快照。</summary>
        /// <param name="id">唯一行 ID。</param>
        /// <param name="isConfiguration">此行是否代表场景配置。</param>
        /// <param name="config">场景配置行资产或任务行所属配置。</param>
        /// <param name="task">任务行资产。</param>
        /// <param name="parentTask">任务的直接父组合。</param>
        /// <param name="configIndex">数据库中的场景配置位置。</param>
        /// <param name="siblingIndex">父组合中的真实顺序。</param>
        /// <param name="referencePath">完整引用位置路径。</param>
        /// <param name="depth">树层级。</param>
        /// <param name="typeLabel">用于辅助辨识的类型标签。</param>
        public SceneLoadTreeItem(
            int id,
            bool isConfiguration,
            SceneLoadConfig config,
            SceneLoadTask task,
            SceneLoadTask parentTask,
            int configIndex,
            int siblingIndex,
            string referencePath,
            int depth,
            string typeLabel)
        {
            Id = id;
            IsConfiguration = isConfiguration;
            Config = config;
            Task = task;
            ParentTask = parentTask;
            ConfigIndex = configIndex;
            SiblingIndex = siblingIndex;
            ReferencePath = referencePath;
            Depth = depth;
            TypeLabel = typeLabel;
        }

        #endregion
    }
}
