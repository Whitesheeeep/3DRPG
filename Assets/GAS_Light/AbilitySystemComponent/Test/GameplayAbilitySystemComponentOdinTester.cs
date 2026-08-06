#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.GameplayAbilitySystem;
using WS_Modules.GAS.Generated;

namespace WS_Modules.GAS.AbilitySystemComponent
{
    /// <summary>
    /// 通过真实 Unity 帧、物理碰撞和 GAS API 验证 ASC 的完整技能运行周期。
    /// </summary>
    public sealed class GameplayAbilitySystemComponentOdinTester : MonoBehaviour
    {
        #region 测试输入

        [Title("ASC 初始化")]
        [SerializeField, AssetsOnly, Required, Tooltip("Source 与 Target 初始化时导入的全部 AttributeSet。")]
        private List<GameplayAttributeSet> attributeSets = new();

        [Title("真实技能 SO")]
        [SerializeField, AssetsOnly, Required]
        private InstantGameplayAbilityData instantAbility;

        [SerializeField, AssetsOnly, Required]
        private AsynchronousGameplayAbilityData asynchronousAbility;

        [SerializeField, AssetsOnly, Required]
        private PassiveGameplayAbilityData passiveAbility;

        [SerializeField, AssetsOnly, Required]
        private SphereProjectileGameplayAbilityData projectileAbility;

        #endregion

        #region 运行状态

        [ShowInInspector, ReadOnly]
        private bool running;

        [ShowInInspector, ReadOnly]
        private int passed;

        [ShowInInspector, ReadOnly]
        private int failed;

        private Coroutine cycle;
        private GameObject sourceObject;
        private GameObject targetObject;
        private GameplayAbilitySystemComponent source;
        private GameplayAbilitySystemComponent target;

        #endregion

        #region Unity 生命周期

        // 作为测试 Owner 在真实 Unity Update 中推进两个 ASC，Coroutine 只负责观察结果。
        private void Update()
        {
            if (!running) return;
            source.Tick(Time.deltaTime);
            target.Tick(Time.deltaTime);
        }

        // 测试组件销毁时停止协程并清理临时 ASC 与投射物。
        private void OnDestroy() => StopAndCleanup();

        #endregion

        #region Odin 操作

        /// <summary>启动一次覆盖 Instant、异步、Passive、Cooldown 和真实投射物命中的 ASC 周期。</summary>
        [Button("执行真实 ASC 技能周期", ButtonSizes.Large)]
        public void RunRealAbilityCycle()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[ASCTest] 请进入 Play Mode 后再执行真实 ASC 技能周期。", this);
                return;
            }

            if (running)
            {
                Debug.LogError("[ASCTest] 当前测试周期仍在运行。", this);
                return;
            }

            if (!ValidateInputs()) return;

