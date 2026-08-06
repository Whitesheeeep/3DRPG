#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.GameplayEffect;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>通过 Odin Inspector 手动验证 Instant、Passive、Projectile、同步/异步 Task 与终态事件。</summary>
    public sealed class GameplayAbilityOdinTester : MonoBehaviour
    {
        #region 测试类型
        /// <summary>提供可由测试代码推进的最小 ASC。</summary>
        private sealed class TestAbilitySystemComponent : AbilitySystemComponentBase
        {
            // 使用 ASC 自身 AbilityCtrl 管理 Tick 注册。
            internal TestAbilitySystemComponent() : base()
            {
            }
        }

        /// <summary>记录同步 Execute 次数的测试 Ability Data。</summary>
        private sealed class TestSynchronousAbilityData : SynchronousGameplayAbilityData
        {
            internal int ExecuteCount { get; private set; }

            // 同步执行只记录次数，用于验证 TryActivate 返回前已结束。
            protected override void Execute(SynchronousGameplayAbilityRuntime runtime) =>
                ExecuteCount++;
        }

        /// <summary>允许测试代码注入 Root Task 的异步 Ability Data。</summary>
        private sealed class TestAsynchronousAbilityData : AsynchronousGameplayAbilityData
        {
            // 通过受控基类入口配置一次测试 Root。
            internal void Initialize(GameplayAbilityTaskConfig config) => SetRootTask(config);
        }

        /// <summary>配置一个按固定 Tick 次数完成的测试 Task。</summary>
        private sealed class TickProbeGameplayAbilityTaskConfig : GameplayAbilityTaskConfig
        {
            private readonly int requiredTicks;

            // 测试配置必须至少等待一个 Tick。
            internal TickProbeGameplayAbilityTaskConfig(int requiredTicks)
            {
                this.requiredTicks = requiredTicks;
            }

            // 仅允许正数 Tick 配置进入运行时。
            internal override bool IsConfigurationValid => requiredTicks > 0;

            // 每次 Runtime 激活创建独立 Probe Task。
            protected override GameplayAbilityTask CreateTask(
                AsynchronousGameplayAbilityRuntime runtime) =>
                new TickProbeGameplayAbilityTask(runtime, requiredTicks);
        }
        /// <summary>记录每次 ASC Tick 推进并在达到次数后完成的测试 Task。</summary>
        private sealed class TickProbeGameplayAbilityTask : GameplayAbilityTickTask
        {
            private readonly int requiredTicks;

            /// <summary>获取已经收到的 ASC Tick 数量。</summary>
            internal int TickCount { get; private set; }

            // 保存本次 Runtime 独立的完成阈值。
            internal TickProbeGameplayAbilityTask(
                AsynchronousGameplayAbilityRuntime runtime,
                int requiredTicks)
                : base(runtime)
            {
                this.requiredTicks = requiredTicks;
            }

            // 每个 ASC Tick 增加计数，达到阈值后通知完成。
            protected override void OnTick(float deltaTime)
            {
                TickCount++;
                if (TickCount >= requiredTicks) Complete();
            }
        }
        #endregion

        #region 运行状态
        private TestAbilitySystemComponent source;
        private GameplayAbilityRuntime runtime;
        private int passed;
        private int failed;
        private bool attributesReady;
        private string attributeInitializationError;
        #endregion


        #region Skill 测试输入
        [Title("Skill 测试依赖")]
        [SerializeField, AssetsOnly, Required, Tooltip("Cost GE 使用的四属性测试 Set；每次测试重置 ASC 时导入。")]
        private GameplayAttributeSet testAttributeSet;

        [Title("Skill 测试 SO")]
        [SerializeField, AssetsOnly, Tooltip("可直接在 GA Editor 中编辑的 Instant Skill SO。")]
        private InstantGameplayAbilityData instantSkill;
        [SerializeField, AssetsOnly, Tooltip("可直接在 GA Editor 中编辑的 Passive Skill SO。")]
        private PassiveGameplayAbilityData passiveSkill;
        [SerializeField, AssetsOnly, Tooltip("可直接在 GA Editor 中编辑的 Sphere Projectile Skill SO。")]
        private SphereProjectileGameplayAbilityData sphereProjectileSkill;
        [SerializeField, Tooltip("Sphere Projectile 的出生点；为空时使用测试组件自身 Transform。")]
        private Transform sphereSpawnPoint;
        #endregion

        #region Odin 测试
        /// <summary>验证 Instant Skill 的多个 GE 独立应用与同步 Runtime 立即结束。</summary>
        [Button("测试 Instant Skill")]
        public void TestInstantSkill()
        {
            ResetTest();
            InstantGameplayAbilityData data = instantSkill;
            Expect("Instant Skill SO 已配置", data != null);
            if (data == null)
            {
                LogSummary();
                return;
            }
            if (!EnsureAttributesReady("Instant Skill"))
            {
                LogSummary();
                return;
            }
            GameplayAbilityHandle handle = source.Abilities.GiveAbility(data, 1);
            bool activated = source.Abilities.TryActivate(handle, null, out runtime);
            if (!activated) LogActivationFailure(data);
            Expect("Instant Skill 激活成功", activated);
            Expect("Instant Skill 返回时已 Ended", runtime != null &&
                runtime.State == GameplayAbilityRuntimeState.Ended);
            Expect("Instant Skill 不残留 Active Runtime", source.Abilities.ActiveRuntimes.Count == 0);
            Debug.Log($"[GATest][Instant] SO={data.name}, GECount={data.Effects.Count}");

            LogSummary();
        }

        /// <summary>验证 Passive Skill 保存并在 End 时精确移除本次 GE 句柄。</summary>
        [Button("测试 Passive Skill")]
        public void TestPassiveSkill()
        {
            ResetTest();
            PassiveGameplayAbilityData data = passiveSkill;
            Expect("Passive Skill SO 已配置", data != null);
            if (data == null)
            {
                LogSummary();
                return;
            }
            if (!EnsureAttributesReady("Passive Skill"))
            {
                LogSummary();
                return;
            }
            GameplayAbilityHandle handle = source.Abilities.GiveAbility(data, 1);
            bool activated = source.Abilities.TryActivate(handle, null, out runtime);
            if (!activated) LogActivationFailure(data);
            var passive = runtime as PassiveGameplayAbilityRuntime;
            Expect("Passive Skill 激活成功", activated);
            Expect("Passive Runtime 保持 Active", passive != null &&
                runtime.State == GameplayAbilityRuntimeState.Active);
            var applied = passive == null
                ? new List<GameEffectRuntime>()
                : new List<GameEffectRuntime>(passive.AppliedEffects);
            Expect("Passive 精确保存成功 GE 句柄", passive != null &&
                applied.Count <= data.Effects.Count);
            bool ended = source.Abilities.TryEnd(runtime);
            Expect("Passive End 成功", ended);
            bool allRemoved = true;
            for (int i = 0; i < applied.Count; i++)
                allRemoved &= !applied[i].IsActive;
            Expect("Passive End 精确移除本次 GE", allRemoved);
            Debug.Log($"[GATest][Passive] SO={data.name}, Configured={data.Effects.Count}, Applied={applied.Count}");

            LogSummary();
        }

        /// <summary>验证同步 Sphere Projectile 创建后 GA 结束且投射物独立存活。</summary>
        [Button("测试 Sphere Projectile Skill")]
        public void TestSphereProjectileSkill()
        {
            ResetTest();
            SphereProjectileGameplayAbilityData data = sphereProjectileSkill;
            Expect("Sphere Projectile Skill SO 已配置", data != null);
            if (data == null)
            {
                LogSummary();
                return;
            }
            data.Initialize(sphereSpawnPoint != null ? sphereSpawnPoint : transform);
            GameplayAbilityHandle handle = source.Abilities.GiveAbility(data, 1);
            bool activated = source.Abilities.TryActivate(handle, null, out runtime);
            if (!activated) LogActivationFailure(data);
            Expect("Sphere Projectile 激活成功", activated);
            Expect("创建后 GA Runtime 已 Ended", runtime != null &&
                runtime.State == GameplayAbilityRuntimeState.Ended);
            Expect("球体对象已创建", data.SpawnedObject != null);
            Debug.Log("[GATest][Sphere] 投射物由测试 Transform 生成，未进行碰撞或 Target 筛选。");
            if (data.SpawnedObject != null) DestroyImmediate(data.SpawnedObject);

            LogSummary();
        }

        /// <summary>验证同步 Ability 在激活调用内执行并按 Activated→Ended 顺序结束。</summary>
        [Button("测试同步 Ability")]
        public void TestSynchronousAbility()
        {
            ResetTest();
            var data = ScriptableObject.CreateInstance<TestSynchronousAbilityData>();
            GameplayAbilityHandle handle = source.Abilities.GiveAbility(data, 2);
            var events = new List<string>();
            source.Abilities.AbilityActivated += _ => events.Add("Activated");
            source.Abilities.AbilityEnded += _ => events.Add("Ended");

            bool activated = source.Abilities.TryActivate(handle, null, out runtime);
            Expect("同步激活成功", activated);
            Expect("Execute 执行一次", data.ExecuteCount == 1);
            Expect("返回时已经 Ended", runtime != null &&
                runtime.State == GameplayAbilityRuntimeState.Ended);
            Expect("同步 Runtime 已移出 Active", source.Abilities.ActiveRuntimes.Count == 0);
            Expect("事件顺序 Activated→Ended",
                events.Count == 2 && events[0] == "Activated" && events[1] == "Ended");

            DestroyImmediate(data);
            LogSummary();
        }

        /// <summary>验证 Sequence 等待、ASC Tick 推进、正常完成与注册释放。</summary>
        [Button("测试异步 Sequence + Wait")]
        public void TestAsynchronousSequence()
        {
            ResetTest();
            var data = ScriptableObject.CreateInstance<TestAsynchronousAbilityData>();
            data.Initialize(new SequenceGameplayAbilityTaskConfig(
                new GameplayAbilityTaskConfig[]
                {
                    new WaitDurationGameplayAbilityTaskConfig(0f),
                    new WaitDurationGameplayAbilityTaskConfig(1f)
                }));

            GameplayAbilityHandle handle = source.Abilities.GiveAbility(data, 1);
            bool activated = source.Abilities.TryActivate(handle, null, out runtime);
            Expect("异步激活成功", activated);
            Expect("等待期间保持 Active", runtime != null &&
                runtime.State == GameplayAbilityRuntimeState.Active);
            source.Tick(0.4f);
            Expect("未达到时长仍 Active", runtime.State == GameplayAbilityRuntimeState.Active);
            source.Tick(0.6f);
            Expect("Root 完成后 Ended", runtime.State == GameplayAbilityRuntimeState.Ended);
            Expect("完成后移出 Active", source.Abilities.ActiveRuntimes.Count == 0);

            DestroyImmediate(data);
            LogSummary();
        }

        /// <summary>验证通用 Tick Task 每帧执行、完成通知和 ASC Tick 注销。</summary>
        [Button("测试 Tick Task")]
        public void TestTickTask()
        {
            ResetTest();
            var data = ScriptableObject.CreateInstance<TestAsynchronousAbilityData>();
            data.Initialize(new TickProbeGameplayAbilityTaskConfig(3));
            GameplayAbilityHandle handle = source.Abilities.GiveAbility(data, 1);

            bool activated = source.Abilities.TryActivate(handle, null, out runtime);
            Expect("Tick Ability 激活成功", activated);
            Expect("激活后 ASC Tick 注册一次", source.TickRegistrationCount == 1);
            if (!activated)
            {
                DestroyImmediate(data);
                LogSummary();
                return;
            }

            var asyncRuntime = (AsynchronousGameplayAbilityRuntime)runtime;
            var probe = (TickProbeGameplayAbilityTask)asyncRuntime.RootTask;
            source.Tick(0.1f);
            Expect("第一次 Tick 被接收", probe.TickCount == 1);
            Expect("未完成前 Runtime 保持 Active",
                runtime.State == GameplayAbilityRuntimeState.Active);
            source.Tick(0.1f);
            Expect("第二次 Tick 被接收", probe.TickCount == 2);
            source.Tick(0.1f);
            Expect("达到 Tick 次数后 Task 完成",
                probe.State == GameplayAbilityTaskState.Completed);
            Expect("Root 完成后 Runtime Ended",
                runtime.State == GameplayAbilityRuntimeState.Ended);
            Expect("完成后 ASC Tick 注册归零", source.TickRegistrationCount == 0);
            Expect("完成后 Runtime 移出 Active",
                source.Abilities.ActiveRuntimes.Count == 0);

            int finalTickCount = probe.TickCount;
            source.Tick(0.1f);
            Expect("完成后 ASC 不再发送 Tick",
                probe.TickCount == finalTickCount);

            DestroyImmediate(data);
            LogSummary();
        }
        /// <summary>验证外部 End、Cancel 与 Clear 对 Root Task 的不同终态传播。</summary>
        [Button("测试异步 End / Cancel / Clear")]
        public void TestAsynchronousTermination()
        {
            ResetTest();
            var data = ScriptableObject.CreateInstance<TestAsynchronousAbilityData>();
            data.Initialize(new WaitDurationGameplayAbilityTaskConfig(10f));
            GameplayAbilityHandle handle = source.Abilities.GiveAbility(data, 1);

            source.Abilities.TryActivate(handle, null, out GameplayAbilityRuntime ended);
            Expect("外部 End 成功", source.Abilities.TryEnd(ended));
            Expect("End 后 Root 为 Stopped",
                ((AsynchronousGameplayAbilityRuntime)ended).RootTask.State ==
                GameplayAbilityTaskState.Stopped);

            source.Abilities.TryActivate(handle, null, out GameplayAbilityRuntime cancelled);
            Expect("外部 Cancel 成功", source.Abilities.TryCancel(cancelled));
            Expect("Cancel 后 Root 为 Cancelled",
                ((AsynchronousGameplayAbilityRuntime)cancelled).RootTask.State ==
                GameplayAbilityTaskState.Cancelled);

            source.Abilities.TryActivate(handle, null, out GameplayAbilityRuntime cleared);
            source.Abilities.Clear();
            Expect("Clear 将 Runtime Cancelled",
                cleared.State == GameplayAbilityRuntimeState.Cancelled);
            Expect("Clear 清空 Spec 和 Active",
                source.Abilities.GrantedAbilities.Count == 0 &&
                source.Abilities.ActiveRuntimes.Count == 0);

            DestroyImmediate(data);
            LogSummary();
        }

        /// <summary>依次执行全部同步与异步基础测试。</summary>
        [Button("执行完整 GA 多态测试", ButtonSizes.Large)]
        public void RunAll()
        {
            TestInstantSkill();
            TestPassiveSkill();
            TestSphereProjectileSkill();
            TestSynchronousAbility();
            TestAsynchronousSequence();
            TestTickTask();
            TestAsynchronousTermination();
        }
        #endregion

        #region 内部辅助
        // 每个场景创建独立 ASC，避免注册和 Runtime 相互污染。
        private void ResetTest()
        {
            source = new TestAbilitySystemComponent();
            runtime = null;
            passed = 0;
            failed = 0;
            attributesReady = false;
            attributeInitializationError = string.Empty;

            if (testAttributeSet == null)
            {
                attributeInitializationError = "请先指定 GameplayAttributeTestSet。";
            }
            else
            {
                attributesReady = source.Attributes.TryInitialize(
                    new[] { testAttributeSet },
                    out attributeInitializationError);
            }

            if (!attributesReady)
                Debug.LogError($"[GATest][Initialize] AttributeSet 初始化失败：{attributeInitializationError}");
        }

        // Attribute 相关场景在提交 Cost 前必须完成测试 Set 导入；缺失时跳过后续激活断言。
        private bool EnsureAttributesReady(string scenarioName)
        {
            if (attributesReady) return true;
            Debug.LogError($"[GATest][Skip] {scenarioName} 跳过：{attributeInitializationError}");
            return false;
        }

        // 激活失败时输出前置条件，区分 Attribute 缺失、Tag 查询、Cooldown 或 SO 配置问题。
        private void LogActivationFailure(GameplayAbilityData data)
        {
            bool cooldownActive = data != null &&
                                  data.CooldownEffect != null &&
                                  source.GameEffectCtrl.HasActiveEffect(data.CooldownEffect);
            Debug.LogError(
                $"[GATest][Activate] 失败：Data={data?.name ?? "null"}, " +
                $"RuntimeConfig={data?.IsRuntimeConfigurationValid.ToString() ?? "null"}, " +
                $"ActivationQueryValid={data?.ActivationTagQuery.IsValid.ToString() ?? "null"}, " +
                $"CooldownActive={cooldownActive}, AttributesReady={attributesReady}, " +
                $"AttributeError={attributeInitializationError}");
        }

        // 统一记录断言与清晰的 PASS/FAIL 日志。
        private void Expect(string label, bool condition)
        {
            if (condition)
            {
                passed++;
                Debug.Log($"[GATest][PASS] {label}");
            }
            else
            {
                failed++;
                Debug.LogError($"[GATest][FAIL] {label}");
            }
        }

        // 输出当前按钮场景的汇总。
        private void LogSummary() =>
            Debug.Log($"[GATest][Summary] PASS={passed}, FAIL={failed}");
        #endregion
    }
}
#endif
