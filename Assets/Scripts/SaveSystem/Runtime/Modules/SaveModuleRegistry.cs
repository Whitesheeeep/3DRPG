using System;
using System.Collections.Generic;

namespace RPG.SaveSystem
{
    /// <summary>
    /// 在启动阶段冻结存档模块集合，并计算稳定采集顺序与依赖恢复顺序。
    /// </summary>
    public sealed class SaveModuleRegistry
    {
        private readonly Dictionary<SaveModuleId, ISaveModule> modules;
        // OrderedModules 是按 ModuleId Ordinal 排序的模块列表，保证采集时顺序稳定。
        private readonly IReadOnlyList<ISaveModule> orderedModules;
        // RestoreOrder 是按依赖拓扑排序的模块列表，保证恢复时依赖模块先于被依赖模块。
        private readonly IReadOnlyList<ISaveModule> restoreOrder;

        /// <summary>
        /// 创建并校验显式注册的模块集合。
        /// </summary>
        /// <param name="modules">待注册模块集合。</param>
        /// <exception cref="ArgumentNullException">集合或模块为空时抛出。</exception>
        /// <exception cref="ArgumentException">模块重复、依赖无效或依赖成环时抛出。</exception>
        public SaveModuleRegistry(IEnumerable<ISaveModule> modules)
        {
            if (modules == null)
            {
                throw new ArgumentNullException(nameof(modules));
            }

            this.modules = new Dictionary<SaveModuleId, ISaveModule>();
            foreach (ISaveModule module in modules)
            {
                ValidateModule(module);
                if (!this.modules.TryAdd(module.ModuleId, module))
                {
                    throw new ArgumentException($"存档模块 ID 重复：{module.ModuleId}", nameof(modules));
                }
            }

            var sorted = new List<ISaveModule>(this.modules.Values);
            sorted.Sort((left, right) => left.ModuleId.CompareTo(right.ModuleId));
            orderedModules = sorted.AsReadOnly();
            restoreOrder = BuildRestoreOrder();
        }

        /// <summary>
        /// 获取按 ModuleId Ordinal 顺序排列的采集列表。
        /// </summary>
        public IReadOnlyList<ISaveModule> OrderedModules => orderedModules;

        /// <summary>
        /// 获取按依赖拓扑顺序排列的恢复列表。
        /// </summary>
        public IReadOnlyList<ISaveModule> RestoreOrder => restoreOrder;

        /// <summary>
        /// 尝试取得指定模块。
        /// </summary>
        /// <param name="moduleId">模块标识。</param>
        /// <param name="module">找到的模块。</param>
        /// <returns>注册表包含该模块时返回 true。</returns>
        public bool TryGet(SaveModuleId moduleId, out ISaveModule module) =>
            modules.TryGetValue(moduleId, out module);

        /// <summary>
        /// 校验模块自身的版本、ID 和快照类型契约。
        /// </summary>
        /// <param name="module">待校验模块。</param>
        /// <exception cref="ArgumentNullException">模块为空时抛出。</exception>
        /// <exception cref="ArgumentException">模块契约不合法时抛出。</exception>
        private static void ValidateModule(ISaveModule module)
        {
            if (module == null)
            {
                throw new ArgumentNullException(nameof(module), "模块集合不能包含空项。");
            }

            if (!module.ModuleId.IsValid)
            {
                throw new ArgumentException("存档模块必须使用有效 ModuleId。", nameof(module));
            }

            if (module.CurrentVersion <= 0)
            {
                throw new ArgumentException($"模块 {module.ModuleId} 的当前版本必须大于零。", nameof(module));
            }

            if (module.CurrentSnapshotType == null ||
                !module.CurrentSnapshotType.IsClass ||
                module.CurrentSnapshotType.IsAbstract ||
                !typeof(ISaveModuleSnapshot).IsAssignableFrom(module.CurrentSnapshotType))
            {
                throw new ArgumentException($"模块 {module.ModuleId} 的当前快照类型不符合契约。", nameof(module));
            }

            if (module.RestoreDependencies == null)
            {
                throw new ArgumentException($"模块 {module.ModuleId} 的恢复依赖列表不能为 null。", nameof(module));
            }
        }

        /// <summary>
        /// 按依赖完成拓扑排序，并用 ModuleId 作为同层稳定顺序。
        /// </summary>
        /// <returns>恢复顺序。</returns>
        /// <exception cref="ArgumentException">依赖不存在、自依赖或成环时抛出。</exception>
        private IReadOnlyList<ISaveModule> BuildRestoreOrder()
        {
            var states = new Dictionary<SaveModuleId, VisitState>();
            var result = new List<ISaveModule>();
            foreach (ISaveModule module in orderedModules)
            {
                Visit(module, states, result);
            }

            return result.AsReadOnly();
        }

        /// <summary>
        /// 深度优先访问一个模块及其恢复依赖。
        /// </summary>
        /// <param name="module">当前模块。</param>
        /// <param name="states">访问状态表。</param>
        /// <param name="result">已完成的拓扑结果。</param>
        /// <exception cref="ArgumentException">依赖不存在或发现循环依赖时抛出。</exception>
        private void Visit(
            ISaveModule module,
            IDictionary<SaveModuleId, VisitState> states,
            IList<ISaveModule> result)
        {
            if (states.TryGetValue(module.ModuleId, out VisitState state))
            {
                if (state == VisitState.Completed)
                {
                    return;
                }

                throw new ArgumentException($"存档模块恢复依赖存在循环：{module.ModuleId}");
            }

            states[module.ModuleId] = VisitState.Visiting;
            var dependencies = new List<SaveModuleId>(module.RestoreDependencies);
            dependencies.Sort((left, right) => left.CompareTo(right));
            foreach (SaveModuleId dependencyId in dependencies)
            {
                if (dependencyId == module.ModuleId)
                {
                    throw new ArgumentException($"存档模块不能依赖自身：{module.ModuleId}");
                }

                if (!modules.TryGetValue(dependencyId, out ISaveModule dependency))
                {
                    throw new ArgumentException(
                        $"模块 {module.ModuleId} 依赖未注册模块：{dependencyId}");
                }

                Visit(dependency, states, result);
            }

            states[module.ModuleId] = VisitState.Completed;
            result.Add(module);
        }

        /// <summary>
        /// 标记拓扑排序中的节点访问状态。
        /// </summary>
        private enum VisitState
        {
            /// <summary>正在访问。</summary>
            Visiting,
            /// <summary>已完成。</summary>
            Completed
        }
    }
}
