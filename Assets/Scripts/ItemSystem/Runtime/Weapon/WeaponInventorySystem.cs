using RPG.SaveSystem;
using WS_Modules.BusinessArchitecture;

namespace RPG.ItemSystem
{
    /// <summary>连接武器实例 Manager 与 SaveManager 的业务 System。</summary>
    public sealed class WeaponInventorySystem : AbstractSystem
    {
        private WeaponInventoryManager manager;
        private SaveManager saveManager;

        /// <summary>初始化武器实例并注册存档模块。</summary>
        protected override void OnInit()
        {
            manager = WeaponInventoryManager.Instance;
            saveManager = this.GetManager<SaveManager>();
            saveManager.RegisterModule(new WeaponInventorySaveModule(manager));
        }

        /// <summary>注销武器实例并清空运行时状态。</summary>
        protected override void OnDeinit()
        {
            manager?.ClearRuntimeState();
            manager = null;
            saveManager = null;
        }
    }
}
