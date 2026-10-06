using System;
using UnityEngine;
using WS_Modules.ConfigInstaller;
using WS_Modules.LogModule;
using WS_Modules.SceneModule;

namespace RPG.Game.Config
{
    /// <summary>通过 WSFrame ConfigInstaller 注册项目的场景加载数据库。</summary>
    [CreateAssetMenu(fileName = "SceneLoadDatabaseConfigProvider", menuName = "RPG/Config/Scene Load Database")]
    public sealed class SceneLoadDatabaseConfigProvider : ConfigRegisterNodeBase
    {
        #region 数据库配置

        // ConfigInstaller 将场景库注入到统一流程的运行期查询入口。
        [SerializeField]
        private SceneLoadDatabase database;

        #endregion

        #region 注册

        /// <summary>将配置资产注入运行时查询入口。</summary>
        public override void Register()
        {
            if (database == null)
                throw new InvalidOperationException("[SceneLoadDatabaseConfigProvider] 没有配置 SceneLoadDatabase。");
            SceneLoadDatabaseRegistry.Register(database);
            WSLog.Log($"[SceneLoadDatabaseConfigProvider] 已注册场景数据库，name={database.name}，sceneCount={database.SceneConfigs.Count}。");
        }

        #endregion
    }
}
