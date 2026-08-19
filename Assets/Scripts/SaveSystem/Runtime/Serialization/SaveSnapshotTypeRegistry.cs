using System;
using System.Collections.Generic;

namespace RPG.SaveSystem
{
    /// <summary>
    /// 以 ModuleId 和模块版本为联合键管理快照具体类型。
    /// </summary>
    public sealed class SaveSnapshotTypeRegistry : ISaveSnapshotTypeResolver
    {
        #region 注册与解析

        private readonly Dictionary<SnapshotTypeKey, Type> snapshotTypes =
            new Dictionary<SnapshotTypeKey, Type>();

        /// <summary>
        /// 注册指定模块版本对应的强类型快照。
        /// </summary>
        /// <typeparam name="TSnapshot">具体、非抽象快照类型。</typeparam>
        /// <param name="moduleId">稳定模块标识。</param>
        /// <param name="version">正整数模块版本。</param>
        /// <exception cref="ArgumentException">模块标识无效、快照类型抽象或注册项重复时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException">版本不是正整数时抛出。</exception>
        public void Register<TSnapshot>(SaveModuleId moduleId, int version)
            where TSnapshot : class, ISaveModuleSnapshot
        {
            Register(moduleId, version, typeof(TSnapshot));
        }

        /// <summary>
        /// 注册指定模块版本对应的运行时快照类型。
        /// </summary>
        /// <param name="moduleId">稳定模块标识。</param>
        /// <param name="version">正整数模块版本。</param>
        /// <param name="snapshotType">实现 ISaveModuleSnapshot 的具体引用类型。</param>
        /// <exception cref="ArgumentNullException">快照类型为空时抛出。</exception>
        /// <exception cref="ArgumentException">模块标识无效、快照类型不符合契约或注册项重复时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException">版本不是正整数时抛出。</exception>
        public void Register(SaveModuleId moduleId, int version, Type snapshotType)
        {
            if (!moduleId.IsValid)
            {
                throw new ArgumentException("快照类型必须注册到有效 ModuleId。", nameof(moduleId));
            }

            if (version <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(version), "模块版本必须大于零。");
            }

            if (snapshotType == null)
            {
                throw new ArgumentNullException(nameof(snapshotType));
            }

            if (!snapshotType.IsClass || snapshotType.IsAbstract ||
                !typeof(ISaveModuleSnapshot).IsAssignableFrom(snapshotType))
            {
                throw new ArgumentException("快照必须是实现 ISaveModuleSnapshot 的具体引用类型。", nameof(snapshotType));
            }

            var key = new SnapshotTypeKey(moduleId, version);
            if (!snapshotTypes.TryAdd(key, snapshotType))
            {
                throw new ArgumentException($"快照类型已注册：{moduleId} v{version}", nameof(snapshotType));
            }
        }

        /// <summary>
        /// 解析指定模块版本的快照具体类型。
        /// </summary>
        /// <param name="moduleId">模块标识。</param>
        /// <param name="version">模块版本。</param>
        /// <returns>已注册类型或 UnsupportedModuleVersion 失败。</returns>
        public SaveResult<Type> Resolve(SaveModuleId moduleId, int version)
        {
            if (moduleId.IsValid && version > 0 &&
                snapshotTypes.TryGetValue(new SnapshotTypeKey(moduleId, version), out Type snapshotType))
            {
                return SaveResult<Type>.Success(snapshotType);
            }

            return SaveResult<Type>.Failure(
                SaveErrorCode.UnsupportedModuleVersion,
                $"未注册模块快照类型：{moduleId} v{version}");
        }

        /// <summary>
        /// 创建当前快照类型注册表的只读时点副本，供一次加载操作隔离后续注册变更。
        /// </summary>
        /// <returns>包含当前全部类型映射的新注册表。</returns>
        public SaveSnapshotTypeRegistry CreateSnapshot()
        {
            var snapshot = new SaveSnapshotTypeRegistry();
            foreach (KeyValuePair<SnapshotTypeKey, Type> pair in snapshotTypes)
            {
                snapshot.snapshotTypes.Add(pair.Key, pair.Value);
            }

            return snapshot;
        }

        #endregion

        #region 联合键

        /// <summary>
        /// 表示快照类型注册表的联合键。
        /// </summary>
        private readonly struct SnapshotTypeKey : IEquatable<SnapshotTypeKey>
        {
            private readonly SaveModuleId moduleId;
            private readonly int version;

            /// <summary>
            /// 创建快照类型联合键。
            /// </summary>
            /// <param name="moduleId">模块标识。</param>
            /// <param name="version">模块版本。</param>
            public SnapshotTypeKey(SaveModuleId moduleId, int version)
            {
                this.moduleId = moduleId;
                this.version = version;
            }

            /// <summary>
            /// 判断联合键的模块和版本是否全部相等。
            /// </summary>
            /// <param name="other">另一个联合键。</param>
            /// <returns>模块和版本都相同时返回 true。</returns>
            public bool Equals(SnapshotTypeKey other) => moduleId.Equals(other.moduleId) && version == other.version;

            /// <summary>
            /// 判断对象是否表示相同联合键。
            /// </summary>
            /// <param name="obj">待比较对象。</param>
            /// <returns>对象为相同联合键时返回 true。</returns>
            public override bool Equals(object obj) => obj is SnapshotTypeKey other && Equals(other);

            /// <summary>
            /// 获取与联合键相等性一致的哈希值。
            /// </summary>
            /// <returns>联合键哈希值。</returns>
            public override int GetHashCode()
            {
                unchecked
                {
                    return (moduleId.GetHashCode() * 397) ^ version;
                }
            }
        }

        #endregion
    }
}
