using System;
using System.Collections.Generic;
using WS_Modules.CustomEventSystem;
using WS_Modules.Singleton;
using WSEventSystem = WS_Modules.CustomEventSystem.EventSystem;

namespace RPG.ItemSystem
{
    /// <summary>管理普通物品和养成道具的聚合数量，不管理独立装备实例。</summary>
    public sealed class StackableInventoryManager : SingletonBase<StackableInventoryManager>
    {
        #region 字段与构造

        private readonly Dictionary<ItemId, StackableInventoryEntry> entries =
            new Dictionary<ItemId, StackableInventoryEntry>();
        private long nextAcquisitionSequence = 1;

        /// <summary>创建空的可堆叠背包状态。</summary>
        private StackableInventoryManager()
        {
        }

        #endregion

        #region 查询

        /// <summary>获取指定物品数量。</summary>
        /// <param name="itemId">物品标识。</param>
        /// <returns>没有条目时返回零。</returns>
        public int GetQuantity(ItemId itemId) => entries.TryGetValue(itemId, out StackableInventoryEntry entry) ? entry.Quantity : 0;

        /// <summary>尝试获取一个可堆叠条目。</summary>
        /// <param name="itemId">物品标识。</param>
        /// <param name="entry">找到时返回条目。</param>
        /// <returns>找到时返回 true。</returns>
        public bool TryGetEntry(ItemId itemId, out StackableInventoryEntry entry) => entries.TryGetValue(itemId, out entry);

        /// <summary>获取当前所有可堆叠条目的只读时点副本。</summary>
        /// <returns>条目列表。</returns>
        public IReadOnlyList<StackableInventoryEntry> GetEntries() => new List<StackableInventoryEntry>(entries.Values);

        /// <summary>按顶层分类获取条目。</summary>
        /// <param name="category">目标分类。</param>
        /// <returns>匹配条目。</returns>
        public IReadOnlyList<StackableInventoryEntry> GetEntries(ItemCategory category)
        {
            var result = new List<StackableInventoryEntry>();
            foreach (StackableInventoryEntry entry in entries.Values)
            {
                if (TryGetDefinition(entry.ItemId, out ItemDefinition definition) && definition.Category == category)
                {
                    result.Add(entry);
                }
            }

            return result;
        }

        /// <summary>按任一养成用途获取仅用于成本的养成道具条目。</summary>
        /// <param name="requestedTypes">养成用途组合。</param>
        /// <returns>匹配条目。</returns>
        public IReadOnlyList<StackableInventoryEntry> GetDevelopmentItems(DevelopmentItemType requestedTypes)
        {
            ValidateDevelopmentTypes(requestedTypes);
            var result = new List<StackableInventoryEntry>();
            foreach (StackableInventoryEntry entry in entries.Values)
            {
                if (TryGetDefinition(entry.ItemId, out ItemDefinition definition) &&
                    definition is DevelopmentItemDefinition development && development.SupportsDevelopmentType(requestedTypes))
                {
                    result.Add(entry);
                }
            }

            // 字典只负责按 ItemId 索引；对外投影按首次获得顺序稳定排列，避免材料卡片顺序随哈希布局变化。
            SortEntriesByAcquisition(result);
            return result;
        }

        /// <summary>按任一成长对象获取养成经验道具条目。</summary>
        /// <param name="requestedTypes">经验适用对象组合。</param>
        /// <returns>匹配条目。</returns>
        public IReadOnlyList<StackableInventoryEntry> GetDevelopmentExperienceItems(
            DevelopmentExperienceItemType requestedTypes)
        {
            ValidateExperienceTypes(requestedTypes);
            var result = new List<StackableInventoryEntry>();
            foreach (StackableInventoryEntry entry in entries.Values)
            {
                if (TryGetDefinition(entry.ItemId, out ItemDefinition definition) &&
                    definition is DevelopmentExperienceItemDefinition experience &&
                    experience.SupportsExperienceType(requestedTypes))
                {
                    result.Add(entry);
                }
            }

            // 升级材料候选和已选材料投影共用确定顺序，避免重载或删除条目后出现视觉跳序。
            SortEntriesByAcquisition(result);
            return result;
        }

