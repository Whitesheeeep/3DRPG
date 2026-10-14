using System;
using Animancer;
using RPG.Character.Animation;
using UnityEngine;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>播放准备 Transition，并在其 End Event 触发时冻结当前姿态。</summary>
    public sealed class ChargePreparationGameplayAbilityTask : GameplayAbilityTask
    {
        #region 配置快照

        private readonly TransitionAsset preparationTransition;

        #endregion

        #region 事件与运行时状态

        // 该 State 与事件序列属于本次激活；Transition 资产只提供可复用的事件时间配置。
        private AnimancerState preparationAnimationState;
        private AnimancerEvent.Sequence preparationAnimationEvents;

        #endregion

        #region 构造

        /// <summary>保存准备动画配置，不在 Task 构造时播放动画或注册事件。</summary>
        /// <param name="runtime">拥有该 Task 的异步 Runtime。</param>
        /// <param name="transition">包含准备动画、播放参数和结束事件的 Transition。</param>
        public ChargePreparationGameplayAbilityTask(
            AsynchronousGameplayAbilityRuntime runtime,
            TransitionAsset transition)
            : base(runtime)
        {
            preparationTransition = transition ?? throw new ArgumentNullException(nameof(transition));
        }

        #endregion

        #region 生命周期

        /// <summary>取得共享控制权，播放准备 Transition 并绑定本次 State 的结束回调。</summary>
        protected override void OnStart()
        {
            ChargedDashGameplayAbilityRuntime runtime = GetChargedDashRuntime();
            try
            {
                runtime.AcquireControlLease();
                preparationAnimationState = runtime.SourceOwner.AnimationPlayer.Play(
                    AnimationLayerType.Action, preparationTransition.GetTransition());
                runtime.ControlLease.SetPreparationState(preparationAnimationState);

                // ref 会从 Transition 创建本次激活独立的事件序列，避免修改共享资产或其他角色的回调。
                preparationAnimationState.Events(ref preparationAnimationEvents);
                preparationAnimationEvents.OnEnd += OnPreparationEnd;
                Debug.Log(
                    $"[ChargePreparationGameplayAbilityTask] ActivationId={GARuntime.ActivationId} " +
                    $"开始播放准备动画，transition={preparationTransition.name}。",
                    GARuntime.SourceASC);
            }
            catch (Exception exception)
            {
                UnregisterPreparationEnd();
                runtime.ReleaseControlLease();
                Debug.LogError(
                    $"[ChargePreparationGameplayAbilityTask] ActivationId={GARuntime.ActivationId} " +
                    $"启动准备动画失败，reason={exception.Message}。",
                    GARuntime.SourceASC);
                throw;
            }
        }

        /// <summary>动画结束事件到达时停止当前状态并同步完成准备阶段。</summary>
        protected override void OnComplete()
        {
            UnregisterPreparationEnd();
        }

        /// <summary>Task 被正常停止时解绑准备动画结束事件。</summary>
        protected override void OnStop()
        {
            UnregisterPreparationEnd();
        }

        /// <summary>Task 被取消时解绑准备动画结束事件。</summary>
        protected override void OnCancel()
        {
            UnregisterPreparationEnd();
        }

        #endregion

        #region 事件处理

        /// <summary>仅在准备 Task 仍运行时冻结当前已求值姿态并推进 Sequence。</summary>
        private void OnPreparationEnd()
        {
            if (State != GameplayAbilityTaskState.Running)
                return;

            // End Event 的触发可能晚于标记所在帧；保留当前已求值姿态，不回写时间或强制重求动画图。
            preparationAnimationState.Speed = 0f;
            UnregisterPreparationEnd();
            Debug.Log(
                $"[ChargePreparationGameplayAbilityTask] ActivationId={GARuntime.ActivationId} " +
                "准备结束事件触发，已冻结当前姿态。",
                GARuntime.SourceASC);
            Complete();
        }

        /// <summary>解绑本次 State 的 End Event，并清空事件序列引用。</summary>
        private void UnregisterPreparationEnd()
        {
            if (preparationAnimationEvents == null)
                return;

            preparationAnimationEvents.OnEnd -= OnPreparationEnd;
            preparationAnimationEvents = null;
            Debug.Log(
                $"[ChargePreparationGameplayAbilityTask] ActivationId={GARuntime.ActivationId} " +
                "已解绑准备动画结束事件。",
                GARuntime.SourceASC);
        }

        #endregion

        #region 内部校验

        /// <summary>取得与本 Task 配置匹配的蓄力专用 Runtime。</summary>
        /// <returns>当前激活 Runtime。</returns>
        /// <exception cref="InvalidOperationException">Task 被其他异步 Ability Data 错误复用时抛出。</exception>
        private ChargedDashGameplayAbilityRuntime GetChargedDashRuntime() =>
            GARuntime as ChargedDashGameplayAbilityRuntime ??
            throw new InvalidOperationException("ChargePreparation Task 需要 ChargedDashGameplayAbilityRuntime。");

        #endregion
    }
}
