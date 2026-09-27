using System;
using System.Collections.Generic;
using RPG.Character;
using RPG.RedDotSystemNS;
using RPG.SaveSystem;
using UnityEngine;
using WS_Modules.BusinessArchitecture;
using WS_Modules.CustomEventSystem;
using WSEventSystem = WS_Modules.CustomEventSystem.EventSystem;

namespace RPG.ItemSystem
{
    /// <summary>管理独立武器实例，提供容量、锁定和成长状态入口。</summary>
    public sealed class WeaponInventoryManager : EquipmentInstanceManagerBase<WeaponInstance>
    {
        #region 静态配置

        private static WeaponInventorySettings settings;
        private static bool configured;

        #endregion

        #region 依赖字段

        private readonly SaveManager saveManager;
        private readonly CharacterRosterManager characterRosterManager;
        private readonly ItemDiscoveryManager itemDiscoveryManager;
        private readonly RedDotSystem redDotSystem;
        private readonly RedDotKey weaponNewRedDotKey;

        /// <summary>获取武器库存容量配置是否已经注入。</summary>
        public static bool IsConfigured => configured && settings != null;

        #endregion

        #region 构造与配置

        /// <summary>创建由 GameArchitecture 持有的武器实例 Manager。</summary>
        /// <param name="saveManager">用于注册武器存档模块的 Manager。</param>
        /// <param name="characterRosterManager">用于校验武器装备关系的角色拥有 Manager。</param>
        /// <param name="itemDiscoveryManager">用于记录首次发现 Definition 的 Manager。</param>
        /// <param name="redDotSystem">统一红点运行时系统。</param>
        /// <param name="weaponNewRedDotKey">武器 New 红点叶节点。</param>
        public WeaponInventoryManager(
            SaveManager saveManager,
            CharacterRosterManager characterRosterManager,
            ItemDiscoveryManager itemDiscoveryManager,
            RedDotSystem redDotSystem,
            RedDotKey weaponNewRedDotKey) : base(GetConfiguredCapacity())
        {
            this.saveManager = saveManager ?? throw new ArgumentNullException(nameof(saveManager));
            this.characterRosterManager = characterRosterManager ??
                                          throw new ArgumentNullException(nameof(characterRosterManager));
            this.itemDiscoveryManager = itemDiscoveryManager ??
                                        throw new ArgumentNullException(nameof(itemDiscoveryManager));
            this.redDotSystem = redDotSystem ?? throw new ArgumentNullException(nameof(redDotSystem));
            this.weaponNewRedDotKey = weaponNewRedDotKey ??
                                      throw new ArgumentNullException(nameof(weaponNewRedDotKey));
        }

        /// <summary>静态注入武器容量配置。</summary>
        /// <param name="inventorySettings">容量配置。</param>
        /// <exception cref="ArgumentNullException">配置为空时抛出。</exception>
        /// <exception cref="InvalidOperationException">重复注入不同配置时抛出。</exception>
        public static void Initialize(WeaponInventorySettings inventorySettings)
        {
            if (inventorySettings == null) throw new ArgumentNullException(nameof(inventorySettings));
            inventorySettings.Validate();
            if (configured && !ReferenceEquals(settings, inventorySettings))
                throw new InvalidOperationException("[WeaponInventoryManager] 已注入其他容量配置，不能静默覆盖。");
            settings = inventorySettings;
            configured = true;
            Debug.Log($"[WeaponInventoryManager] 完成容量配置，storageCapacity={inventorySettings.Capacity}。 ");
        }

        /// <summary>读取首次创建实例所需的容量。</summary>
        /// <returns>已验证容量。</returns>
        private static int GetConfiguredCapacity()
        {
            if (!configured || settings == null)
                throw new InvalidOperationException("[WeaponInventoryManager] 尚未注入武器容量配置。");
            return settings.Capacity;
        }

        #endregion

        #region 分区统计与查询

        /// <summary>获取全部武器实例数量；该数量包含容纳区和装备缓存区。</summary>
        public int TotalCount => instances.Count;

