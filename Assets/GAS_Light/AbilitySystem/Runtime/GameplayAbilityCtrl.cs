using System;
using System.Collections.Generic;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.GameplayEffect;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>为单个 ASC 管理 Ability Spec、激活 Runtime 与一次性基础 GE 执行。</summary>
    public sealed class GameplayAbilityCtrl : IGameplayAbilityCtrl
    {
        #region 字段
        // 记录玩家拥有的技能
        private readonly List<GameplayAbilitySpec> grantedAbilities = new();
        // 记录当前激活的技能运行时
        private readonly List<GameplayAbilityRuntime> activeRuntimes = new();
        // Handle 与 ActivationId 仅在当前 Controller 内唯一，溢出时立即抛出异常。
        private int nextHandleId = 1;
        private int nextActivationId = 1;
        #endregion

        #region 属性与构造
        /// <inheritdoc />
        public AbilitySystemComponentBase Owner { get; }
        /// <inheritdoc />
        public IReadOnlyList<GameplayAbilitySpec> GrantedAbilities => grantedAbilities;
        /// <inheritdoc />
        public IReadOnlyList<GameplayAbilityRuntime> ActiveRuntimes => activeRuntimes;

        /// <summary>创建只服务指定 Source ASC 的 Ability Controller。</summary>
        /// <param name="owner">拥有全部 Spec 与 Runtime 的 ASC。</param>
        /// <exception cref="ArgumentNullException">owner 为 null。</exception>
        public GameplayAbilityCtrl(AbilitySystemComponentBase owner)
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

        #region 激活与执行
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
                !HasValidActivationPolicies(spec.Data))
                return false;

            GameplayEffectData cooldown = spec.Data.CooldownEffect;
            if (cooldown != null && Owner.GameEffectCtrl.HasActiveEffect(cooldown)) return false;

            GameEffectRuntime cooldownRuntime = null;
            if (cooldown != null &&
                !Owner.GameEffectCtrl.TryApply(
                    cooldown,
                    Owner,
                    spec.Level,
                    setByCaller,
                    out cooldownRuntime))
                return false;

            GameplayEffectData cost = spec.Data.CostEffect;
            if (cost != null &&
                !Owner.GameEffectCtrl.TryApply(cost, Owner, spec.Level, setByCaller, out _))
            {
                Owner.GameEffectCtrl.TryRemove(cooldownRuntime);
                return false;
            }

            runtime = new GameplayAbilityRuntime(
                AllocateActivationId(),
                spec,
                Owner,
                setByCaller);
            activeRuntimes.Add(runtime);
            return true;
        }

        /// <inheritdoc />
        public bool TryExecuteEffects(
            GameplayAbilityRuntime runtime,
            IReadOnlyList<AbilitySystemComponentBase> targets,
            out IReadOnlyList<GameEffectRuntime> activeEffects)
        {
            activeEffects = Array.Empty<GameEffectRuntime>();
            if (!CanExecute(runtime, targets)) return false;

            GameplayAbilityData data = runtime.Spec.Data;
            var results = new List<GameEffectRuntime>();
            ApplyEffectsToTarget(data.SelfEffects, runtime, Owner, results);
            for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
                ApplyEffectsToTarget(data.TargetEffects, runtime, targets[targetIndex], results);

            runtime.MarkExecuted();
            activeEffects = results;
            return true;
        }
        #endregion

        #region 生命周期
        /// <inheritdoc />
        public bool TryEnd(GameplayAbilityRuntime runtime) => TryFinish(runtime, false);

        /// <inheritdoc />
        public bool TryCancel(GameplayAbilityRuntime runtime) => TryFinish(runtime, true);

        /// <inheritdoc />
        public void Clear()
        {
            for (int i = activeRuntimes.Count - 1; i >= 0; i--)
                activeRuntimes[i].MarkCancelled();
            activeRuntimes.Clear();
            grantedAbilities.Clear();
        }
        #endregion

        #region 校验与内部辅助
        // Cost/Cooldown 是序列化边界输入，运行时仍验证其 Duration Policy。
        private static bool HasValidActivationPolicies(GameplayAbilityData data) =>
            (data.CostEffect == null ||
             data.CostEffect.DurationType == E_GameEffectDurationType.Instant) &&
            (data.CooldownEffect == null ||
             data.CooldownEffect.DurationType == E_GameEffectDurationType.Duration ||
             data.CooldownEffect.DurationType == E_GameEffectDurationType.Infinite);

        // 只允许所属 Controller 的 Active 且未执行 Runtime；目标引用在任何 GE 提交前统一验证。
        private bool CanExecute(
            GameplayAbilityRuntime runtime,
            IReadOnlyList<AbilitySystemComponentBase> targets)
        {
            if (runtime == null || targets == null ||
                runtime.State != GameplayAbilityRuntimeState.Active ||
                runtime.HasExecuted || !activeRuntimes.Contains(runtime) ||
                !ReferenceEquals(runtime.Source, Owner) ||
                !HasValidEffectReferences(runtime.Spec.Data))
                return false;

            for (int i = 0; i < targets.Count; i++)
                if (targets[i] == null)
                    return false;
            return true;
        }

        // 作者列表为空合法，但任何空元素都会使本轮在提交前失败，避免配置错误造成半执行。
        private static bool HasValidEffectReferences(GameplayAbilityData data) =>
            HasNoNullItems(data.SelfEffects) && HasNoNullItems(data.TargetEffects);

        // 检查只读 GE 列表中不存在空资产引用。
        private static bool HasNoNullItems(IReadOnlyList<GameplayEffectData> effects)
        {
            for (int i = 0; i < effects.Count; i++)
                if (effects[i] == null)
                    return false;
            return true;
        }

        // 单项 GE 拒绝只影响该目标；持续 GE 成功时把现有 Runtime 交还外部管理。
        private static void ApplyEffectsToTarget(
            IReadOnlyList<GameplayEffectData> effects,
            GameplayAbilityRuntime runtime,
            AbilitySystemComponentBase target,
            ICollection<GameEffectRuntime> activeEffects)
        {
            for (int i = 0; i < effects.Count; i++)
                if (target.GameEffectCtrl.TryApply(
                        effects[i],
                        runtime.Source,
                        runtime.Level,
                        runtime.SetByCaller,
                        out GameEffectRuntime activeEffect) &&
                    activeEffect != null)
                    activeEffects.Add(activeEffect);
        }

        // End 与 Cancel 共用所有权检查和集合移除，仅最终状态不同。
        private bool TryFinish(GameplayAbilityRuntime runtime, bool cancelled)
        {
            int index = activeRuntimes.IndexOf(runtime);
            if (index < 0 || runtime == null ||
                runtime.State != GameplayAbilityRuntimeState.Active ||
                !ReferenceEquals(runtime.Source, Owner))
                return false;

            activeRuntimes.RemoveAt(index);
            if (cancelled) runtime.MarkCancelled();
            else runtime.MarkEnded();
            return true;
        }

        // 查找指定 Spec 是否仍有尚未结束或取消的激活实例。
        private bool HasActiveRuntime(GameplayAbilitySpec spec)
        {
            for (int i = 0; i < activeRuntimes.Count; i++)
                if (ReferenceEquals(activeRuntimes[i].Spec, spec))
                    return true;
            return false;
        }

        // 单调分配当前 Controller 内 Spec Handle；溢出时立即暴露生命周期异常。
        private int AllocateHandleId()
        {
            int id = nextHandleId;
            nextHandleId = checked(nextHandleId + 1);
            return id;
        }

        // 单调分配当前 Controller 内 ActivationId；溢出时立即暴露生命周期异常。
        private int AllocateActivationId()
        {
            int id = nextActivationId;
            nextActivationId = checked(nextActivationId + 1);
            return id;
        }
        #endregion
    }
}