using UnityEngine;

namespace RPG.ItemSystem
{
    /// <summary>当前武器资源使用的基础武器类型。</summary>
    public enum WeaponType
    {
        /// <summary>单手剑。</summary>
        [InspectorName("单手剑")] Sword = 0,
        /// <summary>大剑。</summary>
        [InspectorName("大剑")] Greatsword = 1,
        /// <summary>长柄武器。</summary>
        [InspectorName("长柄武器")] Polearm = 2,
        /// <summary>匕首。</summary>
        [InspectorName("匕首")] Dagger = 3,
        /// <summary>盾牌。</summary>
        [InspectorName("盾牌")] Shield = 4,
        /// <summary>战锤。</summary>
        [InspectorName("战锤")] Warhammer = 5
    }
}