        #endregion

        #region 数量操作

        /// <summary>增加一种可堆叠物品。</summary>
        /// <param name="itemId">物品标识。</param>
        /// <param name="quantity">增加数量。</param>
        /// <returns>操作结果。</returns>
        public StackableItemOperationResult AddItem(ItemId itemId, int quantity) =>
            AddItems(new[] { new ItemQuantity(itemId, quantity) });

        /// <summary>原子增加多种可堆叠物品。</summary>
        /// <param name="items">物品数量请求。</param>
        /// <returns>操作结果。</returns>
        public StackableItemOperationResult AddItems(IReadOnlyList<ItemQuantity> items)
        {
            if (items == null || items.Count == 0) return new StackableItemOperationResult(InventoryOperationStatus.InvalidQuantity, default);
            var merged = new Dictionary<ItemId, int>();
            for (int index = 0; index < items.Count; index++)
            {
                ItemQuantity request = items[index];
                if (!request.ItemId.IsValid || request.Quantity <= 0) return new StackableItemOperationResult(InventoryOperationStatus.InvalidQuantity, request.ItemId);
                if (!TryGetDefinition(request.ItemId, out ItemDefinition definition)) return new StackableItemOperationResult(InventoryOperationStatus.UnknownDefinition, request.ItemId);
                if (!(definition is StackableItemDefinition)) return new StackableItemOperationResult(InventoryOperationStatus.DefinitionTypeMismatch, request.ItemId);
                try
                {
                    merged[request.ItemId] = checked(merged.TryGetValue(request.ItemId, out int current) ? current + request.Quantity : request.Quantity);
                }
                catch (OverflowException)
                {
                    return new StackableItemOperationResult(InventoryOperationStatus.ArithmeticOverflow, request.ItemId);
                }
            }

            foreach (KeyValuePair<ItemId, int> pair in merged)
            {
                StackableItemDefinition definition = (StackableItemDefinition)GetDefinition(pair.Key);
                int current = entries.TryGetValue(pair.Key, out StackableInventoryEntry entry) ? entry.Quantity : 0;
                try
                {
                    if (checked(current + pair.Value) > definition.MaxQuantity)
                    {
                        return new StackableItemOperationResult(InventoryOperationStatus.QuantityLimitExceeded, pair.Key);
                    }
                }
                catch (OverflowException)
                {
                    return new StackableItemOperationResult(InventoryOperationStatus.ArithmeticOverflow, pair.Key);
                }
            }

            var changes = new List<StackableItemChangedEvent>(merged.Count);
            // 所有条目都通过校验后才写入字典，保证批量添加不会留下部分状态。
            foreach (KeyValuePair<ItemId, int> pair in merged)
            {
                int previous = entries.TryGetValue(pair.Key, out StackableInventoryEntry oldEntry) ? oldEntry.Quantity : 0;
                // 永久发现与当前 New 解耦：只有第一次成功获得 Definition 才创建 New，追加数量不会重新标记。
                bool newlyDiscovered = ItemDiscoveryManager.Instance.MarkDiscovered(pair.Key);
                bool isNew = (oldEntry != null && oldEntry.IsNew) || newlyDiscovered;
                long sequence = oldEntry == null ? nextAcquisitionSequence++ : oldEntry.AcquisitionSequence;
                int current = previous + pair.Value;
                entries[pair.Key] = new StackableInventoryEntry(pair.Key, current, isNew, sequence);
                changes.Add(new StackableItemChangedEvent(pair.Key, previous, current, isNew));
            }

            // 只有全部条目写入后才广播，订阅方在第一个事件中读取到的是完整批次状态。
            for (int index = 0; index < changes.Count; index++) PublishChanged(changes[index]);

            return new StackableItemOperationResult(InventoryOperationStatus.Succeeded, default);
        }

