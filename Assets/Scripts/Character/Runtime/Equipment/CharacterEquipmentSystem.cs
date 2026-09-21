using System;
using RPG.ItemSystem;
using RPG.SaveSystem;
using UnityEngine;
using WS_Modules.BusinessArchitecture;
using WS_Modules.CustomEventSystem;
using WSEventSystem = WS_Modules.CustomEventSystem.EventSystem;

namespace RPG.Character
{
    /// <summary>协调角色实例与武器、圣遗物库存之间的装备事务。</summary>
    /// <remarks>
    /// CharacterRosterManager 持有最终装备关系；本 System 只在完整校验通过后提交新的装备状态。
    /// 场景 CharacterActor 通过同一个 CharacterInstance 初始化 ASC，装备变化不会复制另一份角色身份数据。
    /// </remarks>
    public sealed class CharacterEquipmentSystem : AbstractSystem
    {
        #region 依赖字段

        // 依赖字段：角色状态是装备关系唯一权威，两个库存只持有各自装备实例。
        private CharacterRosterManager characterRosterManager;
        private WeaponInventoryManager weaponInventoryManager;
        private ArtifactInventoryManager artifactInventoryManager;
        private IUnRegister characterOwnershipChangedUnregister;

        #endregion

        #region 生命周期

        /// <summary>初始化装备事务并注册角色装备存档模块。</summary>
        protected override void OnInit()
        {
            characterRosterManager = this.GetManager<CharacterRosterManager>();
            weaponInventoryManager = this.GetManager<WeaponInventoryManager>();
            artifactInventoryManager = this.GetManager<ArtifactInventoryManager>();
            SaveManager saveManager = this.GetManager<SaveManager>();
            saveManager.RegisterModule(new CharacterEquipmentSaveModule(
                characterRosterManager, weaponInventoryManager, artifactInventoryManager, this));
            characterOwnershipChangedUnregister =
                this.RegisterEvent<CharacterOwnershipChangedEvent>(HandleCharacterOwnershipChanged);
            Debug.Log("[CharacterEquipmentSystem] 角色装备事务已初始化并注册独立装备关系存档。");
        }

        /// <summary>注销角色拥有事件并清理跨业务依赖。</summary>
        protected override void OnDeinit()
        {
            characterOwnershipChangedUnregister?.UnRegister();
            characterOwnershipChangedUnregister = null;
            characterRosterManager = null;
            weaponInventoryManager = null;
            artifactInventoryManager = null;
            Debug.Log("[CharacterEquipmentSystem] 角色装备事务已清理。");
        }

        #endregion

        #region 武器查询与默认装备

        /// <summary>获取角色当前装备武器；缺少时创建并绑定默认武器。</summary>
        /// <param name="characterId">目标角色。</param>
        /// <returns>角色武器解析结果。</returns>
        public CharacterWeaponResolutionResult GetOrCreateEquippedWeapon(CharacterId characterId)
        {
            if (!characterId.IsValid)
                return Fail(CharacterWeaponResolutionStatus.InvalidCharacterId, characterId, InventoryOperationStatus.InvalidQuantity);
            if (!characterRosterManager.IsOwned(characterId))
                return Fail(CharacterWeaponResolutionStatus.CharacterNotOwned, characterId, InventoryOperationStatus.CharacterNotOwned);
            if (TryGetEquippedWeapon(characterId, out WeaponInstance existingWeapon))
                return new CharacterWeaponResolutionResult(CharacterWeaponResolutionStatus.Succeeded, characterId,
                    existingWeapon, InventoryOperationStatus.Succeeded);
            if (!TryResolveDefaultWeapon(characterId, out ItemId defaultWeaponId,
                    out CharacterWeaponResolutionStatus resolveStatus))
                return Fail(resolveStatus, characterId, InventoryOperationStatus.UnknownDefinition);

            EquipmentAddResult<WeaponInstance> addResult = weaponInventoryManager.AddEquippedWeapon(defaultWeaponId, characterId);
            if (!addResult.Succeeded)
                return Fail(CharacterWeaponResolutionStatus.DefaultWeaponOperationFailed, characterId, addResult.Status);

            CharacterInstance character = characterRosterManager.GetRequiredInstance(characterId);
            characterRosterManager.CommitEquipmentState(characterId, character.Equipment.WithWeapon(addResult.Instance.InstanceId));
            Debug.Log($"[CharacterEquipmentSystem] 已创建并绑定默认武器，character={characterId}, instance={addResult.Instance.InstanceId}。");
            return new CharacterWeaponResolutionResult(CharacterWeaponResolutionStatus.Succeeded, characterId,
                addResult.Instance, InventoryOperationStatus.Succeeded);
        }

