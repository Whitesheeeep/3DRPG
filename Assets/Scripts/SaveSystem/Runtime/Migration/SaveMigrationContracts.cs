using System;

namespace RPG.SaveSystem
{
    #region 非泛型迁移接口

    /// <summary>
    /// 定义同一模块相邻快照版本之间的一次纯数据迁移。
    /// </summary>
    public interface ISaveMigration
    {
        /// <summary>
        /// 获取迁移所属模块。
        /// </summary>
        SaveModuleId ModuleId { get; }

        /// <summary>
        /// 获取源快照版本。
        /// </summary>
        int FromVersion { get; }

        /// <summary>
        /// 获取目标快照版本。
        /// </summary>
        int ToVersion { get; }

        /// <summary>
        /// 获取源快照 CLR 类型。
        /// </summary>
        Type SourceSnapshotType { get; }

        /// <summary>
        /// 获取目标快照 CLR 类型。
        /// </summary>
        Type TargetSnapshotType { get; }

        /// <summary>
        /// 将源版本快照迁移为下一版本纯数据快照。
        /// </summary>
        /// <param name="sourceSnapshot">源版本快照。</param>
        /// <returns>下一版本快照。</returns>
        ISaveModuleSnapshot Migrate(ISaveModuleSnapshot sourceSnapshot);
    }

    #endregion

    #region 泛型迁移基类

    /// <summary>
    /// 将非泛型迁移入口安全转发给强类型相邻版本迁移实现。
    /// </summary>
    /// <typeparam name="TSource">源版本快照类型。</typeparam>
    /// <typeparam name="TTarget">目标版本快照类型。</typeparam>
    public abstract class SaveMigration<TSource, TTarget> : ISaveMigration
        where TSource : class, ISaveModuleSnapshot
        where TTarget : class, ISaveModuleSnapshot
    {
        /// <summary>
        /// 初始化相邻版本迁移。
        /// </summary>
        /// <param name="moduleId">迁移所属模块。</param>
        /// <param name="fromVersion">正整数源版本；目标版本固定为源版本加一。</param>
        /// <exception cref="ArgumentException">模块 ID 无效时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException">源版本不是正整数或无法递增时抛出。</exception>
        protected SaveMigration(SaveModuleId moduleId, int fromVersion)
        {
            if (!moduleId.IsValid)
            {
                throw new ArgumentException("存档迁移必须使用有效 ModuleId。", nameof(moduleId));
            }

            if (fromVersion <= 0 || fromVersion == int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(fromVersion), "迁移源版本必须是可递增的正整数。");
            }

            ModuleId = moduleId;
            FromVersion = fromVersion;
            ToVersion = fromVersion + 1;
        }

        /// <summary>
        /// 获取迁移所属模块。
        /// </summary>
        public SaveModuleId ModuleId { get; }

        /// <summary>
        /// 获取源版本。
        /// </summary>
        public int FromVersion { get; }

        /// <summary>
        /// 获取相邻目标版本。
        /// </summary>
        public int ToVersion { get; }

        /// <summary>
        /// 获取源快照 CLR 类型。
        /// </summary>
        public Type SourceSnapshotType => typeof(TSource);

        /// <summary>
        /// 获取目标快照 CLR 类型。
        /// </summary>
        public Type TargetSnapshotType => typeof(TTarget);

        /// <summary>
        /// 实现强类型纯数据迁移。
        /// </summary>
        /// <param name="sourceSnapshot">源版本快照。</param>
        /// <returns>非空下一版本快照。</returns>
        protected abstract TTarget MigrateTyped(TSource sourceSnapshot);

        /// <summary>
        /// 校验源快照运行时类型后转发到强类型迁移逻辑。
        /// </summary>
        /// <param name="sourceSnapshot">源版本快照。</param>
        /// <returns>下一版本快照。</returns>
        /// <exception cref="ArgumentNullException">源快照为空时抛出。</exception>
        /// <exception cref="ArgumentException">源快照类型不匹配时抛出。</exception>
        /// <exception cref="InvalidOperationException">迁移返回空目标快照时抛出。</exception>
        public ISaveModuleSnapshot Migrate(ISaveModuleSnapshot sourceSnapshot)
        {
            if (sourceSnapshot == null)
            {
                throw new ArgumentNullException(nameof(sourceSnapshot));
            }

            if (!(sourceSnapshot is TSource typedSource))
            {
                throw new ArgumentException(
                    $"迁移 {ModuleId} v{FromVersion}->v{ToVersion} 需要 {typeof(TSource).FullName}，" +
                    $"实际收到 {sourceSnapshot.GetType().FullName}。",
                    nameof(sourceSnapshot));
            }

            TTarget target = MigrateTyped(typedSource);
            return target ?? throw new InvalidOperationException(
                $"迁移 {ModuleId} v{FromVersion}->v{ToVersion} 返回了空目标快照。");
        }
    }

    #endregion
}
