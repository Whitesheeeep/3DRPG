#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.GameplayAbilitySystem;
using WS_Modules.GAS.GameplayEffect;
using WS_Modules.GAS.Generated;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayCue
{
    /// <summary>
    /// 使用场景中的真实 Source/Target Actor，验证 GE、GA 与 GameplayCue 的完整运行时链路。
    /// 这两个 Actor 被视为专用测试对象；每个阶段开始前会清理其 ASC 并重新导入测试 AttributeSet，保证重复测试从相同基准开始。
    /// </summary>
    public sealed class GameplayCueIntegrationOdinTester : MonoBehaviour
    {
        #region 测试输入

        [Title("运行时依赖")]
        [SerializeField, AssetsOnly, Required]
        private GameplayTagDatabase tagDatabase;

        [SerializeField, AssetsOnly, Required]
        private GameplayCueDatabase cueDatabase;

        [SerializeField, AssetsOnly, Required]
        private GameplayAttributeSet testAttributeSet;

        [SerializeField, Required]
        private GameObject sourceActor;

        [SerializeField, Required]
        private GameObject targetActor;

        [Title("GE 测试资产")]
        [SerializeField, AssetsOnly, Required]
        private GameplayEffectData instantEffect;

        [SerializeField, AssetsOnly, Required]
        private GameplayEffectData durationEffect;

        [SerializeField, AssetsOnly, Required]
        private GameplayEffectData infiniteEffect;

        [SerializeField, AssetsOnly, Required]
        private GameplayEffectData periodicEffect;

        [Title("GA 测试资产")]
        [SerializeField, AssetsOnly, Required]
        private GameplayAbilityData instantAbility;

        [SerializeField, AssetsOnly, Required]
        private GameplayAbilityData passiveAbility;

        [SerializeField, AssetsOnly, Required]
        private GameplayAbilityData projectileAbility;

        [Title("GE CueTag")]
        [SerializeField]
        private GameplayTag instantEffectCueTag;

        [SerializeField]
        private GameplayTag durationEffectCueTag;

        [SerializeField]
        private GameplayTag infiniteEffectCueTag;

        [SerializeField]
        private GameplayTag periodicEffectCueTag;

        [Title("GA CueTag")]
        [SerializeField]
        private GameplayTag instantAbilityCueTag;

        [SerializeField]
        private GameplayTag passiveAbilityCueTag;

        [SerializeField]
        private GameplayTag projectileAbilityCueTag;

        [Title("测试参数")]
        [SerializeField, Min(0.1f)]
        private float timeoutSeconds = 6f;

        [SerializeField, Min(0.1f), Tooltip("Infinite GE 与 Passive GA 在单阶段测试中保持 Active 的观察时间。")]
        private float activeCueObservationSeconds = 2f;

        #endregion

        #region 运行时状态

        private GameplayAbilitySystemComponent source;
        private GameplayAbilitySystemComponent target;
        private Coroutine testCoroutine;
        private bool driveTicks;
        private bool scenarioFailed;
        private int passCount;
        private int failCount;
        private int observedExecuteCount;
        private int observedActiveCount;
        private int observedRemoveCount;

        private readonly List<GameEffectRuntime> ownedEffects = new();
        private readonly List<GameplayAbilityRuntime> ownedAbilities = new();
        private readonly List<GameplayAbilityHandle> ownedHandles = new();
        private readonly List<GameplayCueRuntime> ownedCues = new();
        private readonly Dictionary<GameplayCueData, int> observedExecuteByData = new();

        /// <summary>GE 集成测试的独立阶段。</summary>
        private enum EffectCueStage
        {
            /// <summary>依次执行全部 GE 阶段。</summary>
            All,
            /// <summary>只执行 Instant GE。</summary>
            Instant,
            /// <summary>只执行 Duration GE。</summary>
            Duration,
            /// <summary>只执行 Infinite GE。</summary>
            Infinite,
            /// <summary>只执行 Periodic GE。</summary>
            Periodic
        }

        /// <summary>GA 集成测试的独立阶段。</summary>
        private enum AbilityCueStage
        {
            /// <summary>依次执行全部 GA 阶段。</summary>
            All,
            /// <summary>只执行 Instant GA。</summary>
            Instant,
            /// <summary>只执行 Passive GA。</summary>
            Passive,
            /// <summary>只执行 Projectile GA。</summary>
            Projectile
        }

        #endregion

        #region Unity 生命周期

        /// <summary>订阅测试 Probe 的生命周期通知。</summary>
        private void OnEnable()
        {
            GameplayCueVisualProbeBehaviour.CueObserved += OnCueObserved;
        }

        /// <summary>停止测试并解除 Probe 事件订阅。</summary>
        private void OnDisable()
        {
            StopRunningTest();
            GameplayCueVisualProbeBehaviour.CueObserved -= OnCueObserved;
        }

        /// <summary>驱动测试 Actor 的真实 GE/GA Tick，不伪造时间或直接调用内部计时逻辑。</summary>
        private void Update()
        {
            if (!driveTicks || source == null || target == null) return;
            source.Tick(Time.deltaTime);
            if (!ReferenceEquals(source, target))
                target.Tick(Time.deltaTime);
        }

        #endregion

        #region Odin 操作

        /// <summary>只检查 Tag、CueDatabase、GE/GA CueTag 和测试资产，不启动运行时测试。</summary>
        [Button("检查 GE/GA Cue 配置", ButtonSizes.Medium)]
        public void ValidateIntegrationConfiguration()
        {
            ResetResults();
            ValidateGameplayEffectConfiguration();
            ValidateGameplayAbilityConfiguration();
            Debug.Log($"[GAS Cue Integration][配置检查] 通过={passCount}，失败={failCount}", this);
        }

        /// <summary>执行 Instant、Duration、Infinite 和 Periodic GE 的 Cue 集成测试。</summary>
        [Button("执行 GE + Cue 测试", ButtonSizes.Large)]
        public void RunGameplayEffectCueTest()
        {
            StartScenario("GE + Cue", () => RunGameplayEffectCueScenario(EffectCueStage.All));
        }

        /// <summary>只执行 Instant GE 与 Execute Cue 阶段，便于观察一次性表现。</summary>
        [Button("GE：Instant + Cue", ButtonSizes.Medium)]
        public void RunInstantEffectCueTest()
        {
            StartScenario("GE Instant + Cue", () => RunGameplayEffectCueScenario(EffectCueStage.Instant));
        }

        /// <summary>只执行 Duration GE 与 Active Cue 阶段，便于观察持续表现和到期回收。</summary>
        [Button("GE：Duration + Cue", ButtonSizes.Medium)]
        public void RunDurationEffectCueTest()
        {
            StartScenario("GE Duration + Cue", () => RunGameplayEffectCueScenario(EffectCueStage.Duration));
        }

        /// <summary>只执行 Infinite GE 与 Active/Remove Cue 阶段，便于观察持续挂载。</summary>
        [Button("GE：Infinite + Cue", ButtonSizes.Medium)]
        public void RunInfiniteEffectCueTest()
        {
            StartScenario("GE Infinite + Cue", () => RunGameplayEffectCueScenario(EffectCueStage.Infinite));
        }

        /// <summary>只执行 Periodic GE 与周期 Execute Cue 阶段，便于观察周期表现。</summary>
        [Button("GE：Periodic + Cue", ButtonSizes.Medium)]
        public void RunPeriodicEffectCueTest()
        {
            StartScenario("GE Periodic + Cue", () => RunGameplayEffectCueScenario(EffectCueStage.Periodic));
        }

        /// <summary>执行 Instant、Passive 和 Projectile GA 的 Cue 集成测试。</summary>
        [Button("执行 GA + Cue 测试", ButtonSizes.Large)]
        public void RunGameplayAbilityCueTest()
        {
            StartScenario("GA + Cue", () => RunGameplayAbilityCueScenario(AbilityCueStage.All));
        }

        /// <summary>只执行 Instant GA 与 Source Execute Cue 阶段。</summary>
        [Button("GA：Instant + Cue", ButtonSizes.Medium)]
        public void RunInstantAbilityCueTest()
        {
            StartScenario("GA Instant + Cue", () => RunGameplayAbilityCueScenario(AbilityCueStage.Instant));
        }

        /// <summary>只执行 Passive GA 与 Source Active/Remove Cue 阶段，保持表现便于观察。</summary>
        [Button("GA：Passive + Cue", ButtonSizes.Medium)]
        public void RunPassiveAbilityCueTest()
        {
            StartScenario("GA Passive + Cue", () => RunGameplayAbilityCueScenario(AbilityCueStage.Passive));
        }

        /// <summary>只执行 Projectile GA 与 Target 命中 Execute Cue 阶段。</summary>
        [Button("GA：Projectile + Cue", ButtonSizes.Medium)]
        public void RunProjectileAbilityCueTest()
        {
            StartScenario("GA Projectile + Cue", () => RunGameplayAbilityCueScenario(AbilityCueStage.Projectile));
        }

        /// <summary>按真实 ASC Tick 顺序执行 GE、GA 和 Cue 全部集成测试。</summary>
        [Button("执行完整 GE/GA/Cue 测试", ButtonSizes.Large)]
        public void RunFullCueIntegrationTest()
        {
            StartScenario("GE/GA/Cue 完整测试", RunFullCueScenario);
        }

        /// <summary>停止当前协程并清理本测试器创建的 GE、GA 和 Cue Runtime。</summary>
        [Button("清理集成测试", ButtonSizes.Medium)]
        public void CleanupIntegrationTest()
        {
            StopRunningTest();
            Debug.Log("[GAS Cue Integration] 已清理本测试器创建的运行时对象。", this);
        }

        #endregion

        #region 测试流程

        /// <summary>运行完整 GE/GA 测试；每个子阶段结束后只清理本测试器拥有的句柄。</summary>
        private IEnumerator RunFullCueScenario()
        {
            yield return RunGameplayEffectCueScenario();
            CleanupOwnedRuntime();
            yield return RunGameplayAbilityCueScenario();
        }

        /// <summary>验证 Instant、Duration、Infinite 和 Periodic GE 的结算与 Cue 生命周期。</summary>
        private IEnumerator RunGameplayEffectCueScenario(EffectCueStage stage = EffectCueStage.All)
        {
            bool valid;
            switch (stage)
            {
                case EffectCueStage.Instant:
                    valid = ValidateEffectCue("GE Instant", instantEffect, instantEffectCueTag, E_GameEffectDurationType.Instant);
                    break;
                case EffectCueStage.Duration:
                    valid = ValidateEffectCue("GE Duration", durationEffect, durationEffectCueTag, E_GameEffectDurationType.Duration);
                    break;
                case EffectCueStage.Infinite:
                    valid = ValidateEffectCue("GE Infinite", infiniteEffect, infiniteEffectCueTag, E_GameEffectDurationType.Infinite);
                    break;
                case EffectCueStage.Periodic:
                    valid = ValidateEffectCue("GE Periodic", periodicEffect, periodicEffectCueTag, E_GameEffectDurationType.Duration);
                    if (valid)
                    {
                        bool periodic = periodicEffect.IsPeriodic;
                        Report("GE Periodic Period", periodic, $"Period={periodicEffect.Period}");
                        valid &= periodic;
                    }
                    break;
                default:
                    valid = ValidateGameplayEffectConfiguration();
                    break;
            }
            if (!valid) yield break;

            if (stage == EffectCueStage.All || stage == EffectCueStage.Instant)
            {
                float healthBefore = ReadValue(GameplayAttributes.Attribute_Health, "GE Instant 前 Health");
                int executeBefore = observedExecuteCount;
                bool applied = target.TryApplyEffect(instantEffect, source, out GameEffectRuntime instantRuntime);
                Check("GE Instant 应用", applied && instantRuntime == null, $"HealthBefore={healthBefore}");
                yield return null;
                float healthAfter = ReadValue(GameplayAttributes.Attribute_Health, "GE Instant 后 Health");
                Check("GE Instant Health", Approximately(healthAfter, healthBefore - 30f), $"实际={healthAfter}，预期={healthBefore - 30f}");
                Check("GE Instant Execute Cue", observedExecuteCount > executeBefore, "需要 VisualProbe 收到 Execute 回调并完成回收");
                if (stage == EffectCueStage.Instant) yield break;
            }

            if (stage == EffectCueStage.All || stage == EffectCueStage.Duration)
            {
                float durationHealthBefore = ReadValue(GameplayAttributes.Attribute_Health, "GE Duration 前 Health");
                bool durationApplied = target.TryApplyEffect(durationEffect, source, out GameEffectRuntime durationRuntime);
                Check("GE Duration 应用", durationApplied && durationRuntime != null, "Duration 必须返回 Active Runtime");
                if (durationRuntime == null) yield break;
                ownedEffects.Add(durationRuntime);
                GameplayCueRuntime durationCue = FindActiveCue(target, durationRuntime, durationEffectCueTag);
                Check("GE Duration Active Cue", durationCue != null && !durationCue.IsReleased, "Duration Cue 应在 GE 生命周期内保持 Active");
                float durationHealth = ReadValue(GameplayAttributes.Attribute_Health, "GE Duration 后 Health");
                float durationMaxHealth = ReadValue(GameplayAttributes.Attribute_MaxHealth, "GE Duration 后 MaxHealth");
                Check("GE Duration MaxHealth", Approximately(durationMaxHealth, 60f), $"实际={durationMaxHealth}，预期=60");
                Check("GE Duration Health Clamp", Approximately(durationHealth, Mathf.Min(durationHealthBefore, 60f)), $"实际={durationHealth}");
                yield return WaitForCondition(
                    () => !ContainsEffect(durationRuntime),
                    Mathf.Max(timeoutSeconds, durationEffect.Duration + 1f));
                Check("GE Duration 到期", waitResult, "Active GE 应在 Duration 后移除");
                Check("GE Duration Cue 移除", FindActiveCue(target, durationRuntime, durationEffectCueTag) == null, "到期后 Active Cue 应回收");
                Check("GE Duration MaxHealth 恢复", Approximately(ReadValue(GameplayAttributes.Attribute_MaxHealth, "GE Duration 到期 MaxHealth"), 100f), "Stat 应恢复基础值");
                if (stage == EffectCueStage.Duration) yield break;
            }

            if (stage == EffectCueStage.All || stage == EffectCueStage.Infinite)
            {
                float armorBefore = ReadValue(GameplayAttributes.Attribute_Armor, "GE Infinite 前 Armor");
                bool infiniteApplied = target.TryApplyEffect(infiniteEffect, source, out GameEffectRuntime infiniteRuntime);
                Check("GE Infinite 应用", infiniteApplied && infiniteRuntime != null, "Infinite 必须返回 Active Runtime");
                if (infiniteRuntime == null) yield break;
                ownedEffects.Add(infiniteRuntime);
                GameplayCueRuntime infiniteCue = FindActiveCue(target, infiniteRuntime, infiniteEffectCueTag);
                Check("GE Infinite Active Cue", infiniteCue != null && !infiniteCue.IsReleased, "Infinite Cue 应持续存在");
                float armorActive = ReadValue(GameplayAttributes.Attribute_Armor, "GE Infinite 后 Armor");
                Check("GE Infinite Armor", Approximately(armorActive, armorBefore + 10f), $"实际={armorActive}，预期={armorBefore + 10f}");
                yield return new WaitForSecondsRealtime(Mathf.Max(0.1f, activeCueObservationSeconds));
                Check("GE Infinite 未自动到期", ContainsEffect(infiniteRuntime), "Infinite 必须等待显式移除");
                bool removedInfinite = target.TryRemoveEffect(infiniteRuntime);
                Check("GE Infinite 手动移除", removedInfinite && !ContainsEffect(infiniteRuntime), "显式移除应结束 GE 和 Cue");
                Check("GE Infinite Cue 移除", FindActiveCue(target, infiniteRuntime, infiniteEffectCueTag) == null, "显式移除后 Active Cue 应回收");
                Check("GE Infinite Armor 恢复", Approximately(ReadValue(GameplayAttributes.Attribute_Armor, "GE Infinite 移除 Armor"), armorBefore), "Armor 应恢复到移除前值");
                ownedEffects.Remove(infiniteRuntime);
                if (stage == EffectCueStage.Infinite) yield break;
            }

            if (stage == EffectCueStage.All || stage == EffectCueStage.Periodic)
            {
                float periodicHealthBefore = ReadValue(GameplayAttributes.Attribute_Health, "GE Periodic 前 Health");
                int periodicExecuteBefore = observedExecuteCount;
                bool periodicApplied = target.TryApplyEffect(periodicEffect, source, out GameEffectRuntime periodicRuntime);
                Check("GE Periodic 应用", periodicApplied && periodicRuntime != null, "Periodic Duration 必须返回 Active Runtime");
                if (periodicRuntime == null) yield break;
                ownedEffects.Add(periodicRuntime);
                yield return WaitForCondition(
                    () => !ContainsEffect(periodicRuntime),
                    Mathf.Max(timeoutSeconds, periodicEffect.Duration + 1f));
                Check("GE Periodic 到期", waitResult, "周期 GE 应在 Duration 后移除");
                float periodicHealthAfter = ReadValue(GameplayAttributes.Attribute_Health, "GE Periodic 到期 Health");
                Check("GE Periodic 结算", periodicHealthAfter < periodicHealthBefore, $"实际={periodicHealthAfter}，开始={periodicHealthBefore}");
                Check("GE Periodic Execute Cue", observedExecuteCount > periodicExecuteBefore, "每次周期结算应发布 Execute Cue");
                Check("GE Periodic Cue 清理", FindActiveCue(target, periodicRuntime, periodicEffectCueTag) == null, "Periodic 到期不应残留 Active Cue");
            }
        }

        /// <summary>验证 Instant、Passive 和 Projectile GA 的 Cue 触发与真实 Tick 行为。</summary>
        private IEnumerator RunGameplayAbilityCueScenario(AbilityCueStage stage = AbilityCueStage.All)
        {
            bool valid;
            switch (stage)
            {
                case AbilityCueStage.Instant:
                    valid = ValidateAbilityCue("GA Instant", instantAbility, instantAbilityCueTag);
                    break;
                case AbilityCueStage.Passive:
                    valid = ValidateAbilityCue("GA Passive", passiveAbility, passiveAbilityCueTag);
                    break;
                case AbilityCueStage.Projectile:
                    valid = ValidateAbilityCue("GA Projectile", projectileAbility, projectileAbilityCueTag);
                    break;
                default:
                    valid = ValidateGameplayAbilityConfiguration();
                    break;
            }
            if (!valid) yield break;

            if (stage == AbilityCueStage.All || stage == AbilityCueStage.Instant)
            {
                float healthBefore = ReadValue(source, GameplayAttributes.Attribute_Health, "GA Instant 前 Source Health");
                float mpBefore = ReadValue(source, GameplayAttributes.Attribute_MP, "GA Instant 前 Source MP");
                GameplayAbilityHandle instantHandle = source.GiveAbility(instantAbility, 1);
                ownedHandles.Add(instantHandle);
                int executeBefore = observedExecuteCount;
                bool instantActivated = source.TryActivateAbility(instantHandle, out GameplayAbilityRuntime instantRuntime);
                Check("GA Instant 激活", instantActivated && instantRuntime != null && instantRuntime.State == GameplayAbilityRuntimeState.Ended, "同步 GA 返回前必须结束");
                Check("GA Instant Cost", Approximately(ReadValue(source, GameplayAttributes.Attribute_MP, "GA Instant 后 Source MP"), mpBefore - 10f), "Source MP 应扣除 Cost");
                Check("GA Instant Effect", Approximately(ReadValue(source, GameplayAttributes.Attribute_Health, "GA Instant 后 Source Health"), healthBefore - 30f), "Source Health 应结算 Instant Effect");
                Check("GA Instant Execute Cue", observedExecuteCount > executeBefore, "需要观察到 GA Execute Cue");
                if (stage == AbilityCueStage.Instant) yield break;
            }

            if (stage == AbilityCueStage.All || stage == AbilityCueStage.Passive)
            {
                float armorBefore = ReadValue(source, GameplayAttributes.Attribute_Armor, "GA Passive 前 Source Armor");
                GameplayAbilityHandle passiveHandle = source.GiveAbility(passiveAbility, 1);
                ownedHandles.Add(passiveHandle);
                int activeBefore = observedActiveCount;
                bool passiveActivated = source.TryActivateAbility(passiveHandle, out GameplayAbilityRuntime passiveRuntime);
                Check("GA Passive 激活", passiveActivated && passiveRuntime != null && passiveRuntime.State == GameplayAbilityRuntimeState.Active, "Passive Runtime 应保持 Active");
                if (passiveRuntime != null) ownedAbilities.Add(passiveRuntime);
                GameplayCueRuntime passiveCue = FindActiveCue(source, passiveRuntime, passiveAbilityCueTag);
                Check("GA Passive Active Cue", passiveCue != null && observedActiveCount > activeBefore, "Passive Cue 应在 Runtime 存续期间显示");
                Check("GA Passive Armor", Approximately(ReadValue(source, GameplayAttributes.Attribute_Armor, "GA Passive 后 Source Armor"), armorBefore + 10f), "Passive Effect 应修改 Source Armor");
                yield return new WaitForSecondsRealtime(Mathf.Max(0.1f, activeCueObservationSeconds));
                bool ended = passiveRuntime != null && source.TryEndAbility(passiveRuntime);
                Check("GA Passive End", ended, "外部 End 应结束 Passive Runtime");
                Check("GA Passive Cue 移除", FindActiveCue(source, passiveRuntime, passiveAbilityCueTag) == null, "Passive End 后 Source Cue 应移除");
                Check("GA Passive Armor 恢复", Approximately(ReadValue(source, GameplayAttributes.Attribute_Armor, "GA Passive 结束 Source Armor"), armorBefore), "Passive Effect 应被移除");
                if (stage == AbilityCueStage.Passive) yield break;
            }

            if (stage == AbilityCueStage.All || stage == AbilityCueStage.Projectile)
            {
                Collider targetCollider = targetActor.GetComponentInChildren<Collider>();
                Check("GA Projectile Target Collider", targetCollider != null, "Target Actor 需要 Collider 才能验证真实命中");
                GameplayAbilityHandle projectileHandle = source.GiveAbility(projectileAbility, 1);
                ownedHandles.Add(projectileHandle);
                float projectileHealthBefore = ReadValue(GameplayAttributes.Attribute_Health, "GA Projectile 前 Health");
                int projectileExecuteBefore = observedExecuteCount;
                bool projectileActivated = source.TryActivateAbility(projectileHandle, out GameplayAbilityRuntime projectileRuntime);
                Check("GA Projectile 激活", projectileActivated && projectileRuntime != null && projectileRuntime.State == GameplayAbilityRuntimeState.Ended, "投射物 GA 创建投射物后应立即结束");
                yield return WaitForCondition(
                    () => ReadValue(GameplayAttributes.Attribute_Health, "GA Projectile 等待 Health") < projectileHealthBefore,
                    timeoutSeconds);
                Check("GA Projectile 命中", waitResult, "投射物应通过真实物理命中 Target");
                Check("GA Projectile Effect", Approximately(ReadValue(GameplayAttributes.Attribute_Health, "GA Projectile 命中 Health"), projectileHealthBefore - 30f), "命中后应应用 Projectile Effects");
                Check("GA Projectile Execute Cue", observedExecuteCount > projectileExecuteBefore, "命中点应发布 GA Projectile Execute Cue");
            }
        }

        #endregion

        #region 配置校验

        /// <summary>检查运行时 Actor、数据库、Tag 数据库和 AttributeSet 是否齐全。</summary>
        /// <param name="requireActors">是否同时要求场景 Actor 和 Play Mode。</param>
        /// <returns>公共配置完整时返回 true。</returns>
        private bool ValidateCommonConfiguration(bool requireActors)
        {
            bool valid = true;
            if (tagDatabase == null)
            {
                Report("TagDatabase", false, "未配置 GameplayTagDatabase；请先绑定并 Bake Tag Database。");
                valid = false;
            }
            else
            {
                GameplayTagManager.Instance.Initialize(tagDatabase);
                Report("TagDatabase", true, tagDatabase.name);
            }

            if (cueDatabase == null)
            {
                Report("CueDatabase", false, "未配置 GameplayCueDatabase；请先注册事件 CueData。");
                valid = false;
            }
            else
            {
                GameplayCueManager.Instance.Initialize(cueDatabase);
                Report("CueDatabase", true, $"注册数量={cueDatabase.Count}");
            }

            if (testAttributeSet == null)
            {
                Report("GameplayAttributeTestSet", false, "未配置 GameplayAttributeTestSet。");
                valid = false;
            }

            if (requireActors)
            {
                if (!Application.isPlaying)
                {
                    Report("Play Mode", false, "集成测试必须在 Play Mode 执行。");
                    valid = false;
                }
                if (sourceActor == null || targetActor == null)
                {
                    Report("Source/Target Actor", false, "必须配置两个真实 Actor。");
                    valid = false;
                }
            }

            return valid;
        }

        /// <summary>检查 GE 测试资产和每个 GE 声明的事件 CueTag。</summary>
        /// <returns>GE 配置完整时返回 true。</returns>
        private bool ValidateGameplayEffectConfiguration()
        {
            bool valid = ValidateCommonConfiguration(false);
            valid &= ValidateEffectCue("GE Instant", instantEffect, instantEffectCueTag, E_GameEffectDurationType.Instant);
            valid &= ValidateEffectCue("GE Duration", durationEffect, durationEffectCueTag, E_GameEffectDurationType.Duration);
            valid &= ValidateEffectCue("GE Infinite", infiniteEffect, infiniteEffectCueTag, E_GameEffectDurationType.Infinite);
            valid &= ValidateEffectCue("GE Periodic", periodicEffect, periodicEffectCueTag, E_GameEffectDurationType.Duration);
            if (periodicEffect != null)
            {
                bool periodic = periodicEffect.IsPeriodic;
                Report("GE Periodic Period", periodic, $"Period={periodicEffect.Period}");
                valid &= periodic;
            }
            return valid;
        }

        /// <summary>检查 GA 测试资产和每个 GA 声明的事件 CueTag。</summary>
        /// <returns>GA 配置完整时返回 true。</returns>
        private bool ValidateGameplayAbilityConfiguration()
        {
            bool valid = ValidateCommonConfiguration(false);
            valid &= ValidateAbilityCue("GA Instant", instantAbility, instantAbilityCueTag);
            valid &= ValidateAbilityCue("GA Passive", passiveAbility, passiveAbilityCueTag);
            valid &= ValidateAbilityCue("GA Projectile", projectileAbility, projectileAbilityCueTag);
            return valid;
        }

        /// <summary>检查单个 GE 的 Duration 类型、CueTag 有效性及数据库映射。</summary>
        /// <param name="label">测试名称。</param>
        /// <param name="effect">待检查的 GE。</param>
        /// <param name="expectedTag">该 GE 预期声明的事件 CueTag。</param>
        /// <param name="durationType">预期 Duration 类型。</param>
        /// <returns>配置合法时返回 true。</returns>
        private bool ValidateEffectCue(string label, GameplayEffectData effect, GameplayTag expectedTag, E_GameEffectDurationType durationType)
        {
            bool valid = effect != null;
            Report($"{label} 资产", valid, effect == null ? "未配置 GameplayEffectData。" : effect.name);
            if (!valid) return false;
            bool typeValid = effect.DurationType == durationType;
            Report($"{label} Duration", typeValid, $"实际={effect.DurationType}，预期={durationType}");
            valid &= typeValid;
            valid &= ValidateCueDeclaration(label, effect.CueTags, expectedTag);
            return valid;
        }

        /// <summary>检查单个 GA 的 CueTag 声明及基础资产引用。</summary>
        /// <param name="label">测试名称。</param>
        /// <param name="ability">待检查的 GA。</param>
        /// <param name="expectedTag">该 GA 预期声明的事件 CueTag。</param>
        /// <returns>配置合法时返回 true。</returns>
        private bool ValidateAbilityCue(string label, GameplayAbilityData ability, GameplayTag expectedTag)
        {
            bool valid = ability != null;
            Report($"{label} 资产", valid, ability == null ? "未配置 GameplayAbilityData。" : ability.name);
            if (!valid) return false;
            return ValidateCueDeclaration(label, ability.CueTags, expectedTag);
        }

        /// <summary>检查 CueTag 是否已 Bake、已注册 CueDatabase 且被当前 GE/GA 声明。</summary>
        /// <param name="label">测试名称。</param>
        /// <param name="declaredTags">GE 或 GA 声明的 CueTag 列表。</param>
        /// <param name="expectedTag">预期事件标签。</param>
        /// <returns>标签、映射和声明均正确时返回 true。</returns>
        private bool ValidateCueDeclaration(string label, IReadOnlyList<GameplayTag> declaredTags, GameplayTag expectedTag)
        {
            bool valid = expectedTag.IsValid && GameplayTagManager.Instance.IsValidTag(expectedTag);
            Report($"{label} CueTag Bake", valid, valid ? expectedTag.ToString() : $"{expectedTag} 未在当前 TagDatabase 中找到，请创建并 Bake。");
            if (!valid) return false;

            bool mapped = GameplayCueManager.Instance.TryGetCue(expectedTag, out GameplayCueData cueData);
            Report($"{label} CueData 映射", mapped, mapped ? cueData.name : $"CueDatabase 中没有 {expectedTag} 对应的 CueData。");
            if (!mapped) return false;

            bool declared = declaredTags != null && ContainsTag(declaredTags, expectedTag);
            Report($"{label} 资产 CueTags", declared, declared ? "已声明" : $"资产未声明 {expectedTag}。");
            if (cueData.FallbackPrefab != null && cueData.FallbackPrefab.GetComponentInChildren<GameplayCueVisualProbeBehaviour>() == null)
                Debug.LogWarning($"[GAS Cue Integration][Warning] CueData '{cueData.name}' 的 Fallback Prefab 没有 GameplayCueVisualProbeBehaviour，无法记录可视化回调。", cueData);
            return declared;
        }

        #endregion

        #region 运行时准备与清理

        /// <summary>启动一个新的集成测试协程，并阻止同一 Tester 同时运行多个测试。</summary>
        /// <param name="name">测试名称。</param>
        /// <param name="scenario">测试协程工厂。</param>
        private void StartScenario(string name, Func<IEnumerator> scenario)
        {
            if (testCoroutine != null)
            {
                Debug.LogWarning("[GAS Cue Integration] 已有测试正在运行，请先点击清理集成测试。", this);
                return;
            }
            ResetResults();
            if (!ValidateCommonConfiguration(true)) return;
            if (!TryPrepareActors()) return;
            testCoroutine = StartCoroutine(RunScenario(name, scenario));
        }

        /// <summary>执行测试协程并在结束时输出汇总、释放 Tick 和清理本次所有权。</summary>
        /// <param name="name">测试名称。</param>
        /// <param name="scenario">具体场景协程。</param>
        private IEnumerator RunScenario(string name, Func<IEnumerator> scenario)
        {
            driveTicks = true;
            yield return scenario();
            driveTicks = false;
            CleanupOwnedRuntime();
            testCoroutine = null;
            string result = scenarioFailed ? "FAIL" : "PASS";
            Debug.Log($"[GAS Cue Integration][{name}] {result}：通过={passCount}，失败={failCount}", this);
        }

        /// <summary>准备专用测试 Actor ASC，清理旧状态后重新导入测试 AttributeSet。</summary>
        /// <returns>两个 ASC 都可用于测试时返回 true。</returns>
        private bool TryPrepareActors()
        {
            source = sourceActor == null ? null : sourceActor.GetComponent<GameplayAbilitySystemComponent>();
            target = targetActor == null ? null : targetActor.GetComponent<GameplayAbilitySystemComponent>();
            bool valid = source != null && target != null && !ReferenceEquals(source, target);
            Report("真实 Source/Target ASC", valid, valid ? "已找到" : "Actor 必须分别挂载 GameplayAbilitySystemComponent。");
            if (!valid) return false;

            valid = source.Attributes != null && target.Attributes != null &&
                    source.Cues != null && target.Cues != null &&
                    source.Abilities != null && target.Abilities != null;
            Report("ASC Awake 初始化", valid, valid ? "Attribute、Cue 和 Ability Controller 均已创建" : "请确认 Actor 已激活并完成 ASC.Awake。");
            if (!valid) return false;

            // 集成测试 Actor 专用于测试，阶段开始时清理全部 GAS 状态，避免上一次伤害、Modifier、Cue 或 Ability 影响本次基准。
            source.Clear();
            target.Clear();
            source.Initialize(new[] { testAttributeSet });
            target.Initialize(new[] { testAttributeSet });
            valid = source.IsInitialized && target.IsInitialized;
            Report("ASC AttributeSet 初始化", valid, valid ? "Source/Target 均已初始化" : "请确认 GameplayAttributeTestSet 可用。");
            return valid;
        }

        /// <summary>停止当前测试协程，关闭 Tick 驱动并释放本测试器拥有的资源。</summary>
        private void StopRunningTest()
        {
            driveTicks = false;
            if (testCoroutine != null)
            {
                StopCoroutine(testCoroutine);
                testCoroutine = null;
            }
            CleanupOwnedRuntime();
        }

        /// <summary>按反向顺序移除本次测试产生的 Cue、GE、GA Runtime 和 Ability Spec。</summary>
        private void CleanupOwnedRuntime()
        {
            if (source != null)
            {
                for (int i = ownedAbilities.Count - 1; i >= 0; i--)
                    if (ownedAbilities[i] != null && ownedAbilities[i].State == GameplayAbilityRuntimeState.Active)
                        source.TryCancelAbility(ownedAbilities[i]);
                for (int i = ownedHandles.Count - 1; i >= 0; i--)
                    source.TryRemoveAbility(ownedHandles[i]);
            }

            if (target != null)
            {
                for (int i = ownedEffects.Count - 1; i >= 0; i--)
                    if (ownedEffects[i] != null)
                        target.TryRemoveEffect(ownedEffects[i]);
            }

            // Cue 的 Owner 是请求 Target；Self Ability 的 Target 等于 Source，不能固定从场景 Target 回收。
            for (int i = ownedCues.Count - 1; i >= 0; i--)
            {
                GameplayCueRuntime cue = ownedCues[i];
                if (cue == null) continue;
                GameplayAbilitySystemComponent cueOwner = cue.Target;
                if (cueOwner != null)
                    cueOwner.Cues.TryRemove(cue);
            }

            ownedAbilities.Clear();
            ownedHandles.Clear();
            ownedEffects.Clear();
            ownedCues.Clear();
        }

        #endregion

        #region 查询与断言

        /// <summary>接收可视化 Probe 事件，并按 Cue 资源累计 Execute、Active 和 Remove 次数。</summary>
        /// <param name="runtime">触发回调的 Cue Runtime。</param>
        /// <param name="eventType">收到的 Cue 生命周期阶段。</param>
        private void OnCueObserved(GameplayCueRuntime runtime, GameplayCueEventType eventType)
        {
            if (runtime == null) return;
            switch (eventType)
            {
                case GameplayCueEventType.Execute:
                    observedExecuteCount++;
                    if (!observedExecuteByData.ContainsKey(runtime.CueData)) observedExecuteByData.Add(runtime.CueData, 0);
                    observedExecuteByData[runtime.CueData]++;
                    break;
                case GameplayCueEventType.Active:
                    observedActiveCount++;
                    break;
                case GameplayCueEventType.Remove:
                    observedRemoveCount++;
                    break;
            }
        }

        /// <summary>在指定 ASC 的 Active Cue 列表中按来源 Runtime 和 CueTag 查找表现。</summary>
        /// <param name="cueOwner">发布请求并实际持有 Cue 的 ASC。</param>
        /// <param name="origin">产生 Cue 的 GE 或 GA Runtime。</param>
        /// <param name="tag">事件 CueTag。</param>
        /// <returns>找到的 Active Cue；否则返回 null。</returns>
        private GameplayCueRuntime FindActiveCue(
            GameplayAbilitySystemComponent cueOwner,
            object origin,
            GameplayTag tag)
        {
            if (cueOwner == null || origin == null) return null;
            IReadOnlyList<GameplayCueRuntime> cues = cueOwner.Cues.ActiveCues;
            for (int i = 0; i < cues.Count; i++)
            {
                GameplayCueRuntime cue = cues[i];
                if (cue.CueTag != tag) continue;
                if (origin is GameEffectRuntime effect && ReferenceEquals(cue.EffectRuntime, effect))
                {
                    TrackOwnedCue(cue);
                    return cue;
                }
                if (origin is GameplayAbilityRuntime ability && ReferenceEquals(cue.AbilityRuntime, ability))
                {
                    TrackOwnedCue(cue);
                    return cue;
                }
            }
            return null;
        }

        /// <summary>记录本测试器发现的 Active Cue，确保异常路径也能在清理阶段回收。</summary>
        /// <param name="cue">待记录的 Active Cue。</param>
        private void TrackOwnedCue(GameplayCueRuntime cue)
        {
            if (cue != null && !ownedCues.Contains(cue))
                ownedCues.Add(cue);
        }

        /// <summary>判断指定 GE Runtime 是否仍在 Target ASC 的 ActiveEffects 列表中。</summary>
        /// <param name="runtime">待查询的 GE Runtime。</param>
        /// <returns>仍然存在时返回 true。</returns>
        private bool ContainsEffect(GameEffectRuntime runtime)
        {
            if (runtime == null || target == null) return false;
            IReadOnlyList<GameEffectRuntime> effects = target.ActiveEffects;
            for (int i = 0; i < effects.Count; i++)
                if (ReferenceEquals(effects[i], runtime)) return true;
            return false;
        }

        /// <summary>读取默认 Target ASC 的 CurrentValue，供 GE 目标结算测试使用。</summary>
        /// <param name="attribute">待读取的 Attribute。</param>
        /// <param name="label">日志名称。</param>
        /// <returns>读取到的值；读取失败时返回 NaN。</returns>
        private float ReadValue(GameplayAttribute attribute, string label)
        {
            return ReadValue(target, attribute, label);
        }

        /// <summary>读取指定 ASC 的 CurrentValue，并在 Attribute 缺失时记录所属对象。</summary>
        /// <param name="owner">实际持有 Attribute 的 ASC。</param>
        /// <param name="attribute">待读取的 Attribute。</param>
        /// <param name="label">日志名称。</param>
        /// <returns>读取到的值；读取失败时返回 NaN。</returns>
        private float ReadValue(
            GameplayAbilitySystemComponent owner,
            GameplayAttribute attribute,
            string label)
        {
            if (owner != null && owner.TryGetCurrentValue(attribute, out float value)) return value;
            string ownerName = ReferenceEquals(owner, source) ? "Source" : "Target";
            Report(label, false, $"Attribute {attribute.Id} 不存在于 {ownerName} AttributeSet。");
            return float.NaN;
        }

        /// <summary>记录一次通过或失败的集成断言。</summary>
        /// <param name="label">断言名称。</param>
        /// <param name="passed">断言结果。</param>
        /// <param name="detail">实际值、预期值或修复提示。</param>
        private void Check(string label, bool passed, string detail)
        {
            Report(label, passed, detail);
            if (!passed) scenarioFailed = true;
        }

        /// <summary>输出配置检查或运行时断言结果。</summary>
        /// <param name="label">检查名称。</param>
        /// <param name="passed">是否通过。</param>
        /// <param name="detail">检查详情。</param>
        private void Report(string label, bool passed, string detail)
        {
            if (passed)
            {
                passCount++;
                Debug.Log($"[GAS Cue Integration][PASS] {label}: {detail}", this);
            }
            else
            {
                failCount++;
                Debug.LogError($"[GAS Cue Integration][FAIL] {label}: {detail}", this);
            }
        }

        /// <summary>判断标签列表中是否存在精确 TagId。</summary>
        /// <param name="tags">待查询标签列表。</param>
        /// <param name="tag">目标标签。</param>
        /// <returns>存在时返回 true。</returns>
        private static bool ContainsTag(IReadOnlyList<GameplayTag> tags, GameplayTag tag)
        {
            if (tags == null) return false;
            for (int i = 0; i < tags.Count; i++)
                if (tags[i] == tag) return true;
            return false;
        }

        /// <summary>使用浮点容差比较 Attribute 结果。</summary>
        /// <param name="actual">实际值。</param>
        /// <param name="expected">预期值。</param>
        /// <returns>数值足够接近时返回 true。</returns>
        private static bool Approximately(float actual, float expected) =>
            !float.IsNaN(actual) && !float.IsNaN(expected) && Mathf.Abs(actual - expected) < 0.01f;

        /// <summary>重置本次测试的计数器和失败状态。</summary>
        private void ResetResults()
        {
            scenarioFailed = false;
            passCount = 0;
            failCount = 0;
            observedExecuteCount = 0;
            observedActiveCount = 0;
            observedRemoveCount = 0;
            observedExecuteByData.Clear();
        }

        #endregion

        #region 协程辅助

        private bool waitResult;

        /// <summary>等待真实 ASC Tick 推动某个条件成立，并提供超时保护。</summary>
        /// <param name="condition">完成条件。</param>
        /// <param name="timeout">最大等待秒数。</param>
        private IEnumerator WaitForCondition(Func<bool> condition, float timeout)
        {
            waitResult = false;
            float elapsed = 0f;
            while (elapsed < timeout)
            {
                if (condition())
                {
                    waitResult = true;
                    yield break;
                }
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            waitResult = condition();
        }

        #endregion
    }
}
#endif
