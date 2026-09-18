using System;
using RPG.ItemSystem;
using UnityEngine;
using WS_Modules.BusinessArchitecture;

namespace RPG.Character
{
    /// <summary>编排角色获取、默认武器生成和合法换装。</summary>
    public sealed class CharacterEquipmentSystem : AbstractSystem
    {
        #region 依赖字段

        // 角色拥有状态与武器实例是两个独立 Manager；本 System 只负责跨业务原子顺序。
        private CharacterRosterManager characterRosterManager;
        private WeaponInventoryManager weaponInventoryManager;

        #endregion

        #region 生命周期

        /// <summary>初始化角色装备编排，并解析 Architecture 持有的业务 Manager。</summary>
        protected override void OnInit()
        {
            characterRosterManager = this.GetManager<CharacterRosterManager>();
            weaponInventoryManager = this.GetManager<WeaponInventoryManager>();
            Debug.Log("[CharacterEquipmentSystem] 角色获取与装备编排已初始化。 ");
        }

        /// <summary>注销跨业务依赖引用；实际状态由各 Manager 生命周期清理。</summary>
        protected override void OnDeinit()
        {
            characterRosterManager = null;
            weaponInventoryManager = null;
            Debug.Log("[CharacterEquipmentSystem] 角色获取与装备编排已注销。 ");
        }

        #endregion

        #region 角色获取

        /// <summary>获取角色并创建一把直接进入装备缓存区的默认武器。</summary>
        /// <param name="characterId">待获取的角色标识。</param>
        /// <returns>角色获取结果；重复获取不会重复创建默认武器。</returns>
        public CharacterAcquisitionResult AcquireCharacter(CharacterId characterId)
        {
            if (!characterId.IsValid)
                return Fail(CharacterAcquisitionStatus.InvalidCharacterId, characterId, InventoryOperationStatus.InvalidQuantity);
            if (!CharacterConfigManager.Instance.TryGetConfig(characterId, out CharacterConfig config))
                return Fail(CharacterAcquisitionStatus.CharacterNotFound, characterId, InventoryOperationStatus.InstanceNotFound);

            config.Validate();
            if (!TryResolveDefaultWeapon(config, out ItemId defaultWeaponId, out CharacterAcquisitionStatus resolveStatus))
                return Fail(resolveStatus, characterId, InventoryOperationStatus.UnknownDefinition);

            // 已经拥有角色时，尝试修复默认武器；任何失败都不会占用容纳区容量。
            if (characterRosterManager.IsOwned(characterId))
            {
                // 如果已经拥有角色但没有默认武器，则尝试生成一把默认武器；如果已经拥有默认武器，则直接返回现有实例。
                if (weaponInventoryManager.TryGetEquippedWeapon(characterId, out WeaponInstance existingWeapon))
                    return new CharacterAcquisitionResult(
                        CharacterAcquisitionStatus.AlreadyOwned,
                        characterId,
                        existingWeapon,
                        InventoryOperationStatus.Succeeded);

                EquipmentAddResult<WeaponInstance> repairResult = weaponInventoryManager.AddEquippedWeapon(
                    defaultWeaponId,
                    characterId);
                if (!repairResult.Succeeded)
                    return Fail(CharacterAcquisitionStatus.DefaultWeaponOperationFailed, characterId, repairResult.Status);
                Debug.Log($"[CharacterEquipmentSystem] 修复角色默认武器，character={characterId}, instance={repairResult.Instance.InstanceId}。 ");
                return new CharacterAcquisitionResult(
                    CharacterAcquisitionStatus.AlreadyOwned,
                    characterId,
                    repairResult.Instance,
                    InventoryOperationStatus.Succeeded);
            }

            // 先校验并写入拥有事实，再写入装备缓存；任何失败都不会占用容纳区容量。
            if (!characterRosterManager.TryAddOwnedCharacter(characterId))
                return new CharacterAcquisitionResult(CharacterAcquisitionStatus.AlreadyOwned, characterId, null,
                    InventoryOperationStatus.Succeeded);

            EquipmentAddResult<WeaponInstance> addResult = weaponInventoryManager.AddEquippedWeapon(
                defaultWeaponId,
                characterId);
            if (!addResult.Succeeded)
            {
                characterRosterManager.RemoveOwnedCharacter(characterId);
                return Fail(CharacterAcquisitionStatus.DefaultWeaponOperationFailed, characterId, addResult.Status);
            }

            // 角色与默认武器都已写入后才发拥有事件，订阅方不会读到“有角色但没有武器”的半提交状态。
            characterRosterManager.PublishOwnershipChanged(characterId);
            Debug.Log($"[CharacterEquipmentSystem] 获取角色并生成默认武器，character={characterId}, " +
                      $"weapon={addResult.Instance.InstanceId}, stored={weaponInventoryManager.StoredCount}/" +
                      $"{weaponInventoryManager.Capacity}, equipped={weaponInventoryManager.EquippedCount}。 ");
            return new CharacterAcquisitionResult(
                CharacterAcquisitionStatus.Succeeded,
                characterId,
                addResult.Instance,
                InventoryOperationStatus.Succeeded);
        }

