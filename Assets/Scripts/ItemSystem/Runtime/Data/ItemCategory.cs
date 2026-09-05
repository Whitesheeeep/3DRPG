using UnityEngine;

namespace RPG.ItemSystem
{
    /// <summary>背包展示和类型默认数据使用的物品分类。</summary>
    public enum ItemCategory
    {
        /// <summary>养成素材。</summary>
        [InspectorName("养成素材")] Material = 0,
        /// <summary>食材。</summary>
        [InspectorName("食材")] Ingredient = 1,
        /// <summary>料理。</summary>
        [InspectorName("料理")] Food = 2,
        /// <summary>摆设。</summary>
        [InspectorName("摆设")] Furnishing = 3,
        /// <summary>武器。</summary>
        [InspectorName("武器")] Weapon = 4,
        /// <summary>圣遗物。</summary>
        [InspectorName("圣遗物")] Artifact = 5,
        /// <summary>角色、武器或圣遗物养成所需的道具。</summary>
        [InspectorName("养成道具")] DevelopmentItem = 6
    }
}
