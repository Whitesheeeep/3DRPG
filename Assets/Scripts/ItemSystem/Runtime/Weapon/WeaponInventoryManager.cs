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

        #region 分区索引字段

        // key：CharacterId；value：当前装备到该角色的武器实例 ID。
        private readonly Dictionary<CharacterId, EquipmentInstanceId> weaponInstanceIdByCharacterIdMap =
            new Dictionary<CharacterId, EquipmentInstanceId>();
        // 容纳区只索引未装备武器；装备缓存区不进入该集合，因此不占用 Capacity。
        private readonly HashSet<EquipmentInstanceId> storedWeaponInstanceIds =
            new HashSet<EquipmentInstanceId>();


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
        public int StoredCount => storedWeaponInstanceIds.Count;

        /// <summary>获取装备缓存区中的武器数量；该数量不参与 Capacity 限制。</summary>
        public int EquippedCount => weaponInstanceIdByCharacterIdMap.Count;

        /// <summary>获取容纳区剩余可用容量。</summary>
        public int RemainingStorageCapacity => Capacity - StoredCount;

        /// <summary>获取容纳区武器的稳定时点副本。</summary>
        /// <returns>只包含未装备武器的实例列表。</returns>
        public IReadOnlyList<WeaponInstance> GetStoredInstances()
        {
            var result = new List<WeaponInstance>(storedWeaponInstanceIds.Count);
            foreach (EquipmentInstanceId instanceId in storedWeaponInstanceIds)
            {
                if (!instances.TryGetValue(instanceId, out WeaponInstance instance))
                    throw new InvalidOperationException($"[WeaponInventoryManager] 容纳区索引缺少武器实例：{instanceId}。 ");
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
            if (!weaponInstanceIdByCharacterIdMap.TryGetValue(characterId, out EquipmentInstanceId instanceId))
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
            saveManager.RegisterModule(new WeaponInventorySaveModule(this, characterRosterManager));
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

            // 先创建实例并写入容纳区索引，再统一发布事件，保证观察者看到的是完整分区状态。
            var created = new List<WeaponInstance>(definitionIds.Count);
            for (int index = 0; index < definitionIds.Count; index++)
            {
                var instance = new WeaponInstance(
                    EquipmentInstanceId.Create(), definitionIds[index], 1, 0, 0, 1,
                    false, TakeAcquisitionSequence(), default(CharacterId));
                instances.Add(instance.InstanceId, instance);
                storedWeaponInstanceIds.Add(instance.InstanceId);
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
            if (weaponInstanceIdByCharacterIdMap.ContainsKey(characterId))
                return new EquipmentAddResult<WeaponInstance>(InventoryOperationStatus.CharacterAlreadyHasWeapon, null);
            if (!definitionId.IsValid || !TryGetDefinition(definitionId, out ItemDefinition definition))
                return new EquipmentAddResult<WeaponInstance>(InventoryOperationStatus.UnknownDefinition, null);
            if (!(definition is WeaponDefinition))
                return new EquipmentAddResult<WeaponInstance>(InventoryOperationStatus.DefinitionTypeMismatch, null);

            WeaponInstance instance = new WeaponInstance(
                EquipmentInstanceId.Create(), definitionId, 1, 0, 0, 1,
                false, TakeAcquisitionSequence(), characterId);
            instances.Add(instance.InstanceId, instance);
            weaponInstanceIdByCharacterIdMap.Add(characterId, instance.InstanceId);
            if (itemDiscoveryManager.MarkDiscovered(instance.DefinitionId))
                MarkDefinitionNew(instance.DefinitionId);
            RefreshNewRedDotCount();
            PublishChange(EquipmentInstanceChangeType.Added, instance);
            Debug.Log($"[WeaponInventoryManager] 添加装备缓存武器，character={characterId}, instance={instance.InstanceId}, " +
                      $"stored={StoredCount}/{Capacity}, equipped={EquippedCount}。 ");
            return new EquipmentAddResult<WeaponInstance>(InventoryOperationStatus.Succeeded, instance);
        }

        /// <summary>把容纳区武器装备到角色；目标已有武器时执行不增加容纳区数量的换装。</summary>
        /// <param name="instanceId">待装备武器实例。</param>
        /// <param name="characterId">目标角色标识。</param>
        /// <returns>操作结果。</returns>
        internal EquipmentOperationResult EquipWeapon(EquipmentInstanceId instanceId, CharacterId characterId)
        {
            if (!characterId.IsValid)
                return new EquipmentOperationResult(InventoryOperationStatus.InvalidQuantity);
            if (!instances.TryGetValue(instanceId, out WeaponInstance current))
                return new EquipmentOperationResult(InventoryOperationStatus.InstanceNotFound);
            // 当前实例已经装备到目标角色时直接返回成功，避免重复换装。
            if (current.IsEquipped)
                return current.EquippedCharacterId == characterId
                    ? new EquipmentOperationResult(InventoryOperationStatus.Succeeded)
                    : new EquipmentOperationResult(InventoryOperationStatus.InstanceEquipped);
            if (!storedWeaponInstanceIds.Contains(instanceId))
                throw new InvalidOperationException($"[WeaponInventoryManager] 未装备武器未进入容纳区索引：{instanceId}。 ");

            WeaponInstance previous = null;
            if (weaponInstanceIdByCharacterIdMap.TryGetValue(characterId, out EquipmentInstanceId previousInstanceId))
            {
                if (!instances.TryGetValue(previousInstanceId, out previous) || !previous.IsEquipped ||
                    previous.EquippedCharacterId != characterId || storedWeaponInstanceIds.Contains(previousInstanceId))
                    throw new InvalidOperationException($"[WeaponInventoryManager] 角色装备缓存索引状态无效：character={characterId}。 ");
            }

            // 先完整更新两个分区索引，再广播 Updated，订阅方在回调中读取到的是一致状态。
            storedWeaponInstanceIds.Remove(instanceId);
            if (previous != null) storedWeaponInstanceIds.Add(previous.InstanceId);
            weaponInstanceIdByCharacterIdMap[characterId] = instanceId;
            if (previous != null)
            {
                WeaponInstance storedPrevious = CopyWithEquipment(previous, default(CharacterId));
                instances[previous.InstanceId] = storedPrevious;
                previous = storedPrevious;
            }

            WeaponInstance equipped = CopyWithEquipment(current, characterId);
            instances[instanceId] = equipped;
            if (previous != null) PublishChange(EquipmentInstanceChangeType.Updated, previous);
            PublishChange(EquipmentInstanceChangeType.Updated, equipped);
            Debug.Log($"[WeaponInventoryManager] 完成武器装备，character={characterId}, instance={instanceId}, " +
                      $"replaced={(previous != null)}, stored={StoredCount}/{Capacity}。 ");
            return new EquipmentOperationResult(InventoryOperationStatus.Succeeded);
        }

        /// <summary>把角色当前武器移回容纳区；容纳区已满时保持装备状态不变。</summary>
        /// <param name="characterId">目标角色标识。</param>
        /// <returns>操作结果。</returns>
        internal EquipmentOperationResult UnequipWeapon(CharacterId characterId)
        {
            if (!characterId.IsValid)
                return new EquipmentOperationResult(InventoryOperationStatus.InvalidQuantity);
            if (!weaponInstanceIdByCharacterIdMap.TryGetValue(characterId, out EquipmentInstanceId instanceId))
                return new EquipmentOperationResult(InventoryOperationStatus.InstanceNotFound);
            if (StoredCount >= Capacity)
                return new EquipmentOperationResult(InventoryOperationStatus.CapacityExceeded);
            if (!instances.TryGetValue(instanceId, out WeaponInstance current) || !current.IsEquipped)
                throw new InvalidOperationException($"[WeaponInventoryManager] 角色装备缓存索引缺少有效实例：character={characterId}。 ");

            WeaponInstance stored = CopyWithEquipment(current, default(CharacterId));
            weaponInstanceIdByCharacterIdMap.Remove(characterId);
            storedWeaponInstanceIds.Add(instanceId);
            instances[instanceId] = stored;
            PublishChange(EquipmentInstanceChangeType.Updated, stored);
            Debug.Log($"[WeaponInventoryManager] 完成武器卸下，character={characterId}, instance={instanceId}, " +
                      $"stored={StoredCount}/{Capacity}, equipped={EquippedCount}。 ");
            return new EquipmentOperationResult(InventoryOperationStatus.Succeeded);
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
                if (instance.IsEquipped) return new EquipmentOperationResult(InventoryOperationStatus.InstanceEquipped);
                if (removed.Exists(item => item.InstanceId == instance.InstanceId))
                    return new EquipmentOperationResult(InventoryOperationStatus.DuplicateInstanceId);
                removed.Add(instance);
            }

            for (int index = 0; index < removed.Count; index++)
            {
                instances.Remove(removed[index].InstanceId);
                if (!storedWeaponInstanceIds.Remove(removed[index].InstanceId))
                    throw new InvalidOperationException($"[WeaponInventoryManager] 待移除武器未进入容纳区索引：{removed[index].InstanceId}。 ");
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
                update.AscensionRank, update.RefinementRank, current.IsLocked, current.AcquisitionSequence,
                current.EquippedCharacterId);
            instances[instanceId] = updated;
            PublishChange(EquipmentInstanceChangeType.Updated, updated);
            return new EquipmentOperationResult(InventoryOperationStatus.Succeeded);
        }

        /// <summary>清空武器运行时状态，包括容纳区与装备缓存区索引。</summary>
        internal void ClearRuntimeState()
        {
            int previousTotalCount = TotalCount;
            storedWeaponInstanceIds.Clear();
            weaponInstanceIdByCharacterIdMap.Clear();
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
            BuildIndexes(restoredInstances,
                out HashSet<EquipmentInstanceId> restoredStoredInstanceIds,
                out Dictionary<CharacterId, EquipmentInstanceId> restoredWeaponInstanceIdByCharacterIdMap);
            ReplaceInstances(restoredInstances, restoredNewDefinitionIds, nextSequence);
            storedWeaponInstanceIds.Clear();
            foreach (EquipmentInstanceId instanceId in restoredStoredInstanceIds)
                storedWeaponInstanceIds.Add(instanceId);
            weaponInstanceIdByCharacterIdMap.Clear();
            foreach (KeyValuePair<CharacterId, EquipmentInstanceId> pair in restoredWeaponInstanceIdByCharacterIdMap)
                weaponInstanceIdByCharacterIdMap.Add(pair.Key, pair.Value);
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
                source.RefinementRank, isLocked, source.AcquisitionSequence, source.EquippedCharacterId);

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
        /// <summary>复制武器并只替换装备角色，不改变培养和锁定状态。</summary>
        /// <param name="source">原武器实例。</param>
        /// <param name="equippedCharacterId">新的装备角色；无效值表示回到容纳区。</param>
        /// <returns>替换装备关系后的武器实例。</returns>
        private static WeaponInstance CopyWithEquipment(WeaponInstance source, CharacterId equippedCharacterId) =>
            new WeaponInstance(source.InstanceId, source.DefinitionId, source.Level, source.CurrentExperience,
                source.AscensionRank, source.RefinementRank, source.IsLocked, source.AcquisitionSequence,
                equippedCharacterId);

        /// <summary>从恢复实例集合重建容纳区与装备缓存区索引。</summary>
        /// <param name="sourceInstances">待恢复的武器实例集合。</param>
        /// <param name="storedInstanceIds">恢复出的容纳区实例 ID 集合。</param>
        /// <param name="equippedInstanceIdByCharacterIdMap">恢复出的角色到武器实例 ID 映射。</param>
        private void BuildIndexes(
            IReadOnlyList<WeaponInstance> sourceInstances,
            out HashSet<EquipmentInstanceId> storedInstanceIds,
            out Dictionary<CharacterId, EquipmentInstanceId> equippedInstanceIdByCharacterIdMap)
        {
            if (sourceInstances == null) throw new ArgumentNullException(nameof(sourceInstances));
            storedInstanceIds = new HashSet<EquipmentInstanceId>();
            equippedInstanceIdByCharacterIdMap = new Dictionary<CharacterId, EquipmentInstanceId>();
            for (int index = 0; index < sourceInstances.Count; index++)
            {
                WeaponInstance instance = sourceInstances[index];
                if (instance == null || !instance.InstanceId.IsValid)
                    throw new InvalidOperationException("武器恢复集合包含无效实例。 ");
                if (instance.IsEquipped)
                {
                    if (!equippedInstanceIdByCharacterIdMap.TryAdd(instance.EquippedCharacterId, instance.InstanceId))
                        throw new InvalidOperationException($"武器恢复集合为角色重复装备：{instance.EquippedCharacterId}。 ");
                }
                else
                {
                    storedInstanceIds.Add(instance.InstanceId);
                }
            }

            if (storedInstanceIds.Count > Capacity)
                throw new InvalidOperationException($"武器恢复集合的容纳区数量超过容量：{storedInstanceIds.Count}/{Capacity}。 ");
        }

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
