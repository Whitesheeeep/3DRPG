using System;
using UnityEngine;
using WS_Modules.ConfigInstaller;

namespace RPG.ItemSystem
{
    /// <summary>通过 ConfigInstaller 注入圣遗物容量配置。</summary>
    [CreateAssetMenu(fileName = "ArtifactInventorySettingsConfigProvider", menuName = "RPG/ItemSystem/Artifact Inventory Settings Provider", order = 33)]
    public sealed class ArtifactInventorySettingsConfigProvider : ConfigRegisterNodeBase
    {
        [SerializeField] private ArtifactInventorySettings settings;

        /// <summary>获取配置。</summary>
        public ArtifactInventorySettings Settings => settings;

        /// <summary>验证并静态注入圣遗物配置。</summary>
        /// <exception cref="InvalidOperationException">配置为空时抛出。</exception>
        public override void Register()
        {
            if (settings == null) throw new InvalidOperationException("ArtifactInventorySettingsConfigProvider 未配置容量资产。");
            settings.Validate();
            ArtifactInventoryManager.Initialize(settings);
        }
    }
}
