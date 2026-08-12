#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using RPG.Markers;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.GameplayAbilitySystem;
using WS_Modules.GAS.GameplayCue;
using WS_Modules.GAS.Generated;
using WS_Modules.GAS.TAG;
using WS_Modules.Pooling;

namespace WS_Modules.GAS.AbilitySystemComponent
{
    /// <summary>
    /// 通过独立的真实 Unity 场景周期验证 ASC、GA、GE、Cue 与物理投射物。
    /// 每个技能场景均重建 Source 和 Target，避免资源、Cooldown 与 Runtime 状态相互污染。
    /// </summary>
    public sealed class GameplayAbilitySystemComponentOdinTester : MonoBehaviour
    {
        #region 测试输入

        [Title("ASC 初始化")]
        [SerializeField, AssetsOnly, Required, Tooltip("Source 与 Target 初始化时导入的全部 AttributeSet。")]
        private List<GameplayAttributeSet> attributeSets = new();

        [SerializeField, AssetsOnly, Required, Tooltip("为 AbilityTags、CancelTags 与 Owner Tags 提供运行时层级关系。")]
        private GameplayTagDatabase tagDatabase;

        [SerializeField, AssetsOnly, Required, Tooltip("为真实周期提供 AbilityId 到 Data 的运行时映射。")]
        private GameplayAbilityDatabase abilityDatabase;

        [SerializeField, AssetsOnly, Required, Tooltip("为真实技能周期提供 CueTag 到 CueData 的运行时映射。")]
        private GameplayCueDatabase cueDatabase;

        [Title("真实技能 SO")]
        [SerializeField, AssetsOnly, Required]
        private InstantGameplayAbilityData instantAbility;

        [SerializeField, AssetsOnly, Required]
        private AsynchronousGameplayAbilityData asynchronousAbility;

        [SerializeField, AssetsOnly, Required]
        private PassiveGameplayAbilityData passiveAbility;

        [SerializeField, AssetsOnly, Required]
        private SphereProjectileGameplayAbilityData projectileAbility;

        [SerializeField, AssetsOnly, Required]
        private LinearProjectileGameplayAbilityData linearProjectileAbility;

        [SerializeField, AssetsOnly, Required]
        private SelfCastGameplayAbilityData selfCastAbility;

        [SerializeField, AssetsOnly, Required]
        private SelfChannelGameplayAbilityData selfChannelAbility;

        [SerializeField, AssetsOnly, Required]
        private ToggleGameplayAbilityData toggleAbility;

        [Title("可视化与测试通道")]
        [SerializeField, Tooltip("相对 Tester 的测试世界偏移，用于避开场景中已有的 Actor。")]
        private Vector3 testWorldOffset = new(-3f, 0f, 0f);

        [SerializeField, Min(1f), Tooltip("Source 与 Target 的世界距离，用于观察投射物飞行。")]
        private float targetDistance = 3f;

        [SerializeField, Min(0.1f), Tooltip("普通技能完成后的观察时间。")]
        private float stageHoldDuration = 1f;

        [SerializeField, Min(0.1f), Tooltip("Toggle 与 Passive 的 Active Cue 保持观察时间。")]
        private float activeCueObserveDuration = 2f;

        [SerializeField, Min(0.1f), Tooltip("Execute/Remove Cue 位置提示球的保持时间。")]
        private float cuePulseDuration = 0.75f;

        #endregion

        #region 运行状态

        [ShowInInspector, ReadOnly]
        private bool running;

        [ShowInInspector, ReadOnly]
        private string currentScenario = "尚未开始";

        [ShowInInspector, ReadOnly]
        private int passed;

        [ShowInInspector, ReadOnly]
        private int failed;

        private Coroutine cycle;
        private GameObject sourceObject;
        private GameObject targetObject;
        private GameplayAbilitySystemComponent source;
        private GameplayAbilitySystemComponent target;
        private GameplayAbilitySystemComponentTestVisualizer visualizer;
        private bool cueEventSubscribed;
        private int observedCueCount;
        private GameplayAbilitySystemComponent lastCueTarget;
        private GameplayCueEventType lastCueEventType;
        private Vector3 lastCuePosition;
        private readonly List<string> abilityEventOrder = new();
        private Action<GameplayAbilityRuntime> abilityActivatedHandler;
        private Action<GameplayAbilityRuntime> abilityCancelledHandler;
        private Action<GameplayAbilityRuntime> abilityEndedHandler;

        #endregion

        #region Unity 生命周期

        /// <summary>作为测试 Owner 在真实 Unity Update 中推进当前场景的两个 ASC。</summary>
        private void Update()
        {
            if (!running || source == null || target == null) return;
            source.Tick(Time.deltaTime);
            target.Tick(Time.deltaTime);
        }

        /// <summary>测试组件销毁时停止协程并清理临时 ASC、Cue 与投射物。</summary>
        private void OnDestroy() => StopAndCleanup();

        #endregion

        #region Odin 单项操作

        /// <summary>独立测试 Instant 的 Cost、Effect、稳定 ID 与 Execute Cue。</summary>
        [Button("测试 Instant", ButtonSizes.Medium)]
        public void TestInstant() => StartSingleScenario(AbilityTestScenario.Instant);

        /// <summary>独立测试异步 Root Task 通过真实 Tick 完成。</summary>
        [Button("测试 Async", ButtonSizes.Medium)]
        public void TestAsync() => StartSingleScenario(AbilityTestScenario.Async);

        /// <summary>独立测试 SelfCast 的读条、重复激活拒绝和完成结算。</summary>
        [Button("测试 SelfCast", ButtonSizes.Medium)]
        public void TestSelfCast() => StartSingleScenario(AbilityTestScenario.SelfCast);

        /// <summary>独立测试 Instant 的 CancelTags 在真实 ASC 周期中取消正在读条的 SelfCast。</summary>
        [Button("测试 AbilityTags 取消", ButtonSizes.Medium)]
        public void TestAbilityTagsCancellation() =>
            StartSingleScenario(AbilityTestScenario.AbilityTagsCancellation);

        /// <summary>独立测试 SelfChannel 的周期运行和结束。</summary>
        [Button("测试 SelfChannel", ButtonSizes.Medium)]
        public void TestSelfChannel() => StartSingleScenario(AbilityTestScenario.SelfChannel);

        /// <summary>独立测试 Toggle 的开启、持续 Cue 和再次激活关闭。</summary>
        [Button("测试 Toggle", ButtonSizes.Medium)]
        public void TestToggle() => StartSingleScenario(AbilityTestScenario.Toggle);

        /// <summary>独立测试 Passive、Infinite Effect、End 和 Cooldown 到期。</summary>
        [Button("测试 Passive 与 Cooldown", ButtonSizes.Medium)]
        public void TestPassive() => StartSingleScenario(AbilityTestScenario.Passive);

        /// <summary>独立测试原生 Sphere 投射物的真实 Trigger 命中。</summary>
        [Button("测试 Sphere Projectile", ButtonSizes.Medium)]
        public void TestSphereProjectile() => StartSingleScenario(AbilityTestScenario.SphereProjectile);

        /// <summary>独立测试对象池 Linear 投射物的真实 Trigger 命中和回收。</summary>
        [Button("测试 Linear Projectile", ButtonSizes.Medium)]
        public void TestLinearProjectile() => StartSingleScenario(AbilityTestScenario.LinearProjectile);