        #endregion

        #region 装备操作

        /// <summary>按角色规则装备容纳区中的指定武器。</summary>
        /// <param name="characterId">目标角色标识。</param>
        /// <param name="instanceId">容纳区武器实例标识。</param>
        /// <returns>装备操作结果。</returns>
        public EquipmentOperationResult EquipWeapon(CharacterId characterId, EquipmentInstanceId instanceId)
        {
            if (!characterRosterManager.IsOwned(characterId))
                return new EquipmentOperationResult(InventoryOperationStatus.CharacterNotOwned);
            if (!weaponInventoryManager.TryGetInstance(instanceId, out WeaponInstance instance))
                return new EquipmentOperationResult(InventoryOperationStatus.InstanceNotFound);
            if (!ItemManager.Instance.TryGetDefinition(instance.DefinitionId, out ItemDefinition itemDefinition) ||
                !(itemDefinition is WeaponDefinition definition))
                return new EquipmentOperationResult(InventoryOperationStatus.DefinitionTypeMismatch);
            if (!CharacterConfigManager.Instance.GetRequiredConfig(characterId).AllowsWeaponType(definition.WeaponType))
                return new EquipmentOperationResult(InventoryOperationStatus.WeaponTypeNotAllowed);

            return weaponInventoryManager.EquipWeapon(instanceId, characterId);
        }

        /// <summary>把角色当前装备武器移回容纳区。</summary>
        /// <param name="characterId">目标角色标识。</param>
        /// <returns>卸下操作结果。</returns>
        public EquipmentOperationResult UnequipWeapon(CharacterId characterId)
        {
            if (!characterRosterManager.IsOwned(characterId))
                return new EquipmentOperationResult(InventoryOperationStatus.CharacterNotOwned);
            return weaponInventoryManager.UnequipWeapon(characterId);
        }

        /// <summary>按角色标识查询当前装备武器。</summary>
        /// <param name="characterId">角色标识。</param>
        /// <param name="instance">装备中的武器实例。</param>
        /// <returns>角色存在装备武器时返回 true。</returns>
        public bool TryGetEquippedWeapon(CharacterId characterId, out WeaponInstance instance) =>
            weaponInventoryManager.TryGetEquippedWeapon(characterId, out instance);

        #endregion

        #region 内部校验

