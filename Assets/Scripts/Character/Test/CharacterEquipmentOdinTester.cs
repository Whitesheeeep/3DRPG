#if UNITY_EDITOR
using System.Collections.Generic;
using RPG.Game;
using RPG.ItemSystem;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.Character
{
    /// <summary>通过 Odin Inspector 手动验证角色获取、默认武器与武器双分区容量契约。</summary>
    [InfoBox("依赖 GameArchitectureStartup、CharacterDatabaseConfigProvider、ItemDatabaseConfigProvider 和 WeaponInventorySettingsProvider 已完成初始化；按钮只调用正式角色与武器业务 API。")]
    public sealed class CharacterEquipmentOdinTester : MonoBehaviour
    {
        #region 测试参数

        [SerializeField, CharacterIdDropdown, LabelText("测试角色")] private CharacterId targetCharacterId;

        #endregion

        #region 手动操作

        /// <summary>通过正式角色获取入口生成角色及其装备缓存区默认武器。</summary>
        [Button("获取角色并生成默认武器")]
        public void AcquireTargetCharacter()
        {
            if (!TryGetSystem(out CharacterEquipmentSystem system)) return;
            CharacterAcquisitionResult result = system.AcquireCharacter(targetCharacterId);
            Debug.Log($"[CharacterEquipmentTester] acquire status={result.Status}, character={targetCharacterId}, " +
                      $"weapon={result.Weapon?.InstanceId.ToString() ?? "<none>"}, weaponStatus={result.WeaponStatus}。", this);
            LogPartitionSnapshot();
        }

        /// <summary>输出角色拥有数量和武器容纳区、装备缓存区的独立统计。</summary>
        [Button("输出角色与武器分区")]
        public void LogPartitionSnapshot()
        {
            if (!TryGetSystem(out CharacterEquipmentSystem system)) return;
            CharacterRosterManager rosterManager = GameArchitecture.Interface.GetManager<CharacterRosterManager>();
            WeaponInventoryManager weaponManager = GameArchitecture.Interface.GetManager<WeaponInventoryManager>();
            bool hasWeapon = system.TryGetEquippedWeapon(targetCharacterId, out WeaponInstance equippedWeapon);
            Debug.Log($"[CharacterEquipmentTester] owned={rosterManager.GetOwnedCharacterIds().Count}, " +
                      $"weaponTotal={weaponManager.TotalCount}, stored={weaponManager.StoredCount}/{weaponManager.Capacity}, " +
                      $"equipped={weaponManager.EquippedCount}, targetWeapon={equippedWeapon?.InstanceId.ToString() ?? "<none>"}, " +
                      $"targetHasWeapon={hasWeapon}。", this);
        }

        /// <summary>将目标角色武器移回容纳区，验证容量不足时保持装备状态。</summary>
        [Button("卸下目标角色武器")]
        public void UnequipTargetCharacter()
        {
            if (!TryGetSystem(out CharacterEquipmentSystem system)) return;
            EquipmentOperationResult result = system.UnequipWeapon(targetCharacterId);
            Debug.Log($"[CharacterEquipmentTester] unequip status={result.Status}, character={targetCharacterId}。", this);
            LogPartitionSnapshot();
        }

        /// <summary>把容纳区中获得顺序最早的武器装备给目标角色，验证换装不改变容纳区总数。</summary>
        [Button("装备容纳区首把武器")]
        public void EquipFirstStoredWeapon()
        {
            if (!TryGetSystem(out CharacterEquipmentSystem system)) return;
            IReadOnlyList<WeaponInstance> storedInstances =
                GameArchitecture.Interface.GetManager<WeaponInventoryManager>().GetStoredInstances();
            if (storedInstances.Count == 0)
            {
                Debug.LogWarning("[CharacterEquipmentTester] 容纳区没有可装备武器。", this);
                return;
            }

            EquipmentOperationResult result = system.EquipWeapon(targetCharacterId, storedInstances[0].InstanceId);
            Debug.Log($"[CharacterEquipmentTester] equip status={result.Status}, character={targetCharacterId}, " +
                      $"instance={storedInstances[0].InstanceId}。", this);
            LogPartitionSnapshot();
        }

        #endregion

        #region 前置条件

        /// <summary>读取已经由 GameArchitecture 注册的角色装备 System。</summary>
        /// <param name="system">找到的角色装备 System。</param>
        /// <returns>架构已经初始化并找到 System 时返回 true。</returns>
        private static bool TryGetSystem(out CharacterEquipmentSystem system)
        {
            system = GameArchitecture.Interface.GetSystem<CharacterEquipmentSystem>();
            return system != null;
        }

        #endregion
    }
}
#endif
