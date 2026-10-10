#if UNITY_EDITOR
using System.Collections.Generic;
using RPG.Character;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.GameplayAbilitySystem;

namespace RPG.NPC
{
    /// <summary>通过 Inspector 按钮手动验证 Boss Idle/Move、FullBody 占据与技能释放。</summary>
    [InfoBox("这是仅用于 BossTest 场景的 Editor 手动测试组件，不应添加到生产 NPC Prefab。")]
    public sealed class BossOdinTester : MonoBehaviour
    {
        #region 测试参数与状态

        [SerializeField, Required] private NPCController npcController;
        [SerializeField, MinValue(0), LabelText("NPCConfig 技能索引")]
        private int abilityIndex;

        /// <summary>获取 Inspector 中测试对象当前的状态与动作占据情况。</summary>
        [ShowInInspector, ReadOnly, LabelText("当前状态")]
        private string CurrentStatus => npcController == null || !npcController.IsInitialized
            ? "未初始化"
            : $"{npcController.Locomotion.CurrentState} / FullBodyOccupied={npcController.IsFullBodyActionOccupied} / " +
              $"NextNormalAttack={npcController.CombatSystem.NextAttackIndex + 1} / " +
              $"CurrentAttack={npcController.CombatSystem.CurrentAttackIndex + 1} / " +
              $"AttackRest={npcController.CombatSystem.RecoveryRemaining:F2}s / " +
              $"WindowDecision={npcController.CombatSystem.LastWindowDecision}";

        /// <summary>获取瞬移预检的路径距离、决策原因和失败退避，便于观察 Conditional Abort。</summary>
        [ShowInInspector, ReadOnly, LabelText("瞬移条件")]
        private string TeleportPrecheckStatus => npcController == null || !npcController.IsInitialized
            ? "未初始化"
            : $"Allowed={npcController.LastTeleportPrecheckAllowed} / " +
              $"Path={npcController.LastTeleportPrecheckPathDistance:F2}m / " +
              $"Retry={npcController.TeleportRetryRemaining:F2}s / " +
              $"Reason={npcController.LastTeleportPrecheckReason}";

        /// <summary>读取行为树攻击节点在 NPCCombatSystem 中冻结的策略值。</summary>
        [ShowInInspector, ReadOnly, LabelText("攻击策略快照")]
        private string AttackDecisionStatus => npcController == null || !npcController.IsInitialized
            ? "未初始化"
            : $"Distance={npcController.CombatSystem.AttackDistanceSnapshot:F2}m / " +
              $"Attack3Rest={npcController.CombatSystem.AttackRestDurationSnapshot:F2}s";

        /// <summary>显示最近一次攻击准入阻断原因和实际水平距离。</summary>
        [ShowInInspector, ReadOnly, LabelText("攻击准入")]
        private string AttackReadinessStatus => npcController == null || !npcController.IsInitialized
            ? "未初始化"
            : $"Reason={npcController.LastAttackReadinessReason} / " +
              $"Distance={npcController.LastAttackReadinessDistance:F2}m / " +
              $"Runtime={npcController.CombatSystem.CurrentRuntime?.State.ToString() ?? "None"} / " +
              $"ActivationBlock={npcController.LastNormalAttackActivationBlock}";

        #endregion

        #region 手动操作

        /// <summary>请求 Boss 进入 Idle 状态并播放对应动画。</summary>
        [Button("切换到 Idle"), GUIColor(0.75f, 0.9f, 1f)]
        public void SetIdle()
        {
            bool changed = npcController.TrySetLocomotionState(E_NPCStateId.Idle);
            Debug.Log(
                $"[BossOdinTester] 请求 Boss Idle，changed={changed}，state={npcController.Locomotion.CurrentState}。",
                npcController);
        }

        /// <summary>请求 Boss 进入 Move 状态并播放移动表现动画，不提交实际位移。</summary>
        [Button("切换到 Move"), GUIColor(0.75f, 1f, 0.75f)]
        public void SetMove()
        {
            bool changed = npcController.TrySetLocomotionState(E_NPCStateId.Move);
            Debug.Log(
                $"[BossOdinTester] 请求 Boss Move，changed={changed}，state={npcController.Locomotion.CurrentState}。",
                npcController);
        }

        /// <summary>按 NPCConfig 中的索引激活已授予技能。</summary>
        [Button("激活选择的技能"), GUIColor(1f, 0.85f, 0.6f)]
        public void ActivateSelectedAbility()
        {
            IReadOnlyList<GameplayAbilityData> abilities = npcController.Config.GrantedAbilities;
            if (abilityIndex < 0 || abilityIndex >= abilities.Count)
            {
                Debug.LogError(
                    $"[BossOdinTester] NPCConfig 技能索引越界，index={abilityIndex}，count={abilities.Count}。",
                    npcController);
                return;
            }

            bool activated = npcController.TryActivateAbility(
                abilities[abilityIndex],
                out GameplayAbilityRuntime runtime);
            Debug.Log(
                $"[BossOdinTester] 激活技能 '{abilities[abilityIndex].Name}'，成功={activated}，" +
                $"ActivationId={(runtime != null ? runtime.ActivationId : -1)}。",
                npcController);
        }

        /// <summary>从 NPCCombatSystem 缓存序号启动下一段 Boss 普通攻击。</summary>
        [Button("启动下一段 Boss 普攻"), GUIColor(1f, 0.85f, 0.6f)]
        public void ActivateNextNormalAttack()
        {
            bool activated = npcController.CombatSystem.TryStartNext(out GameplayAbilityRuntime runtime);
            Debug.Log(
                $"[BossOdinTester] 请求启动下一段 Boss 普攻，成功={activated}，" +
                $"ActivationId={runtime?.ActivationId ?? -1}，NextIndex={npcController.CombatSystem.NextAttackIndex}。",
                npcController);
        }

        /// <summary>取消 Boss 当前所有 Active Ability，用于验证 FullBody 占据和运动请求清理。</summary>
        [Button("取消当前技能"), GUIColor(1f, 0.7f, 0.7f)]
        public void CancelActiveAbilities()
        {
            npcController.CancelActiveAbilities();
            Debug.Log(
                $"[BossOdinTester] 请求取消 Boss Active Ability，FullBodyOccupied={npcController.IsFullBodyActionOccupied}。",
                npcController);
        }

        #endregion
    }
}
#endif
