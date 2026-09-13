using System;
using System.Collections.Generic;
using WS_Modules.Singleton;

namespace RPG.ItemSystem
{
    /// <summary>
    /// 武器与圣遗物 Manager 共享的实例存储、容量和通用状态操作。
    /// </summary>
    /// <typeparam name="TManager">具体 Manager 类型。</typeparam>
    /// <typeparam name="TInstance">具体装备实例类型。</typeparam>
    public abstract class EquipmentInstanceManagerBase<TManager, TInstance> : SingletonBase<TManager>
        where TManager : EquipmentInstanceManagerBase<TManager, TInstance>
        where TInstance : EquipmentInstance
    {
        #region 字段与生命周期

        // 装备实例状态由对应 Manager 唯一持有；外部只能通过只读快照访问。
        protected readonly Dictionary<EquipmentInstanceId, TInstance> instances =
            new Dictionary<EquipmentInstanceId, TInstance>();
        // Definition New 状态由 Manager 统一持有；同一 Definition 的多个实例共享一份提示。
        private readonly HashSet<ItemId> newDefinitionIds = new HashSet<ItemId>();
        private readonly int capacity;
        private long nextAcquisitionSequence = 1;

        /// <summary>创建装备实例 Manager 的公共存储部分。</summary>
        /// <param name="capacity">该装备类型的最大实例数。</param>
        /// <exception cref="ArgumentOutOfRangeException">容量不是正数时抛出。</exception>
        protected EquipmentInstanceManagerBase(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "装备容量必须大于零。");
            }

