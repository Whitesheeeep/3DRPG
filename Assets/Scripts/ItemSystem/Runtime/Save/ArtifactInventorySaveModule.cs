using System;
using System.Collections.Generic;
using RPG.SaveSystem;

namespace RPG.ItemSystem
{
    /// <summary>圣遗物实例的版本化存档快照。</summary>
    [Serializable]
    public sealed class ArtifactInventorySaveSnapshot : ISaveModuleSnapshot
    {
        /// <summary>创建空快照。</summary>
        public ArtifactInventorySaveSnapshot() => Instances = new List<ArtifactInventorySaveEntry>();

        /// <summary>圣遗物实例数据。</summary>
        public List<ArtifactInventorySaveEntry> Instances { get; set; }

        /// <summary>下一个获得顺序。</summary>
        public long NextAcquisitionSequence { get; set; } = 1;

        /// <summary>验证快照结构。</summary>
        public void ValidateShape()
        {
            if (Instances == null || NextAcquisitionSequence <= 0) throw new InvalidOperationException("圣遗物背包快照结构无效。");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < Instances.Count; index++)
            {
                ArtifactInventorySaveEntry entry = Instances[index];
                if (entry == null || string.IsNullOrWhiteSpace(entry.InstanceId) || string.IsNullOrWhiteSpace(entry.DefinitionId) ||
                    entry.Level < 0 || entry.CurrentExperience < 0 || entry.AcquisitionSequence <= 0 || !ids.Add(entry.InstanceId))
                    throw new InvalidOperationException("圣遗物背包快照包含非法或重复实例。");
            }
        }
    }

    /// <summary>圣遗物实例快照数据。</summary>
    [Serializable]
    public sealed class ArtifactInventorySaveEntry
    {
        /// <summary>实例标识。</summary>
        public string InstanceId { get; set; } = string.Empty;
        /// <summary>Definition 标识。</summary>
        public string DefinitionId { get; set; } = string.Empty;
        /// <summary>等级。</summary>
        public int Level { get; set; }
        /// <summary>当前经验。</summary>
        public int CurrentExperience { get; set; }
        /// <summary>锁定状态。</summary>
        public bool IsLocked { get; set; }
        /// <summary>新获得状态。</summary>
        public bool IsNew { get; set; }
        /// <summary>获得顺序。</summary>
        public long AcquisitionSequence { get; set; }
    }

    /// <summary>将 ArtifactInventoryManager 状态接入 SaveSystem。</summary>
    public sealed class ArtifactInventorySaveModule : SaveModule<ArtifactInventorySaveSnapshot>
    {
        private readonly ArtifactInventoryManager manager;

        /// <summary>创建圣遗物实例存档模块。</summary>
        /// <param name="manager">圣遗物 Manager。</param>
        public ArtifactInventorySaveModule(ArtifactInventoryManager manager)
            : base(new SaveModuleId("artifact-inventory"), 1, SaveMissingModulePolicy.Required)
        {
            this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        /// <summary>采集圣遗物实例状态。</summary>
        /// <returns>快照。</returns>
        protected override ArtifactInventorySaveSnapshot CaptureTypedSnapshot()
        {
            var snapshot = new ArtifactInventorySaveSnapshot();
            IReadOnlyList<ArtifactInstance> instances = manager.GetInstances();
            long nextSequence = 1;
            for (int index = 0; index < instances.Count; index++)
            {
                ArtifactInstance instance = instances[index];
                snapshot.Instances.Add(new ArtifactInventorySaveEntry
                {
                    InstanceId = instance.InstanceId.Value, DefinitionId = instance.DefinitionId.Value, Level = instance.Level,
                    CurrentExperience = instance.CurrentExperience, IsLocked = instance.IsLocked, IsNew = instance.IsNew,
                    AcquisitionSequence = instance.AcquisitionSequence
                });
                if (instance.AcquisitionSequence >= nextSequence) nextSequence = instance.AcquisitionSequence + 1;
            }
            snapshot.NextAcquisitionSequence = nextSequence;
            return snapshot;
        }

        /// <summary>验证圣遗物实例快照。</summary>
        /// <param name="snapshot">快照。</param>
        protected override void ValidateTypedSnapshot(ArtifactInventorySaveSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            snapshot.ValidateShape();
            if (snapshot.Instances.Count > manager.Capacity) throw new InvalidOperationException("圣遗物背包快照超过容量。");
            for (int index = 0; index < snapshot.Instances.Count; index++)
            {
                ArtifactInventorySaveEntry entry = snapshot.Instances[index];
                if (!ItemId.TryCreate(entry.DefinitionId, out ItemId definitionId) || !ItemManager.Instance.TryGetDefinition(definitionId, out ItemDefinition definition) || !(definition is ArtifactDefinition artifact))
                    throw new InvalidOperationException($"圣遗物快照引用了无效定义：{entry.DefinitionId}。");
                if (entry.Level > artifact.MaxLevel) throw new InvalidOperationException($"圣遗物实例 {entry.InstanceId} 的等级超出定义上限。");
            }
        }

        /// <summary>恢复已验证的圣遗物实例。</summary>
        /// <param name="snapshot">快照。</param>
        protected override void RestoreTypedSnapshot(ArtifactInventorySaveSnapshot snapshot)
        {
            var instances = new List<ArtifactInstance>(snapshot.Instances.Count);
            for (int index = 0; index < snapshot.Instances.Count; index++)
            {
                ArtifactInventorySaveEntry entry = snapshot.Instances[index];
                instances.Add(new ArtifactInstance(new EquipmentInstanceId(entry.InstanceId), new ItemId(entry.DefinitionId), entry.Level,
                    entry.CurrentExperience, entry.IsLocked, entry.IsNew, entry.AcquisitionSequence));
            }
            manager.RestoreState(instances, snapshot.NextAcquisitionSequence);
            manager.PublishRestored();
        }
    }
}
