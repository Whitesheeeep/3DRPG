using System;
using System.Collections.Generic;
using RPG.SaveSystem;

namespace RPG.ItemSystem
{
    /// <summary>可堆叠背包的版本化存档快照。</summary>
    [Serializable]
    public sealed class StackableInventorySaveSnapshot : ISaveModuleSnapshot
    {
        /// <summary>创建空快照。</summary>
        public StackableInventorySaveSnapshot() => Entries = new List<StackableInventorySaveEntry>();

        /// <summary>获取条目。</summary>
        public List<StackableInventorySaveEntry> Entries { get; set; }

        /// <summary>获取下一个获得顺序。</summary>
        public long NextAcquisitionSequence { get; set; } = 1;

        /// <summary>验证快照结构。</summary>
        /// <exception cref="InvalidOperationException">结构不合法时抛出。</exception>
        public void ValidateShape()
        {
            if (Entries == null || NextAcquisitionSequence <= 0) throw new InvalidOperationException("可堆叠背包快照结构无效。");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < Entries.Count; index++)
            {
                StackableInventorySaveEntry entry = Entries[index];
                if (entry == null || string.IsNullOrWhiteSpace(entry.ItemId) || entry.Quantity <= 0 || entry.AcquisitionSequence <= 0 || !ids.Add(entry.ItemId))
                    throw new InvalidOperationException("可堆叠背包快照包含非法或重复条目。");
            }
        }
    }

    /// <summary>可堆叠背包快照中的一条数量数据。</summary>
    [Serializable]
    public sealed class StackableInventorySaveEntry
    {
        /// <summary>物品标识文本。</summary>
        public string ItemId { get; set; } = string.Empty;

        /// <summary>物品数量。</summary>
        public int Quantity { get; set; }

        /// <summary>新获得标记。</summary>
        public bool IsNew { get; set; }

        /// <summary>获得顺序。</summary>
        public long AcquisitionSequence { get; set; }
    }

    /// <summary>将 StackableInventoryManager 状态接入 SaveSystem。</summary>
    public sealed class StackableInventorySaveModule : SaveModule<StackableInventorySaveSnapshot>
    {
        private readonly StackableInventoryManager manager;

        /// <summary>创建可堆叠背包存档模块。</summary>
        /// <param name="manager">可堆叠背包 Manager。</param>
        public StackableInventorySaveModule(StackableInventoryManager manager)
            : base(new SaveModuleId("stackable-inventory"), 1, SaveMissingModulePolicy.Required)
        {
            this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        /// <summary>采集当前可堆叠状态。</summary>
        /// <returns>快照。</returns>
        protected override StackableInventorySaveSnapshot CaptureTypedSnapshot()
        {
            var snapshot = new StackableInventorySaveSnapshot();
            IReadOnlyList<StackableInventoryEntry> entries = manager.GetEntries();
            long nextSequence = 1;
            for (int index = 0; index < entries.Count; index++)
            {
                StackableInventoryEntry entry = entries[index];
                snapshot.Entries.Add(new StackableInventorySaveEntry
                {
                    ItemId = entry.ItemId.Value,
                    Quantity = entry.Quantity,
                    IsNew = entry.IsNew,
                    AcquisitionSequence = entry.AcquisitionSequence
                });
                if (entry.AcquisitionSequence >= nextSequence) nextSequence = entry.AcquisitionSequence + 1;
            }
            snapshot.NextAcquisitionSequence = nextSequence;
            return snapshot;
        }

        /// <summary>验证可堆叠快照及当前 Definition。</summary>
        /// <param name="snapshot">快照。</param>
        protected override void ValidateTypedSnapshot(StackableInventorySaveSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            snapshot.ValidateShape();
            for (int index = 0; index < snapshot.Entries.Count; index++)
            {
                StackableInventorySaveEntry entry = snapshot.Entries[index];
                if (!ItemId.TryCreate(entry.ItemId, out ItemId itemId) || !ItemManager.Instance.TryGetDefinition(itemId, out ItemDefinition definition) || !(definition is StackableItemDefinition stackable))
                    throw new InvalidOperationException($"可堆叠背包快照引用了无效定义：{entry.ItemId}。");
                if (entry.Quantity > stackable.MaxQuantity) throw new InvalidOperationException($"物品 {entry.ItemId} 超过最大堆叠数量。");
            }
        }

        /// <summary>恢复已验证的可堆叠状态。</summary>
        /// <param name="snapshot">快照。</param>
        protected override void RestoreTypedSnapshot(StackableInventorySaveSnapshot snapshot)
        {
            var entries = new List<StackableInventoryEntry>(snapshot.Entries.Count);
            for (int index = 0; index < snapshot.Entries.Count; index++)
            {
                StackableInventorySaveEntry entry = snapshot.Entries[index];
                entries.Add(new StackableInventoryEntry(new ItemId(entry.ItemId), entry.Quantity, entry.IsNew, entry.AcquisitionSequence));
            }
            manager.RestoreState(entries, snapshot.NextAcquisitionSequence);
        }
    }
}