        /// <summary>消耗一种可堆叠物品。</summary>
        /// <param name="itemId">物品标识。</param>
        /// <param name="quantity">消耗数量。</param>
        /// <returns>操作结果。</returns>
        public StackableItemOperationResult ConsumeItem(ItemId itemId, int quantity) =>
            ConsumeItems(new[] { new ItemQuantity(itemId, quantity) });

        /// <summary>原子消耗多种可堆叠物品。</summary>
        /// <param name="items">消耗请求。</param>
        /// <returns>操作结果。</returns>
        public StackableItemOperationResult ConsumeItems(IReadOnlyList<ItemQuantity> items)
        {
            if (items == null || items.Count == 0) return new StackableItemOperationResult(InventoryOperationStatus.InvalidQuantity, default);
            var merged = new Dictionary<ItemId, int>();
            for (int index = 0; index < items.Count; index++)
            {
                ItemQuantity request = items[index];
                if (!request.ItemId.IsValid || request.Quantity <= 0) return new StackableItemOperationResult(InventoryOperationStatus.InvalidQuantity, request.ItemId);
                if (!entries.TryGetValue(request.ItemId, out StackableInventoryEntry entry) || entry.Quantity < request.Quantity)
                    return new StackableItemOperationResult(InventoryOperationStatus.InsufficientQuantity, request.ItemId);
                try
                {
                    merged[request.ItemId] = checked(merged.TryGetValue(request.ItemId, out int current) ? current + request.Quantity : request.Quantity);
                }
                catch (OverflowException)
                {
                    return new StackableItemOperationResult(InventoryOperationStatus.ArithmeticOverflow, request.ItemId);
                }
            }

            var changes = new List<StackableItemChangedEvent>(merged.Count);
            foreach (KeyValuePair<ItemId, int> pair in merged)
            {
                StackableInventoryEntry oldEntry = entries[pair.Key];
                int current = oldEntry.Quantity - pair.Value;
                if (current == 0) entries.Remove(pair.Key);
                else entries[pair.Key] = new StackableInventoryEntry(pair.Key, current, oldEntry.IsNew, oldEntry.AcquisitionSequence);
                changes.Add(new StackableItemChangedEvent(pair.Key, oldEntry.Quantity, current, oldEntry.IsNew));
            }

            // 删除数量条目完成后统一广播，避免批次中间状态被外部观察到。
            for (int index = 0; index < changes.Count; index++) PublishChanged(changes[index]);

            return new StackableItemOperationResult(InventoryOperationStatus.Succeeded, default);
        }

        /// <summary>确认一个可堆叠条目的新获得提示。</summary>
        /// <param name="itemId">物品标识。</param>
        /// <returns>操作结果。</returns>
        public StackableItemOperationResult AcknowledgeNew(ItemId itemId)
        {
            if (!entries.TryGetValue(itemId, out StackableInventoryEntry oldEntry))
                return new StackableItemOperationResult(InventoryOperationStatus.UnknownDefinition, itemId);
            if (!oldEntry.IsNew) return new StackableItemOperationResult(InventoryOperationStatus.Succeeded, itemId);
            entries[itemId] = new StackableInventoryEntry(itemId, oldEntry.Quantity, false, oldEntry.AcquisitionSequence);
            // 单条操作也复用统一事件模型，确保批量与单条更新的广播形状一致。
            PublishChanged(new StackableItemChangedEvent(itemId, oldEntry.Quantity, oldEntry.Quantity, false));
            return new StackableItemOperationResult(InventoryOperationStatus.Succeeded, itemId);
        }

        #endregion

        #region 存档支持

        /// <summary>清空运行时数量状态。</summary>
        internal void ClearRuntimeState()
        {
            entries.Clear();
            nextAcquisitionSequence = 1;
        }

