#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.GameplayEffect;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>通过 Odin Inspector 手动验证 GA 授予、激活、执行和显式生命周期。</summary>
    public sealed class GameplayAbilityOdinTester : MonoBehaviour
    {
        #region 嵌套类型

        /// <summary>保存 Inspector 可编辑的单项 SetByCaller 输入。</summary>
        [Serializable]
        public struct SetByCallerEntry
        {
            [SerializeField] private GameplayTag key;
            [SerializeField] private float value;

            /// <summary>获取动态数值使用的 Tag Key。</summary>
            public GameplayTag Key => key;
            /// <summary>获取对应动态数值。</summary>
            public float Value => value;
        }

        /// <summary>提供不增加业务逻辑的最小测试 ASC。</summary>
        private sealed class TestAbilitySystemComponent : AbilitySystemComponentBase
        {
        }

        #endregion

        #region 测试配置

        [Title("Gameplay Ability")]
        [SerializeField, AssetsOnly, Required] private GameplayAbilityData ability;
        [SerializeField, Min(1)] private int level = 1;
        [SerializeField] private List<SetByCallerEntry> setByCaller = new();

        [Title("Runtime Dependencies")]
        [SerializeField, AssetsOnly] private GameplayTagDatabase tagDatabase;
        [SerializeField, AssetsOnly] private List<GameplayAttributeSet> sourceSets = new();
        [SerializeField, AssetsOnly] private List<GameplayAttributeSet> targetSets = new();

        #endregion

        #region 运行状态

        private TestAbilitySystemComponent source;
        private TestAbilitySystemComponent targetA;
        private TestAbilitySystemComponent targetB;
        private GameplayAbilityHandle handle;
        private GameplayAbilityRuntime runtime;
        private IReadOnlyList<GameEffectRuntime> lastActiveEffects = Array.Empty<GameEffectRuntime>();
        private int passed;
        private int failed;

        #endregion

        #region Odin 公开操作

        /// <summary>重建隔离的 Source 与两个 Target ASC，并导入配置的 AttributeSet。</summary>
        [Button("初始化 GA 测试")]
        public void InitializeTest()
        {
            if (tagDatabase != null) GameplayTagManager.Instance.Initialize(tagDatabase);
            source = new TestAbilitySystemComponent();
            targetA = new TestAbilitySystemComponent();
            targetB = new TestAbilitySystemComponent();
            bool sourceReady = source.Attributes.TryInitialize(sourceSets, out string sourceError);
            bool targetAReady = targetA.Attributes.TryInitialize(targetSets, out string targetAError);
            bool targetBReady = targetB.Attributes.TryInitialize(targetSets, out string targetBError);
            handle = GameplayAbilityHandle.Invalid;
            runtime = null;
            lastActiveEffects = Array.Empty<GameEffectRuntime>();
            Debug.Log(
                $"[GATest][Initialize] Source={sourceReady} ({sourceError}), " +
                $"TargetA={targetAReady} ({targetAError}), TargetB={targetBReady} ({targetBError})");
        }

        /// <summary>向 Source 授予当前 Ability，并保存返回 Handle。</summary>
        [Button("授予 Ability")]
        public void GiveAbility()
        {
            if (!EnsureInitialized()) return;
            handle = source.Abilities.GiveAbility(ability, level);
            Debug.Log($"[GATest][Give] Handle={handle.Id}, Level={level}");
        }

        /// <summary>使用 Inspector 中的 SetByCaller 数据激活已授予 Ability。</summary>
        [Button("激活 Ability")]
        public void ActivateAbility()
        {
            if (!EnsureHandle()) return;
            bool success = source.Abilities.TryActivate(handle, BuildSetByCaller(), out runtime);
            Debug.Log(
                $"[GATest][Activate] Success={success}, ActivationId={runtime?.ActivationId ?? 0}, " +
                $"Active={source.Abilities.ActiveRuntimes.Count}");
        }

        /// <summary>执行 SelfEffects，不向 TargetEffects 提供目标。</summary>
        [Button("执行 Self Effects")]
        public void ExecuteSelfEffects()
        {
            if (!EnsureRuntime()) return;
            bool success = source.Abilities.TryExecuteEffects(
                runtime,
                Array.Empty<AbilitySystemComponentBase>(),
                out lastActiveEffects);
            LogExecution("Self", success);
        }

        /// <summary>向单个外部 Target 执行 TargetEffects。</summary>
        [Button("执行单目标 Effects")]
        public void ExecuteSingleTargetEffects()
        {
            if (!EnsureRuntime()) return;
            bool success = source.Abilities.TryExecuteEffects(
                runtime,
                new AbilitySystemComponentBase[] { targetA },
                out lastActiveEffects);
            LogExecution("SingleTarget", success);
        }

        /// <summary>向两个外部 Target 执行同一轮 TargetEffects，模拟范围 Targeting 结果。</summary>
        [Button("执行多目标 Effects")]
        public void ExecuteMultipleTargetEffects()
        {
            if (!EnsureRuntime()) return;
            bool success = source.Abilities.TryExecuteEffects(
                runtime,
                new AbilitySystemComponentBase[] { targetA, targetB },
                out lastActiveEffects);
            LogExecution("MultipleTargets", success);
        }

        /// <summary>正常结束当前 Runtime，不移除此前返回的 GE Runtime。</summary>
        [Button("End Ability")]
        public void EndAbility()
        {
            if (!EnsureRuntime()) return;
            bool success = source.Abilities.TryEnd(runtime);
            Debug.Log($"[GATest][End] Success={success}, State={runtime.State}");
        }

        /// <summary>取消当前 Runtime，不移除此前返回的 GE Runtime。</summary>
        [Button("Cancel Ability")]
        public void CancelAbility()
        {
            if (!EnsureRuntime()) return;
            bool success = source.Abilities.TryCancel(runtime);
            Debug.Log($"[GATest][Cancel] Success={success}, State={runtime.State}");
        }

        /// <summary>由外部显式移除上次执行返回的全部 Active GE。</summary>
        [Button("外部移除返回的 Active GE")]
        public void RemoveReturnedEffects()
        {
            if (!EnsureInitialized()) return;
            int removed = 0;
            for (int i = 0; i < lastActiveEffects.Count; i++)
            {
                GameEffectRuntime effectRuntime = lastActiveEffects[i];
                if (effectRuntime != null && effectRuntime.Target.GameEffectCtrl.TryRemove(effectRuntime))
                    removed++;
            }

            Debug.Log($"[GATest][RemoveEffects] Removed={removed}/{lastActiveEffects.Count}");
            lastActiveEffects = Array.Empty<GameEffectRuntime>();
        }

        /// <summary>执行不依赖具体 GE 数值的 Spec、快照、单次执行和生命周期验收。</summary>
        [Button("执行基础 GA 生命周期测试", ButtonSizes.Large)]
        public void RunBasicLifecycleTest()
        {
            passed = 0;
            failed = 0;
            InitializeTest();
            if (!EnsureInitialized()) return;

            GameplayAbilityHandle testHandle = source.Abilities.GiveAbility(ability, 2);
            Expect("Give 返回有效 Handle", testHandle.IsValid);
            Expect("可查询 Spec", source.Abilities.TryGetAbilitySpec(testHandle, out GameplayAbilitySpec spec));
            Expect("Spec 初始等级", spec != null && spec.Level == 2);
            Expect("修改 Spec 等级", source.Abilities.TrySetAbilityLevel(testHandle, 3));
            bool activated = source.Abilities.TryActivate(
                testHandle,
                BuildSetByCaller(),
                out GameplayAbilityRuntime firstRuntime);
            Expect("Ability 激活", activated);
            if (activated)
            {
                Expect("Runtime 复制等级", firstRuntime.Level == 3);
                source.Abilities.TrySetAbilityLevel(testHandle, 4);
                Expect("Spec 改级不影响 Runtime", firstRuntime.Level == 3);
                Expect("Active Runtime 拒绝移除 Spec", !source.Abilities.TryRemoveAbility(testHandle));

                bool executed = source.Abilities.TryExecuteEffects(
                    firstRuntime,
                    new AbilitySystemComponentBase[] { targetA },
                    out IReadOnlyList<GameEffectRuntime> effects);
                Expect("首次执行完成", executed);
                Expect("Runtime 标记已执行", firstRuntime.HasExecuted);
                Expect("同一 Runtime 拒绝重复执行",
                    !source.Abilities.TryExecuteEffects(
                        firstRuntime,
                        Array.Empty<AbilitySystemComponentBase>(),
                        out _));
                Expect("End 成功", source.Abilities.TryEnd(firstRuntime));
                Expect("End 不自动移除返回 GE", AreEffectsStillActive(effects));
                Expect("重复 End 被拒绝", !source.Abilities.TryEnd(firstRuntime));
                Expect("结束后可移除 Spec", source.Abilities.TryRemoveAbility(testHandle));
            }

            Debug.Log(
                $"[GATest][Summary] PASS={passed}, FAIL={failed}");
        }

        #endregion

        #region 内部辅助

        // 测试按钮共用初始化检查，避免空配置掩盖 GA 行为。
        private bool EnsureInitialized()
        {
            if (source != null && targetA != null && targetB != null && ability != null) return true;
            Debug.LogError("[GATest] 请配置 GameplayAbilityData 并先初始化测试。");
            return false;
        }

        // Handle 操作前确认已经成功授予。
        private bool EnsureHandle()
        {
            if (!EnsureInitialized()) return false;
            if (handle.IsValid) return true;
            Debug.LogError("[GATest] 请先授予 Ability。");
            return false;
        }

        // 执行与结束操作前确认当前 Runtime 已创建。
        private bool EnsureRuntime()
        {
            if (!EnsureInitialized()) return false;
            if (runtime != null) return true;
            Debug.LogError("[GATest] 请先激活 Ability。");
            return false;
        }

        // 把 Inspector List 转为运行时只读输入；重复 Key 使用最后一项。
        private Dictionary<GameplayTag, float> BuildSetByCaller()
        {
            var values = new Dictionary<GameplayTag, float>();
            for (int i = 0; i < setByCaller.Count; i++)
                values[setByCaller[i].Key] = setByCaller[i].Value;
            return values;
        }

        // 输出执行状态和交还外部管理的 Active GE 数量。
        private void LogExecution(string scenario, bool success) =>
            Debug.Log(
                $"[GATest][Execute:{scenario}] Success={success}, " +
                $"Executed={runtime.HasExecuted}, ActiveEffects={lastActiveEffects.Count}");

        // 验证 End 后 GA 没有替外部移除已应用的 Active GE。
        private static bool AreEffectsStillActive(IReadOnlyList<GameEffectRuntime> effects)
        {
            for (int i = 0; i < effects.Count; i++)
                if (effects[i] == null || !effects[i].IsActive)
                    return false;
            return true;
        }

        // 记录基础布尔断言，并使用统一前缀方便 Console 过滤。
        private void Expect(string label, bool condition)
        {
            if (condition)
            {
                passed++;
                Debug.Log($"[GATest][PASS] {label}");
                return;
            }

            failed++;
            Debug.LogError($"[GATest][FAIL] {label}");
        }

        #endregion
    }
}
#endif
