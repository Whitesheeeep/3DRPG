#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.CustomEventSystem;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.GameplayEffect;
using WS_Modules.GAS.Generated;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>通过 Odin Inspector 手动验证常用 GA、同步/异步 Task、重复激活策略与终态事件。</summary>
    public sealed class GameplayAbilityOdinTester : MonoBehaviour
    {
        #region 测试类型
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
        private GameObject sourceObject;
        private GameplayAbilitySystemComponent source;
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
        [SerializeField, AssetsOnly, Required, Tooltip("用于验证 AbilityId 查表与 ASC Handle 反查。")]
        private GameplayAbilityDatabase abilityDatabase;

        [Title("Skill 测试 SO")]
        [SerializeField, AssetsOnly, Tooltip("可直接在 GA Editor 中编辑的 Instant Skill SO。")]
        private InstantGameplayAbilityData instantSkill;
        [SerializeField, AssetsOnly, Tooltip("可直接在 GA Editor 中编辑的 Passive Skill SO。")]
        private PassiveGameplayAbilityData passiveSkill;
        [SerializeField, AssetsOnly, Tooltip("可直接在 GA Editor 中编辑的 Sphere Projectile Skill SO。")]
        private SphereProjectileGameplayAbilityData sphereProjectileSkill;
        [SerializeField, AssetsOnly, Tooltip("等待后对 Source 结算的 SelfCast Skill SO。")]
        private SelfCastGameplayAbilityData selfCastSkill;
        [SerializeField, AssetsOnly, Tooltip("周期对 Source 结算的 SelfChannel Skill SO。")]
        private SelfChannelGameplayAbilityData selfChannelSkill;
        [SerializeField, AssetsOnly, Tooltip("再次激活时关闭的 Toggle Skill SO。")]
        private ToggleGameplayAbilityData toggleSkill;
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
            GameplayAbilityHandle handle = source.GiveAbility(data, 1);
            bool activated = source.TryActivateAbility(handle, out runtime);
            if (!activated) LogActivationFailure(data);
            Expect("Instant Skill 激活成功", activated);
            Expect("Instant Skill 返回时已 Ended", runtime != null &&
                runtime.State == GameplayAbilityRuntimeState.Ended);
            Expect("Instant Skill 不残留 Active Runtime", source.ActiveAbilities.Count == 0);
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
            GameplayAbilityHandle handle = source.GiveAbility(data, 1);
            bool activated = source.TryActivateAbility(handle, null, out runtime);
            if (!activated) LogActivationFailure(data);
            Expect("Passive Skill 激活成功", activated);
            Expect("Passive Runtime 保持 Active", runtime is AsynchronousGameplayAbilityRuntime &&
                runtime.State == GameplayAbilityRuntimeState.Active);
            var applied = new List<GameEffectRuntime>();
            for (int i = 0; i < source.ActiveEffects.Count; i++)
            {
                GameEffectRuntime effectRuntime = source.ActiveEffects[i];
                for (int j = 0; j < data.Effects.Count; j++)
                    if (ReferenceEquals(effectRuntime.Data, data.Effects[j]))
                        applied.Add(effectRuntime);
            }
            Expect("Passive 精确保存成功 GE 句柄", applied.Count <= data.Effects.Count);
            bool ended = source.TryEndAbility(runtime);
            Expect("Passive End 成功", ended);
            bool allRemoved = true;
            for (int i = 0; i < applied.Count; i++)
                allRemoved &= !applied[i].IsActive;
            Expect("Passive End 精确移除本次 GE", allRemoved);
            Debug.Log($"[GATest][Passive] SO={data.name}, Configured={data.Effects.Count}, Applied={applied.Count}");

            LogSummary();
        }

        /// <summary>验证 Database 稳定 ID 查表、当前 ASC Handle 反查和重复授予拒绝。</summary>
        [Button("测试 Ability 稳定 ID")]
        public void TestStableAbilityId()
        {
            ResetTest();
            Expect("GameplayAbilityDatabase 已配置", abilityDatabase != null);
            Expect("Instant Skill SO 已配置", instantSkill != null);
            if (abilityDatabase == null || instantSkill == null)
            {
                LogSummary();
                return;
            }

            GameplayAbilityHandle first = source.GiveAbility(instantSkill, 1);
            GameplayAbilityHandle duplicate = source.GiveAbility(instantSkill, 1);
            bool dataResolved = GameplayAbilityManager.Instance.TryGetAbility(
                instantSkill.AbilityId,
                out GameplayAbilityData resolvedData);
            bool resolved = source.TryGetAbilityHandle(instantSkill.AbilityId, out GameplayAbilityHandle queried);
            var otherObject = new GameObject("GA Stable ID Other ASC")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            GameplayAbilitySystemComponent other =
                otherObject.AddComponent<GameplayAbilitySystemComponent>();
            GameplayAbilityHandle otherHandle = other.GiveAbility(instantSkill, 1);
            Expect("AbilityId 已 Bake", instantSkill.AbilityId != GameplayAbilityData.InvalidId);
            Expect("Database 可通过 AbilityId 找回同一 Data",
                dataResolved && ReferenceEquals(resolvedData, instantSkill));
            Expect("首次授予返回有效 Handle", first.IsValid);
            Expect("同一 ASC 拒绝重复授予", !duplicate.IsValid);
            Expect("不同 ASC 可分别授予同一 Ability Data", otherHandle.IsValid);
            Expect("AbilityId 反查返回当前 ASC Handle", resolved && queried == first);
            DestroyImmediate(otherObject);
            LogSummary();
        }

        /// <summary>验证 SelfCast、SelfChannel 与 Toggle 的默认 Root Task 和重复激活策略。</summary>
        [Button("测试常用自身 Ability")]
        public void TestCommonSelfAbilities()
        {
            ResetTest();
            if (!EnsureAttributesReady("常用自身 Ability") ||
                selfCastSkill == null || selfChannelSkill == null || toggleSkill == null)
            {
                Expect("SelfCast、SelfChannel、Toggle SO 已配置",
                    selfCastSkill != null && selfChannelSkill != null && toggleSkill != null);
                LogSummary();
                return;
            }

            GameplayAbilityHandle castHandle = source.GiveAbility(selfCastSkill, 1);
            bool castActivated = source.TryActivateAbility(castHandle, out GameplayAbilityRuntime castRuntime);
            Expect("SelfCast 激活成功", castActivated);
            Expect("SelfCast Active 时拒绝重复激活", !source.TryActivateAbility(castHandle, out _));
            var castSequence = (SequenceGameplayAbilityTaskConfig)selfCastSkill.RootTask;
            var castWait = (WaitDurationGameplayAbilityTaskConfig)castSequence.Children[0];
            source.Tick(castWait.Duration);
            Expect("SelfCast 等待完成后 Ended",
                castRuntime.State == GameplayAbilityRuntimeState.Ended);

            source.TryGetCurrentValue(GameplayAttributes.Attribute_Health, out float healthAfterCast);
            source.TryActivateAbility(castHandle, out GameplayAbilityRuntime stoppedCast);
            Expect("SelfCast 可在上次完成后重新激活", stoppedCast != null);
            Expect("SelfCast 提前 End 成功", source.TryEndAbility(stoppedCast));
            source.Tick(castWait.Duration);
            source.TryGetCurrentValue(GameplayAttributes.Attribute_Health, out float healthAfterStop);
            Expect("SelfCast 提前 End 不结算 Effects",
                Mathf.Approximately(healthAfterStop, healthAfterCast));

            source.TryActivateAbility(castHandle, out GameplayAbilityRuntime cancelledCast);
            Expect("SelfCast 提前 Cancel 成功", source.TryCancelAbility(cancelledCast));
            source.Tick(castWait.Duration);
            source.TryGetCurrentValue(GameplayAttributes.Attribute_Health, out float healthAfterCancel);
            Expect("SelfCast 提前 Cancel 不结算 Effects",
                Mathf.Approximately(healthAfterCancel, healthAfterCast));

            GameplayAbilityHandle channelHandle = source.GiveAbility(selfChannelSkill, 1);
            bool channelActivated = source.TryActivateAbility(
                channelHandle, out GameplayAbilityRuntime channelRuntime);
            Expect("SelfChannel 激活成功", channelActivated);
            Expect("SelfChannel Active 时拒绝重复激活",
                !source.TryActivateAbility(channelHandle, out _));
            var periodic = (PeriodicSelfEffectsGameplayAbilityTaskConfig)selfChannelSkill.RootTask;
            if (periodic.Infinite)
                source.TryEndAbility(channelRuntime);
            else
                source.Tick(periodic.Duration);
            Expect("SelfChannel 完成或结束后不再 Active",
                channelRuntime.State == GameplayAbilityRuntimeState.Ended);
            Expect("SelfChannel 结束后释放 Tick 注册", source.TickRegistrationCount == 0);

            GameplayAbilityHandle toggleHandle = source.GiveAbility(toggleSkill, 1);
            int effectsBeforeToggle = source.ActiveEffects.Count;
            bool toggleActivated = source.TryActivateAbility(
                toggleHandle, out GameplayAbilityRuntime toggleRuntime);
            Expect("Toggle 首次激活成功并保持 Active", toggleActivated &&
                toggleRuntime.State == GameplayAbilityRuntimeState.Active);
            Expect("Toggle 首次激活持有持续 GE",
                source.ActiveEffects.Count > effectsBeforeToggle);
            bool toggledOff = source.TryActivateAbility(
                toggleHandle, out GameplayAbilityRuntime toggledRuntime);
            Expect("Toggle 再次激活正常关闭", toggledOff &&
                ReferenceEquals(toggleRuntime, toggledRuntime) &&
                toggleRuntime.State == GameplayAbilityRuntimeState.Ended);
            Expect("Toggle 关闭后移除本次持续 GE",
                source.ActiveEffects.Count == effectsBeforeToggle);

            LogSummary();
        }

        /// <summary>验证同步 Sphere Projectile 创建对象后立即结束 GA Runtime。</summary>
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

            GameplayAbilityHandle handle = source.GiveAbility(data, 1);
            bool activated = source.TryActivateAbility(handle, null, out runtime);
            if (!activated) LogActivationFailure(data);
            Expect("Sphere Projectile 激活成功", activated);
            Expect("创建后 GA Runtime 已 Ended", runtime != null &&
                runtime.State == GameplayAbilityRuntimeState.Ended);

            GameObject projectile = GameObject.Find("GA Sphere Projectile (Test)");
            Expect("球体对象已创建", projectile != null);
            if (projectile != null) DestroyImmediate(projectile);

            LogSummary();
        }
        /// <summary>验证同步 Ability 在激活调用内执行并按 Activated→Ended 顺序结束。</summary>
        [Button("测试同步 Ability")]
        public void TestSynchronousAbility()
        {
            ResetTest();
            var data = ScriptableObject.CreateInstance<TestSynchronousAbilityData>();
            GameplayAbilityHandle handle = source.GiveAbility(data, 2);
            bool specFound = source.TryGetAbilitySpec(handle, out GameplayAbilitySpec spec);
            Expect("ASC Spec query", specFound && ReferenceEquals(spec.Data, data));
            bool levelChanged = source.TrySetAbilityLevel(handle, 3);
            Expect("ASC Ability level", levelChanged && spec.Level == 3);
            var events = new List<string>();
            source.Abilities.AbilityActivated += _ => events.Add("Activated");
            source.Abilities.AbilityEnded += _ => events.Add("Ended");

            bool activated = source.TryActivateAbility(handle, null, out runtime);
            Expect("同步激活成功", activated);
            Expect("Execute 执行一次", data.ExecuteCount == 1);
            Expect("返回时已经 Ended", runtime != null &&
                runtime.State == GameplayAbilityRuntimeState.Ended);
            Expect("同步 Runtime 已移出 Active", source.ActiveAbilities.Count == 0);
            Expect("事件顺序 Activated→Ended",
                events.Count == 2 && events[0] == "Activated" && events[1] == "Ended");

            bool removed = source.TryRemoveAbility(handle);
            Expect("ASC remove ended spec", removed && source.GrantedAbilities.Count == 0);

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

            GameplayAbilityHandle handle = source.GiveAbility(data, 1);
            bool activated = source.TryActivateAbility(handle, null, out runtime);
            Expect("异步激活成功", activated);
            Expect("等待期间保持 Active", runtime != null &&
                runtime.State == GameplayAbilityRuntimeState.Active);
            source.Tick(0.4f);
            Expect("未达到时长仍 Active", runtime.State == GameplayAbilityRuntimeState.Active);
            source.Tick(0.6f);
            Expect("Root 完成后 Ended", runtime.State == GameplayAbilityRuntimeState.Ended);
            Expect("完成后移出 Active", source.ActiveAbilities.Count == 0);

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
            GameplayAbilityHandle handle = source.GiveAbility(data, 1);

            bool activated = source.TryActivateAbility(handle, null, out runtime);
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
                source.ActiveAbilities.Count == 0);

            int finalTickCount = probe.TickCount;
            source.Tick(0.1f);
            Expect("完成后 ASC 不再发送 Tick",
                probe.TickCount == finalTickCount);

            DestroyImmediate(data);
            LogSummary();
        }

        /// <summary>验证 Tick 回调注销其他回调和注册新回调时不会重复执行、漏执行或提前执行。</summary>
        [Button("测试 Tick 回调修改安全")]
        public void TestTickMutationSafety()
        {
            ResetTest();
            int removedCallbackCount = 0;
            int controllerCallbackCount = 0;
            int nextFrameCallbackCount = 0;
            bool nextFrameRegistered = false;
            IUnRegister nextFrameRegistration = null;
            IUnRegister removedRegistration = source.RegisterAbilityTick(
                _ => removedCallbackCount++);
            IUnRegister controllerRegistration = source.RegisterAbilityTick(_ =>
            {
                controllerCallbackCount++;
                removedRegistration.UnRegister();
                if (nextFrameRegistered) return;
                nextFrameRegistered = true;
                nextFrameRegistration = source.RegisterAbilityTick(
                    _ => nextFrameCallbackCount++);
            });

            // 倒序执行的 Controller 回调先注销前一项，并在当前帧注册第三项。
            source.Tick(0.1f);
            Expect("被其他回调注销后本帧不再执行", removedCallbackCount == 0);
            Expect("修改注册表的回调本帧只执行一次", controllerCallbackCount == 1);
            Expect("Tick 中新增回调不在当前帧执行", nextFrameCallbackCount == 0);

            source.Tick(0.1f);
            Expect("下一帧继续执行原回调一次", controllerCallbackCount == 2);
            Expect("新增回调从下一帧开始执行", nextFrameCallbackCount == 1);

            controllerRegistration.UnRegister();
            nextFrameRegistration?.UnRegister();
            Expect("测试结束后 Tick 注册全部释放", source.TickRegistrationCount == 0);
            LogSummary();
        }

        /// <summary>验证外部 End、Cancel 与 Clear 对 Root Task 的不同终态传播。</summary>
        [Button("测试异步 End / Cancel / Clear")]
        public void TestAsynchronousTermination()
        {
            ResetTest();
            var data = ScriptableObject.CreateInstance<TestAsynchronousAbilityData>();
            data.Initialize(new WaitDurationGameplayAbilityTaskConfig(10f));
            GameplayAbilityHandle handle = source.GiveAbility(data, 1);

            source.TryActivateAbility(handle, null, out GameplayAbilityRuntime ended);
            Expect("外部 End 成功", source.TryEndAbility(ended));
            Expect("End 后 Root 为 Stopped",
                ((AsynchronousGameplayAbilityRuntime)ended).RootTask.State ==
                GameplayAbilityTaskState.Stopped);

            source.TryActivateAbility(handle, null, out GameplayAbilityRuntime cancelled);
            Expect("外部 Cancel 成功", source.TryCancelAbility(cancelled));
            Expect("Cancel 后 Root 为 Cancelled",
                ((AsynchronousGameplayAbilityRuntime)cancelled).RootTask.State ==
                GameplayAbilityTaskState.Cancelled);

            source.TryActivateAbility(handle, null, out GameplayAbilityRuntime cleared);
            source.Abilities.Clear();
            Expect("Clear 将 Runtime Cancelled",
                cleared.State == GameplayAbilityRuntimeState.Cancelled);
            Expect("Clear 清空 Spec 和 Active",
                source.GrantedAbilities.Count == 0 &&
                source.ActiveAbilities.Count == 0);

            DestroyImmediate(data);
            LogSummary();
        }

        /// <summary>依次执行全部同步与异步基础测试。</summary>
        [Button("执行完整 GA 多态测试", ButtonSizes.Large)]
        public void RunAll()
        {
            TestStableAbilityId();
            TestInstantSkill();
            TestPassiveSkill();
            TestSphereProjectileSkill();
            TestCommonSelfAbilities();
            TestSynchronousAbility();
            TestAsynchronousSequence();
            TestTickTask();
            TestTickMutationSafety();
            TestAsynchronousTermination();
        }
        #endregion

        // 测试组件销毁时清理尚未结束按钮留下的临时 ASC。
        private void OnDestroy() => CleanupSource();

        #region 内部辅助
        /// <summary>为每个场景创建独立 ASC 并初始化 Ability Database，避免运行时状态相互污染。</summary>
        private void ResetTest()
        {
            CleanupSource();
            sourceObject = new GameObject("GA Odin Test ASC")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            source = sourceObject.AddComponent<GameplayAbilitySystemComponent>();
            runtime = null;
            passed = 0;
            failed = 0;
            attributesReady = false;
            attributeInitializationError = string.Empty;

            if (abilityDatabase != null)
                GameplayAbilityManager.Instance.Initialize(abilityDatabase);

            if (testAttributeSet == null)
            {
                attributeInitializationError = "请先指定 GameplayAttributeTestSet。";
                Debug.LogError($"[GATest][Initialize] {attributeInitializationError}");
            }
            else
            {
                source.Initialize(new[] { testAttributeSet });
                attributesReady = source.IsInitialized;
                if (!attributesReady)
                {
                    attributeInitializationError = "ASC AttributeSet 初始化失败，请查看前置日志。";
                    Debug.LogError($"[GATest][Initialize] {attributeInitializationError}");
                }
            }
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
                                  source.HasActiveEffect(data.CooldownEffect);
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

        // 输出当前按钮场景的汇总，并释放本场景创建的临时 ASC。
        private void LogSummary()
        {
            Debug.Log($"[GATest][Summary] PASS={passed}, FAIL={failed}");
            CleanupSource();
        }

        /// <summary>销毁隔离测试创建的临时 ASC，并清除本测试的 Manager 数据库引用。</summary>
        private void CleanupSource()
        {
            if (sourceObject != null) DestroyImmediate(sourceObject);
            sourceObject = null;
            source = null;
            GameplayAbilityManager.Instance.Reset();
        }
        #endregion
    }
}
#endif
