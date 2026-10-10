using BehaviorDesigner.Runtime;
using BehaviorDesigner.Runtime.Tasks;
using UnityEngine;
using WS_Modules.GAS.GameplayAbilitySystem;

namespace RPG.NPC.BehaviourTree
{
    /// <summary>启动缓存普攻并消费 SkillRuntimeHost 在取消窗口入口记录的一次续接机会。</summary>
    [TaskCategory("RPG/NPC")]
    [TaskDescription("只在窗口进入时判定一次；未续接则结束当前攻击并让行为树重新判断。第三段自然结束后等待配置休息时间。")]
    public sealed class ExecuteNpcNormalAttack : Action
    {
        #region 黑板与运行依赖

        /// <summary>与发现、导航节点共用的玩家黑板根。</summary>
        public SharedTransform target;
        /// <summary>与导航和瞬移路径查询共用的攻击水平距离。</summary>
        public SharedFloat attackDistance;
        [UnityEngine.Tooltip("第三段自然结束后，CombatSystem 持有的缩放时间休息时长。"), Sirenix.OdinInspector.MinValue(0f)]
        public float thirdAttackRestDuration = 0.8f;
        private NPCController controller;
        private GameplayAbilityRuntime trackedRuntime;
        private bool hasAttackDecisionConfiguration;
        private int lastReportedCancelledActivationId = -1;

        #endregion

        #region 行为树生命周期

        /// <summary>捕获仍在运行或等待自然结束信号的普攻 Runtime；已取消执行留给新节点周期重试。</summary>
        public override void OnStart()
        {
            GameObject ownerObject = Owner != null ? Owner.gameObject : null;
            controller = ownerObject != null ? ownerObject.GetComponent<NPCController>() : null;
            GameplayAbilityRuntime currentRuntime = controller != null
                ? controller.CombatSystem.CurrentRuntime
                : null;
            trackedRuntime = currentRuntime != null && currentRuntime.State != GameplayAbilityRuntimeState.Cancelled
                ? currentRuntime
                : null;
            hasAttackDecisionConfiguration = controller != null && attackDistance != null &&
                                             IsFinitePositive(attackDistance.Value) &&
                                             IsFiniteNonNegative(thirdAttackRestDuration);
            if (hasAttackDecisionConfiguration)
            {
                controller.CombatSystem.ConfigureAttackDecision(attackDistance.Value, thirdAttackRestDuration);
                controller.CombatSystem.SetAttackTarget(target != null ? target.Value : null);
            }
            else if (controller != null)
                Debug.LogError(
                    $"[ExecuteNpcNormalAttack] 行为树攻击策略配置无效，" +
                    $"AttackDistance={attackDistance?.Value ?? 0f}，Attack3Rest={thirdAttackRestDuration}。",
                    controller);

            if (currentRuntime != null && currentRuntime.State == GameplayAbilityRuntimeState.Cancelled &&
                lastReportedCancelledActivationId != currentRuntime.ActivationId)
            {
                lastReportedCancelledActivationId = currentRuntime.ActivationId;
                Debug.Log(
                    $"[ExecuteNpcNormalAttack] Boss '{controller.name}' 上一段攻击已取消，" +
                    $"不再跟踪终止 Runtime，将重新检查并启动缓存段位。",
                    controller);
            }
        }

