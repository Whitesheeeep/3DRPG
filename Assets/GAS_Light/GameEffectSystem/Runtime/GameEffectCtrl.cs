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
    /// <summary>为单个目标 ASC 协调 GE 校验、Modifier 计算、叠层、计时与移除。</summary>
    public sealed class GameEffectCtrl : IGameEffectCtrl
    {
        #region 字段

        private readonly List<GameEffectRuntime> activeEffects = new();

        #endregion

        #region 属性与构造

        /// <inheritdoc />
        public GameplayAbilitySystemComponent Owner { get; }
        /// <inheritdoc />
        public IReadOnlyList<GameEffectRuntime> ActiveEffects => activeEffects;

        /// <summary>创建只服务指定目标 ASC 的 GE Controller。</summary>
        /// <param name="owner">作为所有应用隐式 Target 的 ASC。</param>
        public GameEffectCtrl(GameplayAbilitySystemComponent owner)
        {
            Owner = owner;
        }

        #endregion

        #region 公开操作

        /// <inheritdoc />
        public bool CanApply(GameplayEffectData data, GameplayAbilitySystemComponent source) =>
            Owner != null && source != null && data != null &&
            data.TargetTagQuery.Matches(Owner.Tags) &&
            data.GrantedTags.All(tag => GameplayTagManager.Instance.IsValidTag(tag));

        /// <inheritdoc />
        public bool TryApply(
            GameplayEffectData data,
            GameplayAbilitySystemComponent source,
            int level,
            IReadOnlyDictionary<GameplayTag, float> setByCaller,
            out GameEffectRuntime activeEffect)
        {
            activeEffect = null;
            if (level < 1 || !CanApply(data, source) ||
                !HasRequiredSetByCallerValues(data, setByCaller))
                return false;

            if (data.DurationType == E_GameEffectDurationType.Instant)
                return ApplyInstant(data, source, level, setByCaller);

            GameEffectRuntime existing = FindStackableRuntime(data, source);
            return existing == null
                ? CreateActiveEffect(data, source, level, setByCaller, out activeEffect)
                : ApplyStack(existing, source, level, setByCaller, out activeEffect);
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
            if (index < 0 || activeEffect == null || !ReferenceEquals(activeEffect.Target, Owner)) return false;

            RemoveGrantedTags(activeEffect.Data);
            Owner.MutableAttributes.TryRemoveModifiers(activeEffect, out _);
            activeEffects.RemoveAt(index);
            activeEffect.SetActive(false);
            PublishCues(activeEffect.Data, GameplayCueEventType.Remove, activeEffect.Source, activeEffect, null);
            return true;
        }

        /// <inheritdoc />
        public void Tick(float deltaTime)
        {
            if (!IsFinite(deltaTime) || deltaTime < 0f) return;

            // 倒序执行，避免在 Tick 中移除 Runtime 时破坏索引。
            for (int i = activeEffects.Count - 1; i >= 0; i--)
            {
                GameEffectRuntime runtime = activeEffects[i];
                TickRuntime(runtime, deltaTime);
            }
        }

        /// <inheritdoc />
        public void Clear()
        {
            for (int i = activeEffects.Count - 1; i >= 0; i--) TryRemove(activeEffects[i]);
        }

        #endregion

        #region 应用流程

        // Instant 使用临时 Runtime 提供 Level、StackCount 与 SetByCaller，但不进入 Active 列表。
        private bool ApplyInstant(
            GameplayEffectData data,
            GameplayAbilitySystemComponent source,
            int level,
            IReadOnlyDictionary<GameplayTag, float> setByCaller)
        {
            var runtime = new GameEffectRuntime(data, source, Owner, level, setByCaller);
            List<AttributeModifier> results = runtime.CalculateModifiers(runtime);
            bool applied = Owner.MutableAttributes.TryApplyInstantModifiers(results);
            if (applied)
                PublishCues(data, GameplayCueEventType.Execute, source, runtime, null);
            return applied;
        }

        // 创建新 Active 前完成当前应执行的 Modifier 计算与 Tag 校验；延迟周期不会提前消费随机计算。
        private bool CreateActiveEffect(
            GameplayEffectData data,
            GameplayAbilitySystemComponent source,
            int level,
            IReadOnlyDictionary<GameplayTag, float> setByCaller,
            out GameEffectRuntime activeEffect)
        {
            activeEffect = null;
            var runtime = new GameEffectRuntime(data, source, Owner, level, setByCaller);
            bool needsImmediateCalculation = !data.IsPeriodic || data.ExecutePeriodicOnApplication;
            List<AttributeModifier> results = needsImmediateCalculation
                ? runtime.CalculateModifiers(runtime)
                : null;

            // 不为 Periodic GE 时，即为 Infinite GE，那就应该替换其
            bool numericApplied = data.IsPeriodic
                ? !data.ExecutePeriodicOnApplication || Owner.MutableAttributes.TryApplyInstantModifiers(results)
                : Owner.MutableAttributes.TryReplaceModifiers(runtime, results);
            if (!numericApplied) return false;

            AddGrantedTags(data);
            runtime.SetActive(true);
            activeEffects.Add(runtime);
            activeEffect = runtime;
            PublishCues(data, GameplayCueEventType.Active, source, runtime, null);
            if (data.IsPeriodic && data.ExecutePeriodicOnApplication)
                PublishCues(data, GameplayCueEventType.Execute, source, runtime, null);
            return true;
        }

        // 使用候选 Runtime 计算新层数和最新来源；需要立即结算时失败则保留原 Runtime。
        private bool ApplyStack(
            GameEffectRuntime existing,
            GameplayAbilitySystemComponent source,
            int level,
            IReadOnlyDictionary<GameplayTag, float> setByCaller,
            out GameEffectRuntime activeEffect)
        {
            activeEffect = null;
            GameplayEffectData data = existing.Data;
            // 达到上限
            bool atLimit = existing.StackCount >= data.MaxStackCount;
            // 达到上限且完全拒绝应用，则直接返回
            if (atLimit && data.DenyOverflowApplication) return false;

            int newStackCount = atLimit ? existing.StackCount : existing.StackCount + 1;
            GameEffectRuntime candidate = existing.CreateCandidate(source, level, newStackCount, setByCaller);

            // 结算 Duration 和 Period 的刷新规则；如果刷新后出现 NaN 或 Infinity，则直接拒绝应用。
            ApplyReapplicationTiming(candidate);
            // 避免将 Ge 的持续时间或周期设置为 NaN 或 Infinity，导致后续 Tick 计算异常。
            if (!IsFinite(candidate.RemainingDuration) || !IsFinite(candidate.RemainingPeriod)) return false;

            // 如果是周期 GE 且配置了立即结算，则在应用前先计算一次数值；否则延迟到下一次周期 Tick。
            // 不是周期 GE 就是 infinite (instant 已经结算），则刷新 Modifier
            bool needsImmediateCalculation = !data.IsPeriodic || data.ExecutePeriodicOnApplication;
            List<AttributeModifier> results = needsImmediateCalculation
                ? candidate.CalculateModifiers(existing)
                : null;
            bool numericApplied = data.IsPeriodic
                ? !data.ExecutePeriodicOnApplication || Owner.MutableAttributes.TryApplyInstantModifiers(results)
                : Owner.MutableAttributes.TryReplaceModifiers(existing, results);
            if (!numericApplied) return false;

            existing.CommitCandidate(candidate);
            activeEffect = existing;
            PublishCues(data, GameplayCueEventType.Execute, source, existing, null);
            return true;
        }

        #endregion

        #region 计时与到期

        // 只在 Runtime 实际存续的时间段内执行周期，Duration 到期后的剩余 delta 不再结算。
        private void TickRuntime(GameEffectRuntime runtime, float deltaTime)
        {
            GameplayEffectData data = runtime.Data;
            // Duration GE 的剩余时间可能小于 deltaTime，避免在 Duration 到期后继续结算周期。
            float activeDelta = data.DurationType == E_GameEffectDurationType.Duration
                ? Mathf.Min(deltaTime, runtime.RemainingDuration)
                : deltaTime;

            if (data.IsPeriodic && activeDelta > 0f) TickPeriodic(runtime, activeDelta);
            if (data.DurationType != E_GameEffectDurationType.Duration) return;

            runtime.SetRemainingDuration(Mathf.Max(0f, runtime.RemainingDuration - deltaTime));
            if (runtime.RemainingDuration <= 0f) HandleExpiration(runtime);
        }

        // 大 delta 会按完整 Period 重复结算，且每次都读取当时 Runtime 的 Source 与 StackCount。
        private void TickPeriodic(GameEffectRuntime runtime, float deltaTime)
        {
            float remaining = runtime.RemainingPeriod - deltaTime;
            while (remaining <= 0f)
            {
                List<AttributeModifier> results = runtime.CalculateModifiers(runtime);
                if (Owner.MutableAttributes.TryApplyInstantModifiers(results))
                    PublishCues(runtime.Data, GameplayCueEventType.Execute, runtime.Source, runtime, null);
                remaining += runtime.Data.Period;
            }

            runtime.SetRemainingPeriod(remaining);
        }

        /// 应用配置的持续时间和周期刷新规则；修改发生在候选 Runtime 上。
        private static void ApplyReapplicationTiming(GameEffectRuntime candidate)
        {
            GameplayEffectData data = candidate.Data;

            // Instant 在之前已经结算了，这里只有 Duration 或者 Infinite
            // Infinite 是无限长的，不用结算 Duration
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
                    case E_GameEffectStackingDurationPolicy.NeverRefresh:
                        break;
                }
            }

            // 结算单周期内的 Tick Duration
            if (data.IsPeriodic &&
                data.StackingPeriodPolicy == E_GameEffectStackingPeriodPolicy.ResetOnSuccessfulApplication)
                candidate.SetRemainingPeriod(data.Period);
        }

        // 到期规则可能移除 Runtime、刷新时间，或减少一层并重算持续 Modifier。
        private void HandleExpiration(GameEffectRuntime runtime)
        {
            // Infinite GE 不会过期，Duration GE 才会触发到期处理。
            if (runtime.Data.DurationType != E_GameEffectDurationType.Duration)
                return;

            GameplayEffectData data = runtime.Data;
            switch (data.StackingExpirationPolicy)
            {
                case E_GameEffectStackingExpirationPolicy.RemoveSingleStackAndRefreshDuration:
                    if (runtime.StackCount > 1 && ExpireSingleStack(runtime)) return;
                    TryRemove(runtime);
                    return;
                default:
                    TryRemove(runtime);
                    return;
            }
        }

        // 单层到期先用候选层数重算；失败时返回 false，由调用方安全移除整个 Runtime。
        private bool ExpireSingleStack(GameEffectRuntime runtime)
        {
            GameEffectRuntime candidate = runtime.CreateCandidate(
                runtime.Source,
                runtime.Level,
                runtime.StackCount - 1,
                runtime.SetByCaller);
            candidate.SetRemainingDuration(runtime.Data.Duration);

            if (!runtime.Data.IsPeriodic)
            {
                List<AttributeModifier> results = candidate.CalculateModifiers(runtime);
                if (!Owner.MutableAttributes.TryReplaceModifiers(runtime, results))
                    return false;
            }

            runtime.CommitCandidate(candidate);
            return true;
        }

        #endregion

        #region Tag、叠层与校验辅助

        /// <summary>
        /// 根据 GE 身份和叠层类型查找唯一合并目标；None 永远创建新 Runtime。
        /// </summary>
        private GameEffectRuntime FindStackableRuntime(
            GameplayEffectData data,
            GameplayAbilitySystemComponent source)
        {
            if (data.StackingType == E_GameEffectStackingType.None) return null;
            foreach (var runtime in activeEffects)
            {
                if (!ReferenceEquals(runtime.Data, data)) continue;
                // 找到
                if (data.StackingType == E_GameEffectStackingType.AggregateByTarget ||
                    ReferenceEquals(runtime.Source, source))
                    return runtime;
            }

            return null;
        }

        // GrantedTag 已由公开入口统一校验；运行时计数失败表示违反内部调用契约。
        private void AddGrantedTags(GameplayEffectData data)
        {
            IReadOnlyList<GameplayTag> tags = data.GrantedTags;
            for (int i = 0; i < tags.Count; i++)
                if (!Owner.MutableTags.UpdateTagCount(tags[i], 1))
                    throw new InvalidOperationException("GrantedTag 已通过入口校验，但运行时计数提交失败。");
        }

        // 每个 Active Runtime 只贡献一次 GrantedTags，StackCount 不参与 Tag 来源计数。
        private void RemoveGrantedTags(GameplayEffectData data)
        {
            IReadOnlyList<GameplayTag> tags = data.GrantedTags;
            for (int i = 0; i < tags.Count; i++) Owner.MutableTags.UpdateTagCount(tags[i], -1);
        }

        // 只检查当前 Modifier 真正需要的动态 Key，未使用的调用方数据不参与 GE 校验。
        private static bool HasRequiredSetByCallerValues(
            GameplayEffectData data,
            IReadOnlyDictionary<GameplayTag, float> values)
        {
            var requiredKeys = new HashSet<GameplayTag>();
            data.CollectRequiredSetByCallerKeys(requiredKeys);
            foreach (GameplayTag key in requiredKeys)
                if (!GameplayTagManager.Instance.IsValidTag(key) || values == null ||
                    !values.TryGetValue(key, out float value) || !IsFinite(value))
                    return false;
            return true;
        }

        // GE 计时、等级输入和 SetByCaller 均禁止 NaN 与 Infinity。
        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        // 将已提交的 GE 生命周期转成 ASC 局部事件，Cue 失败不会影响 GE 结果。
        private void PublishCues(
            GameplayEffectData data,
            GameplayCueEventType eventType,
            GameplayAbilitySystemComponent source,
            GameEffectRuntime effectRuntime,
            GameplayAbilityRuntime abilityRuntime)
        {
            IReadOnlyList<GameplayTag> tags = data.CueTags;
            if (tags == null) return;
            for (int i = 0; i < tags.Count; i++)
            {
                Owner.PublishGameplayCue(new GameplayCueRequest(
                    tags[i], eventType, source, Owner, effectRuntime, abilityRuntime));
            }
        }

        #endregion
    }
}
