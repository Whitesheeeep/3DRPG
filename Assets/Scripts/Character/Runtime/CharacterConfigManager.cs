using System;
using System.Collections.Generic;
using WS_Modules.Singleton;

namespace RPG.Character
{
    /// <summary>角色配置的全局查询门面；不持有 Actor，也不负责资源和帧阶段。</summary>
    public sealed class CharacterConfigManager : SingletonBase<CharacterConfigManager>
    {
        #region 依赖字段

        private CharacterDatabase database;

        #endregion

        #region 构造与注入

        /// <summary>创建配置管理器实例。</summary>
        private CharacterConfigManager()
        {
        }

        /// <summary>注入唯一角色数据库；重复注入同一引用保持幂等。</summary>
        /// <param name="sourceDatabase">待注入数据库。</param>
        public void Initialize(CharacterDatabase sourceDatabase)
        {
            if (sourceDatabase == null) throw new ArgumentNullException(nameof(sourceDatabase));
            sourceDatabase.ValidateAndBuildIndex();
            if (database != null && !ReferenceEquals(database, sourceDatabase))
                throw new InvalidOperationException("[CharacterConfigManager] 不允许替换已注入的 CharacterDatabase。");
            database = sourceDatabase;
        }

        #endregion

        #region 查询

        /// <summary>按 CharacterId 尝试读取配置。</summary>
        /// <param name="characterId">稳定角色标识。</param>
        /// <param name="config">找到的配置。</param>
        /// <returns>找到时返回 true。</returns>
        public bool TryGetConfig(CharacterId characterId, out CharacterConfig config)
        {
            EnsureDatabase();
            return database.TryGetConfig(characterId, out config);
        }

        /// <summary>读取指定角色的必需配置。</summary>
        /// <param name="characterId">稳定角色标识。</param>
        /// <returns>对应配置。</returns>
        public CharacterConfig GetRequiredConfig(CharacterId characterId)
        {
            EnsureDatabase();
            return database.GetRequiredConfig(characterId);
        }

        /// <summary>确保配置数据库已经注入。</summary>
        private void EnsureDatabase()
        {
            if (database == null) throw new InvalidOperationException("[CharacterConfigManager] 尚未注入 CharacterDatabase。");
        }

        #endregion
    }
}
