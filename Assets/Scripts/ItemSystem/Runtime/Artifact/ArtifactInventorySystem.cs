using RPG.SaveSystem;
using WS_Modules.BusinessArchitecture;

namespace RPG.ItemSystem
{
    /// <summary>连接圣遗物实例 Manager 与 SaveManager 的业务 System。</summary>
    public sealed class ArtifactInventorySystem : AbstractSystem
    {
        private ArtifactInventoryManager manager;
        private SaveManager saveManager;

        /// <summary>初始化圣遗物实例并注册存档模块。</summary>
        protected override void OnInit()
        {
            manager = ArtifactInventoryManager.Instance;
            saveManager = this.GetManager<SaveManager>();
            saveManager.RegisterModule(new ArtifactInventorySaveModule(manager));
        }

        /// <summary>注销圣遗物实例并清空运行时状态。</summary>
        protected override void OnDeinit()
        {
            manager?.ClearRuntimeState();
            manager = null;
            saveManager = null;
        }
    }
}
