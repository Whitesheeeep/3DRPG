using RPG.ItemSystem;
using UnityEngine;
using WS_Modules.BusinessArchitecture;
using WS_Modules.CustomEventSystem;

namespace RPG.Character
{
    /// <summary>协调已拥有角色与武器库存之间的装备关系。</summary>
    /// <remarks>
    /// 本 System 响应角色获得事件，优先复用角色当前装备，在角色缺少装备时创建默认武器，
    /// 并提供换装、卸下和装备查询入口。角色拥有状态与武器实例分别由对应 Manager 持有，
    /// 本 System 不负责新增角色拥有状态；自动装配失败时保留角色，等待显式修复入口再次处理。
    /// </remarks>
    public sealed class CharacterEquipmentSystem : AbstractSystem
    {
        #region 依赖字段

        // 角色拥有状态与武器实例由两个独立 Manager 持有；本 System 只负责跨业务装备协调。
        private CharacterRosterManager characterRosterManager;
        private WeaponInventoryManager weaponInventoryManager;
        private IUnRegister characterOwnershipChangedUnregister;

        #endregion

        #region 生命周期

        /// <summary>初始化角色武器协调，并订阅角色获得事件。</summary>
        protected override void OnInit()
        {
            characterRosterManager = this.GetManager<CharacterRosterManager>();
            weaponInventoryManager = this.GetManager<WeaponInventoryManager>();
            characterOwnershipChangedUnregister =
                this.RegisterEvent<CharacterOwnershipChangedEvent>(HandleCharacterOwnershipChanged);
            Debug.Log("[CharacterEquipmentSystem] 角色武器协调已初始化并订阅角色获得事件。 ");
        }

        /// <summary>注销角色获得事件并清理跨业务依赖引用。</summary>
        protected override void OnDeinit()
        {
            // System 生命周期结束前先注销事件，避免 Architecture 重建后旧实例继续响应角色获得。
            characterOwnershipChangedUnregister?.UnRegister();
            characterOwnershipChangedUnregister = null;
            characterRosterManager = null;
            weaponInventoryManager = null;
            Debug.Log("[CharacterEquipmentSystem] 角色武器协调已注销并清理事件订阅。 ");
        }

        #endregion

        #region 角色武器保障

