using System;
using System.Collections.Generic;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.GameplayEffect;
using WS_Modules.GAS.TAG;
using WS_Modules.LogModule;

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
        private readonly Dictionary<GameEffectRuntime, GameplayAbilityRuntime> cooldownOwners = new();
        // ASC 初始化时注入的统一阻断规则快照；Controller 不直接持有配置资产数组。
        private GameplayTagQuery activationBlockedOwnerTags;
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
        /// <inheritdoc />
        public event Action<GameplayAbilityCooldownEventArgs> CooldownStarted;
        /// <inheritdoc />
        /// <remarks>Cooldown GE Runtime 已从 ASC 移除时触发（GACtrl 转发 GECtrl，由 Ability Runtime 触发），可能在 Ability Runtime 终态前发生。</remarks>
        public event Action<GameplayAbilityCooldownEventArgs> CooldownEnded;
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
            Owner.GameEffectCtrl.EffectRemoved += OnEffectRemoved;
        }

        /// <summary>
        /// 初始化当前 ASC 的统一 Ability 激活阻断 Tag 快照。
        /// </summary>
        /// <param name="blockedOwnerTags">Owner 拥有任意一个即阻止新 Ability 激活的 Tag 集合。</param>
        internal void InitializeActivationBlockedTags(GameplayTagQuery blockedOwnerTags)
        {
            activationBlockedOwnerTags.Clear();
            activationBlockedOwnerTags = blockedOwnerTags;
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
            WSLog.LogSuccess($"ASC {Owner.name} 授予 Ability {data.name}，Handle={handle.Id}，Level={level}");
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
            WSLog.LogSuccess($"ASC {Owner.name} 移除 Ability，Handle={handle.Id}");
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

            WSLog.Log($"ASC {Owner.name} 尝试激活 Ability {spec.Data.name}，Handle={handle.Id}，Level={spec.Level}");
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

            WSLog.Log($"ASC {Owner.name} 检查激活条件，Ability {spec.Data.name}，Handle={handle.Id}，Level={spec.Level}");

            // 全局阻断规则位于具体 Ability 条件之前，统一拦截对话、眩晕等状态下的新激活。
            if (IsActivationBlockedByOwnerTags()) return false;

            WSLog.Log($"ASC {Owner.name} 统一激活条件检查通过，Ability {spec.Data.name}，Handle={handle.Id}，Level={spec.Level}");
            if (spec.Level < 1 ||
                !spec.Data.ActivationTagQuery.Matches(Owner.Tags) ||
                !HasValidActivationPolicies(spec.Data) ||
                !spec.Data.IsRuntimeConfigurationValid)
                return false;

            WSLog.Log($"ASC {Owner.name} 激活条件通过，Ability {spec.Data.name}，Handle={handle.Id}，Level={spec.Level}");
            GameplayEffectData cooldown = spec.Data.CooldownEffect;
            if (cooldown != null && HasActiveCooldown(cooldown)) return false;

            GameplayAbilityRuntime candidate = spec.Data.CreateRuntimeInstance(
                AllocateActivationId(), spec, Owner, setByCaller);
            if (candidate == null)
                throw new InvalidOperationException("GameplayAbilityData 不能返回空 Runtime。");

            WSLog.LogSuccess(
                $"ASC {Owner.name} 激活 Ability {spec.Data.name}，Handle={handle.Id}，ActivationId={candidate.ActivationId}");
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

            // cost 和 CD 都已成功应用，Runtime 仍可能在 Activated 回调中被取消；只有仍处于 Active 的 Runtime 才能发出 Cancel 指令。
            runtime = candidate;
            runtime.Finished += OnRuntimeFinished;
            activeRuntimes.Add(runtime);
            runtime.Activate();
            WSLog.LogSuccess(
                $"ASC {Owner.name} 激活 Ability {spec.Data.name}，Handle={handle.Id}，ActivationId={runtime.ActivationId}");
            if (cooldownRuntime != null)
            {
                // Cooldown Runtime 是独立于 Ability 生命周期的 GE，只保存关联关系供结束事件还原技能身份。
                cooldownOwners.Add(cooldownRuntime, runtime);
                CooldownStarted?.Invoke(CreateCooldownEventArgs(runtime, cooldownRuntime));
            }

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
                activeRuntimes[^1].Cancel();
            runtimeSnapshot.Clear();
            grantedAbilities.Clear();
            activationBlockedOwnerTags.Clear();
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

        /// <summary>处理 GE Controller 发出的实际移除通知，并转发对应 Ability 的 CooldownEnded。</summary>
        /// <param name="effectRuntime">已经从 Owner ASC 移除的 GE Runtime。</param>
        private void OnEffectRemoved(GameEffectRuntime effectRuntime)
        {
            if (!cooldownOwners.Remove(effectRuntime, out GameplayAbilityRuntime abilityRuntime))
                return;

            CooldownEnded?.Invoke(CreateCooldownEventArgs(abilityRuntime, effectRuntime));
        }
        #endregion

        #region 校验与内部辅助
        /// <summary>
        /// 判断 Owner 是否拥有统一规则中任意一个 Ability 激活阻断 Tag。
        /// </summary>
        /// <returns>命中任意阻断 Tag 时返回 true。</returns>
        private bool IsActivationBlockedByOwnerTags()
        {
            // 统一阻断规则为空或无效时不阻断任何激活。
            return activationBlockedOwnerTags is { IsEmpty: false, IsValid: true } &&
                   activationBlockedOwnerTags.Matches(Owner.Tags);
        }

        // Cost/Cooldown 是序列化边界输入，运行时仍验证 Duration Policy。
        private static bool HasValidActivationPolicies(GameplayAbilityData data) =>
            (data.CostEffect == null ||
             data.CostEffect.DurationType == E_GameEffectDurationType.Instant) &&
            (data.CooldownEffect == null ||
             data.CooldownEffect.DurationType == E_GameEffectDurationType.Duration ||
             data.CooldownEffect.DurationType == E_GameEffectDurationType.Infinite) &&
            HasValidCooldownTags(data.CooldownEffect);

        /// <summary>按 Cooldown GE 的 GrantedTags 判断当前 ASC 是否已处于同类冷却。</summary>
        /// <param name="cooldown">待检查的 Cooldown GE 配置。</param>
        /// <returns>任一 Cooldown Tag 已在 ASC 上匹配时返回 true。</returns>
        private bool HasActiveCooldown(GameplayEffectData cooldown)
        {
            IReadOnlyList<GameplayTag> tags = cooldown.GrantedTags;
            for (int i = 0; i < tags.Count; i++)
                if (Owner.HasTag(tags[i]))
                    return true;
            return false;
        }

        /// <summary>校验 Cooldown GE 的运行时 Tag 契约。</summary>
        /// <param name="cooldown">待检查的 Cooldown GE；可以为空。</param>
        /// <returns>为空或包含至少一个有效 Tag 时返回 true。</returns>
        private static bool HasValidCooldownTags(GameplayEffectData cooldown)
        {
            if (cooldown == null) return true;
            IReadOnlyList<GameplayTag> tags = cooldown.GrantedTags;
            if (tags == null || tags.Count == 0) return false;
            for (int i = 0; i < tags.Count; i++)
                if (!GameplayTagManager.Instance.IsValidTag(tags[i]))
                    return false;
            return true;
        }

        /// <summary>创建供 UI 订阅的不可变 Cooldown 事件参数。</summary>
        /// <param name="abilityRuntime">关联 Ability Runtime。</param>
        /// <param name="cooldownRuntime">关联 Cooldown GE Runtime。</param>
        /// <returns>包含 Ability 身份与时长快照的事件参数。</returns>
        private static GameplayAbilityCooldownEventArgs CreateCooldownEventArgs(
            GameplayAbilityRuntime abilityRuntime,
            GameEffectRuntime cooldownRuntime) =>
            new(abilityRuntime, cooldownRuntime);

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