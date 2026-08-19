using System;
using System.Collections.Generic;

namespace RPG.SaveSystem
{
    #region 快照与缺失策略

    /// <summary>
    /// 标记一个可独立序列化且不持有运行时服务或 Unity 对象的业务快照 DTO。
    /// </summary>
    public interface ISaveModuleSnapshot
    {
    }

    /// <summary>
    /// 定义加载旧存档时缺少某个已注册模块的处理策略。
    /// </summary>
    public enum SaveMissingModulePolicy
    {
        /// <summary>由模块创建明确的默认快照后继续恢复。</summary>
        CreateDefault = 0,
        /// <summary>模块是必需数据，缺失时拒绝加载。</summary>
        Required = 1
    }

    #endregion

    #region 非泛型模块接口

    /// <summary>
    /// 定义存档编排器用于统一管理异构业务模块的运行时契约。
    /// </summary>
    public interface ISaveModule
    {
        /// <summary>
        /// 获取长期稳定的模块标识。
        /// </summary>
        SaveModuleId ModuleId { get; }

        /// <summary>
        /// 获取模块当前快照版本。
        /// </summary>
        int CurrentVersion { get; }

        /// <summary>
        /// 获取当前版本快照的实际 CLR 类型。
        /// </summary>
        Type CurrentSnapshotType { get; }

        /// <summary>
        /// 获取旧存档缺少该模块时的处理策略。
        /// </summary>
        SaveMissingModulePolicy MissingModulePolicy { get; }

        /// <summary>
        /// 获取恢复该模块前必须已经恢复的模块 ID。
        /// </summary>
        IReadOnlyList<SaveModuleId> RestoreDependencies { get; }

        /// <summary>
        /// 在主线程采集当前模块的独立快照。
        /// </summary>
        /// <returns>当前版本快照。</returns>
        ISaveModuleSnapshot CaptureSnapshot();

        /// <summary>
        /// 为缺失且允许默认恢复的模块创建初始快照。
        /// </summary>
        /// <returns>当前版本默认快照。</returns>
        ISaveModuleSnapshot CreateDefaultSnapshot();

        /// <summary>
        /// 只校验当前版本快照，不修改运行中的模块状态。
        /// </summary>
        /// <param name="snapshot">已经完成反序列化和迁移的快照。</param>
        void ValidateSnapshot(ISaveModuleSnapshot snapshot);

        /// <summary>
        /// 将已经完成校验的当前版本快照恢复到模块。
        /// </summary>
        /// <param name="snapshot">已经通过校验的快照。</param>
        void RestoreSnapshot(ISaveModuleSnapshot snapshot);
    }

    #endregion

    #region 泛型模块基类

    /// <summary>
    /// 将非泛型模块入口安全转发给强类型快照实现。
    /// </summary>
    /// <typeparam name="TSnapshot">模块当前版本快照类型。</typeparam>
    public abstract class SaveModule<TSnapshot> : ISaveModule
        where TSnapshot : class, ISaveModuleSnapshot
    {
        private readonly SaveModuleId moduleId;
        private readonly int currentVersion;
        private readonly SaveMissingModulePolicy missingModulePolicy;
        private readonly IReadOnlyList<SaveModuleId> restoreDependencies;

        /// <summary>
        /// 初始化强类型存档模块。
        /// </summary>
        /// <param name="moduleId">稳定模块标识。</param>
        /// <param name="currentVersion">正整数当前快照版本。</param>
        /// <param name="missingModulePolicy">旧档缺少模块时的策略。</param>
        /// <param name="restoreDependencies">恢复前置模块；传空表示没有依赖。</param>
        /// <exception cref="ArgumentException">模块 ID 无效时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException">版本不是正整数时抛出。</exception>
        protected SaveModule(
            SaveModuleId moduleId,
            int currentVersion,
            SaveMissingModulePolicy missingModulePolicy,
            IReadOnlyList<SaveModuleId> restoreDependencies = null)
        {
            if (!moduleId.IsValid)
            {
                throw new ArgumentException("存档模块必须使用有效 ModuleId。", nameof(moduleId));
            }

            if (currentVersion <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(currentVersion), "模块版本必须大于零。");
            }

            this.moduleId = moduleId;
            this.currentVersion = currentVersion;
            this.missingModulePolicy = missingModulePolicy;
            this.restoreDependencies = CopyDependencies(restoreDependencies);
        }

        /// <summary>
        /// 获取长期稳定的模块标识。
        /// </summary>
        public SaveModuleId ModuleId => moduleId;

        /// <summary>
        /// 获取模块当前快照版本。
        /// </summary>
        public int CurrentVersion => currentVersion;

        /// <summary>
        /// 获取当前版本快照类型。
        /// </summary>
        public Type CurrentSnapshotType => typeof(TSnapshot);

        /// <summary>
        /// 获取旧存档缺少模块时的处理策略。
        /// </summary>
        public SaveMissingModulePolicy MissingModulePolicy => missingModulePolicy;

