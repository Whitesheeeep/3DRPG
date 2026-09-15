using UnityEngine;

namespace RPG.ItemSystem
{
    /// <summary>背包展示和类型默认数据使用的互斥物品分类。</summary>
    public enum ItemCategory
    {
        /// <summary>武器。</summary>
        [InspectorName("武器")] Weapon = 0,
        /// <summary>圣遗物。</summary>
        [InspectorName("圣遗物")] Artifact = 1,
        /// <summary>养成经验道具。</summary>
        [InspectorName("养成经验道具")] DevelopmentExperienceItem = 2,
        /// <summary>食物；合并原食材与料理分类。</summary>
        [InspectorName("食物")] Food = 3,
        /// <summary>不提供经验、仅作为配方成本的养成道具。</summary>
        [InspectorName("养成道具")] DevelopmentItem = 4
    }
}
