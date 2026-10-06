using System;

namespace WS_Modules.SceneModule
{
    /// <summary>保存 ConfigInstaller 提供给业务场景流程的唯一场景数据库。</summary>
    public static class SceneLoadDatabaseRegistry
    {
        #region 注册状态

        /// <summary>获取最近一次注册的数据库。</summary>
        public static SceneLoadDatabase Database { get; private set; }

        #endregion

        #region 注册

        /// <summary>注册运行期场景配置数据库。</summary>
        /// <param name="database">由 ConfigInstaller 提供的配置数据库。</param>
        public static void Register(SceneLoadDatabase database)
        {
            Database = database ?? throw new ArgumentNullException(nameof(database), "[SceneLoadDatabaseRegistry] 数据库不能为空。");
        }

        #endregion
    }
}