        /// <summary>跟踪当前 GA，消费单次窗口机会，并在自然结束后交还决策权给行为树。</summary>
        /// <returns>攻击运行或第三段休息时返回 Running；攻击结束时返回 Success。</returns>
        public override TaskStatus OnUpdate()
        {
            if (controller == null || target == null || !hasAttackDecisionConfiguration)
                return TaskStatus.Failure;

            NPCCombatSystem combat = controller.CombatSystem;
            Transform currentTarget = target.Value;
            if (trackedRuntime == null)
            {
                combat.SetAttackTarget(currentTarget);
                if (!controller.IsReadyToAttack(currentTarget, attackDistance.Value))
                    return TaskStatus.Failure;
                return TryStartCachedAttack(combat);
            }

            if (trackedRuntime.State == GameplayAbilityRuntimeState.Active)
            {
                combat.SetAttackTarget(
                    controller.IsValidPlayerTarget(currentTarget) ? currentTarget : null);
                if (!ReferenceEquals(trackedRuntime, combat.CurrentRuntime))
                    return TaskStatus.Failure;

                if (combat.TryConsumeHandoffOpportunity(out GameplayAbilityRuntime nextRuntime))
                    trackedRuntime = nextRuntime;
                return TaskStatus.Running;
            }

            if (trackedRuntime.State == GameplayAbilityRuntimeState.Cancelled)
            {
                Debug.Log(
                    $"[ExecuteNpcNormalAttack] Boss '{controller.name}' 当前攻击 Runtime 已取消，" +
                    $"ActivationId={trackedRuntime.ActivationId}，交还行为树重新决策。",
                    controller);
                return TaskStatus.Failure;
            }

            if (trackedRuntime.State == GameplayAbilityRuntimeState.Ended)
            {
                // 第一、二段自然结束后立刻结束本节点；第三段的同一信号要等全局休息计时完成。
                if (combat.HasPendingAttackCompletion)
                {
                    if (!combat.IsReadyForNextAttack)
                        return TaskStatus.Running;
                    return combat.ConsumeAttackCompletion()
                        ? TaskStatus.Success
                        : TaskStatus.Running;
                }

                combat.SetAttackTarget(currentTarget);
                if (!controller.IsReadyToAttack(currentTarget, attackDistance.Value))
                    return TaskStatus.Failure;
                if (!combat.IsReadyForNextAttack)
                    return TaskStatus.Running;
                return TryStartCachedAttack(combat);
            }

            return TaskStatus.Failure;
        }

        /// <summary>清理节点引用与目标快照，不取消由 GAS 管理的当前普通攻击。</summary>
        public override void OnEnd()
        {
            if (controller != null)
                controller.CombatSystem.SetAttackTarget(null);
            controller = null;
            trackedRuntime = null;
            hasAttackDecisionConfiguration = false;
        }

        /// <summary>在行为树资产重置时清除本节点运行快照。</summary>
        public override void OnReset()
        {
            target = null;
            controller = null;
            trackedRuntime = null;
            hasAttackDecisionConfiguration = false;
            lastReportedCancelledActivationId = -1;
        }

        #endregion

        #region 攻击启动

        /// <summary>按角色级 Combo 缓存启动下一段，并记录用于后续跟踪的 Runtime。</summary>
        /// <param name="combat">保存段位、缓存和自然结束信号的角色级系统。</param>
        /// <returns>成功启动时返回 Running；激活失败时返回 Failure。</returns>
        private TaskStatus TryStartCachedAttack(NPCCombatSystem combat)
        {
            if (!combat.TryStartNext(out GameplayAbilityRuntime startedRuntime))
            {
                controller.ReportNormalAttackActivationResult(combat.NextAttackIndex, false);
                return TaskStatus.Failure;
            }

            controller.ReportNormalAttackActivationResult(combat.CurrentAttackIndex, true);
            trackedRuntime = startedRuntime;
            return TaskStatus.Running;
        }

        /// <summary>判断攻击距离是否为有限正数。</summary>
        /// <param name="value">树共享的水平攻击距离。</param>
        /// <returns>大于零且有限时返回 true。</returns>
        private static bool IsFinitePositive(float value) =>
            value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);

        /// <summary>判断第三段休息时长是否为有限非负值。</summary>
        /// <param name="value">节点配置的缩放时间。</param>
        /// <returns>不小于零且有限时返回 true。</returns>
        private static bool IsFiniteNonNegative(float value) =>
            value >= 0f && !float.IsNaN(value) && !float.IsInfinity(value);

        #endregion
    }
}
