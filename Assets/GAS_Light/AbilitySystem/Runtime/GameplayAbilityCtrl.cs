using System;
using System.Collections.Generic;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.GameplayEffect;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>为单个 ASC 管理 Ability Spec、激活 Runtime 及终态事件。</summary>
    public sealed class GameplayAbilityCtrl : IGameplayAbilityCtrl
    {
        #region 字段
        private readonly List<GameplayAbilitySpec> grantedAbilities = new();
        // 运行时激活的技能
        private readonly List<GameplayAbilityRuntime> activeRuntimes = new();
        // 三个 Unity 阶段复用稳定快照，避免 Runtime 在回调中结束或激活时破坏当前遍历。
        private readonly List<GameplayAbilityRuntime> runtimeSnapshot = new();
        private int nextHandleId = 1;
        private int nextActivationId = 1;
        #endregion

        #region 事件
        /// <inheritdoc />
        public event Action<GameplayAbilityRuntime> AbilityActivated;
        /// <inheritdoc />
        public event Action<GameplayAbilityRuntime> AbilityEnded;
        /// <inheritdoc />
        public event Action<GameplayAbilityRuntime> AbilityCancelled;
        #endregion

        #region 属性与构造
        /// <inheritdoc />
        public GameplayAbilitySystemComponent Owner { get; }
        /// <inheritdoc />
        public IReadOnlyList<GameplayAbilitySpec> GrantedAbilities => grantedAbilities;
        /// <inheritdoc />
        public IReadOnlyList<GameplayAbilityRuntime> ActiveRuntimes => activeRuntimes;

        /// <summary>创建只服务指定 Source ASC 的 Ability Controller。</summary>
        /// <param name="owner">拥有全部 Spec、Runtime 与 Tick 回调的 ASC。</param>
        /// <exception cref="ArgumentNullException">owner 为 null。</exception>
        public GameplayAbilityCtrl(GameplayAbilitySystemComponent owner)
        {
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }
        #endregion

        #region Spec 操作
        /// <inheritdoc />
        public GameplayAbilityHandle GiveAbility(GameplayAbilityData data, int level)
        {
            if (data == null || level < 1) return GameplayAbilityHandle.Invalid;
            for (int i = 0; i < grantedAbilities.Count; i++)
                if (ReferenceEquals(grantedAbilities[i].Data, data))
                    return GameplayAbilityHandle.Invalid;

            var handle = new GameplayAbilityHandle(AllocateHandleId());
            grantedAbilities.Add(new GameplayAbilitySpec(handle, data, level));
            return handle;
        }

        /// <inheritdoc />
        public bool TryGetAbilityHandle(int abilityId, out GameplayAbilityHandle handle)
        {
            if (!GameplayAbilityManager.Instance.TryGetAbility(
                    abilityId,
                    out GameplayAbilityData ability))
            {
                handle = GameplayAbilityHandle.Invalid;
                return false;
            }

            for (int i = 0; i < grantedAbilities.Count; i++)
            {
                GameplayAbilitySpec spec = grantedAbilities[i];
                if (!ReferenceEquals(spec.Data, ability)) continue;
                handle = spec.Handle;
                return true;
            }

            handle = GameplayAbilityHandle.Invalid;
            return false;
        }

        /// <inheritdoc />
        public bool TrySetAbilityLevel(GameplayAbilityHandle handle, int level)
        {
            if (level < 1 || !TryGetAbilitySpec(handle, out GameplayAbilitySpec spec)) return false;
            spec.SetLevel(level);
            return true;
        }

        /// <inheritdoc />
        public bool TryGetAbilitySpec(GameplayAbilityHandle handle, out GameplayAbilitySpec spec)
        {
            for (int i = 0; i < grantedAbilities.Count; i++)
            {
                GameplayAbilitySpec candidate = grantedAbilities[i];
                if (candidate.Handle != handle) continue;
                spec = candidate;
                return true;
            }

            spec = null;
            return false;
        }

        /// <inheritdoc />
        public bool TryRemoveAbility(GameplayAbilityHandle handle)
        {
            if (!TryGetAbilitySpec(handle, out GameplayAbilitySpec spec) || HasActiveRuntime(spec))
                return false;
            return grantedAbilities.Remove(spec);
        }
        #endregion

        #region Tick 推进
        /// <summary>推进当前 Controller 的 Active Runtime 普通更新阶段。</summary>
        /// <param name="deltaTime">本次推进的秒数。</param>
        public void Tick(float deltaTime)
        {
            if (!IsValidDeltaTime(deltaTime)) return;

            AdvanceRuntimes(runtime => runtime.Tick(deltaTime));
        }

        /// <summary>推进当前 Controller 的 Active Runtime 固定更新阶段。</summary>
        /// <param name="fixedDeltaTime">本次固定更新的秒数。</param>
        public void FixedTick(float fixedDeltaTime)
        {
            if (!IsValidDeltaTime(fixedDeltaTime)) return;
            AdvanceRuntimes(runtime => runtime.FixedTick(fixedDeltaTime));
        }

        /// <summary>推进当前 Controller 的 Active Runtime 延迟更新阶段。</summary>
        /// <param name="deltaTime">本次延迟更新使用的秒数。</param>
        public void LateTick(float deltaTime)
        {
            if (!IsValidDeltaTime(deltaTime)) return;
            AdvanceRuntimes(runtime => runtime.LateTick(deltaTime));
        }

        /// <summary>推进当前 Controller 的 Active Runtime 动画根运动阶段。</summary>
        public void UpdateAnimationMove() =>
            AdvanceRuntimes(runtime => runtime.UpdateAnimationMove());
        #endregion

        #region 激活
        /// <inheritdoc />
        public bool TryActivate(
            GameplayAbilityHandle handle,
            IReadOnlyDictionary<GameplayTag, float> setByCaller,
            out GameplayAbilityRuntime runtime)
        {
            runtime = null;
            if (!TryGetAbilitySpec(handle, out GameplayAbilitySpec spec)) return false;

            // 重复激活策略必须先于 Cost、Cooldown 和条件检查处理，Toggle 关闭不会产生新的激活副作用。
            GameplayAbilityRuntime existing = FindActiveRuntime(spec);
            if (existing != null)
            {
                if (spec.Data.ReactivationPolicy == GameplayAbilityReactivationPolicy.RejectWhileActive)
                    return false;
                if (spec.Data.ReactivationPolicy == GameplayAbilityReactivationPolicy.ToggleOff)
                {
                    runtime = existing;
                    return existing.End();
                }
            }

            if (spec.Level < 1 ||
                !spec.Data.ActivationTagQuery.Matches(Owner.Tags) ||
                !HasValidActivationPolicies(spec.Data) ||
                !spec.Data.IsRuntimeConfigurationValid)
                return false;

            GameplayEffectData cooldown = spec.Data.CooldownEffect;
            if (cooldown != null && Owner.GameEffectCtrl.HasActiveEffect(cooldown)) return false;

            GameplayAbilityRuntime candidate = spec.Data.CreateRuntimeInstance(
                AllocateActivationId(), spec, Owner, setByCaller);
            if (candidate == null)
                throw new InvalidOperationException("GameplayAbilityData 不能返回空 Runtime。");

            // 应用冷却效果
            GameEffectRuntime cooldownRuntime = null;
            if (cooldown != null &&
                !Owner.GameEffectCtrl.TryApply(
                    cooldown, Owner, spec.Level, setByCaller, out cooldownRuntime))
                return false;

            GameplayEffectData cost = spec.Data.CostEffect;
            if (cost != null &&
                !Owner.GameEffectCtrl.TryApply(cost, Owner, spec.Level, setByCaller, out _))
            {
                if (cooldownRuntime != null)
                    Owner.GameEffectCtrl.TryRemove(cooldownRuntime);
                return false;
            }

            runtime = candidate;
            runtime.Finished += OnRuntimeFinished;
            activeRuntimes.Add(runtime);
            runtime.Activate();
            AbilityActivated?.Invoke(runtime);
            // Activated 回调可能已经取消新 Runtime；只有仍成立的激活才能发出取消指令。
            if (runtime.State == GameplayAbilityRuntimeState.Active)
                CancelAbilitiesMatching(runtime);
            if (runtime.State == GameplayAbilityRuntimeState.Active)
                runtime.Start();
            return true;
        }
        #endregion

        #region 生命周期
        /// <inheritdoc />
        public bool TryEnd(GameplayAbilityRuntime runtime) =>
            OwnsActiveRuntime(runtime) && runtime.End();

        /// <inheritdoc />
        public bool TryCancel(GameplayAbilityRuntime runtime) =>
            OwnsActiveRuntime(runtime) && runtime.Cancel();

        /// <inheritdoc />
        public void Clear()
        {
            while (activeRuntimes.Count > 0)
                activeRuntimes[activeRuntimes.Count - 1].Cancel();
            runtimeSnapshot.Clear();
            grantedAbilities.Clear();
        }

        // Runtime 已先进入终态；Controller 先移除，再发送唯一公开终态事件。
        private void OnRuntimeFinished(GameplayAbilityRuntime runtime)
        {
            runtime.Finished -= OnRuntimeFinished;
            if (!activeRuntimes.Remove(runtime)) return;

            if (runtime.State == GameplayAbilityRuntimeState.Ended)
                AbilityEnded?.Invoke(runtime);
            else if (runtime.State == GameplayAbilityRuntimeState.Cancelled)
                AbilityCancelled?.Invoke(runtime);
        }
        #endregion

        #region 校验与内部辅助
        // Cost/Cooldown 是序列化边界输入，运行时仍验证 Duration Policy。
        private static bool HasValidActivationPolicies(GameplayAbilityData data) =>
            (data.CostEffect == null ||
             data.CostEffect.DurationType == E_GameEffectDurationType.Instant) &&
            (data.CooldownEffect == null ||
             data.CooldownEffect.DurationType == E_GameEffectDurationType.Duration ||
             data.CooldownEffect.DurationType == E_GameEffectDurationType.Infinite);

        // Runtime 必须仍在当前 Controller 的 Active 集合且 Source 相同。
        private bool OwnsActiveRuntime(GameplayAbilityRuntime runtime) =>
            runtime != null &&
            runtime.State == GameplayAbilityRuntimeState.Active &&
            ReferenceEquals(runtime.SourceASC, Owner) &&
            activeRuntimes.Contains(runtime);

        /// <summary>判断指定 Spec 是否仍有激活实例。</summary>
        /// <param name="spec">需要查询的已授予 Ability Spec。</param>
        /// <returns>存在 Active Runtime 时返回 true。</returns>
        private bool HasActiveRuntime(GameplayAbilitySpec spec)
        {
            return FindActiveRuntime(spec) != null;
        }

        /// <summary>查找指定 Spec 当前第一个 Active Runtime。</summary>
        /// <param name="spec">需要查询的已授予 Ability Spec。</param>
        /// <returns>找到时返回 Runtime，否则返回 null。</returns>
        private GameplayAbilityRuntime FindActiveRuntime(GameplayAbilitySpec spec)
        {
            for (int i = 0; i < activeRuntimes.Count; i++)
                if (ReferenceEquals(activeRuntimes[i].Spec, spec))
                    return activeRuntimes[i];
            return null;
        }

        /// <summary>取消 AbilityTags 与新 Runtime CancelTags 层级匹配的其他 Active Runtime。</summary>
        /// <param name="activatingRuntime">已发送 Activated 事件且仍处于 Active 的新 Runtime。</param>
        private void CancelAbilitiesMatching(GameplayAbilityRuntime activatingRuntime)
        {
            IReadOnlyList<GameplayTag> cancelTags = activatingRuntime.Spec.Data.CancelTags;
            if (cancelTags.Count == 0) return;

            // 每次命令使用独立快照，允许 Cancelled 回调中重入激活其他 Ability。
            var snapshot = new List<GameplayAbilityRuntime>(activeRuntimes);
            for (int i = 0; i < snapshot.Count; i++)
            {
                GameplayAbilityRuntime candidate = snapshot[i];
                // 避免取消自身或已终态的 Runtime；仅 Active Runtime 才能被取消。
                if (ReferenceEquals(candidate, activatingRuntime) ||
                    candidate.State != GameplayAbilityRuntimeState.Active ||
                    !MatchesAnyAbilityTag(candidate.Spec.Data.AbilityTags, cancelTags))
                    continue;

                candidate.Cancel();
            }
        }

        /// <summary>判断任一实际 AbilityTag 是否能匹配任一 CancelTag 或其子标签条件。</summary>
        /// <param name="abilityTags">被检查 Ability 的实际分类标签。</param>
        /// <param name="cancelTags">新 Ability 发出的取消查询标签。</param>
        /// <returns>存在至少一组层级匹配时返回 true。</returns>
        private static bool MatchesAnyAbilityTag(
            IReadOnlyList<GameplayTag> abilityTags,
            IReadOnlyList<GameplayTag> cancelTags)
        {
            for (int i = 0; i < abilityTags.Count; i++)
                for (int j = 0; j < cancelTags.Count; j++)
                    if (abilityTags[i].MatchesTag(cancelTags[j]))
                        return true;
            return false;
        }

        /// <summary>使用阶段开始时的稳定快照推进仍属于当前 Controller 的 Active Runtime。</summary>
        /// <param name="advance">当前 Unity 阶段的 Runtime 推进操作。</param>
        private void AdvanceRuntimes(Action<GameplayAbilityRuntime> advance)
        {
            runtimeSnapshot.Clear();
            runtimeSnapshot.AddRange(activeRuntimes);
            for (int i = runtimeSnapshot.Count - 1; i >= 0; i--)
            {
                GameplayAbilityRuntime runtime = runtimeSnapshot[i];
                // 较早回调可能结束其他 Runtime；失去所有权的项不得在本阶段继续执行。
                if (OwnsActiveRuntime(runtime)) advance(runtime);
            }
        }

        /// <summary>判断阶段推进使用的时间增量是否合法。</summary>
        /// <param name="deltaTime">待检查的时间秒数。</param>
        /// <returns>非负有限值返回 true。</returns>
        private static bool IsValidDeltaTime(float deltaTime) =>
            !float.IsNaN(deltaTime) && !float.IsInfinity(deltaTime) && deltaTime >= 0f;

        // 单调分配当前 Controller 内 Spec Handle。
        private int AllocateHandleId()
        {
            int id = nextHandleId;
            nextHandleId = checked(nextHandleId + 1);
            return id;
        }

        // 单调分配当前 Controller 内 ActivationId。
        private int AllocateActivationId()
        {
            int id = nextActivationId;
            nextActivationId = checked(nextActivationId + 1);
            return id;
        }
        #endregion
    }
}
