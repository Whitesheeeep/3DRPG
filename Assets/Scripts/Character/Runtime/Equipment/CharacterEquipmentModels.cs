using System;
using RPG.ItemSystem;

namespace RPG.Character
{
    /// <summary>角色可以保存的装备槽类型。</summary>
    public enum CharacterEquipmentSlotKind
    {
        /// <summary>角色武器槽。</summary>
        Weapon = 0,
        /// <summary>角色圣遗物槽。</summary>
        Artifact = 1
    }

    /// <summary>描述一件装备在角色实例中的唯一位置。</summary>
    public readonly struct CharacterEquipmentLocation : IEquatable<CharacterEquipmentLocation>
    {
        /// <summary>创建角色装备位置。</summary>
        /// <param name="characterId">装备者角色。</param>
        /// <param name="slotKind">槽类型。</param>
        /// <param name="artifactSlot">圣遗物槽；武器槽忽略该值。</param>
        public CharacterEquipmentLocation(
            CharacterId characterId,
            CharacterEquipmentSlotKind slotKind,
            ArtifactSlot artifactSlot = default(ArtifactSlot))
        {
            if (!characterId.IsValid)
                throw new ArgumentException("角色装备位置必须包含有效角色标识。", nameof(characterId));
            if (!Enum.IsDefined(typeof(CharacterEquipmentSlotKind), slotKind))
                throw new ArgumentOutOfRangeException(nameof(slotKind));
            if (slotKind == CharacterEquipmentSlotKind.Artifact &&
                !Enum.IsDefined(typeof(ArtifactSlot), artifactSlot))
                throw new ArgumentOutOfRangeException(nameof(artifactSlot));

            CharacterId = characterId;
            SlotKind = slotKind;
            ArtifactSlot = artifactSlot;
        }

        /// <summary>获取装备者角色。</summary>
        public CharacterId CharacterId { get; }

        /// <summary>获取槽类型。</summary>
        public CharacterEquipmentSlotKind SlotKind { get; }

        /// <summary>获取圣遗物槽。</summary>
        public ArtifactSlot ArtifactSlot { get; }

        /// <summary>判断两个位置是否相同。</summary>
        public bool Equals(CharacterEquipmentLocation other) =>
            CharacterId == other.CharacterId &&
            SlotKind == other.SlotKind &&
            (SlotKind != CharacterEquipmentSlotKind.Artifact || ArtifactSlot == other.ArtifactSlot);

        /// <summary>判断对象是否为相同位置。</summary>
        public override bool Equals(object obj) => obj is CharacterEquipmentLocation other && Equals(other);

        /// <summary>获取位置哈希。</summary>
        public override int GetHashCode() =>
            ((CharacterId.GetHashCode() * 397) + (int)SlotKind) * 397 +
            (SlotKind == CharacterEquipmentSlotKind.Artifact ? (int)ArtifactSlot : 0);
    }

    /// <summary>不可变保存角色武器和五个圣遗物槽位的装备关系。</summary>
    public sealed class CharacterEquipmentState
    {
        /// <summary>创建空装备状态。</summary>
        public CharacterEquipmentState()
            : this(default(EquipmentInstanceId), default(EquipmentInstanceId), default(EquipmentInstanceId),
                default(EquipmentInstanceId), default(EquipmentInstanceId), default(EquipmentInstanceId))
        {
        }

        /// <summary>创建完整装备状态。</summary>
        /// <param name="equippedWeaponInstanceId">武器实例。</param>
        /// <param name="flowerOfLifeInstanceId">生之花实例。</param>
        /// <param name="plumeOfDeathInstanceId">死之羽实例。</param>
        /// <param name="sandsOfEonInstanceId">时之沙实例。</param>
        /// <param name="gobletOfEonothemInstanceId">空之杯实例。</param>
        /// <param name="circletOfLogosInstanceId">理之冠实例。</param>
        public CharacterEquipmentState(
            EquipmentInstanceId equippedWeaponInstanceId,
            EquipmentInstanceId flowerOfLifeInstanceId,
            EquipmentInstanceId plumeOfDeathInstanceId,
            EquipmentInstanceId sandsOfEonInstanceId,
            EquipmentInstanceId gobletOfEonothemInstanceId,
            EquipmentInstanceId circletOfLogosInstanceId)
        {
            EquippedWeaponInstanceId = equippedWeaponInstanceId;
            FlowerOfLifeInstanceId = flowerOfLifeInstanceId;
            PlumeOfDeathInstanceId = plumeOfDeathInstanceId;
            SandsOfEonInstanceId = sandsOfEonInstanceId;
            GobletOfEonothemInstanceId = gobletOfEonothemInstanceId;
            CircletOfLogosInstanceId = circletOfLogosInstanceId;
            ValidateNoDuplicateInstances();
        }

