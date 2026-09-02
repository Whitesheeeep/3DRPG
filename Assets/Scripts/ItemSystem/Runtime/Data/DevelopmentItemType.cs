using UnityEngine;

namespace RPG.ItemSystem
{
    /// <summary>
    /// 养成道具的具体用途；顶层背包页签仍由 ItemCategory.DevelopmentItem 决定。
    /// </summary>
    public enum DevelopmentItemType
    {
        /// <summary>角色经验素材。</summary>
        [InspectorName("角色经验素材")] CharacterExperience = 0,
        /// <summary>角色突破素材。</summary>
        [InspectorName("角色突破素材")] CharacterAscension = 1,
        /// <summary>角色天赋素材。</summary>
        [InspectorName("角色天赋素材")] CharacterTalent = 2,
        /// <summary>武器强化素材。</summary>
        [InspectorName("武器强化素材")] WeaponExperience = 3,
        /// <summary>武器突破素材。</summary>
        [InspectorName("武器突破素材")] WeaponAscension = 4,
        /// <summary>武器精炼素材。</summary>
        [InspectorName("武器精炼素材")] WeaponRefinement = 5,
        /// <summary>圣遗物强化素材。</summary>
        [InspectorName("圣遗物强化素材")] ArtifactExperience = 6
    }
}