        /// <summary>
        /// 获取恢复依赖的只读副本。
        /// </summary>
        public IReadOnlyList<SaveModuleId> RestoreDependencies => restoreDependencies;

        /// <summary>
        /// 采集强类型当前快照。
        /// </summary>
        /// <returns>非空当前版本快照。</returns>
        protected abstract TSnapshot CaptureTypedSnapshot();

        /// <summary>
        /// 为 CreateDefault 策略创建强类型默认快照。
        /// </summary>
        /// <returns>非空当前版本默认快照。</returns>
        /// <exception cref="InvalidOperationException">派生模块未提供默认快照实现时抛出。</exception>
        protected virtual TSnapshot CreateDefaultTypedSnapshot() =>
            throw new InvalidOperationException($"模块 {ModuleId} 没有提供默认快照。");

        /// <summary>
        /// 校验强类型快照，不产生运行时状态副作用。
        /// </summary>
        /// <param name="snapshot">当前版本快照。</param>
        protected abstract void ValidateTypedSnapshot(TSnapshot snapshot);

        /// <summary>
        /// 将已经通过校验的强类型快照恢复到模块。
        /// </summary>
        /// <param name="snapshot">当前版本快照。</param>
        protected abstract void RestoreTypedSnapshot(TSnapshot snapshot);

        /// <summary>
        /// 通过强类型实现采集模块快照，并阻止模块返回空快照。
        /// </summary>
        /// <returns>当前版本模块快照。</returns>
        ISaveModuleSnapshot ISaveModule.CaptureSnapshot()
        {
            TSnapshot snapshot = CaptureTypedSnapshot();
            return snapshot ?? throw new InvalidOperationException($"模块 {ModuleId} 返回了空存档快照。");
        }

        /// <summary>
        /// 通过强类型实现创建默认快照，并阻止 Required 模块进入默认恢复路径。
        /// </summary>
        /// <returns>当前版本默认快照。</returns>
        ISaveModuleSnapshot ISaveModule.CreateDefaultSnapshot()
        {
            if (MissingModulePolicy == SaveMissingModulePolicy.Required)
            {
                throw new InvalidOperationException($"必需模块 {ModuleId} 不能创建缺失数据的默认快照。");
            }

            TSnapshot snapshot = CreateDefaultTypedSnapshot();
            return snapshot ?? throw new InvalidOperationException($"模块 {ModuleId} 返回了空默认快照。");
        }

        /// <summary>
        /// 校验快照运行时类型后转发到强类型校验逻辑。
        /// </summary>
        /// <param name="snapshot">待校验快照。</param>
        void ISaveModule.ValidateSnapshot(ISaveModuleSnapshot snapshot) =>
            ValidateTypedSnapshot(RequireTypedSnapshot(snapshot));

        /// <summary>
        /// 校验快照运行时类型后转发到强类型恢复逻辑。
        /// </summary>
        /// <param name="snapshot">待恢复快照。</param>
        void ISaveModule.RestoreSnapshot(ISaveModuleSnapshot snapshot) =>
            RestoreTypedSnapshot(RequireTypedSnapshot(snapshot));

        /// <summary>
        /// 将依赖列表复制为稳定只读数组，避免调用方后续修改恢复约束。
        /// </summary>
        /// <param name="dependencies">调用方提供的依赖列表。</param>
        /// <returns>独立依赖数组。</returns>
        private static IReadOnlyList<SaveModuleId> CopyDependencies(IReadOnlyList<SaveModuleId> dependencies)
        {
            if (dependencies == null || dependencies.Count == 0)
            {
                return Array.Empty<SaveModuleId>();
            }

            var copy = new SaveModuleId[dependencies.Count];
            for (int index = 0; index < dependencies.Count; index++)
            {
                SaveModuleId dependency = dependencies[index];
                if (!dependency.IsValid)
                {
                    throw new ArgumentException("恢复依赖不能包含无效 ModuleId。", nameof(dependencies));
                }

                copy[index] = dependency;
            }

            return copy;
        }

        /// <summary>
        /// 验证非泛型入口收到的快照属于当前模块类型。
        /// </summary>
        /// <param name="snapshot">非泛型快照。</param>
        /// <returns>强类型快照。</returns>
        /// <exception cref="ArgumentNullException">快照为空时抛出。</exception>
        /// <exception cref="ArgumentException">快照类型与模块不匹配时抛出。</exception>
        private TSnapshot RequireTypedSnapshot(ISaveModuleSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (!(snapshot is TSnapshot typedSnapshot))
            {
                throw new ArgumentException(
                    $"模块 {ModuleId} 需要 {typeof(TSnapshot).FullName}，实际收到 {snapshot.GetType().FullName}。",
                    nameof(snapshot));
            }

            return typedSnapshot;
        }
    }

    #endregion
}