        /// <summary>依次执行全部独立技能场景，每项之间重建 Source、Target 和所有 ASC 状态。</summary>
        [Button("执行全部独立 ASC 测试", ButtonSizes.Large)]
        public void RunAllScenarios()
        {
            if (!TryBeginRun()) return;
            cycle = StartCoroutine(RunAllScenarioCycle());
        }

        /// <summary>停止当前测试并立即清理临时 Source、Target、Cue 与投射物。</summary>
        [Button("停止并清理 ASC 测试")]
        public void StopTest()
        {
            StopAndCleanup();
            Debug.Log("[ASCTest] 已停止并清理当前测试。", this);
        }

        #endregion

        #region 测试调度

        /// <summary>启动一个只包含指定技能的独立测试周期。</summary>
        /// <param name="scenario">要执行的技能场景。</param>
        private void StartSingleScenario(AbilityTestScenario scenario)
        {
            if (!TryBeginRun()) return;
            cycle = StartCoroutine(RunSingleScenarioCycle(scenario));
        }

        /// <summary>检查运行条件并初始化本轮汇总。</summary>
        /// <returns>允许启动新测试时返回 true。</returns>
        private bool TryBeginRun()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[ASCTest] 请进入 Play Mode 后再执行 ASC 测试。", this);
                return false;
            }

            if (running)
            {
                Debug.LogError("[ASCTest] 当前测试仍在运行。", this);
                return false;
            }

            if (!ValidateInputs()) return false;