        /// <summary>获取角色当前装备武器；缺少装备时创建对应的默认武器。</summary>
        /// <param name="characterId">目标角色标识。</param>
        /// <returns>角色武器解析结果；已有武器会直接返回，不会读取默认武器配置。</returns>
        public CharacterWeaponResolutionResult GetOrCreateEquippedWeapon(CharacterId characterId)
        {
            if (!characterId.IsValid)
                return Fail(
                    CharacterWeaponResolutionStatus.InvalidCharacterId,
                    characterId,
                    InventoryOperationStatus.InvalidQuantity);
            if (!characterRosterManager.IsOwned(characterId))
                return Fail(
                    CharacterWeaponResolutionStatus.CharacterNotOwned,
                    characterId,
                    InventoryOperationStatus.CharacterNotOwned);

            // 已有装备是角色当前武器的权威状态；在此分支不能因为默认武器配置异常而覆盖或拒绝现有武器。
            if (weaponInventoryManager.TryGetEquippedWeapon(characterId, out WeaponInstance existingWeapon))
                return new CharacterWeaponResolutionResult(
                    CharacterWeaponResolutionStatus.Succeeded,
                    characterId,
                    existingWeapon,
                    InventoryOperationStatus.Succeeded);

            if (!TryResolveDefaultWeapon(
                    characterId,
                    out ItemId defaultWeaponId,
                    out CharacterWeaponResolutionStatus resolveStatus))
            {
                return Fail(resolveStatus, characterId, InventoryOperationStatus.UnknownDefinition);
            }

            // 缺少装备时才创建默认武器；AddEquippedWeapon 直接写入装备缓存，不参与容纳区容量。
            EquipmentAddResult<WeaponInstance> addResult = weaponInventoryManager.AddEquippedWeapon(
                defaultWeaponId,
                characterId);
            if (!addResult.Succeeded)
            {
                return Fail(
                    CharacterWeaponResolutionStatus.DefaultWeaponOperationFailed,
                    characterId,
                    addResult.Status);
            }

            Debug.Log($"[CharacterEquipmentSystem] 为角色创建默认武器并进入装备缓存，character={characterId}, " +
                      $"weapon={addResult.Instance.InstanceId}, stored={weaponInventoryManager.StoredCount}/" +
                      $"{weaponInventoryManager.Capacity}, equipped={weaponInventoryManager.EquippedCount}。 ");
            return new CharacterWeaponResolutionResult(
                CharacterWeaponResolutionStatus.Succeeded,
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

        #region 事件处理

        /// <summary>响应新角色获得事件并尝试完成一次默认武器装配。</summary>
        /// <param name="ownershipChangedEvent">角色拥有状态变化事件。</param>
        private void HandleCharacterOwnershipChanged(CharacterOwnershipChangedEvent ownershipChangedEvent)
        {
            if (!ownershipChangedEvent.IsOwned)
                return;

            CharacterWeaponResolutionResult result = GetOrCreateEquippedWeapon(
                ownershipChangedEvent.CharacterId);
            if (!result.Succeeded)
            {
                // 角色拥有事实已经由 RosterManager 提交；事件装配失败只记录错误，不回滚角色。
                Debug.LogError($"[CharacterEquipmentSystem] 新角色自动装配武器失败，角色仍保留，" +
                                $"character={ownershipChangedEvent.CharacterId}, status={result.Status}, " +
                                $"weaponStatus={result.WeaponStatus}。 ");
                return;
            }

            Debug.Log($"[CharacterEquipmentSystem] 完成新角色武器自动装配，" +
                      $"character={ownershipChangedEvent.CharacterId}, weapon={result.Weapon.InstanceId}。 ");
        }

        #endregion

        #region 内部校验

        /// <summary>解析角色默认武器类型对应的 Definition，并校验类型一致性。</summary>
        /// <param name="characterId">待解析默认武器的角色标识。</param>
        /// <param name="definitionId">解析出的默认武器 Definition 标识。</param>
        /// <param name="failureStatus">解析失败时的业务状态。</param>
        /// <returns>解析成功时返回 true。</returns>
        private bool TryResolveDefaultWeapon(
            CharacterId characterId,
            out ItemId definitionId,
            out CharacterWeaponResolutionStatus failureStatus)
        {
            definitionId = default(ItemId);
            if (!CharacterConfigManager.Instance.TryGetConfig(characterId, out CharacterConfig config))
            {
                failureStatus = CharacterWeaponResolutionStatus.CharacterNotFound;
                return false;
            }

            config.Validate();
            if (!CharacterConfigManager.Instance.TryGetDefaultWeaponDefinitionId(
                    config.DefaultWeaponType,
                    out definitionId))
            {
                failureStatus = CharacterWeaponResolutionStatus.MissingDefaultWeapon;
                return false;
            }

            if (!ItemManager.Instance.TryGetDefinition(definitionId, out ItemDefinition definition) ||
                !(definition is WeaponDefinition weapon))
            {
                failureStatus = CharacterWeaponResolutionStatus.DefaultWeaponDefinitionInvalid;
                return false;
            }

            if (weapon.WeaponType != config.DefaultWeaponType ||
                !config.AllowsWeaponType(weapon.WeaponType))
            {
                failureStatus = CharacterWeaponResolutionStatus.DefaultWeaponTypeMismatch;
                return false;
            }

            failureStatus = CharacterWeaponResolutionStatus.Succeeded;
            return true;
        }

        /// <summary>记录角色武器解析失败并构造不含武器实例的结果。</summary>
        /// <param name="status">角色武器解析状态。</param>
        /// <param name="characterId">相关角色标识。</param>
        /// <param name="weaponStatus">底层武器操作状态。</param>
        /// <returns>角色武器解析失败结果。</returns>
        private static CharacterWeaponResolutionResult Fail(
            CharacterWeaponResolutionStatus status,
            CharacterId characterId,
            InventoryOperationStatus weaponStatus)
        {
            Debug.LogWarning($"[CharacterEquipmentSystem] 角色武器解析失败，character={characterId}, status={status}, " +
                             $"weaponStatus={weaponStatus}。 ");
            return new CharacterWeaponResolutionResult(status, characterId, null, weaponStatus);
        }

        #endregion
    }

    /// <summary>角色武器解析与默认武器保障的结果状态。</summary>
    public enum CharacterWeaponResolutionStatus
    {
        /// <summary>成功返回现有武器或创建默认武器。</summary>
        Succeeded = 0,
        /// <summary>角色标识无效。</summary>
        InvalidCharacterId,
        /// <summary>角色尚未写入角色拥有状态。</summary>
        CharacterNotOwned,
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

    /// <summary>角色武器解析操作的不可变结果。</summary>
    public readonly struct CharacterWeaponResolutionResult
    {
        /// <summary>创建角色武器解析结果。</summary>
        /// <param name="status">角色武器解析状态。</param>
        /// <param name="characterId">相关角色标识。</param>
        /// <param name="weapon">现有或新创建的装备武器。</param>
        /// <param name="weaponStatus">底层武器操作状态。</param>
        public CharacterWeaponResolutionResult(
            CharacterWeaponResolutionStatus status,
            CharacterId characterId,
            WeaponInstance weapon,
            InventoryOperationStatus weaponStatus)
        {
            Status = status;
            CharacterId = characterId;
            Weapon = weapon;
            WeaponStatus = weaponStatus;
        }

        /// <summary>获取角色武器解析状态。</summary>
        public CharacterWeaponResolutionStatus Status { get; }

        /// <summary>获取相关角色标识。</summary>
        public CharacterId CharacterId { get; }

        /// <summary>获取现有或新创建的装备武器；失败时为空。</summary>
        public WeaponInstance Weapon { get; }

        /// <summary>获取底层武器操作状态。</summary>
        public InventoryOperationStatus WeaponStatus { get; }

        /// <summary>判断是否成功返回或创建角色装备武器。</summary>
        public bool Succeeded => Status == CharacterWeaponResolutionStatus.Succeeded;
    }
}
