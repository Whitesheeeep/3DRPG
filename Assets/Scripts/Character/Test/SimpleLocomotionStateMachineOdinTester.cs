#if UNITY_EDITOR
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.Character
{
    /// <summary>通过 Inspector 手动切换当前角色的简易 Locomotion 状态。</summary>
    public sealed class SimpleLocomotionStateMachineOdinTester : MonoBehaviour
    {
        [SerializeField, Required] private PlayerController playerController;
        [SerializeField] private CharacterLocomotionStateId targetState = CharacterLocomotionStateId.Idle;

        /// <summary>将当前角色 Locomotion FSM 切换到目标状态。</summary>
        [Button]
        private void ChangeState()
        {
            playerController.CharacterManager.ActiveCharacter.Locomotion.ChangeState(targetState);
            Debug.Log($"[LocomotionTester] Current={targetState}", this);
        }
    }
}
#endif
