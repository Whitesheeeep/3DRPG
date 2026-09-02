using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.ItemSystem
{
    /// <summary>武器实例背包的静态容量配置。</summary>
    [CreateAssetMenu(fileName = "WeaponInventorySettings", menuName = "RPG/ItemSystem/Weapon Inventory Settings", order = 30)]
    public sealed class WeaponInventorySettings : ScriptableObject
    {
        [SerializeField, MinValue(1), LabelText("武器容量")] private int capacity = 2000;

        /// <summary>获取武器容量。</summary>
        public int Capacity => capacity;

        /// <summary>验证武器容量。</summary>
        /// <exception cref="InvalidOperationException">容量无效时抛出。</exception>
        public void Validate()
        {
            if (capacity <= 0) throw new InvalidOperationException("WeaponInventorySettings 的武器容量必须大于零。");
        }
    }
}
