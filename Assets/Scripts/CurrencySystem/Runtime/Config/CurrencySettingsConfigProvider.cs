using System;
using UnityEngine;
using WS_Modules.ConfigInstaller;

namespace RPG.CurrencySystem
{
    /// <summary>通过 ConfigInstaller 注入货币钱包配置。</summary>
    [CreateAssetMenu(fileName = "CurrencySettingsConfigProvider", menuName = "RPG/Currency/Currency Settings Provider", order = 1)]
    public sealed class CurrencySettingsConfigProvider : ConfigRegisterNodeBase
    {
        [SerializeField] private CurrencySettings settings;

        /// <summary>获取货币配置。</summary>
        public CurrencySettings Settings => settings;

        /// <summary>验证并静态注入货币配置。</summary>
        /// <exception cref="InvalidOperationException">配置为空时抛出。</exception>
        public override void Register()
        {
            if (settings == null) throw new InvalidOperationException("CurrencySettingsConfigProvider 未配置货币资产。");
            settings.Validate();
            CurrencyManager.Initialize(settings);
        }
    }
}