        /// <summary>按角色查询当前装备武器。</summary>
        /// <param name="characterId">角色标识。</param>
        /// <param name="instance">装备中的武器。</param>
        /// <returns>角色存在武器时返回 true。</returns>
        public bool TryGetEquippedWeapon(CharacterId characterId, out WeaponInstance instance)
        {
            instance = null;
            if (!characterRosterManager.TryGetEquippedWeaponInstanceId(characterId, out EquipmentInstanceId instanceId)) return false;
            if (!weaponInventoryManager.TryGetInstance(instanceId, out instance))
                throw new InvalidOperationException($"[CharacterEquipmentSystem] 角色装备索引缺少武器实例：{instanceId}。");
            return true;
        }

        #endregion

        #region 武器装备事务

        /// <summary>将未装备武器装备给角色，已被其他角色使用时明确拒绝。</summary>
        /// <param name="characterId">目标角色。</param>
        /// <param name="instanceId">武器实例。</param>
        /// <returns>装备操作结果。</returns>
        public EquipmentOperationResult EquipWeapon(CharacterId characterId, EquipmentInstanceId instanceId)
        {
            if (!characterRosterManager.IsOwned(characterId)) return new EquipmentOperationResult(InventoryOperationStatus.CharacterNotOwned);
            if (!weaponInventoryManager.TryGetInstance(instanceId, out WeaponInstance weapon))
                return new EquipmentOperationResult(InventoryOperationStatus.InstanceNotFound);
            if (!ItemManager.Instance.TryGetDefinition(weapon.DefinitionId, out ItemDefinition definition) ||
                !(definition is WeaponDefinition weaponDefinition))
                return new EquipmentOperationResult(InventoryOperationStatus.DefinitionTypeMismatch);
            if (!CharacterConfigManager.Instance.GetRequiredConfig(characterId).AllowsWeaponType(weaponDefinition.WeaponType))
                return new EquipmentOperationResult(InventoryOperationStatus.WeaponTypeNotAllowed);
            if (characterRosterManager.TryGetEquipmentOwner(instanceId, out CharacterId owner))
                return owner == characterId
                    ? new EquipmentOperationResult(InventoryOperationStatus.Succeeded)
                    : new EquipmentOperationResult(InventoryOperationStatus.InstanceEquipped);

            CharacterInstance character = characterRosterManager.GetRequiredInstance(characterId);
            characterRosterManager.CommitEquipmentState(characterId, character.Equipment.WithWeapon(instanceId));
            Debug.Log($"[CharacterEquipmentSystem] 武器装备事务成功，character={characterId}, instance={instanceId}。");
            return new EquipmentOperationResult(InventoryOperationStatus.Succeeded);
        }

        /// <summary>卸下角色武器；容纳区满时保持原状态。</summary>
        /// <param name="characterId">目标角色。</param>
        /// <returns>卸下结果。</returns>
        public EquipmentOperationResult UnequipWeapon(CharacterId characterId)
        {
            if (!characterRosterManager.IsOwned(characterId)) return new EquipmentOperationResult(InventoryOperationStatus.CharacterNotOwned);
            if (!characterRosterManager.TryGetEquippedWeaponInstanceId(characterId, out _))
                return new EquipmentOperationResult(InventoryOperationStatus.InstanceNotFound);
            if (weaponInventoryManager.StoredCount >= weaponInventoryManager.Capacity)
                return new EquipmentOperationResult(InventoryOperationStatus.CapacityExceeded);

            CharacterInstance character = characterRosterManager.GetRequiredInstance(characterId);
            characterRosterManager.CommitEquipmentState(characterId, character.Equipment.WithWeapon(default(EquipmentInstanceId)));
            Debug.Log($"[CharacterEquipmentSystem] 武器卸下事务成功，character={characterId}。");
            return new EquipmentOperationResult(InventoryOperationStatus.Succeeded);
        }

        #endregion

        #region 圣遗物装备事务

