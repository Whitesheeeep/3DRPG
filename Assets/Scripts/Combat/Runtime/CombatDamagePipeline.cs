using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.GameplayEffect;

namespace RPG.Combat
{
    /// <summary>在 Gameplay 层协调 GE 即时结算、命中上下文关联及受击 Receiver 生命周期。</summary>
    public static class CombatDamagePipeline
    {
        #region 运行时路由

        // key：目标 ASC；value：该 ASC 当前唯一的 Gameplay 反馈 Receiver 与 GE 事件作用域。
        private static readonly Dictionary<GameplayAbilitySystemComponent, ReceiverBinding>
            receiverBindingByTargetASCMap = new();

        #endregion

        #region 公开操作

        /// <summary>注册已准备完成的目标 ASC 与其 Gameplay 受击处理者。</summary>
        /// <param name="target">需要接入 Gameplay 伤害反馈的 ASC。</param>
        /// <param name="receiver">负责该 ASC 伤害反馈的业务对象。</param>
        /// <returns>解除事件订阅和路由关系的生命周期句柄。</returns>
        /// <exception cref="ArgumentException">ASC 未初始化或 Receiver 无效时抛出。</exception>
        /// <exception cref="InvalidOperationException">目标 ASC 已有其他 Receiver 注册时抛出。</exception>
        public static IDisposable RegisterReceiver(
            GameplayAbilitySystemComponent target,
            ICombatDamageReceiver receiver)
        {
            if (target == null || !target.IsInitialized || receiver == null)
                throw new ArgumentException("伤害 Receiver 注册需要已初始化 ASC 与有效 Receiver。");
            if (receiverBindingByTargetASCMap.ContainsKey(target))
                throw new InvalidOperationException(
                    $"Target ASC '{target.name}' 已注册 GameplayDamageReceiver，禁止重复绑定。");

            var binding = new ReceiverBinding(target, receiver);
            receiverBindingByTargetASCMap.Add(target, binding);
            binding.Subscribe();
            Debug.Log(
                $"[CombatDamagePipeline] 已注册目标伤害 Receiver，targetASC='{target.name}'，receiver='{receiver.GetType().Name}'。",
                target);
            return binding;
        }

        /// <summary>向快照中已解析的目标 ASC 应用一个 GE，并将同步执行结果关联到该命中。</summary>
        /// <param name="spec">本次目标伤害使用的已准备 GE Spec。</param>
        /// <param name="snapshot">应用前已经固定来源、目标与空间信息的命中快照。</param>
        /// <param name="applicationResult">GE 应用成功时返回的 GAS 原始结果。</param>
        /// <returns>GE 成功应用时返回 true。</returns>
        /// <exception cref="ArgumentNullException">Spec 为空时抛出。</exception>
        /// <exception cref="InvalidOperationException">快照、Spec 来源或 Receiver 注册违反管线契约时抛出。</exception>
        public static bool TryApplyHitEffect(
            GameplayEffectSpec spec,
            CombatHitSnapshot snapshot,
            out GameplayEffectApplicationResult applicationResult)
        {
            applicationResult = null;
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            if (snapshot.Target == null || snapshot.Source == null)
                throw new InvalidOperationException("命中快照必须包含有效 Source 与 Target ASC。");
            if (!ReferenceEquals(spec.Source, snapshot.Source))
                throw new InvalidOperationException("GE Spec.Source 与命中快照 Source 不一致。");
            if (!receiverBindingByTargetASCMap.TryGetValue(snapshot.Target, out ReceiverBinding binding))
                throw new InvalidOperationException(
                    $"Target ASC '{snapshot.Target.name}' 尚未注册 GameplayDamageReceiver，拒绝提交伤害 GE。");

            var scope = new HitApplicationScope(spec, snapshot);
            binding.PushScope(scope);
            try
            {
                return snapshot.Target.TryApplyEffect(spec, out applicationResult);
            }
            finally
            {
                // GE 的 EffectExecuted 在 TryApplyEffect 内同步发送；此处按返回 Result 引用关联本次快照。
                binding.PopScope(scope);
                binding.DeliverCompletedScope(scope, applicationResult);
            }
        }