        /// <summary>获取已装备武器实例。</summary>
        public EquipmentInstanceId EquippedWeaponInstanceId { get; }
        /// <summary>获取生之花实例。</summary>
        public EquipmentInstanceId FlowerOfLifeInstanceId { get; }
        /// <summary>获取死之羽实例。</summary>
        public EquipmentInstanceId PlumeOfDeathInstanceId { get; }
        /// <summary>获取时之沙实例。</summary>
        public EquipmentInstanceId SandsOfEonInstanceId { get; }
        /// <summary>获取空之杯实例。</summary>
        public EquipmentInstanceId GobletOfEonothemInstanceId { get; }
        /// <summary>获取理之冠实例。</summary>
        public EquipmentInstanceId CircletOfLogosInstanceId { get; }

        /// <summary>按圣遗物部位读取实例。</summary>
        /// <param name="slot">圣遗物部位。</param>
        /// <param name="instanceId">找到的实例。</param>
        /// <returns>槽位有有效实例时返回 true。</returns>
        public bool TryGetArtifactInstanceId(ArtifactSlot slot, out EquipmentInstanceId instanceId)
        {
            switch (slot)
            {
                case ArtifactSlot.FlowerOfLife: instanceId = FlowerOfLifeInstanceId; break;
                case ArtifactSlot.PlumeOfDeath: instanceId = PlumeOfDeathInstanceId; break;
                case ArtifactSlot.SandsOfEon: instanceId = SandsOfEonInstanceId; break;
                case ArtifactSlot.GobletOfEonothem: instanceId = GobletOfEonothemInstanceId; break;
                case ArtifactSlot.CircletOfLogos: instanceId = CircletOfLogosInstanceId; break;
                default: throw new ArgumentOutOfRangeException(nameof(slot));
            }
            return instanceId.IsValid;
        }

        /// <summary>按部位创建替换后的不可变状态。</summary>
        /// <param name="slot">待替换的部位。</param>
        /// <param name="instanceId">新的实例；无效值表示卸下。</param>
        /// <returns>新状态。</returns>
        public CharacterEquipmentState WithArtifact(ArtifactSlot slot, EquipmentInstanceId instanceId)
        {
            switch (slot)
            {
                case ArtifactSlot.FlowerOfLife:
                    return new CharacterEquipmentState(EquippedWeaponInstanceId, instanceId, PlumeOfDeathInstanceId,
                        SandsOfEonInstanceId, GobletOfEonothemInstanceId, CircletOfLogosInstanceId);
                case ArtifactSlot.PlumeOfDeath:
                    return new CharacterEquipmentState(EquippedWeaponInstanceId, FlowerOfLifeInstanceId, instanceId,
                        SandsOfEonInstanceId, GobletOfEonothemInstanceId, CircletOfLogosInstanceId);
                case ArtifactSlot.SandsOfEon:
                    return new CharacterEquipmentState(EquippedWeaponInstanceId, FlowerOfLifeInstanceId, PlumeOfDeathInstanceId,
                        instanceId, GobletOfEonothemInstanceId, CircletOfLogosInstanceId);
                case ArtifactSlot.GobletOfEonothem:
                    return new CharacterEquipmentState(EquippedWeaponInstanceId, FlowerOfLifeInstanceId, PlumeOfDeathInstanceId,
                        SandsOfEonInstanceId, instanceId, CircletOfLogosInstanceId);
                case ArtifactSlot.CircletOfLogos:
                    return new CharacterEquipmentState(EquippedWeaponInstanceId, FlowerOfLifeInstanceId, PlumeOfDeathInstanceId,
                        SandsOfEonInstanceId, GobletOfEonothemInstanceId, instanceId);
                default: throw new ArgumentOutOfRangeException(nameof(slot));
            }
        }

        /// <summary>创建替换武器后的不可变状态。</summary>
        /// <param name="instanceId">新的武器实例；无效值表示卸下。</param>
        /// <returns>新状态。</returns>
        public CharacterEquipmentState WithWeapon(EquipmentInstanceId instanceId) =>
            new CharacterEquipmentState(instanceId, FlowerOfLifeInstanceId, PlumeOfDeathInstanceId,
                SandsOfEonInstanceId, GobletOfEonothemInstanceId, CircletOfLogosInstanceId);

        /// <summary>检查同一装备实例没有被多个槽位引用。</summary>
        private void ValidateNoDuplicateInstances()
        {
            var usedInstanceIds = new System.Collections.Generic.HashSet<EquipmentInstanceId>();
            AddIfValid(EquippedWeaponInstanceId, usedInstanceIds);
            AddIfValid(FlowerOfLifeInstanceId, usedInstanceIds);
            AddIfValid(PlumeOfDeathInstanceId, usedInstanceIds);
            AddIfValid(SandsOfEonInstanceId, usedInstanceIds);
            AddIfValid(GobletOfEonothemInstanceId, usedInstanceIds);
            AddIfValid(CircletOfLogosInstanceId, usedInstanceIds);
        }

        /// <summary>将有效装备实例加入重复引用校验集合。</summary>
        private static void AddIfValid(EquipmentInstanceId instanceId, System.Collections.Generic.HashSet<EquipmentInstanceId> usedInstanceIds)
        {
            if (instanceId.IsValid && !usedInstanceIds.Add(instanceId))
                throw new InvalidOperationException($"角色装备状态重复引用实例：{instanceId}。");
        }
    }
}
