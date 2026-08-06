using System;
using System.Collections.Generic;
using WS_Modules.CustomEventSystem;
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
        private readonly List<Action<float>> tickCallbacks = new();
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
            var handle = new GameplayAbilityHandle(AllocateHandleId());
            grantedAbilities.Add(new GameplayAbilitySpec(handle, data, level));
            return handle;
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
        /// <summary>推进当前 Controller 注册的 Ability Tick 回调。</summary>
        /// <param name="deltaTime">本次推进的秒数。</param>
        public void Tick(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f)
                return;

            // 在 Tick 回调中可能会移除自身或其他回调，因此使用倒序遍历。
            for (int i = tickCallbacks.Count - 1; i >= 0; i--)
            {
                tickCallbacks[i](deltaTime);
            }
        }

        /// <summary>获取当前仍注册在 Controller 中的 Tick 回调数量，仅供测试和诊断使用。</summary>
        internal int TickRegistrationCount => tickCallbacks.Count;

        // TickTask 启动时注册回调；返回的句柄负责在 Task 终止时移除回调。
        internal IUnRegister RegisterTick(Action<float> callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            tickCallbacks.Add(callback);
            return new CustomUnRegister(() => UnRegisterTick(callback));
        }

        private void UnRegisterTick(Action<float> callback) => tickCallbacks.Remove(callback);
        #endregion

        #region 激活
        /// <inheritdoc />
        public bool TryActivate(
            GameplayAbilityHandle handle,
            IReadOnlyDictionary<GameplayTag, float> setByCaller,
            out GameplayAbilityRuntime runtime)
        {
            runtime = null;
            if (!TryGetAbilitySpec(handle, out GameplayAbilitySpec spec) ||
                spec.Level < 1 ||
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
            tickCallbacks.Clear();
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
            ReferenceEquals(runtime.Source, Owner) &&
            activeRuntimes.Contains(runtime);

        // 查找指定 Spec 是否仍有激活实例。
        private bool HasActiveRuntime(GameplayAbilitySpec spec)
        {
            for (int i = 0; i < activeRuntimes.Count; i++)
                if (ReferenceEquals(activeRuntimes[i].Spec, spec))
                    return true;
            return false;
        }

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