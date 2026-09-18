using RPG.SaveSystem;
using RPG.Character;
using WS_Modules.BusinessArchitecture;

namespace RPG.ItemSystem
{
    /// <summary>连接武器实例 Manager 与 SaveManager 的业务 System。</summary>
    public sealed class WeaponInventorySystem : AbstractSystem
    {
        #region 依赖字段

        private WeaponInventoryManager manager;
        private SaveManager saveManager;
        private CharacterRosterManager characterRosterManager;

        #endregion

        #region 生命周期

        /// <summary>初始化武器实例并注册存档模块。</summary>
        protected override void OnInit()
        {
            manager = WeaponInventoryManager.Instance;
            saveManager = this.GetManager<SaveManager>();
            characterRosterManager = this.GetManager<CharacterRosterManager>();
            saveManager.RegisterModule(new WeaponInventorySaveModule(manager, characterRosterManager));
            UnityEngine.Debug.Log("[WeaponInventorySystem] 武器库存存档模块已注册，恢复依赖=character-roster。 ");
        }

        /// <summary>注销武器实例并清空运行时状态。</summary>
        protected override void OnDeinit()
        {
            manager?.ClearRuntimeState();
            manager = null;
            saveManager = null;
            characterRosterManager = null;
            UnityEngine.Debug.Log("[WeaponInventorySystem] 武器库存系统已注销。 ");
        }

        #endregion
    }
}