        /// <summary>获取容纳区中的未装备武器数量；该数量参与 Capacity 限制。</summary>
        public int StoredCount
        {
            get
            {
                int count = 0;
                foreach (WeaponInstance instance in instances.Values)
                    if (!characterRosterManager.IsEquipmentEquipped(instance.InstanceId)) count++;
                return count;
            }
        }

        /// <summary>获取装备缓存区中的武器数量；该数量不参与 Capacity 限制。</summary>
        public int EquippedCount => characterRosterManager.GetEquippedWeaponCount();

        /// <summary>获取容纳区剩余可用容量。</summary>
        public int RemainingStorageCapacity => Capacity - StoredCount;

        /// <summary>获取容纳区武器的稳定时点副本。</summary>
        /// <returns>只包含未装备武器的实例列表。</returns>
        public IReadOnlyList<WeaponInstance> GetStoredInstances()
        {
            var result = new List<WeaponInstance>(StoredCount);
            foreach (WeaponInstance instance in instances.Values)
            {
                if (!characterRosterManager.IsEquipmentEquipped(instance.InstanceId))
                    result.Add(instance);
            }

            result.Sort((left, right) => left.AcquisitionSequence.CompareTo(right.AcquisitionSequence));
            return result;
        }

        /// <summary>按角色标识查询装备缓存区中的武器。</summary>
        /// <param name="characterId">目标角色标识。</param>
        /// <param name="instance">找到的装备武器。</param>
        /// <returns>角色存在装备武器时返回 true。</returns>
        public bool TryGetEquippedWeapon(CharacterId characterId, out WeaponInstance instance)
        {
            if (!characterRosterManager.TryGetEquippedWeaponInstanceId(characterId, out EquipmentInstanceId instanceId))
            {
                instance = null;
                return false;
            }

            if (!instances.TryGetValue(instanceId, out instance))
                throw new InvalidOperationException($"[WeaponInventoryManager] 装备缓存索引缺少武器实例：{instanceId}。 ");
            return true;
        }

        #endregion

        #region 架构生命周期

        /// <summary>注册武器存档模块。</summary>
        protected override void OnInit()
        {
            saveManager.RegisterModule(new WeaponInventorySaveModule(this));
            Debug.Log("[WeaponInventoryManager] 已注册武器存档模块。");
        }

        /// <summary>注销时清空武器运行时状态。</summary>
        protected override void OnDeinit()
        {
            ClearRuntimeState();
            Debug.Log("[WeaponInventoryManager] 已清理武器运行时状态。");
        }

        #endregion

        #region 添加与移除
        /// <summary>添加一把新武器实例。</summary>
        /// <param name="definitionId">武器 Definition 标识。</param>
        /// <returns>添加结果。</returns>
        public EquipmentAddResult<WeaponInstance> AddWeapon(ItemId definitionId)
        {
            EquipmentBatchAddResult<WeaponInstance> result = AddWeapons(new[] { definitionId });
            return result.Succeeded
                ? new EquipmentAddResult<WeaponInstance>(InventoryOperationStatus.Succeeded, result.Instances[0])
                : new EquipmentAddResult<WeaponInstance>(result.Status, null);
        }

