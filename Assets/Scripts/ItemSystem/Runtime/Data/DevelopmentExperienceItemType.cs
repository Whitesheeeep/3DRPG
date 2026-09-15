using System;
using UnityEngine;

namespace RPG.ItemSystem
{
    /// <summary>养成经验道具支持的成长对象；Flags 允许一个经验道具服务多个对象。</summary>
    [Flags]
    public enum DevelopmentExperienceItemType
    {
        /// <summary>未配置支持对象。</summary>
        None = 0,
        /// <summary>角色经验。</summary>
        [InspectorName("角色")] Character = 1 << 0,
        /// <summary>武器经验。</summary>
        [InspectorName("武器")] Weapon = 1 << 1,
        /// <summary>圣遗物经验。</summary>
        [InspectorName("圣遗物")] Artifact = 1 << 2
    }
}
