#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.Generated;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayEffect
{
    /// <summary>通过 Odin Inspector 按钮手动验证 GE 配置、运行时、计时和 Attribute 提交链路。</summary>
    public sealed class GameplayEffectOdinTester : MonoBehaviour
    {
        #region 嵌套类型

        /// <summary>保存 Inspector 可编辑的单项 SetByCaller Tag 与数值。</summary>
        [Serializable]
        public struct SetByCallerEntry
        {
            [SerializeField] private GameplayTag key;
            [SerializeField] private float value;

            /// <summary>获取稳定 Tag Key。</summary>
            public GameplayTag Key => key;
            /// <summary>获取调用方 Magnitude。</summary>
            public float Value => value;
        }

        #endregion

        #region 通用测试输入

        [Title("通用 GE 操作")]
        [SerializeField, AssetsOnly] private GameplayEffectData effect;
        [SerializeField, Min(1)] private int level = 1;
        [SerializeField] private float tickDelta = 1f;
        [SerializeField] private List<SetByCallerEntry> setByCaller = new();

        [Title("通用 Attribute Set")]
        [SerializeField] private List<GameplayAttributeSet> sourceSets = new();
        [SerializeField] private List<GameplayAttributeSet> targetSets = new();

        #endregion

        #region 固定场景资产

        [Title("固定测试依赖")]
        [SerializeField, AssetsOnly, Required] private GameplayTagDatabase tagDatabase;
        [SerializeField, AssetsOnly, Required] private GameplayAttributeSet testAttributeSet;

        [Title("Instant 测试 GE")]
        [SerializeField, AssetsOnly, Required] private GameplayEffectData instantFixed;
        [SerializeField, AssetsOnly, Required] private GameplayEffectData instantCurve;
        [SerializeField, AssetsOnly, Required] private GameplayEffectData instantLevel;
        [SerializeField, AssetsOnly, Required] private GameplayEffectData instantSetByCaller;

        [Title("Duration 与 Infinite 测试 GE")]
        [SerializeField, AssetsOnly, Required] private GameplayEffectData durationPeriod;
        [SerializeField, AssetsOnly, Required] private GameplayEffectData durationStat;
        [SerializeField, AssetsOnly, Required] private GameplayEffectData infinitePeriod;
        [SerializeField, AssetsOnly, Required] private GameplayEffectData infiniteStatAndTag;

        [Title("叠层测试 GE")]
        [SerializeField, AssetsOnly, Required] private GameplayEffectData stackBySourceRefresh;
        [SerializeField, AssetsOnly, Required] private GameplayEffectData stackByTargetExtendExpiration;
        [SerializeField, AssetsOnly, Required] private GameplayEffectData stackPeriodPreserveTiming;
        [SerializeField, AssetsOnly, Required] private GameplayEffectData stackPeriodResetTiming;

        #endregion

        #region 运行状态

        private GameObject sourceObject;
        private GameObject sourceBObject;
        private GameObject targetObject;
        private GameplayAbilitySystemComponent source;
        private GameplayAbilitySystemComponent sourceB;
        private GameplayAbilitySystemComponent target;
        private GameEffectRuntime lastRuntime;
        private int passedAssertions;
        private int failedAssertions;

        #endregion

        // 测试组件销毁时清理通用操作或场景套件留下的临时 ASC。
        private void OnDestroy() => CleanupAscObjects();

        #region 通用 Odin 操作

        /// <summary>重新创建 Source/Target ASC，并导入 Inspector 配置的 AttributeSet。</summary>
        [Button("初始化 GE 测试")]
        public void InitializeTest()
        {
            if (tagDatabase != null) GameplayTagManager.Instance.Initialize(tagDatabase);
            CleanupAscObjects();
            source = CreateAsc("GE Test Source", out sourceObject);
            sourceB = CreateAsc("GE Test Source B", out sourceBObject);
            target = CreateAsc("GE Test Target", out targetObject);
            source.Initialize(sourceSets);
            sourceB.Initialize(sourceSets);
            target.Initialize(targetSets);
            lastRuntime = null;
            Debug.Log($"[GETest][Initialize] Source={source.IsInitialized}, SourceB={sourceB.IsInitialized}, Target={target.IsInitialized}");
        }

        /// <summary>使用真实 Controller API 应用一次当前 GE，并输出 Active 状态。</summary>
        [Button("应用一次 GE")]
        public void ApplyEffect()
        {
            if (!EnsureInitialized()) return;
            Dictionary<GameplayTag, float> values = BuildSetByCaller();
            bool success;
            GameEffectRuntime runtime;
            if (level == 1 && values.Count == 0)
                success = target.TryApplyEffect(effect, source, out runtime);
            else
                success = target.TryApplyEffect(effect, source, level, values, out runtime);
            if (runtime != null) lastRuntime = runtime;
            Debug.Log($"[GETest][Apply] Success={success}, Active={target.ActiveEffects.Count}, " +
                      $"Stack={lastRuntime?.StackCount ?? 0}");
            LogTargetAttributes();
        }

        /// <summary>推进一次配置的 deltaTime，验证 Period 与 Duration 到期。</summary>
        [Button("推进 GE Tick")]
        public void TickEffects()
        {
            if (!EnsureInitialized()) return;
            target.GameEffectCtrl.Tick(tickDelta);
            Debug.Log($"[GETest][Tick] Delta={tickDelta}, Active={target.ActiveEffects.Count}, " +
                      $"Duration={lastRuntime?.RemainingDuration ?? 0f}, Period={lastRuntime?.RemainingPeriod ?? 0f}");
            LogTargetAttributes();
        }

        /// <summary>精确移除最近一次返回的 Active Runtime。</summary>
        [Button("移除最近 Active GE")]
        public void RemoveLastEffect()
        {
            if (!EnsureInitialized()) return;
            bool success = lastRuntime != null && target.TryRemoveEffect(lastRuntime);
            Debug.Log($"[GETest][Remove] Success={success}, Active={target.ActiveEffects.Count}");
            if (success) lastRuntime = null;
            LogTargetAttributes();
        }

        /// <summary>清理全部 Active GE，验证 Modifier 与 GrantedTags 一并撤销。</summary>
        [Button("清理全部 GE")]
        public void ClearEffects()
        {
            if (!EnsureInitialized()) return;
            target.GameEffectCtrl.Clear();
            lastRuntime = null;
            Debug.Log("[GETest][Clear] Active=0");
            LogTargetAttributes();
        }

        #endregion

        #region 固定场景 Odin 操作

        /// <summary>验证 Fixed、Curve、Level 与 SetByCaller 四种 Instant Magnitude。</summary>
        [Button("测试 Instant Magnitude", ButtonSizes.Large)]
        public void TestInstantMagnitude()
        {
            ExecuteSuite("Instant Magnitude", RunInstantMagnitudeScenarios);
        }

        /// <summary>验证有限持续、无限持续、周期结算和持续 Stat 恢复。</summary>
        [Button("测试 Duration 与 Infinite", ButtonSizes.Large)]
        public void TestDurationAndInfinite()
        {
            ExecuteSuite("Duration 与 Infinite", RunDurationAndInfiniteScenarios);
        }

        /// <summary>验证 TargetTagQuery、GrantedTag 与持续 Modifier 的同步生命周期。</summary>
        [Button("测试 Tag 条件与 GrantedTag", ButtonSizes.Large)]
        public void TestTagConditionsAndGrantedTags()
        {
            ExecuteSuite("Tag 条件与 GrantedTag", RunTagScenario);
        }

        /// <summary>验证按 Source/Target 叠层、溢出、Duration、Period 与逐层到期。</summary>
        [Button("测试叠层身份与计时", ButtonSizes.Large)]
        public void TestStackingIdentityAndTiming()
        {
            ExecuteSuite("叠层身份与计时", RunStackingScenarios);
        }

        /// <summary>验证外部输入或目标状态失败时不留下 Runtime、Modifier、Tag 或候选状态。</summary>
        [Button("测试失败原子性", ButtonSizes.Large)]
        public void TestFailureAtomicity()
        {
            ExecuteSuite("失败原子性", RunFailureAtomicityScenarios);
        }

        /// <summary>在相互隔离的 ASC 上依次执行全部固定 GE 验收场景。</summary>
        [Button("执行完整 GE 测试", ButtonSizes.Large)]
        public void RunCompleteGameplayEffectTest()
        {
            ExecuteSuite("完整 GE 验收", () =>
            {
                RunInstantMagnitudeScenarios();
                RunDurationAndInfiniteScenarios();
                RunTagScenario();
                RunStackingScenarios();
                RunFailureAtomicityScenarios();
            });
        }

        #endregion
        #region Instant 场景

        // 每个 Magnitude 变体都使用全新 ASC，确保永久 Instant 结算不会污染下一项。
        private void RunInstantMagnitudeScenarios()
        {
            if (PrepareScenario("Instant Fixed") && RequireEffect(instantFixed, nameof(instantFixed)))
            {
                bool success = Apply(instantFixed, source, 1, null, out _);
                Expect("Instant Fixed 应用", success, success, true);
                ExpectValue("Instant Fixed Health", GameplayAttributes.Attribute_Health, 70f);
                Expect("Instant 不进入 Active", target.ActiveEffects.Count == 0,
                    target.ActiveEffects.Count, 0);
            }

            int[] curveLevels = { 1, 2, 3 };
            float[] curveArmor = { 20f, 30f, 40f };
            for (int i = 0; i < curveLevels.Length; i++)
            {
                if (!PrepareScenario($"Instant Curve Level {curveLevels[i]}") ||
                    !RequireEffect(instantCurve, nameof(instantCurve))) continue;
                bool success = Apply(instantCurve, source, curveLevels[i], null, out _);
                Expect($"Curve Level {curveLevels[i]} 应用", success, success, true);
                ExpectValue($"Curve Level {curveLevels[i]} Armor",
                    GameplayAttributes.Attribute_Armor,
                    curveArmor[i]);
            }

            int[] levelInputs = { 1, 2, 4, 6 };
            float[] levelArmor = { 20f, 20f, 40f, 70f };
            for (int i = 0; i < levelInputs.Length; i++)
            {
                if (!PrepareScenario($"Instant Level {levelInputs[i]}") ||
                    !RequireEffect(instantLevel, nameof(instantLevel))) continue;
                bool success = Apply(instantLevel, source, levelInputs[i], null, out _);
                Expect($"Level {levelInputs[i]} 应用", success, success, true);
                ExpectValue($"Level {levelInputs[i]} Armor",
                    GameplayAttributes.Attribute_Armor,
                    levelArmor[i]);
            }

            if (PrepareScenario("Instant SetByCaller") &&
                RequireEffect(instantSetByCaller, nameof(instantSetByCaller)))
            {
                var values = new Dictionary<GameplayTag, float>
                {
                    [GameplayTags.Tag_Test_Test_SetByCaller] = -25f
                };
                bool success = Apply(instantSetByCaller, source, 1, values, out _);
                Expect("SetByCaller 应用", success, success, true);
                ExpectValue("SetByCaller Health", GameplayAttributes.Attribute_Health, 75f);
            }

            if (PrepareScenario("Instant SetByCaller Missing") &&
                RequireEffect(instantSetByCaller, nameof(instantSetByCaller)))
            {
                bool success = Apply(instantSetByCaller, source, 1, null, out _);
                Expect("缺少 SetByCaller 被拒绝", !success, success, false);
                ExpectValue("缺少 SetByCaller Health 不变", GameplayAttributes.Attribute_Health, 100f);
                Expect("缺少 SetByCaller 无 Active", target.ActiveEffects.Count == 0,
                    target.ActiveEffects.Count, 0);
            }
        }

        #endregion

        #region Duration 与 Infinite 场景

        // 验证周期只在 Runtime 实际存续时间内结算，大 delta 的到期后剩余部分不再执行。
        private void RunDurationAndInfiniteScenarios()
        {
            if (PrepareScenario("Duration Period") && RequireEffect(durationPeriod, nameof(durationPeriod)))
            {
                bool success = Apply(durationPeriod, source, 1, null, out GameEffectRuntime runtime);
                Expect("Duration Period 应用", success, success, true);
                ExpectValue("Duration Period 初始不结算", GameplayAttributes.Attribute_Health, 100f);
                target.GameEffectCtrl.Tick(0.5f);
                ExpectValue("Duration Period 0.5s", GameplayAttributes.Attribute_Health, 100f);
                target.GameEffectCtrl.Tick(0.5f);
                ExpectValue("Duration Period 1s", GameplayAttributes.Attribute_Health, 90f);
                target.GameEffectCtrl.Tick(5f);
                ExpectValue("Duration Period 到期共三跳", GameplayAttributes.Attribute_Health, 70f);
                Expect("Duration Period 到期移除", target.ActiveEffects.Count == 0,
                    target.ActiveEffects.Count, 0);
                Expect("Duration Period Runtime 失活", runtime != null && !runtime.IsActive,
                    runtime?.IsActive, false);
            }

            if (PrepareScenario("Infinite Period") && RequireEffect(infinitePeriod, nameof(infinitePeriod)))
            {
                bool success = Apply(infinitePeriod, source, 1, null, out GameEffectRuntime runtime);
                Expect("Infinite Period 应用", success, success, true);
                ExpectValue("Infinite Period 立即结算", GameplayAttributes.Attribute_MP, 45f);
                target.GameEffectCtrl.Tick(2f);
                ExpectValue("Infinite Period 两次 Tick", GameplayAttributes.Attribute_MP, 35f);
                bool removed = target.TryRemoveEffect(runtime);
                Expect("Infinite Period 主动移除", removed, removed, true);
                ExpectValue("已结算 MP 不回滚", GameplayAttributes.Attribute_MP, 35f);
            }

            if (PrepareScenario("Duration Stat") && RequireEffect(durationStat, nameof(durationStat)))
            {
                bool success = Apply(durationStat, source, 1, null, out _);
                Expect("Duration Stat 应用", success, success, true);
                ExpectValue("Duration Stat MaxHealth", GameplayAttributes.Attribute_MaxHealth, 60f);
                ExpectValue("Duration Stat Health Clamp", GameplayAttributes.Attribute_Health, 60f);
                target.GameEffectCtrl.Tick(2f);
                ExpectValue("Duration Stat 到期恢复 MaxHealth", GameplayAttributes.Attribute_MaxHealth, 100f);
                ExpectValue("Duration Stat 到期不恢复已结算 Health", GameplayAttributes.Attribute_Health, 60f);
                Expect("Duration Stat 到期移除", target.ActiveEffects.Count == 0,
                    target.ActiveEffects.Count, 0);
            }
        }

        #endregion

        #region Tag 场景

        // 先验证查询拒绝，再满足 Required Tag 并检查 GrantedTag 与 Modifier 的共同生命周期。
        private void RunTagScenario()
        {
            if (!PrepareScenario("Tag Query 与 GrantedTag") ||
                !RequireEffect(infiniteStatAndTag, nameof(infiniteStatAndTag))) return;

            bool rejected = Apply(infiniteStatAndTag, source, 1, null, out _);
            Expect("缺少 Required Tag 被拒绝", !rejected, rejected, false);
            Expect("查询失败无 Active", target.ActiveEffects.Count == 0,
                target.ActiveEffects.Count, 0);

            bool requiredAdded = target.MutableTags.UpdateTagCount(GameplayTags.Tag_Test_GE_Required, 1);
            Expect("添加 Required Tag", requiredAdded, requiredAdded, true);
            bool success = Apply(infiniteStatAndTag, source, 1, null, out GameEffectRuntime runtime);
            Expect("满足 TagQuery 后应用", success, success, true);
            ExpectValue("持续 Armor 聚合", GameplayAttributes.Attribute_Armor, 40f);
            Expect("Granted Tag 存在",
                target.Tags.HasTagExact(GameplayTags.Tag_Test_GE_Granted),
                target.Tags.HasTagExact(GameplayTags.Tag_Test_GE_Granted), true);

            bool removed = target.TryRemoveEffect(runtime);
            Expect("移除 Tag GE", removed, removed, true);
            ExpectValue("移除后 Armor 恢复", GameplayAttributes.Attribute_Armor, 10f);
            Expect("Granted Tag 清除",
                !target.Tags.HasTagExact(GameplayTags.Tag_Test_GE_Granted),
                target.Tags.HasTagExact(GameplayTags.Tag_Test_GE_Granted), false);
            Expect("Required Tag 保留",
                target.Tags.HasTagExact(GameplayTags.Tag_Test_GE_Required),
                target.Tags.HasTagExact(GameplayTags.Tag_Test_GE_Required), true);
        }

        #endregion
        #region 叠层场景

        // 汇总按 Source、按 Target、Duration 策略、Period 策略与逐层到期场景。
        private void RunStackingScenarios()
        {
            RunAggregateBySourceScenario();
            RunAggregateByTargetScenario();
            RunPeriodPreserveScenario();
            RunPeriodResetScenario();
        }

        // 同 Source 合并并刷新 Duration，不同 Source 创建独立 Runtime。
        private void RunAggregateBySourceScenario()
        {
            if (!PrepareScenario("AggregateBySource") ||
                !RequireEffect(stackBySourceRefresh, nameof(stackBySourceRefresh))) return;

            bool first = Apply(stackBySourceRefresh, source, 1, null, out GameEffectRuntime runtimeA);
            Expect("BySource 首次应用", first, first, true);
            ExpectValue("BySource 首层 Armor", GameplayAttributes.Attribute_Armor, 15f);
            target.GameEffectCtrl.Tick(4f);
            ExpectFloat("BySource Tick 后 Duration", runtimeA.RemainingDuration, 6f);

            bool second = Apply(stackBySourceRefresh, source, 1, null, out GameEffectRuntime sameRuntime);
            Expect("BySource 同来源应用", second, second, true);
            Expect("BySource 同来源复用 Runtime", ReferenceEquals(runtimeA, sameRuntime),
                ReferenceEquals(runtimeA, sameRuntime), true);
            Expect("BySource 层数为 2", runtimeA.StackCount == 2, runtimeA.StackCount, 2);
            ExpectFloat("BySource 刷新 Duration", runtimeA.RemainingDuration, 10f);

            bool otherSource = Apply(stackBySourceRefresh, sourceB, 1, null, out GameEffectRuntime runtimeB);
            Expect("BySource 不同来源应用", otherSource, otherSource, true);
            Expect("BySource 不同来源新建 Runtime", !ReferenceEquals(runtimeA, runtimeB),
                ReferenceEquals(runtimeA, runtimeB), false);
            Expect("BySource Active 为 2", target.ActiveEffects.Count == 2,
                target.ActiveEffects.Count, 2);
            ExpectValue("BySource 两个 Runtime Armor", GameplayAttributes.Attribute_Armor, 20f);

            Apply(stackBySourceRefresh, source, 1, null, out _);
            float beforeDeny = runtimeA.RemainingDuration;
            bool overflow = Apply(stackBySourceRefresh, source, 1, null, out _);
            Expect("BySource 达上限拒绝", !overflow, overflow, false);
            Expect("BySource 拒绝后层数不变", runtimeA.StackCount == 3, runtimeA.StackCount, 3);
            ExpectFloat("BySource 拒绝后 Duration 不变", runtimeA.RemainingDuration, beforeDeny);
        }

        // 不同 Source 合并到 Target Runtime，并验证 ExtendDuration 与逐层到期。
        private void RunAggregateByTargetScenario()
        {
            if (!PrepareScenario("AggregateByTarget") ||
                !RequireEffect(stackByTargetExtendExpiration, nameof(stackByTargetExtendExpiration))) return;

            Apply(stackByTargetExtendExpiration, source, 1, null, out GameEffectRuntime runtime);
            target.GameEffectCtrl.Tick(3f);
            ExpectFloat("ByTarget Tick 后 Duration", runtime.RemainingDuration, 7f);
            bool second = Apply(stackByTargetExtendExpiration, sourceB, 1, null, out GameEffectRuntime merged);
            Expect("ByTarget 不同来源应用", second, second, true);
            Expect("ByTarget 复用 Runtime", ReferenceEquals(runtime, merged),
                ReferenceEquals(runtime, merged), true);
            Expect("ByTarget Source 更新", ReferenceEquals(runtime.Source, sourceB),
                ReferenceEquals(runtime.Source, sourceB), true);
            Expect("ByTarget 层数为 2", runtime.StackCount == 2, runtime.StackCount, 2);
            ExpectFloat("ByTarget Extend Duration", runtime.RemainingDuration, 17f);

            Apply(stackByTargetExtendExpiration, source, 1, null, out _);
            Expect("ByTarget 层数为 3", runtime.StackCount == 3, runtime.StackCount, 3);
            ExpectFloat("ByTarget 第三层 Extend", runtime.RemainingDuration, 27f);
            bool overflow = Apply(stackByTargetExtendExpiration, sourceB, 1, null, out _);
            Expect("ByTarget 达上限拒绝", !overflow, overflow, false);
            ExpectFloat("ByTarget 拒绝不改 Duration", runtime.RemainingDuration, 27f);

            target.GameEffectCtrl.Tick(27f);
            Expect("ByTarget 首次到期减为 2 层", runtime.StackCount == 2, runtime.StackCount, 2);
            ExpectFloat("ByTarget 首次到期刷新 Duration", runtime.RemainingDuration, 10f);
            target.GameEffectCtrl.Tick(10f);
            Expect("ByTarget 第二次到期减为 1 层", runtime.StackCount == 1, runtime.StackCount, 1);
            target.GameEffectCtrl.Tick(10f);
            Expect("ByTarget 最后一层移除", target.ActiveEffects.Count == 0,
                target.ActiveEffects.Count, 0);
            ExpectValue("ByTarget 最终 Armor 恢复", GameplayAttributes.Attribute_Armor, 10f);
        }

        // NeverRefresh 与 NeverReset 在重复应用和允许溢出时都保留既有计时。
        private void RunPeriodPreserveScenario()
        {
            if (!PrepareScenario("Period Preserve") ||
                !RequireEffect(stackPeriodPreserveTiming, nameof(stackPeriodPreserveTiming))) return;

            Apply(stackPeriodPreserveTiming, source, 1, null, out GameEffectRuntime runtime);
            target.GameEffectCtrl.Tick(0.4f);
            ExpectFloat("Preserve 初始 Period 进度", runtime.RemainingPeriod, 0.6f);
            ExpectFloat("Preserve 初始 Duration 进度", runtime.RemainingDuration, 9.6f);
            Apply(stackPeriodPreserveTiming, sourceB, 1, null, out _);
            ExpectFloat("Preserve 重应用保留 Period", runtime.RemainingPeriod, 0.6f);
            ExpectFloat("Preserve 重应用保留 Duration", runtime.RemainingDuration, 9.6f);
            Apply(stackPeriodPreserveTiming, source, 1, null, out _);
            bool overflow = Apply(stackPeriodPreserveTiming, sourceB, 1, null, out _);
            Expect("Preserve 允许溢出应用", overflow, overflow, true);
            Expect("Preserve 溢出保持最大层数", runtime.StackCount == 3, runtime.StackCount, 3);
            ExpectFloat("Preserve 溢出保留 Period", runtime.RemainingPeriod, 0.6f);
            target.GameEffectCtrl.Tick(0.6f);
            ExpectValue("Preserve 到点只结算一次", GameplayAttributes.Attribute_Armor, 15f);
        }

        // ResetOnSuccessfulApplication 只重置 Period，不修改 NeverRefresh 的 Duration 进度。
        private void RunPeriodResetScenario()
        {
            if (!PrepareScenario("Period Reset") ||
                !RequireEffect(stackPeriodResetTiming, nameof(stackPeriodResetTiming))) return;

            Apply(stackPeriodResetTiming, source, 1, null, out GameEffectRuntime runtime);
            target.GameEffectCtrl.Tick(0.4f);
            ExpectFloat("Reset 初始 Period 进度", runtime.RemainingPeriod, 0.6f);
            ExpectFloat("Reset 初始 Duration 进度", runtime.RemainingDuration, 9.6f);
            Apply(stackPeriodResetTiming, sourceB, 1, null, out _);
            ExpectFloat("Reset 重应用重置 Period", runtime.RemainingPeriod, 1f);
            ExpectFloat("Reset 重应用保留 Duration", runtime.RemainingDuration, 9.6f);
            target.GameEffectCtrl.Tick(0.6f);
            ExpectValue("Reset 0.6s 尚未结算", GameplayAttributes.Attribute_Armor, 10f);
            target.GameEffectCtrl.Tick(0.4f);
            ExpectValue("Reset 完整周期结算一次", GameplayAttributes.Attribute_Armor, 15f);
        }

        #endregion
        #region 失败原子性场景

        // 覆盖缺少动态输入、TagQuery、AttributeSet 和溢出拒绝四类公开失败入口。
        private void RunFailureAtomicityScenarios()
        {
            if (PrepareScenario("失败：SetByCaller") &&
                RequireEffect(instantSetByCaller, nameof(instantSetByCaller)))
            {
                bool success = Apply(instantSetByCaller, source, 1, null, out _);
                Expect("失败后 Health 不变", !success, success, false);
                ExpectValue("SetByCaller 失败原子值", GameplayAttributes.Attribute_Health, 100f);
                Expect("SetByCaller 失败无 Active", target.ActiveEffects.Count == 0,
                    target.ActiveEffects.Count, 0);
            }

            if (PrepareScenario("失败：TagQuery") &&
                RequireEffect(infiniteStatAndTag, nameof(infiniteStatAndTag)))
            {
                bool success = Apply(infiniteStatAndTag, source, 1, null, out _);
                Expect("TagQuery 失败", !success, success, false);
                ExpectValue("TagQuery 失败 Armor 不变", GameplayAttributes.Attribute_Armor, 10f);
                Expect("TagQuery 失败无 GrantedTag",
                    !target.Tags.HasTagExact(GameplayTags.Tag_Test_GE_Granted),
                    target.Tags.HasTagExact(GameplayTags.Tag_Test_GE_Granted), false);
            }

            if (PrepareScenario("失败：目标无 AttributeSet", false) &&
                RequireEffect(infiniteStatAndTag, nameof(infiniteStatAndTag)))
            {
                target.MutableTags.UpdateTagCount(GameplayTags.Tag_Test_GE_Required, 1);
                bool success = Apply(infiniteStatAndTag, source, 1, null, out _);
                Expect("缺少 AttributeSet 提交失败", !success, success, false);
                Expect("缺少 AttributeSet 无 Active", target.ActiveEffects.Count == 0,
                    target.ActiveEffects.Count, 0);
                Expect("缺少 AttributeSet 无 GrantedTag",
                    !target.Tags.HasTagExact(GameplayTags.Tag_Test_GE_Granted),
                    target.Tags.HasTagExact(GameplayTags.Tag_Test_GE_Granted), false);
            }

            if (PrepareScenario("失败：叠层溢出") &&
                RequireEffect(stackBySourceRefresh, nameof(stackBySourceRefresh)))
            {
                Apply(stackBySourceRefresh, source, 1, null, out GameEffectRuntime runtime);
                Apply(stackBySourceRefresh, source, 1, null, out _);
                Apply(stackBySourceRefresh, source, 1, null, out _);
                target.GameEffectCtrl.Tick(2f);
                float durationBefore = runtime.RemainingDuration;
                int activeBefore = target.ActiveEffects.Count;
                bool success = Apply(stackBySourceRefresh, source, 1, null, out _);
                Expect("溢出拒绝返回 false", !success, success, false);
                Expect("溢出拒绝层数不变", runtime.StackCount == 3, runtime.StackCount, 3);
                ExpectFloat("溢出拒绝计时不变", runtime.RemainingDuration, durationBefore);
                Expect("溢出拒绝 Active 不变",
                    target.ActiveEffects.Count == activeBefore,
                    target.ActiveEffects.Count, activeBefore);
                ExpectValue("溢出拒绝 Modifier 不变", GameplayAttributes.Attribute_Armor, 15f);
            }
        }

        #endregion

        #region 场景准备与断言

        // 为单个场景创建隔离 ASC，并初始化唯一测试 Set；失败入口可有意跳过 Target Set。
        private bool PrepareScenario(string scenarioName, bool initializeTarget = true)
        {
            if (tagDatabase == null || testAttributeSet == null)
            {
                Expect($"{scenarioName} 固定依赖", false,
                    $"Database={tagDatabase}, Set={testAttributeSet}", "均已配置");
                return false;
            }

            GameplayTagManager.Instance.Initialize(tagDatabase);
            CleanupAscObjects();
            source = CreateAsc("GE Test Source", out sourceObject);
            sourceB = CreateAsc("GE Test Source B", out sourceBObject);
            target = CreateAsc("GE Test Target", out targetObject);
            lastRuntime = null;
            var sets = new[] { testAttributeSet };
            source.Initialize(sets);
            sourceB.Initialize(sets);
            if (initializeTarget) target.Initialize(sets);
            bool ready = source.IsInitialized && sourceB.IsInitialized &&
                         (!initializeTarget || target.IsInitialized);
            Expect($"{scenarioName} 初始化", ready,
                $"A={source.IsInitialized}, B={sourceB.IsInitialized}, " +
                $"Target={(!initializeTarget || target.IsInitialized)}", "全部成功");
            return ready;
        }

        // 创建挂载真实 ASC 组件的临时对象，避免直接构造 MonoBehaviour。
        private static GameplayAbilitySystemComponent CreateAsc(string objectName, out GameObject owner)
        {
            owner = new GameObject(objectName)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            owner.AddComponent<GameplayAbilitySystemTestOwner>();
            return owner.AddComponent<GameplayAbilitySystemComponent>();
        }

        // 销毁当前场景创建的全部临时 ASC 对象，避免手动测试污染场景。
        private void CleanupAscObjects()
        {
            if (sourceObject != null) DestroyImmediate(sourceObject);
            if (sourceBObject != null) DestroyImmediate(sourceBObject);
            if (targetObject != null) DestroyImmediate(targetObject);
            sourceObject = null;
            sourceBObject = null;
            targetObject = null;
            source = null;
            sourceB = null;
            target = null;
        }
        // 序列化测试资产是外部输入；缺失时记录明确失败并跳过依赖场景。
        private bool RequireEffect(GameplayEffectData value, string fieldName)
        {
            bool available = value != null;
            Expect($"测试资产 {fieldName}", available, value?.name ?? "null", "已配置");
            return available;
        }

        // 调用目标 Controller 的唯一应用入口，Target 始终隐式为当前场景 Target。
        private bool Apply(
            GameplayEffectData data,
            GameplayAbilitySystemComponent effectSource,
            int effectLevel,
            IReadOnlyDictionary<GameplayTag, float> values,
            out GameEffectRuntime runtime) =>
            target.TryApplyEffect(data, effectSource, effectLevel, values, out runtime);

        // 套件同步执行期间临时使用测试 Tag Database，并在结束或异常时恢复原单例状态。
        private void ExecuteSuite(string name, Action scenarios)
        {
            GameplayTagDatabase previousDatabase = GameplayTagManager.Instance.Database;
            BeginRun(name);
            try
            {
                scenarios();
            }
            finally
            {
                if (previousDatabase != null) GameplayTagManager.Instance.Initialize(previousDatabase);
                else GameplayTagManager.Instance.Reset();
                CompleteRun(name);
                CleanupAscObjects();
            }
        }
        // 开始一组独立统计，单项按钮与完整套件共享相同断言实现。
        private void BeginRun(string name)
        {
            passedAssertions = 0;
            failedAssertions = 0;
            Debug.Log($"[GETest][Begin] {name}");
        }

        // 输出本组最终汇总；失败数非零时使用 Error 级别方便 Console 筛选。
        private void CompleteRun(string name)
        {
            string message = $"[GETest][Summary] {name}: PASS={passedAssertions}, FAIL={failedAssertions}";
            if (failedAssertions == 0) Debug.Log(message);
            else Debug.LogError(message);
        }

        // 记录通用布尔断言，actual/expected 保留业务上下文而非只打印 true/false。
        private void Expect(string label, bool condition, object actual, object expected)
        {
            if (condition)
            {
                passedAssertions++;
                Debug.Log($"[GETest][PASS] {label} | Actual={actual} | Expected={expected}");
                return;
            }

            failedAssertions++;
            Debug.LogError($"[GETest][FAIL] {label} | Actual={actual} | Expected={expected}");
        }

        // 使用统一容差比较 GE 计时与 Attribute 浮点结果。
        private void ExpectFloat(string label, float actual, float expected) =>
            Expect(label, Mathf.Approximately(actual, expected), actual, expected);

        // 通过公开 CurrentValue 查询验证业务最终值，不读取框架内部 BaseValue。
        private void ExpectValue(string label, GameplayAttribute attribute, float expected)
        {
            bool found = target.Attributes.TryGetCurrentValue(attribute, out float actual);
            Expect(label, found && Mathf.Approximately(actual, expected),
                found ? actual : "Attribute missing", expected);
        }

        #endregion

        #region 通用辅助

        // 通用测试按钮统一检查运行对象与配置，避免空引用掩盖业务结果。
        private bool EnsureInitialized()
        {
            if (source != null && target != null && effect != null) return true;
            Debug.LogError("[GETest] 请先指定 GameplayEffectData 并执行初始化。");
            return false;
        }

        // 将 Inspector List 转成 Runtime 字典；重复 Key 使用最后一项，和常规配置覆盖一致。
        private Dictionary<GameplayTag, float> BuildSetByCaller()
        {
            var values = new Dictionary<GameplayTag, float>();
            for (int i = 0; i < setByCaller.Count; i++)
                values[setByCaller[i].Key] = setByCaller[i].Value;
            return values;
        }

        // 输出 Target 的公开 CurrentValue，测试代码不读取内部 BaseValue。
        private void LogTargetAttributes()
        {
            IReadOnlyList<GameplayAttributeDefinition> definitions = target.Attributes.Attributes;
            
            for (int i = 0; i < definitions.Count; i++)
                Debug.Log($"[GETest][Attribute] {definitions[i].Attribute} = {definitions[i].CurrentValue}");
        }

        #endregion
    }
}
#endif