            passed = 0;
            failed = 0;
            PrepareWorld();
            running = true;
            cycle = StartCoroutine(RunCycle());
        }

        /// <summary>停止当前测试周期并立即清理临时 Source、Target 和投射物。</summary>
        [Button("停止并清理 ASC 测试")]
        public void StopTest()
        {
            StopAndCleanup();
            Debug.Log("[ASCTest] 已停止并清理当前测试周期。", this);
        }

        #endregion

        #region 测试流程

        // 依次调用真实 GA、GE 与 Attribute API，并跨帧等待 Task、Cooldown 和物理命中。
        private IEnumerator RunCycle()
        {
            Debug.Log("[ASCTest][Begin] 开始真实 ASC 技能周期。", this);
            yield return null;

            Expect("Source ASC 初始化成功", source.IsInitialized);
            Expect("Target ASC 初始化成功", target.IsInitialized);
            ExpectCurrent("Source 初始 Health", source, GameplayAttributes.Attribute_Health, 100f);
            ExpectCurrent("Source 初始 MP", source, GameplayAttributes.Attribute_MP, 50f);
            ExpectCurrent("Source 初始 Armor", source, GameplayAttributes.Attribute_Armor, 10f);
            ExpectCurrent("Target 初始 Health", target, GameplayAttributes.Attribute_Health, 100f);

            GameplayAbilityHandle instantHandle = source.GiveAbility(instantAbility, 1);
            GameplayAbilityHandle asynchronousHandle = source.GiveAbility(asynchronousAbility, 1);
            GameplayAbilityHandle passiveHandle = source.GiveAbility(passiveAbility, 1);
            GameplayAbilityHandle projectileHandle = source.GiveAbility(projectileAbility, 1);

            bool instantActivated = source.TryActivateAbility(
                instantHandle,
                out GameplayAbilityRuntime instantRuntime);
            Expect("Instant 激活成功", instantActivated);
            Expect("Instant 返回时已结束", instantRuntime != null &&
                instantRuntime.State == GameplayAbilityRuntimeState.Ended);
            ExpectCurrent("Instant 扣除 MP", source, GameplayAttributes.Attribute_MP, 40f);
            ExpectCurrent("Instant 修改 Health", source, GameplayAttributes.Attribute_Health, 70f);

            bool asynchronousActivated = source.TryActivateAbility(
                asynchronousHandle,
                out GameplayAbilityRuntime asynchronousRuntime);
            Expect("Async 激活成功", asynchronousActivated);
            Expect("Async 激活后保持 Active", asynchronousRuntime != null &&
                asynchronousRuntime.State == GameplayAbilityRuntimeState.Active);

            yield return null;
            Expect("Async 至少跨一帧保持 Active",
                asynchronousRuntime.State == GameplayAbilityRuntimeState.Active);

            float asynchronousDeadline = Time.realtimeSinceStartup + 2f;
            while (asynchronousRuntime.State == GameplayAbilityRuntimeState.Active &&
                   Time.realtimeSinceStartup < asynchronousDeadline)
                yield return null;

            Expect("Async 由真实 Tick 自动结束",
                asynchronousRuntime.State == GameplayAbilityRuntimeState.Ended);

            bool passiveActivated = source.TryActivateAbility(
                passiveHandle,
                out GameplayAbilityRuntime passiveRuntime);
            Expect("Passive 激活成功", passiveActivated);
            Expect("Passive Runtime 保持 Active", passiveRuntime != null &&
                passiveRuntime.State == GameplayAbilityRuntimeState.Active);
            ExpectCurrent("Passive 扣除 MP", source, GameplayAttributes.Attribute_MP, 30f);
            ExpectCurrent("Passive Infinite GE 修改 Armor",
                source,
                GameplayAttributes.Attribute_Armor,
                20f);

            bool passiveEnded = source.TryEndAbility(passiveRuntime);
            Expect("Passive 正常 End", passiveEnded);
            ExpectCurrent("Passive End 后 Armor 恢复",
                source,
                GameplayAttributes.Attribute_Armor,
                10f);
            Expect("Passive End 后 Cooldown 仍 Active",
                source.HasActiveEffect(passiveAbility.CooldownEffect));

            bool blockedByCooldown = source.TryActivateAbility(passiveHandle, out _);
            Expect("Cooldown 期间拒绝再次激活 Passive", !blockedByCooldown);

            float cooldownDeadline = Time.realtimeSinceStartup + 6f;
            while (source.HasActiveEffect(passiveAbility.CooldownEffect) &&
                   Time.realtimeSinceStartup < cooldownDeadline)
                yield return null;

            Expect("Passive Cooldown 由真实 GE Tick 到期",
                !source.HasActiveEffect(passiveAbility.CooldownEffect));

            bool passiveReactivated = source.TryActivateAbility(
                passiveHandle,
                out GameplayAbilityRuntime secondPassiveRuntime);
            Expect("Cooldown 到期后 Passive 可再次激活", passiveReactivated);
            if (passiveReactivated)
                source.TryEndAbility(secondPassiveRuntime);
            ExpectCurrent("第二次 Passive 扣除 MP",
                source,
                GameplayAttributes.Attribute_MP,
                20f);

            bool projectileActivated = source.TryActivateAbility(
                projectileHandle,
                out GameplayAbilityRuntime projectileRuntime);
            Expect("Projectile 激活成功", projectileActivated);
            Expect("Projectile 创建后 GA Runtime 已结束", projectileRuntime != null &&
                projectileRuntime.State == GameplayAbilityRuntimeState.Ended);
            ExpectCurrent("Projectile Cost 扣除 MP",
                source,
                GameplayAttributes.Attribute_MP,
                10f);

            float projectileDeadline = Time.realtimeSinceStartup + 3f;
            while (ReadCurrent(target, GameplayAttributes.Attribute_Health) > 70f &&
                   Time.realtimeSinceStartup < projectileDeadline)
                yield return null;

            ExpectCurrent("Projectile 真实 Trigger 命中 Target",
                target,
                GameplayAttributes.Attribute_Health,
                70f);
            yield return null;
            Expect("命中后投射物已销毁",
                GameObject.Find("GA Sphere Projectile (Test)") == null);

            Debug.Log(
                $"[ASCTest][Summary] PASS={passed}, FAIL={failed}",
                this);
            cycle = null;
            StopAndCleanup();
        }

        #endregion

        #region 场景准备与清理

        // 检查 Inspector 测试夹具，避免缺失 SO 时输出误导性的运行时失败。
        private bool ValidateInputs()
        {
            bool valid = attributeSets != null && attributeSets.Count > 0 &&
                         instantAbility != null &&
                         asynchronousAbility != null &&
                         passiveAbility != null &&
                         projectileAbility != null;
            if (valid)
            {
                for (int i = 0; i < attributeSets.Count; i++)
                    valid &= attributeSets[i] != null;
            }

            if (!valid)
                Debug.LogError("[ASCTest] 请先配置完整 AttributeSet 与四个真实 GA SO。", this);
            return valid;
        }

        // 创建真实 Source、Target、Collider 和 Mono ASC，Target 固定在 Source 前方一米。
        private void PrepareWorld()
        {
            StopAndCleanup();

            sourceObject = new GameObject("ASC Real Test Source");
            sourceObject.transform.SetPositionAndRotation(transform.position, transform.rotation);
            source = sourceObject.AddComponent<GameplayAbilitySystemComponent>();
            source.Initialize(attributeSets);

            targetObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            targetObject.name = "ASC Real Test Target";
            targetObject.transform.position = transform.position + transform.forward;
            targetObject.transform.rotation = transform.rotation;
            target = targetObject.AddComponent<GameplayAbilitySystemComponent>();
            target.Initialize(attributeSets);

            Physics.SyncTransforms();
        }

        // 停止协程、清理运行状态并销毁所有测试临时对象。
        private void StopAndCleanup()
        {
            running = false;
            if (cycle != null)
            {
                StopCoroutine(cycle);
                cycle = null;
            }

            if (source != null) source.Clear();
            if (target != null) target.Clear();

            GameObject projectile = GameObject.Find("GA Sphere Projectile (Test)");
            if (projectile != null) Destroy(projectile);
            if (sourceObject != null) Destroy(sourceObject);
            if (targetObject != null) Destroy(targetObject);

            source = null;
            target = null;
            sourceObject = null;
            targetObject = null;
        }

        #endregion

        #region 断言辅助

        // 统一记录布尔断言，使完整周期日志可以按 PASS/FAIL 过滤。
        private void Expect(string label, bool condition)
        {
            if (condition)
            {
                passed++;
                Debug.Log($"[ASCTest][PASS] {label}", this);
                return;
            }

            failed++;
            Debug.LogError($"[ASCTest][FAIL] {label}", this);
        }

        // 读取 CurrentValue 并按 Unity 浮点容差验证预期值。
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

        // 读取 Attribute CurrentValue；测试夹具缺失时返回 NaN 以终止错误等待条件。
        private static float ReadCurrent(
            GameplayAbilitySystemComponent asc,
            GameplayAttribute attribute) =>
            asc.TryGetCurrentValue(attribute, out float value) ? value : float.NaN;

        #endregion
    }
}
#endif