        /// <summary>原子添加多把新武器实例。</summary>
        /// <param name="definitionIds">武器 Definition 标识。</param>
        /// <returns>添加结果。</returns>
        public EquipmentBatchAddResult<WeaponInstance> AddWeapons(IReadOnlyList<ItemId> definitionIds)
        {
            // 校验：输入参数是否有效
            if (definitionIds == null || definitionIds.Count == 0)
                return new EquipmentBatchAddResult<WeaponInstance>(InventoryOperationStatus.InvalidQuantity,
                    Array.Empty<WeaponInstance>());
            if (definitionIds.Count > RemainingStorageCapacity)
                return new EquipmentBatchAddResult<WeaponInstance>(InventoryOperationStatus.CapacityExceeded,
                    Array.Empty<WeaponInstance>());

            for (int index = 0; index < definitionIds.Count; index++)
            {
                if (!definitionIds[index].IsValid ||
                    !TryGetDefinition(definitionIds[index], out ItemDefinition definition))
                    return new EquipmentBatchAddResult<WeaponInstance>(InventoryOperationStatus.UnknownDefinition,
                        Array.Empty<WeaponInstance>());
                if (!(definition is WeaponDefinition))
                    return new EquipmentBatchAddResult<WeaponInstance>(InventoryOperationStatus.DefinitionTypeMismatch,
                        Array.Empty<WeaponInstance>());
            }

            // 先创建实例并写入统一实例集合，再统一发布事件，保证观察者看到的是完整状态。
            var created = new List<WeaponInstance>(definitionIds.Count);
            for (int index = 0; index < definitionIds.Count; index++)
            {
                var instance = new WeaponInstance(
                    EquipmentInstanceId.Create(), definitionIds[index], 1, 0, 0, 1,
                    false, TakeAcquisitionSequence());
                instances.Add(instance.InstanceId, instance);
                created.Add(instance);
            }

            // 先完成全部实例写入，再按 Definition 记录永久发现状态；同批次同名武器只会产生一次当前 New。
            for (int index = 0; index < created.Count; index++)
            {
                ItemId definitionId = created[index].DefinitionId;
                if (itemDiscoveryManager.MarkDiscovered(definitionId)) MarkDefinitionNew(definitionId);
            }

            RefreshNewRedDotCount();

            // 所有实例已经写入后才发布事件，订阅方读取 Manager 时能够拿到完整状态。
            for (int index = 0; index < created.Count; index++)
                PublishChange(EquipmentInstanceChangeType.Added, created[index]);
            Debug.Log($"[WeaponInventoryManager] 添加容纳区武器，added={created.Count}, stored={StoredCount}/{Capacity}, equipped={EquippedCount}。 ");
            return new EquipmentBatchAddResult<WeaponInstance>(InventoryOperationStatus.Succeeded, created);
        }

        /// <summary>直接创建一把已装备到指定角色的武器，绕过容纳区容量。</summary>
        /// <param name="definitionId">武器 Definition 标识。</param>
        /// <param name="characterId">目标角色标识。</param>
        /// <returns>添加结果。</returns>
        internal EquipmentAddResult<WeaponInstance> AddEquippedWeapon(ItemId definitionId, CharacterId characterId)
        {
            if (!characterId.IsValid)
                return new EquipmentAddResult<WeaponInstance>(InventoryOperationStatus.InvalidQuantity, null);
            if (characterRosterManager.TryGetEquippedWeaponInstanceId(characterId, out _))
                return new EquipmentAddResult<WeaponInstance>(InventoryOperationStatus.CharacterAlreadyHasWeapon, null);
            if (!definitionId.IsValid || !TryGetDefinition(definitionId, out ItemDefinition definition))
                return new EquipmentAddResult<WeaponInstance>(InventoryOperationStatus.UnknownDefinition, null);
            if (!(definition is WeaponDefinition))
                return new EquipmentAddResult<WeaponInstance>(InventoryOperationStatus.DefinitionTypeMismatch, null);

            WeaponInstance instance = new WeaponInstance(
                EquipmentInstanceId.Create(), definitionId, 1, 0, 0, 1,
                false, TakeAcquisitionSequence());
            instances.Add(instance.InstanceId, instance);
            if (itemDiscoveryManager.MarkDiscovered(instance.DefinitionId))
                MarkDefinitionNew(instance.DefinitionId);
            RefreshNewRedDotCount();
            PublishChange(EquipmentInstanceChangeType.Added, instance);
            Debug.Log($"[WeaponInventoryManager] 添加装备缓存武器，character={characterId}, instance={instance.InstanceId}, " +
                      $"stored={StoredCount}/{Capacity}, equipped={EquippedCount}。 ");
            return new EquipmentAddResult<WeaponInstance>(InventoryOperationStatus.Succeeded, instance);
        }

        /// <summary>移除一把未锁定武器。</summary>
        /// <param name="instanceId">实例标识。</param>
        /// <returns>操作结果。</returns>
        public EquipmentOperationResult RemoveWeapon(EquipmentInstanceId instanceId) =>
            RemoveWeapons(new[] { instanceId });

