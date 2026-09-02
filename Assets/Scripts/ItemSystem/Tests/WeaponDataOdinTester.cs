#if UNITY_EDITOR
using RPG.Character;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.ItemSystem.Tests
{
    /// <summary>通过 Odin Inspector 手动检查武器新增状态契约。</summary>
    public sealed class WeaponDataOdinTester : MonoBehaviour
    {
        /// <summary>检查默认角色标识、有效角色标识和装备删除状态枚举。</summary>
        [Button("检查武器数据契约")]
        public void CheckWeaponDataContracts()
        {
            CharacterId empty = default(CharacterId);
            CharacterId equipped = new CharacterId("character.test");
            bool valid = !empty.IsValid && equipped.IsValid &&
                         InventoryOperationStatus.InstanceEquipped != InventoryOperationStatus.Succeeded;
            Debug.Log($"[WeaponDataTest] emptyValid={empty.IsValid}, equippedValid={equipped.IsValid}, contract={valid}", this);
        }
    }
}
#endif