        #endregion

        #region 进入 Play 生命周期重置

        /// <summary>清理禁用 Domain Reload 时可能跨 Play Session 保留的静态订阅。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            ReceiverBinding[] existingBindings = receiverBindingByTargetASCMap.Values.ToArray();
            for (int index = 0; index < existingBindings.Length; index++)
                existingBindings[index].Dispose();
            receiverBindingByTargetASCMap.Clear();
            if (existingBindings.Length > 0)
                Debug.Log($"[CombatDamagePipeline] 新 Play Session 已清理旧 Receiver 订阅，count={existingBindings.Length}。");
        }

        #endregion

        #region 内部事件与结算辅助

        /// <summary>把 GE 输出中的每个正伤害 Execution 转成独立 Gameplay Result 并同步分发。</summary>
        /// <param name="binding">目标 ASC 当前的 Receiver 注册。</param>
        /// <param name="effectResult">GAS 发布的原始结算结果。</param>
        /// <param name="hitSnapshot">匹配本次命中应用时提供的快照；直接或周期执行为空。</param>
        private static void DeliverDamageResults(
            ReceiverBinding binding,
            GameplayEffectApplicationResult effectResult,
            CombatHitSnapshot? hitSnapshot)
        {
            IReadOnlyList<GameplayEffectExecutionResult> executionResults =
                effectResult.CalculationOutput.ExecutionResults;
            for (int index = 0; index < executionResults.Count; index++)
            {
                if (executionResults[index] is not BasicDamageGameplayEffectExecutionResult damage ||
                    damage.FinalDamage <= 0f)
                    continue;

                var damageResult = new CombatDamageResult(effectResult, hitSnapshot, damage);
                binding.Receiver.ReceiveDamage(damageResult);
                Debug.Log(
                    $"[CombatDamagePipeline] 已交付伤害结算，targetASC='{effectResult.Target.name}'，" +
                    $"GE='{effectResult.Spec.Data.name}'，damage={damage.FinalDamage:F1}，" +
                    $"critical={damage.IsCritical}，hasHitSnapshot={hitSnapshot.HasValue}。",
                    effectResult.Target);
            }
        }

        #endregion

        #region 嵌套路由类型

        /// <summary>维护单一目标 ASC 的事件订阅、同步应用栈与 Receiver 注册。</summary>
        private sealed class ReceiverBinding : IDisposable
        {
            #region 依赖与运行时状态

            // 订阅本目标 ASC 的 GE 执行；栈顶作用域只用于同步关联某一次命中应用。
            private readonly GameplayAbilitySystemComponent target;
            private readonly List<HitApplicationScope> hitApplicationScopes = new();
            private bool subscribed;

            #endregion

            #region 属性

            /// <summary>获取该 ASC 唯一注册的 Gameplay Receiver。</summary>
            internal ICombatDamageReceiver Receiver { get; }

            #endregion

            #region 构造与生命周期

            /// <summary>创建目标 ASC 与业务 Receiver 的同步事件绑定。</summary>
            /// <param name="target">被监听的 GE Target ASC。</param>
            /// <param name="receiver">接收正伤害 Gameplay Result 的业务对象。</param>
            internal ReceiverBinding(GameplayAbilitySystemComponent target, ICombatDamageReceiver receiver)
            {
                this.target = target;
                Receiver = receiver;
            }

            /// <summary>订阅目标 GE 的实际执行通知。</summary>
            internal void Subscribe()
            {
                if (subscribed) throw new InvalidOperationException("ReceiverBinding 已经订阅 GE 事件。");
                target.GameEffectCtrl.EffectExecuted += OnEffectExecuted;
                subscribed = true;
            }