            passed = 0;
            failed = 0;
            observedCueCount = 0;
            lastCueTarget = null;
            running = true;
            return true;
        }

        /// <summary>执行一个单项场景并保留最终汇总。</summary>
        /// <param name="scenario">要执行的技能场景。</param>
        /// <returns>跨真实帧运行场景的协程枚举器。</returns>
        private IEnumerator RunSingleScenarioCycle(AbilityTestScenario scenario)
        {
            yield return RunScenario(scenario, 1, 1, "单项测试");
            CompleteRun();
        }

        /// <summary>依次运行所有技能场景，每项之间等待一帧完成临时对象销毁。</summary>
        /// <returns>完整套件协程枚举器。</returns>
        private IEnumerator RunAllScenarioCycle()
        {
            AbilityTestScenario[] scenarios = GetAllScenarios();
            for (int i = 0; i < scenarios.Length && running; i++)
            {
                yield return RunScenario(scenarios[i], i + 1, scenarios.Length, "完整套件");
                CleanupWorld();
                yield return null;
            }

            CompleteRun();
        }

        /// <summary>准备独立世界、验证默认数据并分发到指定技能场景。</summary>
        /// <param name="scenario">当前技能场景。</param>
        /// <param name="scenarioIndex">场景序号，从 1 开始。</param>
        /// <param name="scenarioCount">本轮场景总数。</param>
        /// <param name="runMode">单项测试或完整套件。</param>
        /// <returns>当前技能场景的协程枚举器。</returns>
        private IEnumerator RunScenario(
            AbilityTestScenario scenario,
            int scenarioIndex,
            int scenarioCount,
            string runMode)
        {
            currentScenario = GetScenarioName(scenario);
            if (!PrepareWorld(scenario, scenarioIndex, scenarioCount, runMode)) yield break;

            Debug.Log($"[ASCTest][Begin] {currentScenario}", this);
            yield return null;
            Expect("Source ASC 初始化成功", source.IsInitialized);
            Expect("Target ASC 初始化成功", target.IsInitialized);
            ExpectCurrent("Source 初始 Health", source, GameplayAttributes.Attribute_Health, 100f);
            ExpectCurrent("Source 初始 MP", source, GameplayAttributes.Attribute_MP, 50f);
            ExpectCurrent("Source 初始 Armor", source, GameplayAttributes.Attribute_Armor, 10f);
            ExpectCurrent("Target 初始 Health", target, GameplayAttributes.Attribute_Health, 100f);

            switch (scenario)
            {
                case AbilityTestScenario.Instant:
                    yield return RunInstantScenario();
                    break;
                case AbilityTestScenario.Async:
                    yield return RunAsyncScenario();
                    break;
                case AbilityTestScenario.SelfCast:
                    yield return RunSelfCastScenario();
                    break;
                case AbilityTestScenario.AbilityTagsCancellation:
                    yield return RunAbilityTagsCancellationScenario();
                    break;
                case AbilityTestScenario.SelfChannel:
                    yield return RunSelfChannelScenario();
                    break;
                case AbilityTestScenario.Toggle:
                    yield return RunToggleScenario();
                    break;
                case AbilityTestScenario.Passive:
                    yield return RunPassiveScenario();
                    break;
                case AbilityTestScenario.SphereProjectile:
                    yield return RunSphereProjectileScenario();
                    break;
                case AbilityTestScenario.LinearProjectile:
                    yield return RunLinearProjectileScenario();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
            }

            Debug.Log($"[ASCTest][Scenario End] {currentScenario}，累计 PASS={passed}, FAIL={failed}", this);
            yield return HoldStage($"{currentScenario}：观察结果", stageHoldDuration);

            if (scenarioCount == 1)
                CleanupWorld();
        }

        /// <summary>输出最终汇总，结束运行状态并保留 OnGUI 结果。</summary>
        private void CompleteRun()
        {
            Debug.Log($"[ASCTest][Summary] PASS={passed}, FAIL={failed}", this);
            if (visualizer != null)
                visualizer.Finish(passed, failed);
            CleanupWorld();
            running = false;
            cycle = null;
        }

        #endregion

        #region 独立技能场景

        /// <summary>验证 Instant 的稳定授予身份、Cost、Effect 和 Execute Cue。</summary>
        /// <returns>用于观察结果的协程枚举器。</returns>
        private IEnumerator RunInstantScenario()
        {
            SetStage("Instant：Cost、Effect 与 Execute Cue", stageHoldDuration);
            GameplayAbilityHandle handle = source.GiveAbility(instantAbility, 1);
            GameplayAbilityHandle duplicate = source.GiveAbility(instantAbility, 1);
            bool resolved = source.TryGetAbilityHandle(
                instantAbility.AbilityId,
                out GameplayAbilityHandle resolvedHandle);
            Expect("Instant 授予成功", handle.IsValid);
            Expect("同一 ASC 拒绝重复授予 Instant", !duplicate.IsValid);
            Expect("AbilityId 可反查当前 ASC Handle", resolved && resolvedHandle == handle);

            int cueCountBefore = observedCueCount;
            bool activated = source.TryActivateAbility(handle, out GameplayAbilityRuntime runtime);
            Expect("Instant 激活成功", activated);
            Expect("Instant 返回时已结束", runtime != null && runtime.State == GameplayAbilityRuntimeState.Ended);
            ExpectCurrent("Instant 扣除 MP", source, GameplayAttributes.Attribute_MP, 40f);
            ExpectCurrent("Instant 修改 Health", source, GameplayAttributes.Attribute_Health, 70f);
            ExpectCueObserved("Instant Execute Cue 可视化回调", cueCountBefore, instantAbility);
            yield break;
        }

        /// <summary>验证 Async Root Task 至少跨一帧保持 Active 并由真实 Tick 完成。</summary>
        /// <returns>等待异步 Runtime 完成的协程枚举器。</returns>
        private IEnumerator RunAsyncScenario()
        {
            SetStage("Async：等待 Root Task 完成", 2f);
            GameplayAbilityHandle handle = source.GiveAbility(asynchronousAbility, 1);
            bool activated = source.TryActivateAbility(handle, out GameplayAbilityRuntime runtime);
            Expect("Async 激活成功", activated);
            Expect("Async 激活后保持 Active", runtime != null && runtime.State == GameplayAbilityRuntimeState.Active);
            yield return null;
            Expect("Async 至少跨一帧保持 Active", runtime.State == GameplayAbilityRuntimeState.Active);

            float deadline = Time.realtimeSinceStartup + 2f;
            while (runtime.State == GameplayAbilityRuntimeState.Active && Time.realtimeSinceStartup < deadline)
                yield return null;
            Expect("Async 由真实 Tick 自动结束", runtime.State == GameplayAbilityRuntimeState.Ended);
        }

        /// <summary>验证 SelfCast 的重复激活策略和读条完成语义。</summary>
        /// <returns>等待读条完成的协程枚举器。</returns>
        private IEnumerator RunSelfCastScenario()
        {
            SetStage("SelfCast：读条后结算自身 Effects", 2f);
            GameplayAbilityHandle handle = source.GiveAbility(selfCastAbility, 1);
            int cueCountBefore = observedCueCount;
            bool activated = source.TryActivateAbility(handle, out GameplayAbilityRuntime runtime);
            Expect("SelfCast 激活成功", activated);
            Expect("SelfCast 运行时拒绝重复激活", !source.TryActivateAbility(handle, out _));

            float deadline = Time.realtimeSinceStartup + 2f;
            while (runtime.State == GameplayAbilityRuntimeState.Active && Time.realtimeSinceStartup < deadline)
                yield return null;
            Expect("SelfCast 由真实 Tick 完成", runtime.State == GameplayAbilityRuntimeState.Ended);
            ExpectCueObserved("SelfCast 完成 Cue 可视化回调", cueCountBefore, selfCastAbility);
        }

        /// <summary>验证成功激活 Instant 后按父级 CancelTag 取消 Active SelfCast 的完整 ASC 生命周期。</summary>
        /// <returns>等待真实 Tick 超过 SelfCast 读条时间的协程枚举器。</returns>
        private IEnumerator RunAbilityTagsCancellationScenario()
        {
            if (!TryGetSelfCastWaitDuration(out float waitDuration))
            {
                Expect("AbilityTags Cancel：SelfCast Root Task 包含首项 WaitDuration", false);
                Debug.LogError(
                    $"[ASCTest][AbilityTags Cancel] '{selfCastAbility.name}' 必须使用 " +
                    "SequenceGameplayAbilityTaskConfig，且首项必须是 WaitDurationGameplayAbilityTaskConfig。",
                    selfCastAbility);
                yield break;
            }

            float observationDuration = Mathf.Min(stageHoldDuration, waitDuration * 0.25f);
            SetStage("AbilityTags Cancel：SelfCast Active", observationDuration);
            GameplayAbilityHandle castHandle = source.GiveAbility(selfCastAbility, 1);
            GameplayAbilityHandle instantHandle = source.GiveAbility(instantAbility, 1);
            Expect("AbilityTags Cancel：SelfCast 授予成功", castHandle.IsValid);
            Expect("AbilityTags Cancel：Instant 授予成功", instantHandle.IsValid);

            bool castActivated = source.TryActivateAbility(
                castHandle,
                out GameplayAbilityRuntime castRuntime);
            Expect("AbilityTags Cancel：SelfCast 激活成功并保持 Active",
                castActivated && castRuntime != null &&
                castRuntime.State == GameplayAbilityRuntimeState.Active);
            yield return null;
            Expect("AbilityTags Cancel：SelfCast 至少跨一帧保持 Active",
                castRuntime.State == GameplayAbilityRuntimeState.Active);
            yield return HoldStage("AbilityTags Cancel：观察 SelfCast Active", observationDuration);
            GameplayAbilityRuntimeState stateBeforeInstant = castRuntime.State;
            Expect("AbilityTags Cancel：Instant 激活前 SelfCast 仍为 Active",
                stateBeforeInstant == GameplayAbilityRuntimeState.Active);

            float healthBeforeInstant = ReadCurrent(source, GameplayAttributes.Attribute_Health);
            float mpBeforeInstant = ReadCurrent(source, GameplayAttributes.Attribute_MP);
            int cueCountBeforeInstant = observedCueCount;
            SubscribeAbilityCancellationEvents(castRuntime);

            SetStage("AbilityTags Cancel：Instant 取消 SelfCast", stageHoldDuration);
            bool instantActivated = source.TryActivateAbility(
                instantHandle,
                out GameplayAbilityRuntime instantRuntime);
            Expect("AbilityTags Cancel：Instant 激活成功", instantActivated);
            Expect("AbilityTags Cancel：SelfCast Runtime 进入 Cancelled",
                castRuntime.State == GameplayAbilityRuntimeState.Cancelled);
            Expect("AbilityTags Cancel：SelfCast 已从 ActiveAbilities 移除",
                !ContainsRuntime(source.ActiveAbilities, castRuntime));
            Expect("AbilityTags Cancel：Instant 返回时为 Ended",
                instantRuntime != null && instantRuntime.State == GameplayAbilityRuntimeState.Ended);
            Expect("AbilityTags Cancel：事件顺序为 Activated → Cancelled → Ended",
                HasExpectedAbilityEventOrder());
            ExpectCurrent("AbilityTags Cancel：Instant 正常扣除 MP",
                source,
                GameplayAttributes.Attribute_MP,
                mpBeforeInstant - 10f);
            ExpectCurrent("AbilityTags Cancel：Instant 正常结算 Health",
                source,
                GameplayAttributes.Attribute_Health,
                healthBeforeInstant - 30f);
            ExpectCueObserved(
                "AbilityTags Cancel：Instant Execute Cue 可视化回调",
                cueCountBeforeInstant,
                instantAbility);
            Expect("AbilityTags Cancel：分类标签未写入 ASC Owner Tags", source.Tags.IsEmpty);

            float healthAfterInstant = ReadCurrent(source, GameplayAttributes.Attribute_Health);
            int cuesAfterInstant = observedCueCount;
            float deadline = Time.realtimeSinceStartup + waitDuration + 0.5f;
            while (Time.realtimeSinceStartup < deadline) yield return null;

            Expect("AbilityTags Cancel：取消后 SelfCast 不再结算 Effects",
                Mathf.Approximately(
                    ReadCurrent(source, GameplayAttributes.Attribute_Health),
                    healthAfterInstant));
            Expect("AbilityTags Cancel：取消后 SelfCast 不产生完成 Cue",
                observedCueCount == cuesAfterInstant);
            Expect("AbilityTags Cancel：场景结束时没有 Active Ability",
                source.ActiveAbilities.Count == 0);
            Expect("AbilityTags Cancel：场景结束时没有 Tick 注册",
                source.TickRegistrationCount == 0);
            Expect("AbilityTags Cancel：场景结束时没有 Active Cue",
                source.Cues.ActiveCues.Count == 0);

            Debug.Log(
                $"[ASCTest][AbilityTags Cancel] Health={healthAfterInstant}, " +
                $"MP={ReadCurrent(source, GameplayAttributes.Attribute_MP)}, " +
                $"ActiveAbilities={source.ActiveAbilities.Count}, " +
                $"CastStateBeforeInstant={stateBeforeInstant}, " +
                $"CastStateAfterInstant={castRuntime.State}, " +
                $"AbilityTag={FormatTags(selfCastAbility.AbilityTags)}, " +
                $"CancelTag={FormatTags(instantAbility.CancelTags)}, " +
                $"TagManagerInitialized={GameplayTagManager.Instance.IsInitialized}, " +
                $"Events={string.Join(" -> ", abilityEventOrder)}",
                this);
            UnsubscribeAbilityCancellationEvents();
        }

        /// <summary>验证 SelfChannel 的重复激活策略和有限周期结束语义。</summary>
        /// <returns>等待引导结束的协程枚举器。</returns>
        private IEnumerator RunSelfChannelScenario()
        {
            SetStage("SelfChannel：周期结算自身 Effects", 2f);
            GameplayAbilityHandle handle = source.GiveAbility(selfChannelAbility, 1);
            int cueCountBefore = observedCueCount;
            bool activated = source.TryActivateAbility(handle, out GameplayAbilityRuntime runtime);
            Expect("SelfChannel 激活成功", activated);
            Expect("SelfChannel 运行时拒绝重复激活", !source.TryActivateAbility(handle, out _));

            float deadline = Time.realtimeSinceStartup + 2f;
            while (runtime.State == GameplayAbilityRuntimeState.Active && Time.realtimeSinceStartup < deadline)
                yield return null;
            Expect("有限 SelfChannel 由真实 Tick 完成", runtime.State == GameplayAbilityRuntimeState.Ended);
            ExpectCueObserved("SelfChannel 周期 Cue 可视化回调", cueCountBefore, selfChannelAbility);
        }

        /// <summary>验证 Toggle 开启时的持续 Effect/Cue 以及再次激活关闭语义。</summary>
        /// <returns>保持 Active Cue 可见的协程枚举器。</returns>
        private IEnumerator RunToggleScenario()
        {
            SetStage("Toggle：观察 Active Cue 与 Armor", activeCueObserveDuration);
            GameplayAbilityHandle handle = source.GiveAbility(toggleAbility, 1);
            int cueCountBefore = observedCueCount;
            bool activated = source.TryActivateAbility(handle, out GameplayAbilityRuntime runtime);
            Expect("Toggle 首次激活保持 Active", activated && runtime.State == GameplayAbilityRuntimeState.Active);
            ExpectCurrent("Toggle 首次激活应用持续 Armor", source, GameplayAttributes.Attribute_Armor, 20f);
            ExpectCueObserved("Toggle Active Cue 可视化回调", cueCountBefore, toggleAbility);
            yield return new WaitForSecondsRealtime(activeCueObserveDuration);

            bool toggledOff = source.TryActivateAbility(handle, out GameplayAbilityRuntime endedRuntime);
            Expect("Toggle 再次激活正常关闭旧 Runtime",
                toggledOff && ReferenceEquals(runtime, endedRuntime) && runtime.State == GameplayAbilityRuntimeState.Ended);
            ExpectCurrent("Toggle 关闭后恢复 Armor", source, GameplayAttributes.Attribute_Armor, 10f);
        }

        /// <summary>验证 Passive 的 Infinite Effect、精确移除和 Cooldown 生命周期。</summary>
        /// <returns>等待 Active Cue 与 Cooldown 到期的协程枚举器。</returns>
        private IEnumerator RunPassiveScenario()
        {
            SetStage("Passive：观察 Infinite GE 与 Active Cue", activeCueObserveDuration);
            GameplayAbilityHandle handle = source.GiveAbility(passiveAbility, 1);
            int cueCountBefore = observedCueCount;
            bool activated = source.TryActivateAbility(handle, out GameplayAbilityRuntime runtime);
            Expect("Passive 激活成功", activated);
            Expect("Passive Runtime 保持 Active", runtime != null && runtime.State == GameplayAbilityRuntimeState.Active);
            ExpectCurrent("Passive 首次 Cost 扣除 MP", source, GameplayAttributes.Attribute_MP, 40f);
            ExpectCurrent("Passive Infinite GE 修改 Armor", source, GameplayAttributes.Attribute_Armor, 20f);
            ExpectCueObserved("Passive Active Cue 可视化回调", cueCountBefore, passiveAbility);
            yield return new WaitForSecondsRealtime(activeCueObserveDuration);

            Expect("Passive 正常 End", source.TryEndAbility(runtime));
            ExpectCurrent("Passive End 后 Armor 恢复", source, GameplayAttributes.Attribute_Armor, 10f);
            Expect("Passive End 后 Cooldown 仍 Active", source.HasActiveEffect(passiveAbility.CooldownEffect));
            Expect("Cooldown 期间拒绝再次激活 Passive", !source.TryActivateAbility(handle, out _));

            SetStage("Passive：等待 Cooldown 到期", 6f);
            float deadline = Time.realtimeSinceStartup + 6f;
            while (source.HasActiveEffect(passiveAbility.CooldownEffect) && Time.realtimeSinceStartup < deadline)
                yield return null;
            Expect("Passive Cooldown 由真实 GE Tick 到期", !source.HasActiveEffect(passiveAbility.CooldownEffect));

            bool reactivated = source.TryActivateAbility(handle, out GameplayAbilityRuntime secondRuntime);
            Expect("Cooldown 到期后 Passive 可再次激活", reactivated);
            if (reactivated)
                source.TryEndAbility(secondRuntime);
            ExpectCurrent("第二次 Passive Cost 扣除 MP", source, GameplayAttributes.Attribute_MP, 30f);
        }

        /// <summary>验证 Sphere 投射物只命中当前测试的专用 Target。</summary>
        /// <returns>等待真实 Trigger 命中的协程枚举器。</returns>
        private IEnumerator RunSphereProjectileScenario()
        {
            SetStage("Sphere Projectile：真实物理飞行与命中", 3f);
            if (!ValidateProjectileLane()) yield break;

            GameplayAbilityHandle handle = source.GiveAbility(projectileAbility, 1);
            ResetCueObservationTarget();
            int cueCountBefore = observedCueCount;
            bool activated = source.TryActivateAbility(handle, out GameplayAbilityRuntime runtime);
            Expect("Sphere Projectile 激活成功", activated);
            Expect("Sphere Projectile 创建后 Runtime 已结束",
                runtime != null && runtime.State == GameplayAbilityRuntimeState.Ended);
            ExpectCurrent("Sphere Projectile Cost 扣除 MP", source, GameplayAttributes.Attribute_MP, 40f);

            float deadline = Time.realtimeSinceStartup + 3f;
            while (ReadCurrent(target, GameplayAttributes.Attribute_Health) > 70f &&
                   Time.realtimeSinceStartup < deadline)
                yield return null;

            ExpectCurrent("Sphere Projectile 真实 Trigger 命中专用 Target",
                target,
                GameplayAttributes.Attribute_Health,
                70f);
            ExpectProjectileCue("Sphere Projectile 命中 Cue 指向专用 Target", cueCountBefore, projectileAbility);
            yield return null;
            Expect("Sphere Projectile 命中后已销毁", GameObject.Find("GA Sphere Projectile (Test)") == null);
        }

        /// <summary>验证对象池 Linear 投射物只命中当前测试的专用 Target并完成回收。</summary>
        /// <returns>等待真实 Trigger 命中的协程枚举器。</returns>
        private IEnumerator RunLinearProjectileScenario()
        {
            SetStage("Linear Projectile：对象池投射物飞行与命中", 3f);
            if (!ValidateProjectileLane()) yield break;

            GameplayAbilityHandle handle = source.GiveAbility(linearProjectileAbility, 1);
            yield return RunLinearProjectileShot(handle, "首次获取", 70f);
            yield return HoldStage("Linear Projectile：准备复用池中实例", 0.5f);
            yield return RunLinearProjectileShot(handle, "对象池复用", 40f);
        }

        /// <summary>执行一次 Linear Projectile 发射，并记录池化 Rigidbody 的生成 Pose 与飞行轨迹。</summary>
        /// <param name="handle">当前 ASC 的 Linear Ability Handle。</param>
        /// <param name="shotLabel">首次获取或对象池复用标签。</param>
        /// <param name="expectedHealth">本次命中后的 Target Health。</param>
        /// <returns>等待投射物命中或到期回收的协程枚举器。</returns>
        private IEnumerator RunLinearProjectileShot(
            GameplayAbilityHandle handle,
            string shotLabel,
            float expectedHealth)
        {
            SetStage($"Linear Projectile：{shotLabel}", 3f);
            ResetCueObservationTarget();
            int cueCountBefore = observedCueCount;
            bool activated = source.TryActivateAbility(handle, out GameplayAbilityRuntime runtime);
            Expect($"Linear Projectile {shotLabel}激活成功", activated);
            Expect($"Linear Projectile {shotLabel}发射后 Runtime 已结束",
                runtime != null && runtime.State == GameplayAbilityRuntimeState.Ended);

            GameObject activeProjectile = GameObject.Find("GA_Test_LinearProjectile");
            Expect($"Linear Projectile {shotLabel}生成活动实例", activeProjectile != null);
            if (activeProjectile == null) yield break;

            Rigidbody projectileBody = activeProjectile.GetComponent<Rigidbody>();
            ResolveLinearProjectileSpawnPose(
                out Vector3 expectedSpawnPosition,
                out Quaternion expectedSpawnRotation);
            Vector3 initialTransformPosition = activeProjectile.transform.position;
            Quaternion initialTransformRotation = activeProjectile.transform.rotation;
            Vector3 initialBodyPosition = projectileBody != null
                ? projectileBody.position
                : new Vector3(float.NaN, float.NaN, float.NaN);
            Quaternion initialBodyRotation = projectileBody != null
                ? projectileBody.rotation
                : Quaternion.identity;
            Vector3 lastObservedPosition = initialTransformPosition;
            Vector3 expectedScale = linearProjectileAbility.FallbackPrefab != null
                ? linearProjectileAbility.FallbackPrefab.transform.localScale
                : activeProjectile.transform.localScale;

            Expect($"Linear Projectile {shotLabel}包含 Rigidbody", projectileBody != null);
            bool transformAtExpected =
                Vector3.Distance(initialTransformPosition, expectedSpawnPosition) <= 0.01f;
            bool bodyAtExpected = projectileBody != null &&
                                  Vector3.Distance(initialBodyPosition, expectedSpawnPosition) <= 0.01f;
            bool posesMatch = projectileBody != null &&
                              Vector3.Distance(initialTransformPosition, initialBodyPosition) <= 0.01f;
            bool transformRotationAtExpected =
                Quaternion.Angle(initialTransformRotation, expectedSpawnRotation) <= 0.1f;
            bool bodyRotationAtExpected = projectileBody != null &&
                                          Quaternion.Angle(initialBodyRotation, expectedSpawnRotation) <= 0.1f;
            bool rotationsMatch = projectileBody != null &&
                                  Quaternion.Angle(initialTransformRotation, initialBodyRotation) <= 0.1f;

            Expect($"Linear Projectile {shotLabel}的 Transform 位于期望生成点", transformAtExpected);
            Expect($"Linear Projectile {shotLabel}的 Rigidbody 位于期望生成点", bodyAtExpected);
            Expect($"Linear Projectile {shotLabel}的 Transform 与 Rigidbody Pose 一致",
                posesMatch);
            Expect($"Linear Projectile {shotLabel}的 Transform 旋转与期望一致", transformRotationAtExpected);
            Expect($"Linear Projectile {shotLabel}的 Rigidbody 旋转与期望一致", bodyRotationAtExpected);
            Expect($"Linear Projectile {shotLabel}的 Transform 与 Rigidbody 旋转一致", rotationsMatch);
            Expect($"Linear Projectile {shotLabel}恢复 Prefab Scale",
                Vector3.Distance(activeProjectile.transform.localScale, expectedScale) <= 0.001f);

            if (!transformAtExpected || !bodyAtExpected || !posesMatch ||
                !transformRotationAtExpected || !bodyRotationAtExpected || !rotationsMatch)
                Debug.LogError(
                    $"[ASCTest][Linear Spawn Pose] Shot={shotLabel}, Expected={expectedSpawnPosition}, " +
                    $"Transform={initialTransformPosition}, Rigidbody={initialBodyPosition}, " +
                    $"ExpectedRotation={expectedSpawnRotation.eulerAngles}, " +
                    $"TransformRotation={initialTransformRotation.eulerAngles}, " +
                    $"RigidbodyRotation={initialBodyRotation.eulerAngles}, Source={source.transform.position}。",
                    this);

            float deadline = Time.realtimeSinceStartup + 3f;
            while (ReadCurrent(target, GameplayAttributes.Attribute_Health) > expectedHealth &&
                   Time.realtimeSinceStartup < deadline)
            {
                if (activeProjectile != null && activeProjectile.activeInHierarchy)
                {
                    lastObservedPosition = activeProjectile.transform.position;
                    if (visualizer != null)
                        visualizer.SetProjectilePosition(lastObservedPosition, true);
                }
                yield return null;
            }

            float actualHealth = ReadCurrent(target, GameplayAttributes.Attribute_Health);
            ExpectCurrent($"Linear Projectile {shotLabel}命中专用 Target",
                target,
                GameplayAttributes.Attribute_Health,
                expectedHealth);
            ExpectProjectileCue(
                $"Linear Projectile {shotLabel}命中 Cue 指向专用 Target",
                cueCountBefore,
                linearProjectileAbility);

            if (!Mathf.Approximately(actualHealth, expectedHealth))
                Debug.LogError(
                    $"[ASCTest][Linear Diagnostics] Shot={shotLabel}, " +
                    $"TransformStart={initialTransformPosition}, RigidbodyStart={initialBodyPosition}, " +
                    $"LastPosition={lastObservedPosition}, Target={target.transform.position}, " +
                    $"Active={(activeProjectile != null && activeProjectile.activeInHierarchy)}, " +
                    $"LifetimeRecycled={(activeProjectile != null && !activeProjectile.activeInHierarchy)}。",
                    this);

            yield return null;
            Expect($"Linear Projectile {shotLabel}命中后已回收到对象池",
                GameObject.Find("GA_Test_LinearProjectile") == null);
            if (visualizer != null)
                visualizer.SetProjectilePosition(lastObservedPosition, false);
        }

        /// <summary>按当前 Source Marker 和 Linear Ability 的作者偏移计算本次期望生成 Pose。</summary>
        /// <param name="position">与正式 Linear Projectile 配置对应的世界生成位置。</param>
        /// <param name="rotation">与正式 Linear Projectile 配置对应的世界生成旋转。</param>
        private void ResolveLinearProjectileSpawnPose(
            out Vector3 position,
            out Quaternion rotation)
        {
            Transform spawnTransform = source.transform;
            MarkerKey markerKey = linearProjectileAbility.SpawnMarker;
            if (markerKey != null)
            {
                IMarkerProvider provider = source.GetComponent<IMarkerProvider>();
                if (provider != null && provider.TryGetMarker(markerKey, out Transform marker))
                    spawnTransform = marker;
            }

            position = spawnTransform.TransformPoint(linearProjectileAbility.LocalPosition);
            rotation = spawnTransform.rotation *
                       Quaternion.Euler(linearProjectileAbility.LocalEulerAngles);
        }

        #endregion

        #region 场景准备与清理

        /// <summary>检查 Inspector 测试夹具，避免缺失 SO 时输出误导性的运行时失败。</summary>
        /// <returns>全部测试资产均已配置时返回 true。</returns>
        private bool ValidateInputs()
        {
            bool valid = tagDatabase != null &&
                         abilityDatabase != null &&
                         cueDatabase != null &&
                         attributeSets != null && attributeSets.Count > 0 &&
                         instantAbility != null &&
                         asynchronousAbility != null &&
                         passiveAbility != null &&
                         projectileAbility != null &&
                         linearProjectileAbility != null &&
                         selfCastAbility != null &&
                         selfChannelAbility != null &&
                         toggleAbility != null;
            if (valid)
                for (int i = 0; i < attributeSets.Count; i++)
                    valid &= attributeSets[i] != null;

            if (!valid)
                Debug.LogError(
                    "[ASCTest] 请配置 TagDatabase、AbilityDatabase、CueDatabase、完整 AttributeSet 和八个 GA SO。",
                    this);
            return valid;
        }

        /// <summary>创建当前技能专用的可见 Source、Target 和完整 ASC 状态。</summary>
        /// <param name="scenario">当前技能场景。</param>
        /// <param name="scenarioIndex">当前场景序号。</param>
        /// <param name="scenarioCount">场景总数。</param>
        /// <param name="runMode">单项测试或完整套件。</param>
        /// <returns>场景与当前 Ability 的 CueTag 映射准备成功时返回 true。</returns>
        private bool PrepareWorld(
            AbilityTestScenario scenario,
            int scenarioIndex,
            int scenarioCount,
            string runMode)
        {
            CleanupWorld();
            GameplayTagManager.Instance.Initialize(tagDatabase);
            GameplayAbilityManager.Instance.Initialize(abilityDatabase);
            GameplayCueManager.Instance.Initialize(cueDatabase);

            Vector3 sourcePosition = transform.position + transform.rotation * testWorldOffset;
            sourceObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            sourceObject.name = $"ASC Test Source - {currentScenario}";
            sourceObject.transform.SetPositionAndRotation(sourcePosition, transform.rotation);
            Collider sourceCollider = sourceObject.GetComponent<Collider>();
            if (sourceCollider != null)
                sourceCollider.enabled = false;
            source = sourceObject.AddComponent<GameplayAbilitySystemComponent>();
            source.Initialize(attributeSets);

            targetObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            targetObject.name = $"ASC Test Target - {currentScenario}";
            targetObject.transform.SetPositionAndRotation(
                sourcePosition + transform.forward * targetDistance,
                transform.rotation);
            target = targetObject.AddComponent<GameplayAbilitySystemComponent>();
            target.Initialize(attributeSets);

            GameplayAbilityData ability = GetAbility(scenario);
            bool dependenciesValid = ValidateAbilityRegistration(ability) &&
                                     ValidateCueMappings(ability);
            if (scenario == AbilityTestScenario.AbilityTagsCancellation)
            {
                // 组合场景同时依赖 SelfCast 与 Instant，不能只校验主场景返回的 Instant。
                dependenciesValid &= ValidateAbilityRegistration(selfCastAbility);
                dependenciesValid &= ValidateCueMappings(selfCastAbility);
            }

            if (!dependenciesValid)
            {
                CleanupWorld();
                return false;
            }

            visualizer = GetComponent<GameplayAbilitySystemComponentTestVisualizer>();
            if (visualizer == null)
                visualizer = gameObject.AddComponent<GameplayAbilitySystemComponentTestVisualizer>();
            visualizer.Begin(
                source,
                target,
                sourceObject.GetComponent<Renderer>(),
                targetObject.GetComponent<Renderer>(),
                cuePulseDuration);
            visualizer.SetTestContext(runMode, currentScenario, scenarioIndex, scenarioCount, passed, failed);
            visualizer.SetProjectileLane(source.transform.position, target.transform.position, IsProjectileScenario(scenario));

            SubscribeCueEvents();
            Physics.SyncTransforms();
            return true;
        }

        /// <summary>检查当前 Ability 的非空 CueTag 是否都能从当前 CueDatabase 解析。</summary>
        /// <param name="ability">当前场景使用的 Ability。</param>
        /// <returns>全部 CueTag 均存在映射时返回 true。</returns>
        private bool ValidateCueMappings(GameplayAbilityData ability)
        {
            bool valid = true;
            for (int i = 0; i < ability.CueTags.Count; i++)
            {
                if (GameplayCueManager.Instance.TryGetCue(ability.CueTags[i], out _)) continue;
                valid = false;
                Expect($"{ability.name} 的 CueTag 已注册", false);
                Debug.LogError(
                    $"[ASCTest] Ability '{ability.name}' 的 CueTag {ability.CueTags[i]} 未注册到 '{cueDatabase.name}'。",
                    ability);
            }

            return valid;
        }

        /// <summary>验证测试 Ability 已完成稳定 ID Bake，且当前 Database 能按 ID 解析回同一资产。</summary>
        /// <param name="ability">当前场景依赖的 Ability 资产。</param>
        /// <returns>Database 注册项与传入资产一致时返回 true。</returns>
        private bool ValidateAbilityRegistration(GameplayAbilityData ability)
        {
            bool registered = ability != null &&
                              abilityDatabase.TryGetAbility(ability.AbilityId, out GameplayAbilityData registeredAbility) &&
                              ReferenceEquals(registeredAbility, ability);
            Expect($"{ability?.name ?? "未配置 Ability"} 已注册到 Ability Database", registered);
            if (!registered)
            {
                Debug.LogError(
                    $"[ASCTest] Ability '{ability?.name ?? "None"}' 未以 AbilityId={ability?.AbilityId ?? -1} " +
                    $"注册到 '{abilityDatabase.name}'，请先在 GA Editor 中执行 Bake。",
                    abilityDatabase);
            }

            return registered;
        }

        /// <summary>扫描投射物通道中的第三方 ASC，防止投射物先命中错误角色。</summary>
        /// <returns>通道中只有本次 Source 与 Target 时返回 true。</returns>
        private bool ValidateProjectileLane()
        {
            Vector3 start = source.transform.position;
            Vector3 end = target.transform.position;
            Vector3 center = (start + end) * 0.5f;
            Vector3 halfExtents = new(0.45f, 1.1f, targetDistance * 0.5f);
            Collider[] colliders = Physics.OverlapBox(
                center,
                halfExtents,
                source.transform.rotation,
                ~0,
                QueryTriggerInteraction.Collide);

            for (int i = 0; i < colliders.Length; i++)
            {
                GameplayAbilitySystemComponent otherAsc =
                    colliders[i].GetComponentInParent<GameplayAbilitySystemComponent>();
                if (otherAsc == null || ReferenceEquals(otherAsc, source) || ReferenceEquals(otherAsc, target))
                    continue;

                Expect("投射物测试通道无第三方 ASC", false);
                Debug.LogError(
                    $"[ASCTest][Projectile Lane] '{otherAsc.name}' 位于测试通道中，位置={otherAsc.transform.position}。请调整 Test World Offset。",
                    otherAsc);
                return false;
            }

            Expect("投射物测试通道无第三方 ASC", true);
            return true;
        }

        /// <summary>停止当前协程并清理本轮所有临时状态。</summary>
        private void StopAndCleanup()
        {
            bool interrupted = running;
            running = false;
            if (cycle != null)
            {
                StopCoroutine(cycle);
                cycle = null;
            }

            CleanupWorld();
            if (interrupted && visualizer != null)
                visualizer.Stop(passed, failed);
        }

        /// <summary>清理当前技能场景，不改变完整套件的累计断言和运行标记。</summary>
        private void CleanupWorld()
        {
            // 先解除 Ability 观察，避免 Clear 产生的 Cancel 事件污染本场景已记录的严格顺序。
            UnsubscribeAbilityCancellationEvents();
            // 再 Clear ASC，使 Active Cue 走正式 Remove 路径，随后解除 Cue 观察事件。
            if (source != null) source.Clear();
            if (target != null) target.Clear();
            UnsubscribeCueEvents();

            GameObject sphereProjectile = GameObject.Find("GA Sphere Projectile (Test)");
            if (sphereProjectile != null)
                Destroy(sphereProjectile);

            GameObject linearProjectile = GameObject.Find("GA_Test_LinearProjectile");
            if (linearProjectile != null)
                PoolManager.Instance.Recycle(linearProjectile);

            if (sourceObject != null) Destroy(sourceObject);
            if (targetObject != null) Destroy(targetObject);
            source = null;
            target = null;
            sourceObject = null;
            targetObject = null;
            lastCueTarget = null;
            if (visualizer != null)
                visualizer.DetachActors();
            GameplayAbilityManager.Instance.Reset();
            GameplayCueManager.Instance.Reset();
            GameplayTagManager.Instance.Reset();
        }

        #endregion

        #region Ability 生命周期观察

        /// <summary>从 SelfCast 的固定 Sequence 配置读取真实等待时长，避免观察窗口耗尽读条。</summary>
        /// <param name="duration">解析成功时返回首个 WaitDuration Task 的配置时长。</param>
        /// <returns>Root Task 结构符合测试夹具契约且时长大于零时返回 true。</returns>
        private bool TryGetSelfCastWaitDuration(out float duration)
        {
            if (selfCastAbility.RootTask is SequenceGameplayAbilityTaskConfig sequence &&
                sequence.Children.Count > 0 &&
                sequence.Children[0] is WaitDurationGameplayAbilityTaskConfig wait &&
                !float.IsNaN(wait.Duration) &&
                !float.IsInfinity(wait.Duration) &&
                wait.Duration > 0f)
            {
                duration = wait.Duration;
                return true;
            }

            duration = 0f;
            return false;
        }

        /// <summary>将测试资产中的 TagId 列表格式化为诊断日志文本。</summary>
        /// <param name="tags">需要输出的 AbilityTags 或 CancelTags。</param>
        /// <returns>按数组顺序排列的 TagId 文本。</returns>
        private static string FormatTags(IReadOnlyList<GameplayTag> tags)
        {
            if (tags.Count == 0) return "[]";

            var values = new string[tags.Count];
            for (int i = 0; i < tags.Count; i++)
                values[i] = tags[i].Id.ToString();
            return $"[{string.Join(", ", values)}]";
        }

        /// <summary>订阅组合场景所需的 Ability 生命周期事件，并只记录指定技能的事件。</summary>
        /// <param name="castRuntime">本次应被 CancelTags 取消的 SelfCast Runtime。</param>
        private void SubscribeAbilityCancellationEvents(GameplayAbilityRuntime castRuntime)
        {
            UnsubscribeAbilityCancellationEvents();
            abilityEventOrder.Clear();

            abilityActivatedHandler = runtime =>
            {
                if (ReferenceEquals(runtime.Spec.Data, instantAbility))
                    abilityEventOrder.Add("Instant.Activated");
            };
            abilityCancelledHandler = runtime =>
            {
                if (ReferenceEquals(runtime, castRuntime))
                    abilityEventOrder.Add("SelfCast.Cancelled");
            };
            abilityEndedHandler = runtime =>
            {
                if (ReferenceEquals(runtime.Spec.Data, instantAbility))
                    abilityEventOrder.Add("Instant.Ended");
            };

            source.Abilities.AbilityActivated += abilityActivatedHandler;
            source.Abilities.AbilityCancelled += abilityCancelledHandler;
            source.Abilities.AbilityEnded += abilityEndedHandler;
        }

        /// <summary>解除组合场景的 Ability 生命周期观察，避免停止、清理或重复测试时累积回调。</summary>
        private void UnsubscribeAbilityCancellationEvents()
        {
            if (source != null)
            {
                if (abilityActivatedHandler != null)
                    source.Abilities.AbilityActivated -= abilityActivatedHandler;
                if (abilityCancelledHandler != null)
                    source.Abilities.AbilityCancelled -= abilityCancelledHandler;
                if (abilityEndedHandler != null)
                    source.Abilities.AbilityEnded -= abilityEndedHandler;
            }

            abilityActivatedHandler = null;
            abilityCancelledHandler = null;
            abilityEndedHandler = null;
        }

        /// <summary>检查 Runtime 是否仍存在于 ASC 暴露的只读 Active Runtime 列表中。</summary>
        /// <param name="runtimes">ASC 当前 Active Runtime 列表。</param>
        /// <param name="expected">需要按引用查找的 Runtime。</param>
        /// <returns>列表包含同一 Runtime 实例时返回 true。</returns>
        private static bool ContainsRuntime(
            IReadOnlyList<GameplayAbilityRuntime> runtimes,
            GameplayAbilityRuntime expected)
        {
            for (int i = 0; i < runtimes.Count; i++)
            {
                if (ReferenceEquals(runtimes[i], expected)) return true;
            }

            return false;
        }

        /// <summary>验证新 Instant 激活、旧 SelfCast 取消和同步 Instant 结束的严格事件顺序。</summary>
        /// <returns>事件数量及三项顺序完全符合运行时契约时返回 true。</returns>
        private bool HasExpectedAbilityEventOrder() =>
            abilityEventOrder.Count == 3 &&
            abilityEventOrder[0] == "Instant.Activated" &&
            abilityEventOrder[1] == "SelfCast.Cancelled" &&
            abilityEventOrder[2] == "Instant.Ended";

        #endregion

        #region Cue 与可视化

        /// <summary>订阅测试 Cue 行为事件，确保每个独立世界只注册一次。</summary>
        private void SubscribeCueEvents()
        {
            if (cueEventSubscribed) return;
            GameplayCueVisualProbeBehaviour.CueObserved += OnCueObserved;
            cueEventSubscribed = true;
        }

        /// <summary>解除测试 Cue 行为事件，避免场景重建后重复回调。</summary>
        private void UnsubscribeCueEvents()
        {
            if (!cueEventSubscribed) return;
            GameplayCueVisualProbeBehaviour.CueObserved -= OnCueObserved;
            cueEventSubscribed = false;
        }

        /// <summary>记录属于当前 Source 或 Target 的 Cue，并保存实际 Cue Target。</summary>
        /// <param name="runtime">发生表现回调的 Cue Runtime。</param>
        /// <param name="eventType">Cue 生命周期阶段。</param>
        private void OnCueObserved(GameplayCueRuntime runtime, GameplayCueEventType eventType)
        {
            bool belongsToTest = ReferenceEquals(runtime.Source, source) ||
                                 ReferenceEquals(runtime.Source, target) ||
                                 ReferenceEquals(runtime.Target, source) ||
                                 ReferenceEquals(runtime.Target, target);
            if (!belongsToTest || visualizer == null) return;

            observedCueCount++;
            lastCueTarget = runtime.Target;
            lastCueEventType = eventType;
            lastCuePosition = runtime.CueObject != null
                ? runtime.CueObject.transform.position
                : runtime.Position;
            visualizer.RecordCue(runtime, eventType);
        }

        /// <summary>清除投射物发射前的 Cue Target，确保断言只读取本次命中事件。</summary>
        private void ResetCueObservationTarget()
        {
            lastCueTarget = null;
            lastCueEventType = default;
            lastCuePosition = default;
        }

        /// <summary>设置当前可视化阶段并按真实时间保留观察窗口。</summary>
        /// <param name="stage">面板中显示的阶段名称。</param>
        /// <param name="duration">观察窗口秒数。</param>
        /// <returns>等待真实时间的协程枚举器。</returns>
        private IEnumerator HoldStage(string stage, float duration)
        {
            SetStage(stage, duration);
            yield return new WaitForSecondsRealtime(duration);
        }

        /// <summary>将当前流程阶段同步到可视化面板。</summary>
        /// <param name="stage">阶段名称。</param>
        /// <param name="duration">预计持续秒数。</param>
        private void SetStage(string stage, float duration)
        {
            if (visualizer != null)
                visualizer.SetStage(stage, duration);
        }

        #endregion

        #region 断言辅助

        /// <summary>统一记录布尔断言并同步可视化汇总。</summary>
        /// <param name="label">断言名称。</param>
        /// <param name="condition">断言是否通过。</param>
        private void Expect(string label, bool condition)
        {
            if (condition)
            {
                passed++;
                Debug.Log($"[ASCTest][PASS] {label}", this);
            }
            else
            {
                failed++;
                Debug.LogError($"[ASCTest][FAIL] {label}", this);
            }

            if (visualizer != null)
                visualizer.SetSummary(passed, failed);
        }

        /// <summary>读取 CurrentValue 并按 Unity 浮点容差验证预期值。</summary>
        /// <param name="label">断言名称。</param>
        /// <param name="asc">Attribute 所属 ASC。</param>
        /// <param name="attribute">待读取的 Attribute。</param>
        /// <param name="expected">预期 CurrentValue。</param>
        private void ExpectCurrent(
            string label,
            GameplayAbilitySystemComponent asc,
            GameplayAttribute attribute,
            float expected)
        {
            bool found = asc.TryGetCurrentValue(attribute, out float actual);
            Expect(label, found && Mathf.Approximately(actual, expected));
            if (!found || !Mathf.Approximately(actual, expected))
                Debug.LogError(
                    $"[ASCTest][Value] {label}：Actual={(found ? actual.ToString() : "Missing")}, Expected={expected}",
                    this);
        }

        /// <summary>验证声明了 CueTag 的 Ability 在当前阶段触发了 Probe 回调。</summary>
        /// <param name="label">断言名称。</param>
        /// <param name="countBeforeStage">阶段开始前累计回调数。</param>
        /// <param name="ability">当前阶段 Ability。</param>
        private void ExpectCueObserved(string label, int countBeforeStage, GameplayAbilityData ability)
        {
            bool observed = ability.CueTags.Count > 0 && observedCueCount > countBeforeStage;
            Expect(label, observed);
            if (!observed)
                Debug.LogError(
                    $"[ASCTest][Cue] Ability '{ability.name}' 未触发 GameplayCueVisualProbeBehaviour。请检查 CueTag 映射和 Cube Prefab。",
                    ability);
        }

        /// <summary>验证投射物 Cue 确实来自本次命中并指向专用 Target。</summary>
        /// <param name="label">断言名称。</param>
        /// <param name="countBeforeStage">发射前累计 Cue 回调数。</param>
        /// <param name="ability">投射物 Ability。</param>
        private void ExpectProjectileCue(string label, int countBeforeStage, GameplayAbilityData ability)
        {
            bool valid = ability.CueTags.Count > 0 &&
                         observedCueCount > countBeforeStage &&
                         ReferenceEquals(lastCueTarget, target) &&
                         lastCueEventType == GameplayCueEventType.Execute;
            Expect(label, valid);
            if (!valid)
                Debug.LogError(
                    $"[ASCTest][Projectile Cue] Ability='{ability.name}', ActualTarget='{(lastCueTarget != null ? lastCueTarget.name : "None")}', Event={lastCueEventType}, Position={lastCuePosition}。",
                    ability);
        }

        /// <summary>读取 Attribute CurrentValue；缺失时返回 NaN 以终止错误等待条件。</summary>
        /// <param name="asc">Attribute 所属 ASC。</param>
        /// <param name="attribute">待读取的 Attribute。</param>
        /// <returns>找到时返回 CurrentValue，否则返回 NaN。</returns>
        private static float ReadCurrent(
            GameplayAbilitySystemComponent asc,
            GameplayAttribute attribute) =>
            asc.TryGetCurrentValue(attribute, out float value) ? value : float.NaN;

        #endregion

        #region 场景元数据

        /// <summary>取得完整套件的固定场景顺序。</summary>
        /// <returns>八个彼此隔离的技能场景。</returns>
        private static AbilityTestScenario[] GetAllScenarios() =>
            (AbilityTestScenario[])Enum.GetValues(typeof(AbilityTestScenario));

        /// <summary>取得场景对应的 Ability Data。</summary>
        /// <param name="scenario">技能场景。</param>
        /// <returns>Inspector 中配置的对应 Ability Data。</returns>
        private GameplayAbilityData GetAbility(AbilityTestScenario scenario) => scenario switch
        {
            AbilityTestScenario.Instant => instantAbility,
            AbilityTestScenario.Async => asynchronousAbility,
            AbilityTestScenario.SelfCast => selfCastAbility,
            AbilityTestScenario.AbilityTagsCancellation => instantAbility,
            AbilityTestScenario.SelfChannel => selfChannelAbility,
            AbilityTestScenario.Toggle => toggleAbility,
            AbilityTestScenario.Passive => passiveAbility,
            AbilityTestScenario.SphereProjectile => projectileAbility,
            AbilityTestScenario.LinearProjectile => linearProjectileAbility,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null)
        };

        /// <summary>取得场景的中文显示名称。</summary>
        /// <param name="scenario">技能场景。</param>
        /// <returns>日志和 OnGUI 使用的场景名称。</returns>
        private static string GetScenarioName(AbilityTestScenario scenario) => scenario switch
        {
            AbilityTestScenario.Instant => "Instant",
            AbilityTestScenario.Async => "Async",
            AbilityTestScenario.SelfCast => "SelfCast",
            AbilityTestScenario.AbilityTagsCancellation => "AbilityTags Cancel",
            AbilityTestScenario.SelfChannel => "SelfChannel",
            AbilityTestScenario.Toggle => "Toggle",
            AbilityTestScenario.Passive => "Passive 与 Cooldown",
            AbilityTestScenario.SphereProjectile => "Sphere Projectile",
            AbilityTestScenario.LinearProjectile => "Linear Projectile",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null)
        };

        /// <summary>判断场景是否需要显示和校验投射物通道。</summary>
        /// <param name="scenario">技能场景。</param>
        /// <returns>Sphere 或 Linear Projectile 时返回 true。</returns>
        private static bool IsProjectileScenario(AbilityTestScenario scenario) =>
            scenario == AbilityTestScenario.SphereProjectile ||
            scenario == AbilityTestScenario.LinearProjectile;

        /// <summary>定义 ASC Tester 支持的独立技能场景。</summary>
        private enum AbilityTestScenario
        {
            Instant,
            Async,
            SelfCast,
            AbilityTagsCancellation,
            SelfChannel,
            Toggle,
            Passive,
            SphereProjectile,
            LinearProjectile
        }

        #endregion
    }
}
#endif
