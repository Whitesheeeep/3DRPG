using System;
using System.Collections.Generic;
using RPG.ItemSystem;
using RPG.SaveSystem;
using UnityEngine;
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
        #region 模块标识

        /// <summary>角色装备关系存档模块的稳定 ID。</summary>
        public static readonly SaveModuleId StableModuleId = new SaveModuleId("character-equipment");

        #endregion

        #region 依赖字段

        private readonly CharacterRosterManager characterRosterManager;
        private readonly CharacterEquipmentSystem characterEquipmentSystem;

        #endregion

        /// <summary>创建角色装备关系存档模块。</summary>
        /// <param name="characterRosterManager">角色实例 Manager。</param>
        /// <param name="characterEquipmentSystem">装备事务 System。</param>
        /// <exception cref="ArgumentNullException">任一业务依赖为空时抛出。</exception>
        public CharacterEquipmentSaveModule(CharacterRosterManager characterRosterManager, CharacterEquipmentSystem characterEquipmentSystem)
            : base(StableModuleId, 1, SaveMissingModulePolicy.Required,
                new[] { CharacterRosterSaveModule.StableModuleId, WeaponInventorySaveModule.StableModuleId, ArtifactInventorySaveModule.StableModuleId })
        {
            this.characterRosterManager = characterRosterManager ?? throw new ArgumentNullException(nameof(characterRosterManager));
            this.characterEquipmentSystem = characterEquipmentSystem ?? throw new ArgumentNullException(nameof(characterEquipmentSystem));
        }

        /// <summary>采集角色当前关联的武器与圣遗物槽位。</summary>
        /// <returns>全部角色的装备关系快照。</returns>
        protected override CharacterEquipmentSaveSnapshot CaptureTypedSnapshot()
        {
            var snapshot = new CharacterEquipmentSaveSnapshot();
            IReadOnlyList<CharacterInstance> characterInstances = characterRosterManager.GetInstances();
            // 角色实例是装备归属的唯一权威；库存只负责提供被引用的实例。
            for (int index = 0; index < characterInstances.Count; index++)
            {
                CharacterInstance instance = characterInstances[index];
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

        /// <summary>把已校验快照转为角色槽位状态并整体恢复。</summary>
        /// <param name="snapshot">已校验的当前版本快照。</param>
        protected override void RestoreTypedSnapshot(CharacterEquipmentSaveSnapshot snapshot)
        {
            // 先完成所有 DTO 到业务值对象的转换，再交给装备事务系统一次性提交。
            // key：CharacterId；value：该角色全部装备槽位状态。
            var equipmentByCharacterIdMap = new Dictionary<CharacterId, CharacterEquipmentState>();
            for (int index = 0; index < snapshot.Characters.Count; index++)
            {
                CharacterEquipmentSaveEntry entry = snapshot.Characters[index];
                CharacterId characterId = new CharacterId(entry.CharacterId);
                equipmentByCharacterIdMap.Add(characterId, new CharacterEquipmentState(
                    ParseOptionalId(entry.WeaponInstanceId), ParseOptionalId(entry.FlowerOfLifeInstanceId),
                    ParseOptionalId(entry.PlumeOfDeathInstanceId), ParseOptionalId(entry.SandsOfEonInstanceId),
                    ParseOptionalId(entry.GobletOfEonothemInstanceId), ParseOptionalId(entry.CircletOfLogosInstanceId)));
            }

            characterEquipmentSystem.RestoreEquipmentState(equipmentByCharacterIdMap);
            Debug.Log($"[CharacterEquipmentSaveModule] 已恢复角色装备关系，characterCount={equipmentByCharacterIdMap.Count}。");
        }

        /// <summary>仅校验快照结构；跨业务引用由装备运行时在依赖恢复后检查。</summary>
        /// <param name="snapshot">待验证快照。</param>
        protected override void ValidateTypedSnapshot(CharacterEquipmentSaveSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            snapshot.ValidateShape();
        }

        /// <summary>为缺少模块的新档创建所有角色空装备关系。</summary>
        protected override CharacterEquipmentSaveSnapshot CreateDefaultTypedSnapshot() => new CharacterEquipmentSaveSnapshot();

        #region 校验辅助

        /// <summary>解析允许为空的装备实例标识。</summary>
        /// <param name="value">快照中的实例标识文本。</param>
        /// <returns>装备实例标识或默认空标识。</returns>
        private static EquipmentInstanceId ParseOptionalId(string value) => string.IsNullOrEmpty(value)
            ? default(EquipmentInstanceId)
            : new EquipmentInstanceId(value);

        /// <summary>将有效装备实例标识转换为稳定字符串，空槽转换为空文本。</summary>
        /// <param name="instanceId">装备实例标识。</param>
        /// <returns>稳定标识文本或空字符串。</returns>
        private static string ToValue(EquipmentInstanceId instanceId) =>
            instanceId.IsValid ? instanceId.Value : string.Empty;

        #endregion
    }
}
