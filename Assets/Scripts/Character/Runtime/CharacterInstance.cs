using System;
using RPG.ItemSystem;

namespace RPG.Character
{
    /// <summary>
    /// 保存玩家已经拥有的一名角色的持久化成长状态。
    /// </summary>
    /// <remarks>
    /// 角色静态名称、稀有度、头像和成长曲线仍由 CharacterConfig 提供；
    /// 本实例只保存会随玩家进度变化的字段。当前阶段不负责把进度同步到场景 CharacterActor。
    /// </remarks>
    public sealed class CharacterInstance
    {
        #region 状态属性

        /// <summary>
        /// 创建角色实例状态。
        /// </summary>
        /// <param name="characterId">角色稳定标识。</param>
        /// <param name="level">当前等级。</param>
        /// <param name="currentExperience">当前等级内经验。</param>
        /// <param name="ascensionRank">当前突破阶数。</param>
        /// <param name="acquisitionSequence">获得顺序。</param>
        internal CharacterInstance(
            CharacterId characterId,
            int level,
            int currentExperience,
            int ascensionRank,
            long acquisitionSequence,
            CharacterEquipmentState equipment = null)
        {
            CharacterId = characterId;
            Level = level;
            CurrentExperience = currentExperience;
            AscensionRank = ascensionRank;
            AcquisitionSequence = acquisitionSequence;
            Equipment = equipment ?? new CharacterEquipmentState();
        }

        /// <summary>获取角色稳定标识。</summary>
        public CharacterId CharacterId { get; }

        /// <summary>获取当前等级。</summary>
        public int Level { get; }

        /// <summary>获取当前等级内经验。</summary>
        public int CurrentExperience { get; }

        /// <summary>获取当前突破阶数。</summary>
        public int AscensionRank { get; }

        /// <summary>获取角色首次获得时分配的稳定顺序。</summary>
        public long AcquisitionSequence { get; }

        /// <summary>获取角色当前完整装备状态。</summary>
        public CharacterEquipmentState Equipment { get; }

        /// <summary>获取已装备武器实例。</summary>
        public EquipmentInstanceId EquippedWeaponInstanceId => Equipment.EquippedWeaponInstanceId;

        /// <summary>按圣遗物部位读取角色已装备实例。</summary>
        /// <param name="slot">圣遗物部位。</param>
        /// <param name="instanceId">找到的圣遗物实例。</param>
        /// <returns>槽位有装备时返回 true。</returns>
        public bool TryGetEquippedArtifactInstanceId(ArtifactSlot slot, out EquipmentInstanceId instanceId) =>
            Equipment.TryGetArtifactInstanceId(slot, out instanceId);

        #endregion
    }
}