        /// <summary>按圣遗物 Definition 部位装备实例。</summary>
        /// <param name="characterId">目标角色。</param>
        /// <param name="artifactInstanceId">圣遗物实例。</param>
        /// <returns>装备操作结果。</returns>
        public EquipmentOperationResult EquipArtifact(CharacterId characterId, EquipmentInstanceId artifactInstanceId)
        {
            if (!characterRosterManager.IsOwned(characterId)) return new EquipmentOperationResult(InventoryOperationStatus.CharacterNotOwned);
            if (!artifactInventoryManager.TryGetInstance(artifactInstanceId, out ArtifactInstance artifact))
                return new EquipmentOperationResult(InventoryOperationStatus.InstanceNotFound);
            if (!ItemManager.Instance.TryGetDefinition(artifact.DefinitionId, out ItemDefinition definition) ||
                !(definition is ArtifactDefinition artifactDefinition))
                return new EquipmentOperationResult(InventoryOperationStatus.DefinitionTypeMismatch);
            if (characterRosterManager.TryGetEquipmentOwner(artifactInstanceId, out CharacterId owner))
                return owner == characterId
                    ? new EquipmentOperationResult(InventoryOperationStatus.Succeeded)
                    : new EquipmentOperationResult(InventoryOperationStatus.InstanceEquipped);

            CharacterInstance character = characterRosterManager.GetRequiredInstance(characterId);
            // 圣遗物槽位唯一，直接覆盖原有槽位。
            characterRosterManager.CommitEquipmentState(characterId,
                character.Equipment.WithArtifact(artifactDefinition.Slot, artifactInstanceId));
            Debug.Log($"[CharacterEquipmentSystem] 圣遗物装备事务成功，character={characterId}, instance={artifactInstanceId}, slot={artifactDefinition.Slot}。");
            return new EquipmentOperationResult(InventoryOperationStatus.Succeeded);
        }

        /// <summary>卸下角色指定圣遗物槽。</summary>
        /// <param name="characterId">目标角色。</param>
        /// <param name="slot">圣遗物部位。</param>
        /// <returns>卸下结果。</returns>
        public EquipmentOperationResult UnequipArtifact(CharacterId characterId, ArtifactSlot slot)
        {
            if (!characterRosterManager.IsOwned(characterId)) return new EquipmentOperationResult(InventoryOperationStatus.CharacterNotOwned);
            if (!characterRosterManager.TryGetEquippedArtifactInstanceId(characterId, slot, out _))
                return new EquipmentOperationResult(InventoryOperationStatus.InstanceNotFound);
            CharacterInstance character = characterRosterManager.GetRequiredInstance(characterId);
            characterRosterManager.CommitEquipmentState(characterId, character.Equipment.WithArtifact(slot, default(EquipmentInstanceId)));
            Debug.Log($"[CharacterEquipmentSystem] 圣遗物卸下事务成功，character={characterId}, slot={slot}。");
            return new EquipmentOperationResult(InventoryOperationStatus.Succeeded);
        }

        /// <summary>按角色和部位查询圣遗物。</summary>
        /// <param name="characterId">角色标识。</param>
        /// <param name="slot">圣遗物部位。</param>
        /// <param name="instance">装备中的圣遗物。</param>
        /// <returns>槽位有圣遗物时返回 true。</returns>
        public bool TryGetEquippedArtifact(CharacterId characterId, ArtifactSlot slot, out ArtifactInstance instance)
        {
            instance = null;
            if (!characterRosterManager.TryGetEquippedArtifactInstanceId(characterId, slot, out EquipmentInstanceId instanceId)) return false;
            if (!artifactInventoryManager.TryGetInstance(instanceId, out instance))
                throw new InvalidOperationException($"[CharacterEquipmentSystem] 角色装备索引缺少圣遗物实例：{instanceId}。");
            return true;
        }

        #endregion

        #region 存档恢复

        /// <summary>由独立存档模块整体恢复角色装备状态并发布一次完成事件。</summary>
        /// <param name="equipmentByCharacterIdMap">已校验装备关系。</param>
        internal void RestoreEquipmentState(System.Collections.Generic.IReadOnlyDictionary<CharacterId, CharacterEquipmentState> equipmentByCharacterIdMap)
        {
            characterRosterManager.RestoreEquipmentState(equipmentByCharacterIdMap);
            WSEventSystem.EventTrigger_Type(typeof(CharacterEquipmentRestoredEvent), new CharacterEquipmentRestoredEvent());
        }

