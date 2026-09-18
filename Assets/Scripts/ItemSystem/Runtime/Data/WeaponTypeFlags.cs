using System;
using UnityEngine;

namespace RPG.ItemSystem
{
    /// <summary>角色配置可使用的武器类型位掩码。</summary>
    [Flags]
    public enum WeaponTypeFlags
    {
        /// <summary>不允许装备任何武器。</summary>
        None = 0,
        /// <summary>允许装备单手剑。</summary>
        [InspectorName("单手剑")] Sword = 1 << (int)WeaponType.Sword,
        /// <summary>允许装备大剑。</summary>
        [InspectorName("大剑")] Greatsword = 1 << (int)WeaponType.Greatsword,
        /// <summary>允许装备长柄武器。</summary>
        [InspectorName("长柄武器")] Polearm = 1 << (int)WeaponType.Polearm,
        /// <summary>允许装备匕首。</summary>
        [InspectorName("匕首")] Dagger = 1 << (int)WeaponType.Dagger,
        /// <summary>允许装备盾牌。</summary>
        [InspectorName("盾牌")] Shield = 1 << (int)WeaponType.Shield,
        /// <summary>允许装备战锤。</summary>
        [InspectorName("战锤")] Warhammer = 1 << (int)WeaponType.Warhammer,
        /// <summary>允许装备当前项目已定义的全部武器类型。</summary>
        All = Sword | Greatsword | Polearm | Dagger | Shield | Warhammer
    }

    /// <summary>提供基础武器类型与角色武器位掩码之间的显式转换。</summary>
    public static class WeaponTypeFlagsExtensions
    {
        /// <summary>把一个基础武器类型转换为对应的单比特掩码。</summary>
        /// <param name="weaponType">待转换的武器类型。</param>
        /// <returns>对应的单比特武器类型掩码。</returns>
        /// <exception cref="ArgumentOutOfRangeException">武器类型不是已定义枚举值时抛出。</exception>
        public static WeaponTypeFlags ToFlag(this WeaponType weaponType)
        {
            if (!Enum.IsDefined(typeof(WeaponType), weaponType))
                throw new ArgumentOutOfRangeException(nameof(weaponType), weaponType, "武器类型无效。");
            return (WeaponTypeFlags)(1 << (int)weaponType);
        }

        /// <summary>判断位掩码是否允许指定基础武器类型。</summary>
        /// <param name="allowedWeaponTypes">角色允许的武器类型掩码。</param>
        /// <param name="weaponType">待检查的基础武器类型。</param>
        /// <returns>掩码包含该类型时返回 true。</returns>
        public static bool Includes(this WeaponTypeFlags allowedWeaponTypes, WeaponType weaponType) =>
            (allowedWeaponTypes & weaponType.ToFlag()) != WeaponTypeFlags.None;
    }
}
