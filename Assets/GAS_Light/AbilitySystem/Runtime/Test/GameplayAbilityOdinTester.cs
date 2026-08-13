#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using RPG.SkillSystem;
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
        private sealed class TickProbeGameplayAbilityTask : GameplayAbilityTask
        {
            private readonly int requiredTicks;

            /// <summary>获取已经收到的 ASC Tick 数量。</summary>
            internal int TickCount { get; private set; }
            /// <summary>获取已经收到的 ASC FixedTick 数量。</summary>
            internal int FixedTickCount { get; private set; }
            /// <summary>获取已经收到的 ASC LateTick 数量。</summary>
            internal int LateTickCount { get; private set; }

            // 保存本次 Runtime 独立的完成阈值。
            internal TickProbeGameplayAbilityTask(
                AsynchronousGameplayAbilityRuntime runtime,
                int requiredTicks)
                : base(runtime)
            {
                this.requiredTicks = requiredTicks;
            }

            /// <summary>Probe 启动时不产生同步副作用，等待后续普通阶段推进。</summary>
            protected override void OnStart()
            {
            }

            /// <summary>每个 ASC 普通阶段增加计数，达到阈值后通知完成。</summary>
            /// <param name="deltaTime">本帧普通更新时间。</param>
            protected override void OnTick(float deltaTime)
            {
                TickCount++;
                if (TickCount >= requiredTicks) Complete();
            }

            /// <summary>记录固定更新阶段，验证该阶段不会冒充普通 Tick。</summary>
            /// <param name="fixedDeltaTime">本次固定更新时间。</param>
            protected override void OnFixedTick(float fixedDeltaTime) => FixedTickCount++;

            /// <summary>记录延迟更新阶段，验证该阶段不会冒充普通 Tick。</summary>
            /// <param name="deltaTime">本次延迟更新时间。</param>
            protected override void OnLateTick(float deltaTime) => LateTickCount++;
        }

        /// <summary>配置一个只接收 LateUpdate 的测试 Task，验证单阶段覆写契约。</summary>
        private sealed class LateOnlyProbeGameplayAbilityTaskConfig : GameplayAbilityTaskConfig
        {
            /// <summary>获取最近一次由测试配置创建的 Task。</summary>
            internal LateOnlyProbeGameplayAbilityTask LastCreated { get; private set; }

            /// <summary>为当前 Runtime 创建只实现 LateUpdate 的测试 Task。</summary>
            /// <param name="runtime">拥有该 Task 的异步 Runtime。</param>
            /// <returns>新的单阶段测试 Task。</returns>
            protected override GameplayAbilityTask CreateTask(
                AsynchronousGameplayAbilityRuntime runtime)
            {
                LastCreated = new LateOnlyProbeGameplayAbilityTask(runtime);
                return LastCreated;
            }
        }

        /// <summary>仅覆写 LateUpdate，并在第一次调用时完成。</summary>
        private sealed class LateOnlyProbeGameplayAbilityTask : GameplayAbilityTask
        {
            /// <summary>获取收到的 LateUpdate 次数。</summary>
            internal int LateTickCount { get; private set; }

            /// <summary>创建等待第一次 LateUpdate 的测试 Task。</summary>
            /// <param name="runtime">拥有该 Task 的异步 Runtime。</param>
            internal LateOnlyProbeGameplayAbilityTask(AsynchronousGameplayAbilityRuntime runtime)
                : base(runtime)
            {
            }

            /// <summary>启动后保持 Running，等待 LateUpdate。</summary>
            protected override void OnStart()
            {
            }

            /// <summary>记录 LateUpdate 并立即完成，用于验证终态阶段保护。</summary>
            /// <param name="deltaTime">本次延迟阶段的秒数。</param>
            protected override void OnLateTick(float deltaTime)
            {
                LateTickCount++;
                Complete();
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

        /// <summary>验证新 Ability 成功激活后使用 CancelTags 按层级匹配并取消旧 Active Ability。</summary>
        [Button("测试 Ability Tags 取消")]
        public void TestAbilityTagCancellation()
        {
            ResetTest();
            if (!EnsureAttributesReady("Ability Tags 取消") ||
                selfCastSkill == null || instantSkill == null)
            {
                Expect("SelfCast 与 Instant Skill SO 已配置",
                    selfCastSkill != null && instantSkill != null);
                LogSummary();
                return;
            }

            GameplayAbilityHandle castHandle = source.GiveAbility(selfCastSkill, 1);
            GameplayAbilityHandle instantHandle = source.GiveAbility(instantSkill, 1);
            bool castActivated = source.TryActivateAbility(
                castHandle, out GameplayAbilityRuntime castRuntime);
            Expect("SelfCast 在取消测试前保持 Active",
                castActivated && castRuntime.State == GameplayAbilityRuntimeState.Active);

            var eventOrder = new List<string>();
            source.Abilities.AbilityActivated += activeRuntime =>
            {
                if (ReferenceEquals(activeRuntime.Spec.Data, instantSkill))
                    eventOrder.Add("Instant.Activated");
            };
            source.Abilities.AbilityCancelled += cancelledRuntime =>
            {
                if (ReferenceEquals(cancelledRuntime, castRuntime))
                    eventOrder.Add("SelfCast.Cancelled");
            };
            source.Abilities.AbilityEnded += endedRuntime =>
            {
                if (ReferenceEquals(endedRuntime.Spec.Data, instantSkill))
                    eventOrder.Add("Instant.Ended");
            };

            bool instantActivated = source.TryActivateAbility(
                instantHandle, out GameplayAbilityRuntime instantRuntime);
            Expect("Instant 成功激活并发出 CancelTags", instantActivated);
            Expect("SelfCast 被父级 CancelTag 取消",
                castRuntime.State == GameplayAbilityRuntimeState.Cancelled);
            Expect("取消事件顺序为 Activated → Cancelled → Ended",
                eventOrder.Count == 3 &&
                eventOrder[0] == "Instant.Activated" &&
                eventOrder[1] == "SelfCast.Cancelled" &&
                eventOrder[2] == "Instant.Ended");

            var castSequence = (SequenceGameplayAbilityTaskConfig)selfCastSkill.RootTask;
            var castWait = (WaitDurationGameplayAbilityTaskConfig)castSequence.Children[0];
            source.TryGetCurrentValue(GameplayAttributes.Attribute_Health, out float healthAfterInstant);
            source.Tick(castWait.Duration);
            source.TryGetCurrentValue(GameplayAttributes.Attribute_Health, out float healthAfterWait);
            Expect("被取消 SelfCast 不会在等待后结算 Effects",
                Mathf.Approximately(healthAfterWait, healthAfterInstant));
            Expect("AbilityTags 与 CancelTags 不进入 ASC Owner Tags",
                source.Tags.IsEmpty);
            Expect("Instant Runtime 正常同步结束",
                instantRuntime.State == GameplayAbilityRuntimeState.Ended);
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
            Expect("SelfChannel 结束后移出 Active Runtime", !ContainsActiveRuntime(channelRuntime));

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

        /// <summary>验证通用 Task 通过 Active Runtime 树逐帧执行并在完成后停止推进。</summary>
        [Button("测试三阶段 Task")]
        public void TestThreePhaseTask()
        {
            ResetTest();
            var data = ScriptableObject.CreateInstance<TestAsynchronousAbilityData>();
            data.Initialize(new TickProbeGameplayAbilityTaskConfig(3));
            GameplayAbilityHandle handle = source.GiveAbility(data, 1);

            bool activated = source.TryActivateAbility(handle, null, out runtime);
            Expect("Tick Ability 激活成功", activated);
            if (!activated)
            {
                DestroyImmediate(data);
                LogSummary();
                return;
            }

            var asyncRuntime = (AsynchronousGameplayAbilityRuntime)runtime;
            var probe = (TickProbeGameplayAbilityTask)asyncRuntime.RootTask;
            source.FixedTick(0.02f);
            source.LateTick(0.1f);
            Expect("FixedTick 只进入固定阶段", probe.FixedTickCount == 1 && probe.TickCount == 0);
            Expect("LateTick 只进入延迟阶段", probe.LateTickCount == 1 && probe.TickCount == 0);
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
            Expect("完成后 Runtime 移出 Active",
                source.ActiveAbilities.Count == 0);

            int finalTickCount = probe.TickCount;
            int finalFixedTickCount = probe.FixedTickCount;
            int finalLateTickCount = probe.LateTickCount;
            source.Tick(0.1f);
            source.FixedTick(0.02f);
            source.LateTick(0.1f);
            Expect("完成后 ASC 不再发送 Tick",
                probe.TickCount == finalTickCount &&
                probe.FixedTickCount == finalFixedTickCount &&
                probe.LateTickCount == finalLateTickCount);

            DestroyImmediate(data);
            LogSummary();
        }

        /// <summary>验证单阶段 Task 与 Sequence 只推进当前 Running 子 Task。</summary>
        [Button("测试单阶段与 Sequence 转发")]
        public void TestSinglePhaseAndSequence()
        {
            ResetTest();
            var data = ScriptableObject.CreateInstance<TestAsynchronousAbilityData>();
            var lateConfig = new LateOnlyProbeGameplayAbilityTaskConfig();
            var tickConfig = new TickProbeGameplayAbilityTaskConfig(1);
            data.Initialize(new SequenceGameplayAbilityTaskConfig(
                new GameplayAbilityTaskConfig[]
                {
                    lateConfig,
                    tickConfig
                }));
            GameplayAbilityHandle handle = source.GiveAbility(data, 1);

            bool activated = source.TryActivateAbility(handle, null, out runtime);
            Expect("单阶段 Sequence 激活成功", activated);
            var lateOnly = lateConfig.LastCreated;
            source.Tick(0.1f);
            source.FixedTick(0.02f);
            Expect("LateOnly 不接收未覆写阶段", lateOnly.LateTickCount == 0 &&
                runtime.State == GameplayAbilityRuntimeState.Active);
            source.LateTick(0.1f);
            Expect("LateOnly 只接收一次 LateUpdate", lateOnly.LateTickCount == 1);

            Expect("Sequence 完成前一项后才启动下一项",
                runtime.State == GameplayAbilityRuntimeState.Active);
            source.Tick(0.1f);
            Expect("当前 Tick 子 Task 完成后 Runtime Ended",
                runtime.State == GameplayAbilityRuntimeState.Ended);
            int finalLateCount = lateOnly.LateTickCount;
            source.LateTick(0.1f);
            Expect("终态 Runtime 不再接收后续阶段", lateOnly.LateTickCount == finalLateCount);

            DestroyImmediate(data);
            LogSummary();
        }

        /// <summary>验证 SkillConfig Task 缐少 Host 时明确结束且不残留 Active Runtime。</summary>
        [Button("测试 SkillConfig 缺少 Host")]
        public void TestSkillConfigMissingHost()
        {
            ResetTest();
            var config = ScriptableObject.CreateInstance<SkillConfig>();
            var data = ScriptableObject.CreateInstance<TestAsynchronousAbilityData>();
            data.Initialize(new PlaySkillConfigGameplayAbilityTaskConfig(config));
            GameplayAbilityHandle handle = source.GiveAbility(data, 1);

            bool activated = source.TryActivateAbility(handle, null, out runtime);
            Expect("缺少 Host 时激活流程已经提交", activated);
            Expect("缺少 Host 时 Task 立即完成",
                runtime != null && runtime.State == GameplayAbilityRuntimeState.Ended);
            Expect("缺少 Host 时不残留 Active Runtime", source.ActiveAbilities.Count == 0);

            DestroyImmediate(data);
            DestroyImmediate(config);
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
            TestAbilityTagCancellation();
            TestPassiveSkill();
            TestSphereProjectileSkill();
            TestCommonSelfAbilities();
            TestSynchronousAbility();
            TestAsynchronousSequence();
            TestThreePhaseTask();
            TestSinglePhaseAndSequence();
            TestSkillConfigMissingHost();
            TestAsynchronousTermination();
        }
        #endregion

        // 测试组件销毁时清理尚未结束按钮留下的临时 ASC。
        private void OnDestroy() => CleanupSource();

        #region 内部辅助
        /// <summary>判断当前 ASC 的 Active Runtime 列表是否仍包含指定实例。</summary>
        /// <param name="candidate">待查询的 Runtime。</param>
        /// <returns>仍存在相同引用时返回 true。</returns>
        private bool ContainsActiveRuntime(GameplayAbilityRuntime candidate)
        {
            for (int i = 0; i < source.ActiveAbilities.Count; i++)
                if (ReferenceEquals(source.ActiveAbilities[i], candidate)) return true;
            return false;
        }

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
