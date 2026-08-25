using System;
using UnityEngine;
using WS_Modules.ConfigInstaller;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS
{
    /// <summary>
    /// 将项目 Gameplay Tag 数据库注册到运行时管理器，保证输入 Intent 等标签容器在业务启动前可用。
    /// </summary>
    [CreateAssetMenu(
        fileName = "GameplayTagDatabaseConfigProvider",
        menuName = "WSFrame/GAS/Gameplay Tag Database Config Provider",
        order = 1)]
    public sealed class GameplayTagDatabaseConfigProvider : ConfigRegisterNodeBase
    {
        #region 配置

        // 由 ConfigInstaller 注入的烘焙数据库；运行时只读取，不修改资产内容。
        [SerializeField] private GameplayTagDatabase database;

        #endregion

        #region 注册

        /// <summary>
        /// 将序列化的 Gameplay Tag 数据库绑定到全局管理器。
        /// </summary>
        /// <exception cref="InvalidOperationException">未配置数据库资产时抛出。</exception>
        public override void Register()
        {
            if (database == null)
                throw new InvalidOperationException(
                    "GameplayTagDatabaseConfigProvider 未配置 GameplayTagDatabase。 ");

            GameplayTagManager.Instance.Initialize(database);
        }

        #endregion
    }
}
