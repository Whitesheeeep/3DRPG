using System;
using WS_Modules.Singleton;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>管理项目当前 GameplayAbilityDatabase，并提供全局稳定 ID 查询入口。</summary>
    public sealed class GameplayAbilityManager : SingletonBase<GameplayAbilityManager>
    {
        #region 字段与属性

        private GameplayAbilityDatabase database;

        /// <summary>获取 Manager 是否已绑定并构建 Database 索引。</summary>
        public bool IsInitialized => database != null;

        /// <summary>获取当前使用的 GameplayAbilityDatabase。</summary>
        public GameplayAbilityDatabase Database => database;

        #endregion

        #region 生命周期与查询

        /// <summary>供 SingletonBase 通过反射创建单例。</summary>
        private GameplayAbilityManager()
        {
        }

        /// <summary>绑定已经完成 Bake 的 GameplayAbilityDatabase。</summary>
        /// <param name="abilityDatabase">当前项目 Bake 生成的 Ability Database。</param>
        /// <exception cref="ArgumentNullException">abilityDatabase 为 null。</exception>
        public void Initialize(GameplayAbilityDatabase abilityDatabase)
        {
            if (abilityDatabase == null) throw new ArgumentNullException(nameof(abilityDatabase));
            database = abilityDatabase;
        }

        /// <summary>清除当前 Database 引用，供退出流程和测试隔离使用。</summary>
        public void Reset() => database = null;

        /// <summary>按全局稳定 AbilityId 查询 Ability 资产。</summary>
        /// <param name="abilityId">待查询的稳定 ID。</param>
        /// <param name="ability">查询成功时返回资产。</param>
        /// <returns>Manager 已初始化且 ID 存在时返回 true。</returns>
        public bool TryGetAbility(int abilityId, out GameplayAbilityData ability)
        {
            if (database != null && database.TryGetAbility(abilityId, out ability)) return true;
            ability = null;
            return false;
        }

        #endregion
    }
}