            this.capacity = capacity;
        }

        #endregion

        #region 属性与通用操作

        /// <summary>获取当前实例数量。</summary>
        public int Count => instances.Count;

        /// <summary>获取当前实例容量。</summary>
        public int Capacity => capacity;

        /// <summary>尝试按实例 ID 查询装备。</summary>
        /// <param name="instanceId">实例标识。</param>
        /// <param name="instance">找到时返回实例。</param>
        /// <returns>找到时返回 true。</returns>
        public bool TryGetInstance(EquipmentInstanceId instanceId, out TInstance instance) =>
            instances.TryGetValue(instanceId, out instance);

        /// <summary>获取当前实例的只读时点副本。</summary>
        /// <returns>不暴露内部字典的新列表。</returns>
        public IReadOnlyList<TInstance> GetInstances() => new List<TInstance>(instances.Values);

        /// <summary>获取下一个可分配的获得顺序，供对应存档模块保存。</summary>
        public long NextAcquisitionSequence => nextAcquisitionSequence;

        /// <summary>判断一个 Definition 当前是否显示新获得提示。</summary>
        /// <param name="definitionId">Definition 标识。</param>
        /// <returns>该 Definition 已标记为 New 时返回 true。</returns>
        public bool IsDefinitionNew(ItemId definitionId) => newDefinitionIds.Contains(definitionId);

        /// <summary>获取 Definition New 状态的稳定只读快照。</summary>
        /// <returns>按 Definition 标识排序的新列表。</returns>
        public IReadOnlyList<ItemId> GetNewDefinitionIds()
        {
            var result = new List<ItemId>(newDefinitionIds);
            result.Sort((left, right) => left.CompareTo(right));
            return result;
        }

        /// <summary>设置装备锁定状态。</summary>
        /// <param name="instanceId">目标实例。</param>
        /// <param name="isLocked">新的锁定状态。</param>
        /// <returns>操作结果。</returns>
        public EquipmentOperationResult SetLocked(EquipmentInstanceId instanceId, bool isLocked)
        {
            if (!instances.TryGetValue(instanceId, out TInstance current))
            {
                return new EquipmentOperationResult(InventoryOperationStatus.InstanceNotFound);
            }

            if (current.IsLocked == isLocked)
            {
                return new EquipmentOperationResult(InventoryOperationStatus.Succeeded);
            }

            TInstance updated = CopyWithState(current, current.Level, current.CurrentExperience, isLocked);
            instances[instanceId] = updated;
            PublishChange(EquipmentInstanceChangeType.Updated, updated);
            return new EquipmentOperationResult(InventoryOperationStatus.Succeeded);
        }

        /// <summary>确认实例所属 Definition 的新获得提示。</summary>
        /// <param name="instanceId">目标实例。</param>
        /// <returns>操作结果。</returns>
        public EquipmentOperationResult AcknowledgeNew(EquipmentInstanceId instanceId)
        {
            if (!instances.TryGetValue(instanceId, out TInstance current))
            {
                return new EquipmentOperationResult(InventoryOperationStatus.InstanceNotFound);
            }

            if (!newDefinitionIds.Remove(current.DefinitionId))
            {
                return new EquipmentOperationResult(InventoryOperationStatus.Succeeded);
            }

            // Definition 状态变化不伪造实例 Updated 事件，由具体 Manager 发布领域事件。
            PublishDefinitionNewStateChanged(current.DefinitionId, false);
            return new EquipmentOperationResult(InventoryOperationStatus.Succeeded);
        }

        #endregion

        #region 派生 Manager 扩展点

        /// <summary>获取下一个获得顺序并递增计数。</summary>
        /// <returns>本次实例使用的获得顺序。</returns>
        protected long TakeAcquisitionSequence() => nextAcquisitionSequence++;

        /// <summary>设置恢复后的下一个获得顺序。</summary>
        /// <param name="nextSequence">下一个可用的顺序值。</param>
        protected void SetNextAcquisitionSequence(long nextSequence)
        {
            if (nextSequence <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(nextSequence), "获得顺序必须大于零。");
            }

            nextAcquisitionSequence = nextSequence;
        }

        /// <summary>清空运行时实例，供存档系统反初始化和恢复使用。</summary>
        protected void ClearInstances()
        {
            instances.Clear();
            newDefinitionIds.Clear();
            nextAcquisitionSequence = 1;
        }

        /// <summary>用存档系统已验证的实例集合替换当前状态。</summary>
        /// <param name="restoredInstances">恢复实例。</param>
        /// <param name="restoredNewDefinitionIds">恢复后的 Definition New 标识。</param>
        /// <param name="nextSequence">下一个获得顺序。</param>
        /// <exception cref="ArgumentNullException">实例列表为空时抛出。</exception>
        protected void ReplaceInstances(
            IReadOnlyList<TInstance> restoredInstances,
            IReadOnlyList<ItemId> restoredNewDefinitionIds,
            long nextSequence)
        {
            if (restoredInstances == null) throw new ArgumentNullException(nameof(restoredInstances));
            if (restoredNewDefinitionIds == null) throw new ArgumentNullException(nameof(restoredNewDefinitionIds));
            if (nextSequence <= 0) throw new ArgumentOutOfRangeException(nameof(nextSequence));
            instances.Clear();
            newDefinitionIds.Clear();
            for (int index = 0; index < restoredInstances.Count; index++)
            {
                TInstance instance = restoredInstances[index];
                if (instance == null || !instance.InstanceId.IsValid || instances.ContainsKey(instance.InstanceId))
                    throw new InvalidOperationException("装备实例快照包含空项或重复实例标识。");
                instances.Add(instance.InstanceId, instance);
            }

            ReplaceNewDefinitions(restoredNewDefinitionIds);
            nextAcquisitionSequence = nextSequence;
        }

        /// <summary>复制实例并替换通用状态。</summary>
        /// <param name="source">原实例。</param>
        /// <param name="level">新等级。</param>
        /// <param name="currentExperience">新经验。</param>
        /// <param name="isLocked">新锁定状态。</param>
        /// <returns>更新后的实例。</returns>
        protected abstract TInstance CopyWithState(
            TInstance source,
            int level,
            int currentExperience,
            bool isLocked);

        /// <summary>向领域事件中心发布实例变化。</summary>
        /// <param name="changeType">变化类型。</param>
        /// <param name="instance">变化后的或删除前实例。</param>
        protected abstract void PublishChange(
            EquipmentInstanceChangeType changeType,
            TInstance instance);

        /// <summary>发布 Definition New 状态变化。</summary>
        /// <param name="definitionId">发生变化的 Definition。</param>
        /// <param name="isNew">变化后的 New 状态。</param>
        protected abstract void PublishDefinitionNewStateChanged(ItemId definitionId, bool isNew);

        /// <summary>标记一个 Definition 为新获得。</summary>
        /// <param name="definitionId">Definition 标识。</param>
        /// <returns>本次是否新增了集合项。</returns>
        protected bool MarkDefinitionNew(ItemId definitionId)
        {
            if (!definitionId.IsValid) throw new ArgumentException("Definition 标识无效。", nameof(definitionId));
            return newDefinitionIds.Add(definitionId);
        }

        /// <summary>恢复并校验 Definition New 集合。</summary>
        /// <param name="definitionIds">待恢复的 Definition 标识。</param>
        protected void ReplaceNewDefinitions(IReadOnlyList<ItemId> definitionIds)
        {
            if (definitionIds == null) throw new ArgumentNullException(nameof(definitionIds));
            var existingDefinitionIds = new HashSet<ItemId>();
            foreach (TInstance instance in instances.Values) existingDefinitionIds.Add(instance.DefinitionId);

            newDefinitionIds.Clear();
            for (int index = 0; index < definitionIds.Count; index++)
            {
                ItemId definitionId = definitionIds[index];
                if (!definitionId.IsValid || !existingDefinitionIds.Contains(definitionId) || !newDefinitionIds.Add(definitionId))
                    throw new InvalidOperationException("装备存档的 Definition New 集合包含无效、孤立或重复标识。");
            }
        }

        /// <summary>当 Definition 已无实例时清理其 New 状态。</summary>
        /// <param name="definitionId">待检查的 Definition。</param>
        protected void RemoveDefinitionNewIfUnused(ItemId definitionId)
        {
            foreach (TInstance instance in instances.Values)
            {
                if (instance.DefinitionId == definitionId) return;
            }

            newDefinitionIds.Remove(definitionId);
        }

        #endregion
    }

    /// <summary>独立装备实例集合变化类型。</summary>
    public enum EquipmentInstanceChangeType
    {
        /// <summary>新增实例。</summary>
        Added = 0,
        /// <summary>移除实例。</summary>
        Removed = 1,
        /// <summary>已有实例状态更新。</summary>
        Updated = 2
    }
}
