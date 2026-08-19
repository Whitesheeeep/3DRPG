using System;
using System.Collections.Generic;

namespace RPG.SaveSystem
{
    /// <summary>
    /// 在启动阶段冻结相邻模块版本迁移，并按模块和源版本解析迁移步骤。
    /// </summary>
    public sealed class SaveMigrationRegistry
    {
        private readonly Dictionary<MigrationKey, ISaveMigration> migrations;

        /// <summary>
        /// 创建并校验显式注册的迁移集合。
        /// </summary>
        /// <param name="migrations">待注册迁移集合。</param>
        /// <exception cref="ArgumentNullException">集合或迁移为空时抛出。</exception>
        /// <exception cref="ArgumentException">迁移契约非法或重复时抛出。</exception>
        public SaveMigrationRegistry(IEnumerable<ISaveMigration> migrations)
        {
            if (migrations == null)
            {
                throw new ArgumentNullException(nameof(migrations));
            }

            this.migrations = new Dictionary<MigrationKey, ISaveMigration>();
            foreach (ISaveMigration migration in migrations)
            {
                ValidateMigration(migration);
                var key = new MigrationKey(migration.ModuleId, migration.FromVersion);
                if (!this.migrations.TryAdd(key, migration))
                {
                    throw new ArgumentException(
                        $"迁移步骤重复：{migration.ModuleId} v{migration.FromVersion}",
                        nameof(migrations));
                }
            }
        }

        /// <summary>
        /// 尝试取得一个模块的下一版本迁移步骤。
        /// </summary>
        /// <param name="moduleId">模块标识。</param>
        /// <param name="fromVersion">源快照版本。</param>
        /// <param name="migration">找到的迁移步骤。</param>
        /// <returns>存在对应迁移时返回 true。</returns>
        public bool TryGet(
            SaveModuleId moduleId,
            int fromVersion,
            out ISaveMigration migration) =>
            migrations.TryGetValue(new MigrationKey(moduleId, fromVersion), out migration);

        /// <summary>
        /// 校验迁移的版本连续性、模块标识和快照类型。
        /// </summary>
        /// <param name="migration">待校验迁移。</param>
        /// <exception cref="ArgumentNullException">迁移为空时抛出。</exception>
        /// <exception cref="ArgumentException">迁移契约非法时抛出。</exception>
        private static void ValidateMigration(ISaveMigration migration)
        {
            if (migration == null)
            {
                throw new ArgumentNullException(nameof(migration), "迁移集合不能包含空项。");
            }

            if (!migration.ModuleId.IsValid || migration.FromVersion <= 0 || migration.ToVersion <= 0)
            {
                throw new ArgumentException("迁移必须包含有效模块 ID 和正整数版本。", nameof(migration));
            }

            if (migration.ToVersion != migration.FromVersion + 1)
            {
                throw new ArgumentException("第一版迁移必须连接相邻版本。", nameof(migration));
            }

            if (!IsSnapshotType(migration.SourceSnapshotType) ||
                !IsSnapshotType(migration.TargetSnapshotType))
            {
                throw new ArgumentException("迁移源和目标类型必须是具体快照类型。", nameof(migration));
            }
        }

        /// <summary>
        /// 判断类型是否为可实例化的快照 DTO 类型。
        /// </summary>
        /// <param name="snapshotType">待校验类型。</param>
        /// <returns>类型符合快照契约时返回 true。</returns>
        private static bool IsSnapshotType(Type snapshotType) =>
            snapshotType != null &&
            snapshotType.IsClass &&
            !snapshotType.IsAbstract &&
            typeof(ISaveModuleSnapshot).IsAssignableFrom(snapshotType);

        /// <summary>
        /// 标识一个模块和源版本迁移步骤。
        /// </summary>
        private readonly struct MigrationKey : IEquatable<MigrationKey>
        {
            private readonly SaveModuleId moduleId;
            private readonly int fromVersion;

            /// <summary>
            /// 创建迁移索引键。
            /// </summary>
            /// <param name="moduleId">模块标识。</param>
            /// <param name="fromVersion">源版本。</param>
            public MigrationKey(SaveModuleId moduleId, int fromVersion)
            {
                this.moduleId = moduleId;
                this.fromVersion = fromVersion;
            }

            /// <summary>
            /// 判断两个迁移键是否相等。
            /// </summary>
            /// <param name="other">另一个迁移键。</param>
            /// <returns>键相等时返回 true。</returns>
            public bool Equals(MigrationKey other) =>
                moduleId.Equals(other.moduleId) && fromVersion == other.fromVersion;

            /// <summary>
            /// 判断对象是否为相等迁移键。
            /// </summary>
            /// <param name="obj">待比较对象。</param>
            /// <returns>对象为相同迁移键时返回 true。</returns>
            public override bool Equals(object obj) => obj is MigrationKey other && Equals(other);

            /// <summary>
            /// 获取与键相等性一致的哈希值。
            /// </summary>
            /// <returns>迁移键哈希值。</returns>
            public override int GetHashCode()
            {
                unchecked
                {
                    return (moduleId.GetHashCode() * 397) ^ fromVersion;
                }
            }
        }
    }
}
