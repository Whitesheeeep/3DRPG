using System;
using UnityEngine;
using WS_Modules.ConfigInstaller;

namespace RPG.ItemSystem
{
    /// <summary>通过 ConfigInstaller 注入武器实例容量配置。</summary>
    [CreateAssetMenu(fileName = "WeaponInventorySettingsConfigProvider", menuName = "RPG/ItemSystem/Weapon Inventory Settings Provider", order = 31)]
    public sealed class WeaponInventorySettingsConfigProvider : ConfigRegisterNodeBase
    {
        [SerializeField] private WeaponInventorySettings settings;

        /// <summary>获取配置。</summary>
        public WeaponInventorySettings Settings => settings;

        /// <summary>验证并静态注入武器配置。</summary>
        /// <exception cref="InvalidOperationException">配置为空时抛出。</exception>
        public override void Register()
        {
            if (settings == null) throw new InvalidOperationException("WeaponInventorySettingsConfigProvider 未配置容量资产。");
            settings.Validate();
            WeaponInventoryManager.Initialize(settings);
        }
    }
}
