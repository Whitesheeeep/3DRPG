using System;
using System.Collections.Generic;
using RPG.SaveSystem;

namespace RPG.ItemSystem
{
    /// <summary>武器实例自身状态的版本化存档快照，不保存角色装备关系。</summary>
    [Serializable]
    public sealed class WeaponInventorySaveSnapshot : ISaveModuleSnapshot
    {
        /// <summary>创建空快照。</summary>
        public WeaponInventorySaveSnapshot()
        {
            Instances = new List<WeaponInventorySaveEntry>();
            NewDefinitionIds = new List<string>();
        }

        /// <summary>武器实例数据。</summary>
        public List<WeaponInventorySaveEntry> Instances { get; set; }
        /// <summary>下一个获得顺序。</summary>
        public long NextAcquisitionSequence { get; set; } = 1;
        /// <summary>当前显示 New 的武器 Definition。</summary>
        public List<string> NewDefinitionIds { get; set; }

        /// <summary>验证快照结构。</summary>
        public void ValidateShape()
        {
            if (Instances == null || NewDefinitionIds == null || NextAcquisitionSequence <= 0)
                throw new InvalidOperationException("武器背包快照结构无效。");
            var instanceIdSet = new HashSet<string>(StringComparer.Ordinal);
            var definitionIdSet = new HashSet<string>(StringComparer.Ordinal);
            long maxSequence = 0;
            for (int index = 0; index < Instances.Count; index++)
            {
                WeaponInventorySaveEntry entry = Instances[index];
                if (entry == null || string.IsNullOrWhiteSpace(entry.InstanceId) ||
                    string.IsNullOrWhiteSpace(entry.DefinitionId) || entry.Level < 1 ||
                    entry.CurrentExperience < 0 || entry.AscensionRank < 0 || entry.RefinementRank < 1 ||
                    entry.AcquisitionSequence <= 0 || !instanceIdSet.Add(entry.InstanceId))
                    throw new InvalidOperationException("武器背包快照包含非法或重复实例。");
                definitionIdSet.Add(entry.DefinitionId);
                maxSequence = Math.Max(maxSequence, entry.AcquisitionSequence);
            }

            if (NextAcquisitionSequence <= maxSequence)
                throw new InvalidOperationException("武器背包快照的下一个获得顺序必须大于现有实例顺序。");
            var newDefinitionIdSet = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < NewDefinitionIds.Count; index++)
            {
                string definitionId = NewDefinitionIds[index];
                if (!ItemId.TryCreate(definitionId, out _) || !newDefinitionIdSet.Add(definitionId) ||
                    !definitionIdSet.Contains(definitionId))
                    throw new InvalidOperationException("武器背包快照的 Definition New 集合无效。");
            }
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
        /// <summary>获得顺序。</summary>
        public long AcquisitionSequence { get; set; }
    }

    /// <summary>将武器实例状态接入 SaveSystem。</summary>
    public sealed class WeaponInventorySaveModule : SaveModule<WeaponInventorySaveSnapshot>
    {
        #region 依赖字段

        private readonly WeaponInventoryManager manager;

        #endregion

        /// <summary>创建武器实例存档模块。</summary>
        /// <param name="manager">武器 Manager。</param>
        public WeaponInventorySaveModule(WeaponInventoryManager manager)
            : base(new SaveModuleId("weapon-inventory"), 1, SaveMissingModulePolicy.Required,
                new[] { ItemDiscoverySaveModule.StableModuleId })
        {
            this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        /// <summary>采集武器实例状态。</summary>
        /// <returns>武器实例快照。</returns>
        protected override WeaponInventorySaveSnapshot CaptureTypedSnapshot()
        {
            var snapshot = new WeaponInventorySaveSnapshot();
            IReadOnlyList<ItemId> newDefinitionIds = manager.GetNewDefinitionIds();
            for (int index = 0; index < newDefinitionIds.Count; index++)
                snapshot.NewDefinitionIds.Add(newDefinitionIds[index].Value);

            IReadOnlyList<WeaponInstance> instances = manager.GetInstances();
            for (int index = 0; index < instances.Count; index++)
            {
                WeaponInstance instance = instances[index];
                snapshot.Instances.Add(new WeaponInventorySaveEntry
                {
                    InstanceId = instance.InstanceId.Value,
                    DefinitionId = instance.DefinitionId.Value,
                    Level = instance.Level,
                    CurrentExperience = instance.CurrentExperience,
                    AscensionRank = instance.AscensionRank,
                    RefinementRank = instance.RefinementRank,
                    IsLocked = instance.IsLocked,
                    AcquisitionSequence = instance.AcquisitionSequence
                });
            }

            snapshot.NextAcquisitionSequence = manager.NextAcquisitionSequence;
            return snapshot;
        }

        /// <summary>验证武器定义和成长状态。</summary>
        /// <param name="snapshot">待验证快照。</param>
        protected override void ValidateTypedSnapshot(WeaponInventorySaveSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            snapshot.ValidateShape();
            for (int index = 0; index < snapshot.Instances.Count; index++)
            {
                WeaponInventorySaveEntry entry = snapshot.Instances[index];
                if (!ItemId.TryCreate(entry.DefinitionId, out ItemId definitionId) ||
                    !ItemManager.Instance.TryGetDefinition(definitionId, out ItemDefinition definition) ||
                    !(definition is WeaponDefinition weapon))
                    throw new InvalidOperationException($"武器快照引用了无效定义：{entry.DefinitionId}。");
                if (entry.Level > weapon.MaxLevel || entry.AscensionRank > weapon.MaxAscensionRank ||
                    entry.RefinementRank > weapon.MaxRefinementRank)
                    throw new InvalidOperationException($"武器实例 {entry.InstanceId} 的成长状态超出定义上限。");
            }
        }

        /// <summary>恢复已验证的武器实例，装备关系由独立模块随后恢复。</summary>
        /// <param name="snapshot">已经完成验证的快照。</param>
        protected override void RestoreTypedSnapshot(WeaponInventorySaveSnapshot snapshot)
        {
            var instances = new List<WeaponInstance>(snapshot.Instances.Count);
            for (int index = 0; index < snapshot.Instances.Count; index++)
            {
                WeaponInventorySaveEntry entry = snapshot.Instances[index];
                instances.Add(new WeaponInstance(new EquipmentInstanceId(entry.InstanceId), new ItemId(entry.DefinitionId),
                    entry.Level, entry.CurrentExperience, entry.AscensionRank, entry.RefinementRank,
                    entry.IsLocked, entry.AcquisitionSequence));
            }

            var newDefinitionIds = new List<ItemId>(snapshot.NewDefinitionIds.Count);
            for (int index = 0; index < snapshot.NewDefinitionIds.Count; index++)
                newDefinitionIds.Add(new ItemId(snapshot.NewDefinitionIds[index]));

            manager.RestoreState(instances, newDefinitionIds, snapshot.NextAcquisitionSequence);
            manager.PublishRestored();
        }
    }
}