        /// <summary>解析角色默认武器类型对应的 Definition，并校验类型一致性。</summary>
        /// <param name="config">角色配置。</param>
        /// <param name="definitionId">解析出的默认武器 Definition 标识。</param>
        /// <param name="failureStatus">解析失败时的业务状态。</param>
        /// <returns>解析成功时返回 true。</returns>
        private bool TryResolveDefaultWeapon(
            CharacterConfig config,
            out ItemId definitionId,
            out CharacterAcquisitionStatus failureStatus)
        {
            if (!CharacterConfigManager.Instance.TryGetDefaultWeaponDefinitionId(
                    config.DefaultWeaponType,
                    out definitionId))
            {
                failureStatus = CharacterAcquisitionStatus.MissingDefaultWeapon;
                return false;
            }

            if (!ItemManager.Instance.TryGetDefinition(definitionId, out ItemDefinition definition) ||
                !(definition is WeaponDefinition weapon))
            {
                failureStatus = CharacterAcquisitionStatus.DefaultWeaponDefinitionInvalid;
                return false;
            }

            if (weapon.WeaponType != config.DefaultWeaponType ||
                !config.AllowsWeaponType(weapon.WeaponType))
            {
                failureStatus = CharacterAcquisitionStatus.DefaultWeaponTypeMismatch;
                return false;
            }

            failureStatus = CharacterAcquisitionStatus.Succeeded;
            return true;
        }

        /// <summary>记录角色获取失败并构造不含武器实例的结果。</summary>
        /// <param name="status">获取失败状态。</param>
        /// <param name="characterId">相关角色标识。</param>
        /// <param name="weaponStatus">底层武器操作状态。</param>
        /// <returns>失败结果。</returns>
        private static CharacterAcquisitionResult Fail(
            CharacterAcquisitionStatus status,
            CharacterId characterId,
            InventoryOperationStatus weaponStatus)
        {
            Debug.LogWarning($"[CharacterEquipmentSystem] 角色获取失败，character={characterId}, status={status}, " +
                             $"weaponStatus={weaponStatus}。 ");
            return new CharacterAcquisitionResult(status, characterId, null, weaponStatus);
        }

        #endregion
    }

    /// <summary>角色获取与默认武器生成的结果状态。</summary>
    public enum CharacterAcquisitionStatus
    {
        /// <summary>成功获取新角色。</summary>
        Succeeded = 0,
        /// <summary>角色已经拥有；返回现有或修复后的默认武器。</summary>
        AlreadyOwned,
        /// <summary>角色标识无效。</summary>
        InvalidCharacterId,
        /// <summary>角色配置不存在。</summary>
        CharacterNotFound,
        /// <summary>角色默认武器类型没有配置对应 Definition。</summary>
        MissingDefaultWeapon,
        /// <summary>默认武器 Definition 不存在或不是武器。</summary>
        DefaultWeaponDefinitionInvalid,
        /// <summary>默认武器 Definition 的类型与角色配置不一致。</summary>
        DefaultWeaponTypeMismatch,
        /// <summary>底层武器缓存写入失败。</summary>
        DefaultWeaponOperationFailed
    }

    /// <summary>角色获取操作的不可变结果。</summary>
    public readonly struct CharacterAcquisitionResult
    {
        /// <summary>创建角色获取结果。</summary>
        /// <param name="status">获取状态。</param>
        /// <param name="characterId">相关角色标识。</param>
        /// <param name="weapon">现有或新生成的默认武器。</param>
        /// <param name="weaponStatus">底层武器操作状态。</param>
        public CharacterAcquisitionResult(
            CharacterAcquisitionStatus status,
            CharacterId characterId,
            WeaponInstance weapon,
            InventoryOperationStatus weaponStatus)
        {
            Status = status;
            CharacterId = characterId;
            Weapon = weapon;
            WeaponStatus = weaponStatus;
        }

        /// <summary>获取角色获取状态。</summary>
        public CharacterAcquisitionStatus Status { get; }

        /// <summary>获取相关角色标识。</summary>
        public CharacterId CharacterId { get; }

        /// <summary>获取现有或新生成的默认武器；失败时为空。</summary>
        public WeaponInstance Weapon { get; }

        /// <summary>获取底层武器操作状态。</summary>
        public InventoryOperationStatus WeaponStatus { get; }

        /// <summary>判断角色是否已经拥有或本次成功获取。</summary>
        public bool Succeeded => Status == CharacterAcquisitionStatus.Succeeded ||
                                  Status == CharacterAcquisitionStatus.AlreadyOwned;
    }
}