        /// <summary>用已经验证的快照替换当前状态。</summary>
        /// <param name="restoredEntries">恢复条目。</param>
        /// <param name="nextSequence">下一个获得顺序。</param>
        internal void RestoreState(IReadOnlyList<StackableInventoryEntry> restoredEntries, long nextSequence)
        {
            if (restoredEntries == null || nextSequence <= 0) throw new ArgumentException("可堆叠背包恢复数据无效。", nameof(restoredEntries));
            entries.Clear();
            for (int index = 0; index < restoredEntries.Count; index++)
            {
                StackableInventoryEntry entry = restoredEntries[index];
                if (entry == null || !entry.ItemId.IsValid || entry.Quantity <= 0) throw new InvalidOperationException("可堆叠背包快照包含非法条目。");
                entries.Add(entry.ItemId, entry);
            }

            nextAcquisitionSequence = nextSequence;
            WSEventSystem.EventTrigger_Type(typeof(StackableInventoryRestoredEvent), new StackableInventoryRestoredEvent());
        }

        #endregion

        #region 内部辅助

        /// <summary>查询并返回有效 Definition。</summary>
        /// <param name="itemId">物品标识。</param>
        /// <returns>定义。</returns>
        private ItemDefinition GetDefinition(ItemId itemId)
        {
            if (!TryGetDefinition(itemId, out ItemDefinition definition)) throw new InvalidOperationException($"[StackableInventoryManager] 找不到物品定义：{itemId}。");
            return definition;
        }

        /// <summary>通过 ItemManager 查询 Definition。</summary>
        /// <param name="itemId">物品标识。</param>
        /// <param name="definition">找到时返回 Definition。</param>
        /// <returns>找到时返回 true。</returns>
        private bool TryGetDefinition(ItemId itemId, out ItemDefinition definition) => ItemManager.Instance.TryGetDefinition(itemId, out definition);

        /// <summary>校验普通养成用途查询掩码。</summary>
        /// <param name="requestedTypes">待校验用途组合。</param>
        private static void ValidateDevelopmentTypes(DevelopmentItemType requestedTypes)
        {
            const DevelopmentItemType definedTypes = DevelopmentItemType.CharacterAscension |
                                                      DevelopmentItemType.CharacterTalent |
                                                      DevelopmentItemType.WeaponAscension |
                                                      DevelopmentItemType.WeaponRefinement;
            if (requestedTypes == DevelopmentItemType.None ||
                (requestedTypes & ~definedTypes) != DevelopmentItemType.None)
                throw new ArgumentException("养成道具查询用途必须包含有效用途。", nameof(requestedTypes));
        }

        /// <summary>校验养成经验用途查询掩码。</summary>
        /// <param name="requestedTypes">待校验对象组合。</param>
        private static void ValidateExperienceTypes(DevelopmentExperienceItemType requestedTypes)
        {
            const DevelopmentExperienceItemType definedTypes = DevelopmentExperienceItemType.Character |
                                                                DevelopmentExperienceItemType.Weapon |
                                                                DevelopmentExperienceItemType.Artifact;
            if (requestedTypes == DevelopmentExperienceItemType.None ||
                (requestedTypes & ~definedTypes) != DevelopmentExperienceItemType.None)
                throw new ArgumentException("养成经验道具查询对象必须包含有效对象。", nameof(requestedTypes));
        }

        /// <summary>按首次获得顺序和稳定 ItemId 对可堆叠条目排序。</summary>
        /// <param name="entriesToSort">待排序的条目列表。</param>
        private static void SortEntriesByAcquisition(List<StackableInventoryEntry> entriesToSort)
        {
            entriesToSort.Sort((left, right) =>
            {
                int sequenceComparison = left.AcquisitionSequence.CompareTo(right.AcquisitionSequence);
                return sequenceComparison != 0
                    ? sequenceComparison
                    : left.ItemId.CompareTo(right.ItemId);
            });
        }

        /// <summary>发布已提交的数量变化事件。</summary>
        /// <param name="itemId">物品标识。</param>
        /// <param name="previous">旧数量。</param>
        /// <param name="current">新数量。</param>
        /// <param name="isNew">新获得状态。</param>
        private static void PublishChanged(StackableItemChangedEvent change) =>
            WSEventSystem.EventTrigger_Type(typeof(StackableItemChangedEvent), change);

        #endregion
    }
}
