#if UNITY_EDITOR
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.GameplayAbilitySystem;

namespace RPG.Character
{
    /// <summary>通过 Inspector 手动检查队伍初始化与角色切换结果。</summary>
    public sealed class CharacterManagerOdinTester : MonoBehaviour
    {
        [SerializeField, Required] private PlayerController playerController;
        [SerializeField] private CharacterId targetCharacterId;
        [SerializeField, Range(0, 3)] private int targetSlotIndex;

        /// <summary>输出当前队伍与 ActiveCharacter 快照。</summary>
        [Button]
        private void LogRoster()
        {
            CharacterManager manager = playerController.CharacterManager;
            Debug.Log($"[CharacterManagerTester] Count={manager.Characters.Count}, Active={manager.ActiveCharacter?.name}", this);
        }

        /// <summary>通过 PlayerController 玩家级入口请求切换测试角色。</summary>
        [Button]
        private void SwitchTarget() => Debug.Log(
            $"[CharacterManagerTester] Result={playerController.TrySwitchCharacter(targetCharacterId)}", this);

        /// <summary>通过 PlayerController 玩家级槽位入口请求切换测试角色。</summary>
        [Button]
        private void SwitchSlot() => Debug.Log(
            $"[CharacterManagerTester] Slot={targetSlotIndex + 1}, Result={playerController.TrySwitchCharacterSlot(targetSlotIndex)}", this);

        /// <summary>输出当前角色已授予的 Ability Spec，验证 Binding 初始化是否完成。</summary>
        [Button]
        private void LogActiveCharacterAbilities()
        {
            CharacterActor active = playerController.CharacterManager.ActiveCharacter;
            if (active == null)
            {
                Debug.LogWarning("[CharacterManagerTester] 当前没有 ActiveCharacter。", this);
                return;
            }

            string abilities = string.Empty;
            foreach (GameplayAbilitySpec spec in active.AbilitySystemComponent.GrantedAbilities)
                abilities += $"{spec.Data?.name ?? "<null>"}({spec.Handle}) ";
            Debug.Log($"[CharacterManagerTester] Active={active.name}, GrantedAbilities={abilities}", this);
        }
    }
}
#endif
