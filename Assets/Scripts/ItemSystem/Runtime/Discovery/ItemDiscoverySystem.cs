using RPG.SaveSystem;
using UnityEngine;
using WS_Modules.BusinessArchitecture;

namespace RPG.ItemSystem
{
    /// <summary>连接物品发现 Manager 与 SaveManager 的业务 System。</summary>
    public sealed class ItemDiscoverySystem : AbstractSystem
    {
        #region 依赖字段

        private ItemDiscoveryManager manager;
        private SaveManager saveManager;

        #endregion

        #region 生命周期

        /// <summary>初始化发现状态并注册必需的发现记录存档模块。</summary>
        protected override void OnInit()
        {
            manager = ItemDiscoveryManager.Instance;
            saveManager = this.GetManager<SaveManager>();
            saveManager.RegisterModule(new ItemDiscoverySaveModule(manager));
            Debug.Log("[ItemDiscoverySystem] 已注册物品发现 Manager 与 item-discovery 存档模块。" );
        }

        /// <summary>注销发现状态并清空运行时集合。</summary>
        protected override void OnDeinit()
        {
            manager?.ClearRuntimeState();
            Debug.Log("[ItemDiscoverySystem] 已清理物品发现运行时状态。" );
            manager = null;
            saveManager = null;
        }

        #endregion
    }
}
