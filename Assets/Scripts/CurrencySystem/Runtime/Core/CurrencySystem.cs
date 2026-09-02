using RPG.SaveSystem;
using WS_Modules.BusinessArchitecture;

namespace RPG.CurrencySystem
{
    /// <summary>连接货币 Manager 与 SaveManager 的业务 System。</summary>
    public sealed class CurrencySystem : AbstractSystem
    {
        private CurrencyManager manager;
        private SaveManager saveManager;

        /// <summary>初始化货币钱包并注册存档模块。</summary>
        protected override void OnInit()
        {
            manager = CurrencyManager.Instance;
            saveManager = this.GetManager<SaveManager>();
            saveManager.RegisterModule(new CurrencySaveModule(manager));
        }

        /// <summary>注销货币钱包并清空运行时状态。</summary>
        protected override void OnDeinit()
        {
            manager?.ClearRuntimeState();
            manager = null;
            saveManager = null;
        }
    }
}
