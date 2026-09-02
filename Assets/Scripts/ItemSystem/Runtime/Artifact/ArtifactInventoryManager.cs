using System;
using System.Collections.Generic;
using WS_Modules.CustomEventSystem;
using WSEventSystem = WS_Modules.CustomEventSystem.EventSystem;

namespace RPG.ItemSystem
{
    /// <summary>管理独立圣遗物实例、容量、锁定和基础成长状态。</summary>
    public sealed class ArtifactInventoryManager : EquipmentInstanceManagerBase<ArtifactInventoryManager, ArtifactInstance>
    {
        #region 静态配置与构造

        private static ArtifactInventorySettings settings;
        private static bool configured;

        /// <summary>创建圣遗物实例 Manager。</summary>
        private ArtifactInventoryManager() : base(GetConfiguredCapacity())
        {
        }

        /// <summary>静态注入圣遗物容量配置。</summary>
        /// <param name="inventorySettings">容量配置。</param>
        /// <exception cref="ArgumentNullException">配置为空时抛出。</exception>
        /// <exception cref="InvalidOperationException">重复注入不同配置时抛出。</exception>
        public static void Initialize(ArtifactInventorySettings inventorySettings)
        {
            if (inventorySettings == null) throw new ArgumentNullException(nameof(inventorySettings));
            inventorySettings.Validate();
            if (configured && !ReferenceEquals(settings, inventorySettings))
                throw new InvalidOperationException("[ArtifactInventoryManager] 已注入其他容量配置，不能静默覆盖。");
            settings = inventorySettings;
            configured = true;
        }

        /// <summary>读取首次创建实例所需容量。</summary>
        /// <returns>已验证容量。</returns>
        private static int GetConfiguredCapacity()
        {
            if (!configured || settings == null) throw new InvalidOperationException("[ArtifactInventoryManager] 尚未注入圣遗物容量配置。");
            return settings.Capacity;
        }

        #endregion

        #region 添加与移除

        /// <summary>添加一件新圣遗物。</summary>
        /// <param name="definitionId">圣遗物 Definition 标识。</param>
        /// <returns>添加结果。</returns>
        public EquipmentAddResult<ArtifactInstance> AddArtifact(ItemId definitionId)
        {
            EquipmentBatchAddResult<ArtifactInstance> result = AddArtifacts(new[] { definitionId });
            return result.Succeeded
                ? new EquipmentAddResult<ArtifactInstance>(InventoryOperationStatus.Succeeded, result.Instances[0])
                : new EquipmentAddResult<ArtifactInstance>(result.Status, null);
        }

        /// <summary>原子添加多件新圣遗物。</summary>
        /// <param name="definitionIds">圣遗物 Definition 标识。</param>
        /// <returns>添加结果。</returns>
        public EquipmentBatchAddResult<ArtifactInstance> AddArtifacts(IReadOnlyList<ItemId> definitionIds)
        {
            if (definitionIds == null || definitionIds.Count == 0)
                return new EquipmentBatchAddResult<ArtifactInstance>(InventoryOperationStatus.InvalidQuantity, Array.Empty<ArtifactInstance>());
            if (definitionIds.Count > Capacity - Count)
                return new EquipmentBatchAddResult<ArtifactInstance>(InventoryOperationStatus.CapacityExceeded, Array.Empty<ArtifactInstance>());

            for (int index = 0; index < definitionIds.Count; index++)
            {
                if (!definitionIds[index].IsValid || !TryGetDefinition(definitionIds[index], out ItemDefinition definition))
                    return new EquipmentBatchAddResult<ArtifactInstance>(InventoryOperationStatus.UnknownDefinition, Array.Empty<ArtifactInstance>());
                if (!(definition is ArtifactDefinition))
                    return new EquipmentBatchAddResult<ArtifactInstance>(InventoryOperationStatus.DefinitionTypeMismatch, Array.Empty<ArtifactInstance>());
            }

            var created = new List<ArtifactInstance>(definitionIds.Count);
            for (int index = 0; index < definitionIds.Count; index++)
            {
                var instance = new ArtifactInstance(EquipmentInstanceId.Create(), definitionIds[index], 0, 0, false, true, TakeAcquisitionSequence());
                instances.Add(instance.InstanceId, instance);
                created.Add(instance);
            }

            for (int index = 0; index < created.Count; index++) PublishChange(EquipmentInstanceChangeType.Added, created[index]);
            return new EquipmentBatchAddResult<ArtifactInstance>(InventoryOperationStatus.Succeeded, created);
        }

        /// <summary>移除一件未锁定圣遗物。</summary>
        /// <param name="instanceId">实例标识。</param>
        /// <returns>操作结果。</returns>
        public EquipmentOperationResult RemoveArtifact(EquipmentInstanceId instanceId) => RemoveArtifacts(new[] { instanceId });

