using System;
using UnityEngine;
using WS_Modules.ConfigInstaller;
using WS_Modules.SceneModule;

namespace RPG.Game.Config
{
    /// <summary>通过 WSFrame ConfigInstaller 注册项目的场景加载数据库。</summary>
    [CreateAssetMenu(fileName = "SceneLoadDatabaseConfigProvider", menuName = "RPG/Config/Scene Load Database")]
    public sealed class SceneLoadDatabaseConfigProvider : ConfigRegisterNodeBase
    {
        #region 数据库配置

        // ConfigInstaller 在 Architecture 创建加载系统实例前注入场景库并建立查询索引。
        [SerializeField]
        private SceneLoadDatabase database;

        #endregion

        #region 注册

        /// <summary>将配置资产注入运行时查询入口。</summary>
        public override void Register()
        {
            if (database == null)
                throw new InvalidOperationException("[SceneLoadDatabaseConfigProvider] 没有配置 SceneLoadDatabase。");
            SceneLoadingSystem.RegisterDatabase(database);
        }

        #endregion
    }
}
