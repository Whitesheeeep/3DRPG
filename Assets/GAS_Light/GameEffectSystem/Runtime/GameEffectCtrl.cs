using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.GameplayAbilitySystem;
using WS_Modules.GAS.GameplayCue;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayEffect
{
    /// <summary>为单个目标 ASC 协调 GE Spec 校验、计算、叠层、计时与移除。</summary>
    public sealed class GameEffectCtrl : IGameEffectCtrl
    {
        #region 字段

        private readonly List<GameEffectRuntime> activeEffects = new();

        #endregion

        #region 属性与事件

        /// <inheritdoc />
        public event Action<GameEffectRuntime> EffectRemoved;

        /// <inheritdoc />
        public event Action<GameplayEffectApplicationResult> EffectExecuted;

        /// <inheritdoc />
        public GameplayAbilitySystemComponent Owner { get; }

        /// <inheritdoc />
        public IReadOnlyList<GameEffectRuntime> ActiveEffects => activeEffects;

        /// <summary>创建只服务指定 Target ASC 的 GE Controller。</summary>
        /// <param name="owner">作为所有应用隐式 Target 的 ASC。</param>
        public GameEffectCtrl(GameplayAbilitySystemComponent owner)
        {
            Owner = owner;
        }

        #endregion

        #region 公开操作

        /// <inheritdoc />
        public bool CanApply(GameplayEffectSpec spec) =>
            spec != null &&
            Owner != null &&
            spec.Source != null &&
            spec.Data != null &&
            spec.Data.TryValidateApplicationConfiguration() &&
            spec.Data.TargetTagQuery.Matches(Owner.Tags) &&
            spec.Data.GrantedTags.All(tag => GameplayTagManager.Instance.IsValidTag(tag));

        /// <summary>检查 Data 快捷入口的基础 Target 条件。</summary>
        /// <param name="data">待应用的 GE 配置。</param>
        /// <param name="source">效果来源 ASC。</param>
        /// <returns>基础配置和目标条件合法时返回 true。</returns>
        public bool CanApply(GameplayEffectData data, GameplayAbilitySystemComponent source) =>
            data != null && source != null && Owner != null &&
            data.TryValidateApplicationConfiguration() &&
            data.TargetTagQuery.Matches(Owner.Tags) &&
            data.GrantedTags.All(tag => GameplayTagManager.Instance.IsValidTag(tag));

        /// <inheritdoc />
        public bool TryApply(
            GameplayEffectSpec spec,
            out GameplayEffectApplicationResult result)
        {
            result = null;
            if (spec == null || !spec.TrySeal() || !CanApply(spec)) return false;

            if (spec.Data.DurationType == E_GameEffectDurationType.Instant)
                return ApplyInstant(spec, out result);

            GameEffectRuntime existing = FindStackableRuntime(spec.Data, spec.Source);
            return existing == null
                ? CreateActiveEffect(spec, out result)
                : ApplyStack(existing, spec, out result);
        }

        /// <inheritdoc />
        public bool TryApply(
            GameplayEffectData data,
            GameplayAbilitySystemComponent source,
            int level,
            IReadOnlyDictionary<GameplayTag, float> setByCaller,
            out GameEffectRuntime activeEffect)
        {
            activeEffect = null;
            if (level < 1 || !CanApply(data, source)) return false;
            // 兼容入口仍由 Source ASC 统一创建 Spec，保证 Source 绑定和 SetByCaller 复制策略只有一处。
            if (!source.TryCreateOutgoingEffectSpec(
                    data,
                    level,
                    setByCaller,
                    out GameplayEffectSpec spec) ||
                !spec.TrySeal() ||
                !TryApply(spec, out GameplayEffectApplicationResult result))
                return false;
            activeEffect = result.ActiveEffect;
            return true;
        }

        /// <inheritdoc />
        public bool HasActiveEffect(GameplayEffectData data)
        {
            if (data == null) return false;
            for (int i = 0; i < activeEffects.Count; i++)
                if (ReferenceEquals(activeEffects[i].Data, data))
                    return true;
            return false;
        }

        /// <inheritdoc />
        public bool TryRemove(GameEffectRuntime activeEffect)
        {
            int index = activeEffects.IndexOf(activeEffect);
            if (index < 0 || activeEffect == null || !ReferenceEquals(activeEffect.Target, Owner))
                return false;

            RemoveGrantedTags(activeEffect.Data);
            Owner.MutableAttributes.TryRemoveModifiers(activeEffect, out _);
            activeEffects.RemoveAt(index);
            activeEffect.SetActive(false);
            PublishCues(
                activeEffect.Data,
                GameplayCueEventType.Remove,
                activeEffect.Source,
                activeEffect,
                null,
                activeEffect.Spec,
                activeEffect.LastApplicationResult);
            EffectRemoved?.Invoke(activeEffect);
            return true;
        }

        /// <inheritdoc />
        public void Tick(float deltaTime)
        {
            if (!IsFinite(deltaTime) || deltaTime < 0f) return;

            // 倒序执行，避免周期结算回调同步移除 Runtime 时破坏索引。
            for (int i = activeEffects.Count - 1; i >= 0; i--)
                TickRuntime(activeEffects[i], deltaTime);
        }

        /// <inheritdoc />
        public void Clear()
        {
            for (int i = activeEffects.Count - 1; i >= 0; i--)
                TryRemove(activeEffects[i]);
        }

        #endregion

        #region 应用流程

        /// <summary>应用 Instant Spec；计算失败时不产生任何属性变化。</summary>
        /// <param name="spec">已经封存的 GE Spec。</param>
        /// <param name="result">成功时返回本次应用结果。</param>
        /// <returns>计算与原子提交成功时返回 true。</returns>
        private bool ApplyInstant(
            GameplayEffectSpec spec,
            out GameplayEffectApplicationResult result)
        {
            result = null;
            var runtime = new GameEffectRuntime(spec, Owner);
            GameplayEffectCalculationOutput output = runtime.CalculateApplication(spec);
            if (output == null || !Owner.MutableAttributes.TryApplyInstantModifiers(output.Modifiers))
                return false;

            result = new GameplayEffectApplicationResult(spec, Owner, null, output);
            runtime.SetLastApplicationResult(result);
            PublishCues(
                spec.Data,
                GameplayCueEventType.Execute,
                spec.Source,
                runtime,
                null,
                spec,
                result);
            EffectExecuted?.Invoke(result);
            return true;
        }

        /// <summary>创建新的 Active Runtime，并按 GE 类型决定是否立即结算。</summary>
        /// <param name="spec">已经封存的 GE Spec。</param>
        /// <param name="result">成功时返回 Active Runtime 和初次结果。</param>
        /// <returns>创建与必要的数值提交成功时返回 true。</returns>
        private bool CreateActiveEffect(
            GameplayEffectSpec spec,
            out GameplayEffectApplicationResult result)
        {
            result = null;
            GameplayEffectData data = spec.Data;
            var runtime = new GameEffectRuntime(spec, Owner);
            bool needsImmediateCalculation = !data.IsPeriodic || data.ExecutePeriodicOnApplication;
            GameplayEffectCalculationOutput output = needsImmediateCalculation
                ? runtime.CalculateApplication(runtime)
                : GameplayEffectCalculationOutput.Empty;
            if (output == null) return false;

            bool numericApplied = data.IsPeriodic
                ? !data.ExecutePeriodicOnApplication || Owner.MutableAttributes.TryApplyInstantModifiers(output.Modifiers)
                : Owner.MutableAttributes.TryReplaceModifiers(runtime, output.Modifiers);
            if (!numericApplied) return false;

            AddGrantedTags(data);
            runtime.SetActive(true);
            activeEffects.Add(runtime);
            result = new GameplayEffectApplicationResult(spec, Owner, runtime, output);
            runtime.SetLastApplicationResult(result);
            PublishCues(data, GameplayCueEventType.Active, spec.Source, runtime, null, spec, result);
            if (needsImmediateCalculation)
            {
                PublishCues(data, GameplayCueEventType.Execute, spec.Source, runtime, null, spec, result);
                EffectExecuted?.Invoke(result);
            }
            return true;
        }

        /// <summary>使用候选 Spec 更新已有 Active Runtime；失败时保留原状态。</summary>
        /// <param name="existing">当前 Active Runtime。</param>
        /// <param name="spec">本次重应用的封存 Spec。</param>
        /// <param name="result">成功时返回更新后的 Runtime 结果。</param>
        /// <returns>候选计时、计算和提交都成功时返回 true。</returns>
        private bool ApplyStack(
            GameEffectRuntime existing,
            GameplayEffectSpec spec,
            out GameplayEffectApplicationResult result)
        {
            result = null;
            GameplayEffectData data = existing.Data;
            bool atLimit = existing.StackCount >= data.MaxStackCount;
            if (atLimit && data.DenyOverflowApplication) return false;

            int newStackCount = atLimit ? existing.StackCount : existing.StackCount + 1;
            GameEffectRuntime candidate = existing.CreateCandidate(spec, newStackCount);
            ApplyReapplicationTiming(candidate);
            if (!IsFinite(candidate.RemainingDuration) || !IsFinite(candidate.RemainingPeriod)) return false;

            bool needsImmediateCalculation = !data.IsPeriodic || data.ExecutePeriodicOnApplication;
            GameplayEffectCalculationOutput output = needsImmediateCalculation
                ? candidate.CalculateApplication(existing)
                : GameplayEffectCalculationOutput.Empty;
            if (output == null) return false;

            bool numericApplied = data.IsPeriodic
                ? !data.ExecutePeriodicOnApplication || Owner.MutableAttributes.TryApplyInstantModifiers(output.Modifiers)
                : Owner.MutableAttributes.TryReplaceModifiers(existing, output.Modifiers);
            if (!numericApplied) return false;

            existing.CommitCandidate(candidate);
            result = new GameplayEffectApplicationResult(spec, Owner, existing, output);
            existing.SetLastApplicationResult(result);
            if (needsImmediateCalculation)
            {
                PublishCues(data, GameplayCueEventType.Execute, spec.Source, existing, null, spec, result);
                EffectExecuted?.Invoke(result);
            }
            return true;
        }

        #endregion

        #region 计时与到期

        /// <summary>只在 Runtime 实际存续时间内推进周期和 Duration。</summary>
        /// <param name="runtime">当前 Active Runtime。</param>
        /// <param name="deltaTime">本次推进秒数。</param>
        private void TickRuntime(GameEffectRuntime runtime, float deltaTime)
        {
            GameplayEffectData data = runtime.Data;
            float activeDelta = data.DurationType == E_GameEffectDurationType.Duration
                ? Mathf.Min(deltaTime, runtime.RemainingDuration)
                : deltaTime;
            if (data.IsPeriodic && activeDelta > 0f) TickPeriodic(runtime, activeDelta);
            if (data.DurationType != E_GameEffectDurationType.Duration) return;

            runtime.SetRemainingDuration(Mathf.Max(0f, runtime.RemainingDuration - deltaTime));
            if (runtime.RemainingDuration <= 0f) HandleExpiration(runtime);
        }

        /// <summary>按完整 Period 结算周期；每次结算都读取当前 Source/Target 属性。</summary>
        /// <param name="runtime">当前周期 Runtime。</param>
        /// <param name="deltaTime">本次有效推进秒数。</param>
        private void TickPeriodic(GameEffectRuntime runtime, float deltaTime)
        {
            float remaining = runtime.RemainingPeriod - deltaTime;
            while (remaining <= 0f)
            {
                GameplayEffectCalculationOutput output = runtime.CalculateApplication(runtime);
                if (output != null && Owner.MutableAttributes.TryApplyInstantModifiers(output.Modifiers))
                {
                    if (runtime.SetPeriodicCalculationFailed(false))
                        Debug.Log(
                            $"[GameEffectCtrl] GE '{runtime.Data.name}' 周期结算已恢复，" +
                            $"Source='{runtime.Source.name}'。",
                            Owner);
                    var result = new GameplayEffectApplicationResult(
                        runtime.Spec,
                        Owner,
                        runtime,
                        output);
                    runtime.SetLastApplicationResult(result);
                    PublishCues(runtime.Data, GameplayCueEventType.Execute, runtime.Source, runtime, null, runtime.Spec, result);
                    EffectExecuted?.Invoke(result);
                }
                else if (runtime.SetPeriodicCalculationFailed(true))
                {
                    Debug.LogWarning(
                        $"[GameEffectCtrl] GE '{runtime.Data.name}' 周期结算失败，本 Tick 不提交数值，" +
                        $"Runtime 将继续存在。Source='{runtime.Source.name}'。",
                        Owner);
                }
                remaining += runtime.Data.Period;
            }

            runtime.SetRemainingPeriod(remaining);
        }

        /// <summary>应用配置的 Duration 与 Period 重应用规则。</summary>
        /// <param name="candidate">等待提交的候选 Runtime。</param>
        private static void ApplyReapplicationTiming(GameEffectRuntime candidate)
        {
            GameplayEffectData data = candidate.Data;
            if (data.DurationType == E_GameEffectDurationType.Duration)
            {
                switch (data.StackingDurationPolicy)
                {
                    case E_GameEffectStackingDurationPolicy.RefreshOnSuccessfulApplication:
                        candidate.SetRemainingDuration(data.Duration);
                        break;
                    case E_GameEffectStackingDurationPolicy.ExtendDuration:
                        candidate.SetRemainingDuration(candidate.RemainingDuration + data.Duration);
                        break;
                }
            }

            if (data.IsPeriodic &&
                data.StackingPeriodPolicy == E_GameEffectStackingPeriodPolicy.ResetOnSuccessfulApplication)
                candidate.SetRemainingPeriod(data.Period);
        }

        /// <summary>处理 Duration 到期和单层移除策略。</summary>
        /// <param name="runtime">已到期的 Runtime。</param>
        private void HandleExpiration(GameEffectRuntime runtime)
        {
            if (runtime.Data.DurationType != E_GameEffectDurationType.Duration) return;
            if (runtime.Data.StackingExpirationPolicy ==
                E_GameEffectStackingExpirationPolicy.RemoveSingleStackAndRefreshDuration &&
                runtime.StackCount > 1 && ExpireSingleStack(runtime))
                return;
            TryRemove(runtime);
        }

        /// <summary>减少一层并重新计算非周期持续 Modifier。</summary>
        /// <param name="runtime">当前 Active Runtime。</param>
        /// <returns>候选层数成功提交时返回 true。</returns>
        private bool ExpireSingleStack(GameEffectRuntime runtime)
        {
            GameplayEffectSpec spec = runtime.Spec;
            GameEffectRuntime candidate = runtime.CreateCandidate(spec, runtime.StackCount - 1);
            candidate.SetRemainingDuration(runtime.Data.Duration);
            if (!runtime.Data.IsPeriodic)
            {
                GameplayEffectCalculationOutput output = candidate.CalculateApplication(runtime);
                if (output == null || !Owner.MutableAttributes.TryReplaceModifiers(runtime, output.Modifiers))
                    return false;
            }
            runtime.CommitCandidate(candidate);
            return true;
        }

        #endregion

        #region Tag、叠层与辅助

        /// <summary>根据 GE 身份和叠层类型查找唯一合并目标。</summary>
        /// <param name="data">GE 作者配置。</param>
        /// <param name="source">本次应用 Source。</param>
        /// <returns>找到时返回当前 Runtime，否则返回 null。</returns>
        private GameEffectRuntime FindStackableRuntime(
            GameplayEffectData data,
            GameplayAbilitySystemComponent source)
        {
            if (data.StackingType == E_GameEffectStackingType.None) return null;
            foreach (GameEffectRuntime runtime in activeEffects)
            {
                if (!ReferenceEquals(runtime.Data, data)) continue;
                if (data.StackingType == E_GameEffectStackingType.AggregateByTarget ||
                    ReferenceEquals(runtime.Source, source))
                    return runtime;
            }
            return null;
        }

        /// <summary>登记当前 Active Runtime 的 GrantedTags。</summary>
        /// <param name="data">需要贡献标签的 GE。</param>
        private void AddGrantedTags(GameplayEffectData data)
        {
            IReadOnlyList<GameplayTag> tags = data.GrantedTags;
            for (int i = 0; i < tags.Count; i++)
                if (!Owner.MutableTags.UpdateTagCount(tags[i], 1))
                    throw new InvalidOperationException("GrantedTag 已通过入口校验，但运行时计数提交失败。");
        }

        /// <summary>移除当前 Active Runtime 的 GrantedTags。</summary>
        /// <param name="data">需要移除贡献的 GE。</param>
        private void RemoveGrantedTags(GameplayEffectData data)
        {
            IReadOnlyList<GameplayTag> tags = data.GrantedTags;
            for (int i = 0; i < tags.Count; i++) Owner.MutableTags.UpdateTagCount(tags[i], -1);
        }

        /// <summary>判断数值是否有限。</summary>
        /// <param name="value">待校验值。</param>
        /// <returns>不是 NaN 且不是 Infinity 时返回 true。</returns>
        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        /// <summary>把 GE 生命周期转换为带 Spec 和应用结果的 Cue 请求。</summary>
        /// <param name="data">Cue 来源 GE。</param>
        /// <param name="eventType">Cue 生命周期阶段。</param>
        /// <param name="source">Cue 来源 ASC。</param>
        /// <param name="effectRuntime">可选 GE Runtime。</param>
        /// <param name="abilityRuntime">可选 GA Runtime。</param>
        /// <param name="spec">本次应用 Spec。</param>
        /// <param name="applicationResult">本次应用结果。</param>
        private void PublishCues(
            GameplayEffectData data,
            GameplayCueEventType eventType,
            GameplayAbilitySystemComponent source,
            GameEffectRuntime effectRuntime,
            GameplayAbilityRuntime abilityRuntime,
            GameplayEffectSpec spec,
            GameplayEffectApplicationResult applicationResult)
        {
            IReadOnlyList<GameplayTag> tags = data.CueTags;
            if (tags == null) return;
            for (int i = 0; i < tags.Count; i++)
                Owner.PublishGameplayCue(new GameplayCueRequest(
                    tags[i],
                    eventType,
                    source,
                    Owner,
                    effectRuntime,
                    abilityRuntime,
                    spec,
                    applicationResult));
        }

        #endregion
    }
}