        /// <summary>原子移除多把未锁定武器。</summary>
        /// <param name="instanceIds">实例标识。</param>
        /// <returns>操作结果。</returns>
        public EquipmentOperationResult RemoveWeapons(IReadOnlyList<EquipmentInstanceId> instanceIds)
        {
            if (instanceIds == null || instanceIds.Count == 0)
                return new EquipmentOperationResult(InventoryOperationStatus.InvalidQuantity);
            var removed = new List<WeaponInstance>(instanceIds.Count);
            for (int index = 0; index < instanceIds.Count; index++)
            {
                if (!instances.TryGetValue(instanceIds[index], out WeaponInstance instance))
                    return new EquipmentOperationResult(InventoryOperationStatus.InstanceNotFound);
                if (instance.IsLocked) return new EquipmentOperationResult(InventoryOperationStatus.InstanceLocked);
                if (characterRosterManager.IsEquipmentEquipped(instance.InstanceId))
                    return new EquipmentOperationResult(InventoryOperationStatus.InstanceEquipped);
                if (removed.Exists(item => item.InstanceId == instance.InstanceId))
                    return new EquipmentOperationResult(InventoryOperationStatus.DuplicateInstanceId);
                removed.Add(instance);
            }

            for (int index = 0; index < removed.Count; index++)
            {
                instances.Remove(removed[index].InstanceId);
            }

            for (int index = 0; index < removed.Count; index++)
                RemoveDefinitionNewIfUnused(removed[index].DefinitionId);

            RefreshNewRedDotCount();

            // 批量移除完成后再广播，保证订阅方读取到完整的武器集合。
            for (int index = 0; index < removed.Count; index++)
                PublishChange(EquipmentInstanceChangeType.Removed, removed[index]);

            Debug.Log($"[WeaponInventoryManager] 移除容纳区武器，removed={removed.Count}, stored={StoredCount}/{Capacity}, equipped={EquippedCount}。 ");
            return new EquipmentOperationResult(InventoryOperationStatus.Succeeded);
        }
        #endregion

        #region 状态修改与存档

        /// <summary>更新武器等级、经验、突破和精炼状态。</summary>
        /// <param name="instanceId">实例标识。</param>
        /// <param name="update">目标状态。</param>
        /// <returns>操作结果。</returns>
        public EquipmentOperationResult UpdateWeaponProgress(EquipmentInstanceId instanceId,
            WeaponProgressUpdate update)
        {
            if (!instances.TryGetValue(instanceId, out WeaponInstance current))
                return new EquipmentOperationResult(InventoryOperationStatus.InstanceNotFound);
            WeaponDefinition definition = GetWeaponDefinition(current.DefinitionId);
            if (update.Level < 1 || update.Level > definition.MaxLevel)
                return new EquipmentOperationResult(InventoryOperationStatus.LevelOutOfRange);
            if (update.CurrentExperience < 0)
                return new EquipmentOperationResult(InventoryOperationStatus.ExperienceOutOfRange);
            if (update.AscensionRank < 0 || update.AscensionRank > definition.MaxAscensionRank)
                return new EquipmentOperationResult(InventoryOperationStatus.AscensionRankOutOfRange);
            if (update.RefinementRank < 1 || update.RefinementRank > definition.MaxRefinementRank)
                return new EquipmentOperationResult(InventoryOperationStatus.RefinementRankOutOfRange);
            WeaponInstance updated = new WeaponInstance(current.InstanceId, current.DefinitionId, update.Level,
                update.CurrentExperience,
                update.AscensionRank, update.RefinementRank, current.IsLocked, current.AcquisitionSequence);
            instances[instanceId] = updated;
            PublishChange(EquipmentInstanceChangeType.Updated, updated);
            return new EquipmentOperationResult(InventoryOperationStatus.Succeeded);
        }

        /// <summary>清空武器运行时状态，包括容纳区与装备缓存区索引。</summary>
        internal void ClearRuntimeState()
        {
            int previousTotalCount = TotalCount;
            ClearInstances();
            if (previousTotalCount > 0)
                Debug.Log($"[WeaponInventoryManager] 清空武器运行时状态，previousTotal={previousTotalCount}。 ");
        }