        /// <summary>原子移除多件未锁定圣遗物。</summary>
        /// <param name="instanceIds">实例标识。</param>
        /// <returns>操作结果。</returns>
        public EquipmentOperationResult RemoveArtifacts(IReadOnlyList<EquipmentInstanceId> instanceIds)
        {
            if (instanceIds == null || instanceIds.Count == 0) return new EquipmentOperationResult(InventoryOperationStatus.InvalidQuantity);
            var removed = new List<ArtifactInstance>(instanceIds.Count);
            for (int index = 0; index < instanceIds.Count; index++)
            {
                if (!instances.TryGetValue(instanceIds[index], out ArtifactInstance instance)) return new EquipmentOperationResult(InventoryOperationStatus.InstanceNotFound);
                if (instance.IsLocked) return new EquipmentOperationResult(InventoryOperationStatus.InstanceLocked);
                if (removed.Exists(item => item.InstanceId == instance.InstanceId)) return new EquipmentOperationResult(InventoryOperationStatus.DuplicateInstanceId);
                removed.Add(instance);
            }

            for (int index = 0; index < removed.Count; index++)
            {
                instances.Remove(removed[index].InstanceId);
            }

            // 批量移除完成后再广播，保证订阅方读取到完整的圣遗物集合。
            for (int index = 0; index < removed.Count; index++)
                PublishChange(EquipmentInstanceChangeType.Removed, removed[index]);

            return new EquipmentOperationResult(InventoryOperationStatus.Succeeded);
        }

        #endregion

        #region 状态修改与存档

        /// <summary>更新圣遗物等级和经验。</summary>
        /// <param name="instanceId">实例标识。</param>
        /// <param name="update">目标状态。</param>
        /// <returns>操作结果。</returns>
        public EquipmentOperationResult UpdateArtifactProgress(EquipmentInstanceId instanceId, ArtifactProgressUpdate update)
        {
            if (!instances.TryGetValue(instanceId, out ArtifactInstance current)) return new EquipmentOperationResult(InventoryOperationStatus.InstanceNotFound);
            if (!TryGetDefinition(current.DefinitionId, out ItemDefinition definition) || !(definition is ArtifactDefinition artifact))
                return new EquipmentOperationResult(InventoryOperationStatus.DefinitionTypeMismatch);
            if (update.Level < 0 || update.Level > artifact.MaxLevel) return new EquipmentOperationResult(InventoryOperationStatus.LevelOutOfRange);
            if (update.CurrentExperience < 0) return new EquipmentOperationResult(InventoryOperationStatus.ExperienceOutOfRange);
            ArtifactInstance updated = new ArtifactInstance(current.InstanceId, current.DefinitionId, update.Level, update.CurrentExperience,
                current.IsLocked, current.IsNew, current.AcquisitionSequence);
            instances[instanceId] = updated;
            PublishChange(EquipmentInstanceChangeType.Updated, updated);
            return new EquipmentOperationResult(InventoryOperationStatus.Succeeded);
        }

        /// <summary>清空圣遗物运行时状态。</summary>
        internal void ClearRuntimeState() => ClearInstances();

        /// <summary>用已经验证的圣遗物实例替换运行时状态。</summary>
        /// <param name="restoredInstances">圣遗物实例。</param>
        /// <param name="nextSequence">下一个获得顺序。</param>
        internal void RestoreState(IReadOnlyList<ArtifactInstance> restoredInstances, long nextSequence) => ReplaceInstances(restoredInstances, nextSequence);

        /// <summary>发布圣遗物背包恢复事件。</summary>
        internal void PublishRestored() => WSEventSystem.EventTrigger_Type(typeof(ArtifactInventoryRestoredEvent), new ArtifactInventoryRestoredEvent());

        /// <summary>复制实例的公共状态，供基类处理锁定和新提示。</summary>
        /// <param name="source">原实例。</param>
        /// <param name="level">等级。</param>
        /// <param name="currentExperience">经验。</param>
        /// <param name="isLocked">锁定。</param>
        /// <param name="isNew">新提示。</param>
        /// <returns>更新后的圣遗物实例。</returns>
        protected override ArtifactInstance CopyWithState(ArtifactInstance source, int level, int currentExperience, bool isLocked, bool isNew) =>
            new ArtifactInstance(source.InstanceId, source.DefinitionId, level, currentExperience, isLocked, isNew, source.AcquisitionSequence);

        /// <summary>发布单个圣遗物变化事件。</summary>
        /// <param name="changeType">变化类型。</param>
        /// <param name="instance">实例。</param>
        protected override void PublishChange(EquipmentInstanceChangeType changeType, ArtifactInstance instance) =>
            WSEventSystem.EventTrigger_Type(typeof(ArtifactInstanceChangedEvent), new ArtifactInstanceChangedEvent(changeType, instance));

        #endregion

        #region 内部辅助

        /// <summary>通过 ItemManager 查询 Definition。</summary>
        /// <param name="definitionId">定义标识。</param>
        /// <param name="definition">找到的定义。</param>
        /// <returns>是否找到。</returns>
        private static bool TryGetDefinition(ItemId definitionId, out ItemDefinition definition) => ItemManager.Instance.TryGetDefinition(definitionId, out definition);

        #endregion
    }
}
