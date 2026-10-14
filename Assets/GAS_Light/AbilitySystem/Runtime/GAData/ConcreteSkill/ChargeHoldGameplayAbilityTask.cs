using System;
using UnityEngine;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>保持准备动画冻结时的姿态，并等待当前玩家手势松开。</summary>
    public sealed class ChargeHoldGameplayAbilityTask : GameplayAbilityTask
    {
        #region 构造

        /// <summary>创建只负责本次蓄力等待的 Task，不在构造期间改变角色动画。</summary>
        /// <param name="runtime">拥有该 Task 的异步 Ability Runtime。</param>
        public ChargeHoldGameplayAbilityTask(AsynchronousGameplayAbilityRuntime runtime)
            : base(runtime)
        {
        }

        #endregion

        #region 生命周期

        /// <summary>已经松开时同步跳过蓄力；否则保持准备阶段冻结姿态并开始等待。</summary>
        protected override void OnStart()
        {
            ChargedDashGameplayAbilityRuntime runtime = GetChargedDashRuntime();
            if (runtime.HasReleased)
            {
                // 准备阶段松开不进入蓄力等待，Sequence 会在当前调用栈同步启动释放 Task。
                Debug.Log(
                    $"[ChargeHoldGameplayAbilityTask] ActivationId={GARuntime.ActivationId} " +
                    "准备期间已松开，跳过蓄力等待。",
                    GARuntime.SourceASC);
                Complete();
                return;
            }

            // Preparation Task 已在 End Event 中冻结 Action State；保留它作为蓄力姿态和释放动画的淡出来源。
            runtime.MarkChargeWaitEntered();
            Debug.Log(
                $"[ChargeHoldGameplayAbilityTask] ActivationId={GARuntime.ActivationId} " +
                "保持准备结束时的冻结姿态并开始蓄力等待。",
                GARuntime.SourceASC);
        }

        #endregion

        #region 阶段推进

        /// <summary>收到本次手势 Release 后完成 Task，交由 Sequence 立即启动释放阶段。</summary>
        /// <param name="deltaTime">本次缩放技能推进时间。</param>
        protected override void OnTick(float deltaTime)
        {
            if (!GetChargedDashRuntime().HasReleased)
                return;

            Debug.Log(
                $"[ChargeHoldGameplayAbilityTask] ActivationId={GARuntime.ActivationId} " +
                "收到本次手势释放，开始释放阶段。",
                GARuntime.SourceASC);
            Complete();
        }

        #endregion

        #region 内部辅助

        /// <summary>取得与本 Task 配置匹配的蓄力专用 Runtime。</summary>
        /// <returns>当前激活 Runtime。</returns>
        /// <exception cref="InvalidOperationException">Task 被其他异步 Ability Data 错误复用时抛出。</exception>
        private ChargedDashGameplayAbilityRuntime GetChargedDashRuntime() =>
            GARuntime as ChargedDashGameplayAbilityRuntime ??
            throw new InvalidOperationException("ChargeHold Task 需要 ChargedDashGameplayAbilityRuntime。");

        #endregion
    }
}
