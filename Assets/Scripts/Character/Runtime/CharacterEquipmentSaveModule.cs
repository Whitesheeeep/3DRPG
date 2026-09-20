using System;
using System.Collections.Generic;
using RPG.ItemSystem;
using RPG.SaveSystem;
using WS_Modules.CustomEventSystem;
using WSEventSystem = WS_Modules.CustomEventSystem.EventSystem;

namespace RPG.Character
{
    /// <summary>角色与武器、圣遗物槽位关系的版本化存档快照。</summary>
    [Serializable]
    public sealed class CharacterEquipmentSaveSnapshot : ISaveModuleSnapshot
    {
        /// <summary>创建空装备关系快照。</summary>
        public CharacterEquipmentSaveSnapshot() => Characters = new List<CharacterEquipmentSaveEntry>();

        /// <summary>角色装备关系条目。</summary>
        public List<CharacterEquipmentSaveEntry> Characters { get; set; }

        /// <summary>校验快照基本结构。</summary>
        public void ValidateShape()
        {
            if (Characters == null) throw new InvalidOperationException("角色装备关系快照不能为 null。");
            var characterIdSet = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < Characters.Count; index++)
            {
                CharacterEquipmentSaveEntry entry = Characters[index];
                if (entry == null || string.IsNullOrWhiteSpace(entry.CharacterId) || !characterIdSet.Add(entry.CharacterId))
                    throw new InvalidOperationException("角色装备关系快照包含空项或重复角色。");
            }
        }
    }

    /// <summary>一名角色六个装备槽位的存档条目。</summary>
    [Serializable]
    public sealed class CharacterEquipmentSaveEntry
    {
        /// <summary>角色标识。</summary>
        public string CharacterId { get; set; } = string.Empty;
        /// <summary>武器实例标识。</summary>
        public string WeaponInstanceId { get; set; } = string.Empty;
        /// <summary>生之花实例标识。</summary>
        public string FlowerOfLifeInstanceId { get; set; } = string.Empty;
        /// <summary>死之羽实例标识。</summary>
        public string PlumeOfDeathInstanceId { get; set; } = string.Empty;
        /// <summary>时之沙实例标识。</summary>
        public string SandsOfEonInstanceId { get; set; } = string.Empty;
        /// <summary>空之杯实例标识。</summary>
        public string GobletOfEonothemInstanceId { get; set; } = string.Empty;
        /// <summary>理之冠实例标识。</summary>
        public string CircletOfLogosInstanceId { get; set; } = string.Empty;
    }

    /// <summary>角色装备关系恢复完成事件。</summary>
    public readonly struct CharacterEquipmentRestoredEvent
    {
    }

    /// <summary>将角色装备关系接入 SaveSystem。</summary>
    public sealed class CharacterEquipmentSaveModule : SaveModule<CharacterEquipmentSaveSnapshot>
    {
        #region 依赖字段

        private readonly CharacterRosterManager characterRosterManager;
        private readonly WeaponInventoryManager weaponInventoryManager;
        private readonly ArtifactInventoryManager artifactInventoryManager;
        private readonly CharacterEquipmentSystem characterEquipmentSystem;

        #endregion

        /// <summary>创建角色装备关系存档模块。</summary>
        /// <param name="characterRosterManager">角色实例 Manager。</param>
        /// <param name="weaponInventoryManager">武器实例 Manager。</param>
        /// <param name="artifactInventoryManager">圣遗物实例 Manager。</param>
        /// <param name="characterEquipmentSystem">装备事务 System。</param>
        public CharacterEquipmentSaveModule(CharacterRosterManager characterRosterManager,
            WeaponInventoryManager weaponInventoryManager, ArtifactInventoryManager artifactInventoryManager,
            CharacterEquipmentSystem characterEquipmentSystem)
            : base(new SaveModuleId("character-equipment"), 1, SaveMissingModulePolicy.Required,
                new[] { CharacterRosterSaveModule.StableModuleId, new SaveModuleId("weapon-inventory"), new SaveModuleId("artifact-inventory") })
        {
            this.characterRosterManager = characterRosterManager ?? throw new ArgumentNullException(nameof(characterRosterManager));
            this.weaponInventoryManager = weaponInventoryManager ?? throw new ArgumentNullException(nameof(weaponInventoryManager));
            this.artifactInventoryManager = artifactInventoryManager ?? throw new ArgumentNullException(nameof(artifactInventoryManager));
            this.characterEquipmentSystem = characterEquipmentSystem ?? throw new ArgumentNullException(nameof(characterEquipmentSystem));
        }

        /// <summary>采集所有角色的装备关系。</summary>
        /// <returns>装备关系快照。</returns>
        protected override CharacterEquipmentSaveSnapshot CaptureTypedSnapshot()
        {
            var snapshot = new CharacterEquipmentSaveSnapshot();
            IReadOnlyList<CharacterInstance> instances = characterRosterManager.GetInstances();
            for (int index = 0; index < instances.Count; index++)
            {
                CharacterInstance instance = instances[index];
                snapshot.Characters.Add(new CharacterEquipmentSaveEntry
                {
                    CharacterId = instance.CharacterId.ToString(),
                    WeaponInstanceId = ToValue(instance.EquippedWeaponInstanceId),
                    FlowerOfLifeInstanceId = ToValue(instance.Equipment.FlowerOfLifeInstanceId),
                    PlumeOfDeathInstanceId = ToValue(instance.Equipment.PlumeOfDeathInstanceId),
                    SandsOfEonInstanceId = ToValue(instance.Equipment.SandsOfEonInstanceId),
                    GobletOfEonothemInstanceId = ToValue(instance.Equipment.GobletOfEonothemInstanceId),
                    CircletOfLogosInstanceId = ToValue(instance.Equipment.CircletOfLogosInstanceId)
                });
            }
            return snapshot;
        }

        /// <summary>校验角色、装备实例、部位和重复引用。</summary>
        /// <param name="snapshot">待验证快照。</param>
        protected override void ValidateTypedSnapshot(CharacterEquipmentSaveSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            snapshot.ValidateShape();
            var referencedInstanceIds = new HashSet<EquipmentInstanceId>();
            int equippedWeaponCount = 0;
            for (int index = 0; index < snapshot.Characters.Count; index++)
            {
                CharacterEquipmentSaveEntry entry = snapshot.Characters[index];
                CharacterId characterId = new CharacterId(entry.CharacterId);
                if (!characterId.IsValid || !characterRosterManager.IsOwned(characterId))
                    throw new InvalidOperationException($"角色装备快照引用了未拥有角色：{entry.CharacterId}。");

                ValidateWeapon(entry.WeaponInstanceId, characterId, referencedInstanceIds, ref equippedWeaponCount);
                ValidateArtifact(entry.FlowerOfLifeInstanceId, ArtifactSlot.FlowerOfLife, referencedInstanceIds);
                ValidateArtifact(entry.PlumeOfDeathInstanceId, ArtifactSlot.PlumeOfDeath, referencedInstanceIds);
                ValidateArtifact(entry.SandsOfEonInstanceId, ArtifactSlot.SandsOfEon, referencedInstanceIds);
                ValidateArtifact(entry.GobletOfEonothemInstanceId, ArtifactSlot.GobletOfEonothem, referencedInstanceIds);
                ValidateArtifact(entry.CircletOfLogosInstanceId, ArtifactSlot.CircletOfLogos, referencedInstanceIds);
            }

            int unEquippedWeaponCount = weaponInventoryManager.Count - equippedWeaponCount;
            if (unEquippedWeaponCount > weaponInventoryManager.Capacity)
                throw new InvalidOperationException($"角色装备快照恢复后武器容纳区超过容量：{unEquippedWeaponCount}/{weaponInventoryManager.Capacity}。");
        }

        /// <summary>整体恢复角色装备关系。</summary>
        /// <param name="snapshot">已经完成验证的快照。</param>
        protected override void RestoreTypedSnapshot(CharacterEquipmentSaveSnapshot snapshot)
        {
            var equipmentByCharacterIdMap = new Dictionary<CharacterId, CharacterEquipmentState>();
            for (int index = 0; index < snapshot.Characters.Count; index++)
            {
                CharacterEquipmentSaveEntry entry = snapshot.Characters[index];
                CharacterId characterId = new CharacterId(entry.CharacterId);
                equipmentByCharacterIdMap.Add(characterId, new CharacterEquipmentState(
                    ParseOptionalId(entry.WeaponInstanceId),
                    ParseOptionalId(entry.FlowerOfLifeInstanceId),
                    ParseOptionalId(entry.PlumeOfDeathInstanceId),
                    ParseOptionalId(entry.SandsOfEonInstanceId),
                    ParseOptionalId(entry.GobletOfEonothemInstanceId),
                    ParseOptionalId(entry.CircletOfLogosInstanceId)));
            }
            characterEquipmentSystem.RestoreEquipmentState(equipmentByCharacterIdMap);
        }

        /// <summary>为缺少模块的新档创建所有角色空装备关系。</summary>
        protected override CharacterEquipmentSaveSnapshot CreateDefaultTypedSnapshot() => new CharacterEquipmentSaveSnapshot();

        #region 校验辅助

        /// <summary>校验武器引用、角色类型规则和重复引用。</summary>
        private void ValidateWeapon(string value, CharacterId characterId, HashSet<EquipmentInstanceId> referencedInstanceIds,
            ref int equippedWeaponCount)
        {
            if (string.IsNullOrEmpty(value)) return;
            EquipmentInstanceId instanceId = ParseRequiredId(value);
            if (!weaponInventoryManager.TryGetInstance(instanceId, out WeaponInstance weapon))
                throw new InvalidOperationException($"角色 {characterId} 装备关系引用了不存在的武器：{value}。");
            if (!ItemManager.Instance.TryGetDefinition(weapon.DefinitionId, out ItemDefinition definition) ||
                !(definition is WeaponDefinition weaponDefinition) ||
                !CharacterConfigManager.Instance.GetRequiredConfig(characterId).AllowsWeaponType(weaponDefinition.WeaponType))
                throw new InvalidOperationException($"角色 {characterId} 的武器装备规则不匹配：{value}。");
            if (!referencedInstanceIds.Add(instanceId)) throw new InvalidOperationException($"装备实例重复引用：{value}。");
            equippedWeaponCount++;
        }

        /// <summary>校验圣遗物实例与保存槽位部位一致。</summary>
        private void ValidateArtifact(string value, ArtifactSlot slot, HashSet<EquipmentInstanceId> referencedInstanceIds)
        {
            if (string.IsNullOrEmpty(value)) return;
            EquipmentInstanceId instanceId = ParseRequiredId(value);
            if (!artifactInventoryManager.TryGetInstance(instanceId, out ArtifactInstance artifact))
                throw new InvalidOperationException($"角色装备关系引用了不存在的圣遗物：{value}。");
            if (!ItemManager.Instance.TryGetDefinition(artifact.DefinitionId, out ItemDefinition definition) ||
                !(definition is ArtifactDefinition artifactDefinition) || artifactDefinition.Slot != slot)
                throw new InvalidOperationException($"圣遗物实例 {value} 与保存部位 {slot} 不匹配。");
            if (!referencedInstanceIds.Add(instanceId)) throw new InvalidOperationException($"装备实例重复引用：{value}。");
        }

        /// <summary>解析允许为空的实例标识。</summary>
        private static EquipmentInstanceId ParseOptionalId(string value) => string.IsNullOrEmpty(value)
            ? default(EquipmentInstanceId)
            : ParseRequiredId(value);

        /// <summary>解析必需的实例标识。</summary>
        private static EquipmentInstanceId ParseRequiredId(string value) => new EquipmentInstanceId(value);

        /// <summary>将实例标识转换为空槽位或稳定字符串。</summary>
        private static string ToValue(EquipmentInstanceId instanceId) => instanceId.IsValid ? instanceId.Value : string.Empty;

        #endregion
    }
}
