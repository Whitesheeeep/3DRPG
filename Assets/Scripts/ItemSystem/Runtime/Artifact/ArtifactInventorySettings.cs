using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.ItemSystem
{
    /// <summary>圣遗物实例背包容量配置。</summary>
    [CreateAssetMenu(fileName = "ArtifactInventorySettings", menuName = "RPG/ItemSystem/Artifact Inventory Settings", order = 32)]
    public sealed class ArtifactInventorySettings : ScriptableObject
    {
        [SerializeField, MinValue(1), LabelText("圣遗物容量")] private int capacity = 1800;

        /// <summary>获取圣遗物容量。</summary>
        public int Capacity => capacity;

        /// <summary>验证圣遗物容量。</summary>
        /// <exception cref="InvalidOperationException">容量无效时抛出。</exception>
        public void Validate()
        {
            if (capacity <= 0) throw new InvalidOperationException("ArtifactInventorySettings 的圣遗物容量必须大于零。");
        }
    }
}
