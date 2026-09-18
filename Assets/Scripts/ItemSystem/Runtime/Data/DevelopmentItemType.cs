using System;
using UnityEngine;

namespace RPG.ItemSystem
{
    /// <summary>
    /// 不提供经验的养成道具用途；同一种道具可以通过 Flags 支持多个成本用途。
    /// </summary>
    [Flags]
    public enum DevelopmentItemType
    {
        /// <summary>未配置用途。</summary>
        None = 0,
        /// <summary>角色突破所需道具。</summary>
        [InspectorName("角色突破")] CharacterAscension = 1 << 0,
        /// <summary>角色天赋所需道具。</summary>
        [InspectorName("角色天赋")] CharacterTalent = 1 << 1,
        /// <summary>武器突破所需道具。</summary>
        [InspectorName("武器突破")] WeaponAscension = 1 << 2,
        /// <summary>武器精炼所需道具。</summary>
        [InspectorName("武器精炼")] WeaponRefinement = 1 << 3
    }
}
