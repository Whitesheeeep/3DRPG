using System;
using System.Collections.Generic;
using RPG.SaveSystem;
using RPG.Character;

namespace RPG.ItemSystem
{
    /// <summary>武器实例的版本化存档快照。</summary>
    [Serializable]
    public sealed class WeaponInventorySaveSnapshot : ISaveModuleSnapshot
    {
        /// <summary>创建空快照。</summary>
        public WeaponInventorySaveSnapshot() => Instances = new List<WeaponInventorySaveEntry>();

        /// <summary>武器实例数据。</summary>
        public List<WeaponInventorySaveEntry> Instances { get; set; }

        /// <summary>下一个获得顺序。</summary>
        public long NextAcquisitionSequence { get; set; } = 1;

        /// <summary>验证快照结构。</summary>
        public void ValidateShape()
        {
            if (Instances == null || NextAcquisitionSequence <= 0) throw new InvalidOperationException("武器背包快照结构无效。");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            long maxAcquisitionSequence = 0;
            for (int index = 0; index < Instances.Count; index++)
            {
                WeaponInventorySaveEntry entry = Instances[index];
                if (entry == null || string.IsNullOrWhiteSpace(entry.InstanceId) || string.IsNullOrWhiteSpace(entry.DefinitionId) ||
                    entry.Level < 1 || entry.CurrentExperience < 0 || entry.AscensionRank < 0 || entry.RefinementRank < 1 ||
                    entry.AcquisitionSequence <= 0 || !ids.Add(entry.InstanceId))
                    throw new InvalidOperationException("武器背包快照包含非法或重复实例。");
                if (!string.IsNullOrEmpty(entry.EquippedCharacterId) &&
                    !new CharacterId(entry.EquippedCharacterId).IsValid)
                    throw new InvalidOperationException($"武器背包快照实例 {entry.InstanceId} 的装备角色标识无效（第 {index} 项）。");
                if (entry.AcquisitionSequence > maxAcquisitionSequence) maxAcquisitionSequence = entry.AcquisitionSequence;
            }
            if (NextAcquisitionSequence <= maxAcquisitionSequence)
                throw new InvalidOperationException("武器背包快照的下一个获得顺序必须大于现有实例顺序。");
        }
    }

    /// <summary>武器实例快照数据。</summary>
    [Serializable]
    public sealed class WeaponInventorySaveEntry
    {
        /// <summary>实例标识。</summary>
        public string InstanceId { get; set; } = string.Empty;
        /// <summary>Definition 标识。</summary>
        public string DefinitionId { get; set; } = string.Empty;
        /// <summary>等级。</summary>
        public int Level { get; set; }
        /// <summary>当前经验。</summary>
        public int CurrentExperience { get; set; }
        /// <summary>突破阶数。</summary>
        public int AscensionRank { get; set; }
        /// <summary>精炼阶数。</summary>
        public int RefinementRank { get; set; }
        /// <summary>锁定状态。</summary>
        public bool IsLocked { get; set; }
        /// <summary>新获得状态。</summary>
        public bool IsNew { get; set; }
        /// <summary>获得顺序。</summary>
        public long AcquisitionSequence { get; set; }
        /// <summary>装备该武器的角色稳定标识；空字符串表示未装备。</summary>
        public string EquippedCharacterId { get; set; } = string.Empty;
    }

    /// <summary>将 WeaponInventoryManager 状态接入 SaveSystem。</summary>
    public sealed class WeaponInventorySaveModule : SaveModule<WeaponInventorySaveSnapshot>
    {
        private readonly WeaponInventoryManager manager;

        /// <summary>创建武器实例存档模块。</summary>
        /// <param name="manager">武器 Manager。</param>
        public WeaponInventorySaveModule(WeaponInventoryManager manager)
            : base(new SaveModuleId("weapon-inventory"), 1, SaveMissingModulePolicy.Required)
        {
            this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        /// <summary>采集武器实例状态。</summary>
        /// <returns>快照。</returns>
        protected override WeaponInventorySaveSnapshot CaptureTypedSnapshot()
        {
            var snapshot = new WeaponInventorySaveSnapshot();
            IReadOnlyList<WeaponInstance> instances = manager.GetInstances();
            for (int index = 0; index < instances.Count; index++)
            {
                WeaponInstance instance = instances[index];
                snapshot.Instances.Add(new WeaponInventorySaveEntry
                {
                    InstanceId = instance.InstanceId.Value, DefinitionId = instance.DefinitionId.Value, Level = instance.Level,
                    CurrentExperience = instance.CurrentExperience, AscensionRank = instance.AscensionRank, RefinementRank = instance.RefinementRank,
                    IsLocked = instance.IsLocked,
                    IsNew = instance.IsNew,
                    AcquisitionSequence = instance.AcquisitionSequence,
                    EquippedCharacterId = instance.EquippedCharacterId.ToString()
                });
            }
            // 直接保存 Manager 的真实计数器，删除实例后也不会根据剩余集合回退或复用序号。
            snapshot.NextAcquisitionSequence = manager.NextAcquisitionSequence;
            return snapshot;
        }

        /// <summary>验证武器实例快照。</summary>
        /// <param name="snapshot">快照。</param>
        protected override void ValidateTypedSnapshot(WeaponInventorySaveSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            snapshot.ValidateShape();
            if (snapshot.Instances.Count > manager.Capacity) throw new InvalidOperationException("武器背包快照超过容量。");
            for (int index = 0; index < snapshot.Instances.Count; index++)
            {
                WeaponInventorySaveEntry entry = snapshot.Instances[index];
                if (!ItemId.TryCreate(entry.DefinitionId, out ItemId definitionId) || !ItemManager.Instance.TryGetDefinition(definitionId, out ItemDefinition definition) || !(definition is WeaponDefinition weapon))
                    throw new InvalidOperationException($"武器快照引用了无效定义：{entry.DefinitionId}。");
                if (entry.Level > weapon.MaxLevel || entry.AscensionRank > weapon.MaxAscensionRank || entry.RefinementRank > weapon.MaxRefinementRank)
                    throw new InvalidOperationException($"武器实例 {entry.InstanceId} 的成长状态超出定义上限。");
            }
        }

        /// <summary>恢复已验证的武器实例。</summary>
        /// <param name="snapshot">快照。</param>
        protected override void RestoreTypedSnapshot(WeaponInventorySaveSnapshot snapshot)
        {
            var instances = new List<WeaponInstance>(snapshot.Instances.Count);
            for (int index = 0; index < snapshot.Instances.Count; index++)
            {
                WeaponInventorySaveEntry entry = snapshot.Instances[index];
                instances.Add(new WeaponInstance(new EquipmentInstanceId(entry.InstanceId), new ItemId(entry.DefinitionId), entry.Level,
                    entry.CurrentExperience, entry.AscensionRank, entry.RefinementRank, entry.IsLocked, entry.IsNew, entry.AcquisitionSequence,
                    new CharacterId(entry.EquippedCharacterId ?? string.Empty)));
            }
            manager.RestoreState(instances, snapshot.NextAcquisitionSequence);
            manager.PublishRestored();
        }
    }
}