        #endregion

        #region 事件处理与校验

        /// <summary>角色首次获得后创建并绑定默认武器。</summary>
        /// <param name="ownershipChangedEvent">角色拥有事件。</param>
        private void HandleCharacterOwnershipChanged(CharacterOwnershipChangedEvent ownershipChangedEvent)
        {
            if (!ownershipChangedEvent.IsOwned) return;
            CharacterWeaponResolutionResult result = GetOrCreateEquippedWeapon(ownershipChangedEvent.CharacterId);
            if (!result.Succeeded)
                Debug.LogError($"[CharacterEquipmentSystem] 新角色默认武器绑定失败，character={ownershipChangedEvent.CharacterId}, status={result.Status}。");
        }

        /// <summary>解析角色默认武器 Definition。</summary>
        private bool TryResolveDefaultWeapon(CharacterId characterId, out ItemId definitionId,
            out CharacterWeaponResolutionStatus failureStatus)
        {
            definitionId = default(ItemId);
            if (!CharacterConfigManager.Instance.TryGetConfig(characterId, out CharacterConfig config))
            {
                failureStatus = CharacterWeaponResolutionStatus.CharacterNotFound;
                return false;
            }
            config.Validate();
            if (!CharacterConfigManager.Instance.TryGetDefaultWeaponDefinitionId(config.DefaultWeaponType, out definitionId))
            {
                failureStatus = CharacterWeaponResolutionStatus.MissingDefaultWeapon;
                return false;
            }
            if (!ItemManager.Instance.TryGetDefinition(definitionId, out ItemDefinition definition) ||
                !(definition is WeaponDefinition weapon) || weapon.WeaponType != config.DefaultWeaponType)
            {
                failureStatus = CharacterWeaponResolutionStatus.DefaultWeaponDefinitionInvalid;
                return false;
            }
            failureStatus = CharacterWeaponResolutionStatus.Succeeded;
            return true;
        }

        /// <summary>创建失败结果并记录上下文。</summary>
        private static CharacterWeaponResolutionResult Fail(CharacterWeaponResolutionStatus status, CharacterId characterId,
            InventoryOperationStatus weaponStatus)
        {
            Debug.LogWarning($"[CharacterEquipmentSystem] 角色武器解析失败，character={characterId}, status={status}, weaponStatus={weaponStatus}。");
            return new CharacterWeaponResolutionResult(status, characterId, null, weaponStatus);
        }

        #endregion
    }

    /// <summary>角色默认武器保障结果状态。</summary>
    public enum CharacterWeaponResolutionStatus
    {
        /// <summary>成功。</summary>
        Succeeded = 0,
        /// <summary>角色标识无效。</summary>
        InvalidCharacterId,
        /// <summary>角色未拥有。</summary>
        CharacterNotOwned,
        /// <summary>角色配置不存在。</summary>
        CharacterNotFound,
        /// <summary>缺少默认武器。</summary>
        MissingDefaultWeapon,
        /// <summary>默认武器无效。</summary>
        DefaultWeaponDefinitionInvalid,
        /// <summary>默认武器操作失败。</summary>
        DefaultWeaponOperationFailed
    }

    /// <summary>角色武器解析结果。</summary>
    public readonly struct CharacterWeaponResolutionResult
    {
        /// <summary>创建结果。</summary>
        public CharacterWeaponResolutionResult(CharacterWeaponResolutionStatus status, CharacterId characterId,
            WeaponInstance weapon, InventoryOperationStatus weaponStatus)
        {
            Status = status;
            CharacterId = characterId;
            Weapon = weapon;
            WeaponStatus = weaponStatus;
        }

        /// <summary>获取状态。</summary>
        public CharacterWeaponResolutionStatus Status { get; }
        /// <summary>获取角色。</summary>
        public CharacterId CharacterId { get; }
        /// <summary>获取武器。</summary>
        public WeaponInstance Weapon { get; }
        /// <summary>获取底层操作状态。</summary>
        public InventoryOperationStatus WeaponStatus { get; }
        /// <summary>判断成功。</summary>
        public bool Succeeded => Status == CharacterWeaponResolutionStatus.Succeeded;
    }
}