            /// <summary>解除本次 ASC 事件订阅与静态注册，并释放尚未完成的同步作用域。</summary>
            public void Dispose()
            {
                if (!subscribed) return;
                target.GameEffectCtrl.EffectExecuted -= OnEffectExecuted;
                subscribed = false;
                hitApplicationScopes.Clear();
                if (receiverBindingByTargetASCMap.TryGetValue(target, out ReceiverBinding current) &&
                    ReferenceEquals(current, this))
                    receiverBindingByTargetASCMap.Remove(target);
                Debug.Log(
                    $"[CombatDamagePipeline] 已解除目标伤害 Receiver，targetASC='{target.name}'，receiver='{Receiver.GetType().Name}'。",
                    target);
            }

            #endregion

            #region GE 应用作用域

            /// <summary>压入当前命中应用；嵌套提交各自保有独立快照。</summary>
            /// <param name="scope">当前 Spec 与命中快照。</param>
            internal void PushScope(HitApplicationScope scope) => hitApplicationScopes.Add(scope);

            /// <summary>按严格后进先出顺序关闭当前命中应用作用域。</summary>
            /// <param name="scope">刚完成提交的作用域。</param>
            internal void PopScope(HitApplicationScope scope)
            {
                int lastIndex = hitApplicationScopes.Count - 1;
                if (lastIndex < 0 || !ReferenceEquals(hitApplicationScopes[lastIndex], scope))
                    throw new InvalidOperationException("GE 命中作用域没有按嵌套顺序关闭。");
                hitApplicationScopes.RemoveAt(lastIndex);
            }

            /// <summary>结束当前应用后，依据应用返回的同一 Result 实例恢复快照关联。</summary>
            /// <param name="scope">刚刚退出的同步命中应用。</param>
            /// <param name="applicationResult">TryApplyEffect 返回的 GE 结果，可能为空。</param>
            internal void DeliverCompletedScope(
                HitApplicationScope scope,
                GameplayEffectApplicationResult applicationResult)
            {
                for (int index = 0; index < scope.ExecutedResults.Count; index++)
                {
                    GameplayEffectApplicationResult executedResult = scope.ExecutedResults[index];
                    CombatHitSnapshot? hitSnapshot = ReferenceEquals(executedResult, applicationResult)
                        ? scope.Snapshot
                        : null;
                    DeliverDamageResults(this, executedResult, hitSnapshot);
                }
            }

            #endregion

            #region GE 事件

            /// <summary>收集属于当前命中 Spec 的同步结果，其余结算作为无命中 Gameplay 伤害处理。</summary>
            /// <param name="effectResult">GAS 刚完成的实际 GE Execution。</param>
            private void OnEffectExecuted(GameplayEffectApplicationResult effectResult)
            {
                if (hitApplicationScopes.Count > 0)
                {
                    HitApplicationScope scope = hitApplicationScopes[^1];
                    if (ReferenceEquals(scope.Spec, effectResult.Spec))
                    {
                        scope.ExecutedResults.Add(effectResult);
                        return;
                    }
                }

                // 直接 GE 与周期 Tick 没有本次攻击几何信息，绝不复用旧命中快照。
                DeliverDamageResults(this, effectResult, null);
            }

            #endregion
        }

        /// <summary>保存一个尚未退出 TryApplyHitEffect 的 Spec 与命中快照。</summary>
        private sealed class HitApplicationScope
        {
            #region 状态

            /// <summary>获取本次提交的 Spec 身份。</summary>
            internal GameplayEffectSpec Spec { get; }

            /// <summary>获取本次提交固定的命中快照。</summary>
            internal CombatHitSnapshot Snapshot { get; }

            /// <summary>保存同步通知到达的所有 Result，返回时按引用身份恢复快照归属。</summary>
            internal List<GameplayEffectApplicationResult> ExecutedResults { get; } = new();

            #endregion

            #region 构造

            /// <summary>创建独立嵌套安全的 GE 应用关联作用域。</summary>
            /// <param name="spec">本次正在提交的 Spec。</param>
            /// <param name="snapshot">本次命中的稳定快照。</param>
            internal HitApplicationScope(GameplayEffectSpec spec, CombatHitSnapshot snapshot)
            {
                Spec = spec;
                Snapshot = snapshot;
            }

            #endregion
        }

        #endregion
    }
}
