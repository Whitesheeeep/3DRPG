#if UNITY_EDITOR
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.Character
{
    /// <summary>通过 Inspector 手动检查队伍初始化与角色切换结果。</summary>
    public sealed class CharacterManagerOdinTester : MonoBehaviour
    {
        [SerializeField, Required] private PlayerController playerController;
        [SerializeField] private CharacterId targetCharacterId;

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
    }
}
#endif