        /// <summary>用已经验证的武器实例替换运行时状态。</summary>
        /// <param name="restoredInstances">武器实例。</param>
        /// <param name="restoredNewDefinitionIds">武器 Definition New 标识。</param>
        /// <param name="nextSequence">下一个获得顺序。</param>
        internal void RestoreState(
            IReadOnlyList<WeaponInstance> restoredInstances,
            IReadOnlyList<ItemId> restoredNewDefinitionIds,
            long nextSequence)
        {
            ReplaceInstances(restoredInstances, restoredNewDefinitionIds, nextSequence);
            Debug.Log($"[WeaponInventoryManager] 恢复武器分区，total={TotalCount}, stored={StoredCount}/{Capacity}, equipped={EquippedCount}。 ");
            RefreshNewRedDotCount();
        }

        /// <summary>发布武器背包恢复事件。</summary>
        internal void PublishRestored() => WSEventSystem.EventTrigger_Type(typeof(WeaponInventoryRestoredEvent),
            new WeaponInventoryRestoredEvent());

        /// <summary>复制实例的公共状态，供基类处理锁定状态。</summary>
        /// <param name="source">原实例。</param>
        /// <param name="level">等级。</param>
        /// <param name="currentExperience">经验。</param>
        /// <param name="isLocked">锁定。</param>
        /// <returns>更新后的武器实例。</returns>
        protected override WeaponInstance CopyWithState(WeaponInstance source, int level, int currentExperience,
            bool isLocked) =>
            new WeaponInstance(source.InstanceId, source.DefinitionId, level, currentExperience, source.AscensionRank,
                source.RefinementRank, isLocked, source.AcquisitionSequence);

        /// <summary>发布单个武器变化事件。</summary>
        /// <param name="changeType">变化类型。</param>
        /// <param name="instance">实例。</param>
        protected override void PublishChange(EquipmentInstanceChangeType changeType, WeaponInstance instance) =>
            WSEventSystem.EventTrigger_Type(typeof(WeaponInstanceChangedEvent),
                new WeaponInstanceChangedEvent(changeType, instance));

        /// <summary>发布武器 Definition New 状态变化。</summary>
        /// <param name="definitionId">发生变化的武器 Definition。</param>
        /// <param name="isNew">变化后的 New 状态。</param>
        protected override void PublishDefinitionNewStateChanged(ItemId definitionId, bool isNew) =>
            WSEventSystem.EventTrigger_Type(
                typeof(WeaponDefinitionNewStateChangedEvent),
                new WeaponDefinitionNewStateChangedEvent(definitionId, isNew));

        /// <summary>将武器 Definition New 数量写入统一红点系统。</summary>
        protected override void RefreshNewRedDotCount()
        {
            redDotSystem.SetSelfValue(weaponNewRedDotKey, GetNewDefinitionIds().Count);
        }
        #endregion

        #region 内部校验
        /// <summary>查询武器 Definition。</summary>
        /// <param name="definitionId">定义标识。</param>
        /// <returns>武器定义。</returns>
        private WeaponDefinition GetWeaponDefinition(ItemId definitionId)
        {
            if (!TryGetDefinition(definitionId, out ItemDefinition definition))
                throw new InvalidOperationException($"[WeaponInventoryManager] 找不到武器定义：{definitionId}。");
            if (!(definition is WeaponDefinition weapon))
                throw new InvalidOperationException($"[WeaponInventoryManager] 定义不是武器：{definitionId}。");
            return weapon;
        }

        /// <summary>通过 ItemManager 查询定义。</summary>
        /// <param name="definitionId">定义标识。</param>
        /// <param name="definition">找到的定义。</param>
        /// <returns>是否找到。</returns>
        private static bool TryGetDefinition(ItemId definitionId, out ItemDefinition definition) =>
            ItemManager.Instance.TryGetDefinition(definitionId, out definition);
        #endregion
    }
}
