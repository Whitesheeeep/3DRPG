using RPG.SaveSystem;
using WS_Modules.BusinessArchitecture;

namespace RPG.ItemSystem
{
    /// <summary>连接可堆叠背包 Manager 与 SaveManager 的业务 System。</summary>
    public sealed class StackableInventorySystem : AbstractSystem
    {
        private StackableInventoryManager manager;
        private SaveManager saveManager;

        /// <summary>初始化可堆叠背包并注册存档模块。</summary>
        protected override void OnInit()
        {
            manager = StackableInventoryManager.Instance;
            saveManager = this.GetManager<SaveManager>();
            saveManager.RegisterModule(new StackableInventorySaveModule(manager));
        }

        /// <summary>注销可堆叠背包并清空运行时状态。</summary>
        protected override void OnDeinit()
        {
            manager?.ClearRuntimeState();
            manager = null;
            saveManager = null;
        }
    }
}
