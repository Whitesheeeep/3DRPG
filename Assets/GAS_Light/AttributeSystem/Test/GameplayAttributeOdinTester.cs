#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.Generated;

namespace WS_Modules.GAS.AttributeSystem
{
    /// <summary>
    /// 通过 Odin Inspector 按钮手动验证 Attribute 初始化、Pre/Post、Instant 与 Modifier。
    /// </summary>
    public sealed class GameplayAttributeOdinTester : MonoBehaviour, IModifierSource
    {
        #region 测试输入与状态

        [Title("通用 Attribute 测试")]
        [SerializeField] private List<GameplayAttributeSet> sets = new();
        [SerializeField] private GameplayAttribute attribute = GameplayAttribute.Empty;

        [Title("Modifier 测试输入")]
        [SerializeField] private AttributeModifierType modifierType;
        [SerializeField] private float modifierMagnitude;
        [SerializeField] private int modifierPriority;
        [SerializeField] private int selectedModifierIndex;

        [Title("四属性场景")]
        [SerializeField, Required] private GameplayAttributeTestSet fourAttributeTestSet;

        [Title("运行时状态")]
        [SerializeField, ReadOnly] private GameplayAttributeContainer container = new();
        [NonSerialized, ShowInInspector, ReadOnly]
        private readonly List<AttributeModifier> appliedModifiers = new();

        #endregion

        #region 通用 Attribute 测试

        /// <summary>使用 Inspector 中配置的 Set 初始化运行时 Container。</summary>
        [Button("初始化 Attribute Container")]
        public void InitializeContainer()
        {
            bool success = container.TryInitialize(sets, out string error);
            if (success) appliedModifiers.Clear();
            Debug.Log(success
                ? $"Attribute 初始化成功，数量：{container.Count}"
                : $"Attribute 初始化失败：{error}");
        }

        /// <summary>恢复全部默认结算值；Stat 保留已应用 Modifier，Resource 恢复 CurrentValue。</summary>
        [Button("恢复默认值并保留 Modifier")]
        public void ResetValues()
        {
            container.ResetToDefaultValues();
            Debug.Log($"已恢复默认结算值，Attribute 数量：{container.Count}");
        }

        /// <summary>输出选中 Attribute 的 CurrentValue，并附带框架内部 BaseValue 诊断。</summary>
        [Button("打印 Attribute")]
        public void LogAttribute()
        {
            if (!container.TryGetDefinition(attribute, out GameplayAttributeDefinition definition))
            {
                Debug.LogWarning($"Attribute {attribute.Id} 不存在。");
                return;
            }

            Debug.Log(
                $"Attribute={attribute.Id}, Type={definition.Type}, Default={definition.DefaultValue}, " +
                $"Min={definition.MinValue}, Max={definition.MaxValue}, " +
                $"Base={definition.BaseValue}, Current={definition.CurrentValue}");
        }

        #endregion

        #region 通用 Modifier 测试

        /// <summary>对选中 Attribute 执行一次不绑定 Container Owner 的即时结算。</summary>
        [Button("执行 Instant Modifier")]
        public void ApplyInstantModifier()
        {
            var modifier = new AttributeModifier(
                this,
                attribute,
                modifierType,
                modifierMagnitude,
                modifierPriority);
            container.ApplyInstantModifier(modifier);
            Debug.Log(
                $"Instant 结算完成：Attribute={attribute.Id}, Type={modifierType}, " +
                $"Magnitude={modifierMagnitude}, Current={GetCurrentValueText(attribute)}");
        }

        /// <summary>使用当前 Tester 作为 Source 创建并应用一个运行时 Modifier。</summary>
        [Button("添加 Modifier")]
        public void AddModifier()
        {
            var modifier = new AttributeModifier(
                this,
                attribute,
                modifierType,
                modifierMagnitude,
                modifierPriority);
            bool success = container.TryAddModifier(modifier);
            if (success)
            {
                appliedModifiers.Add(modifier);
                selectedModifierIndex = appliedModifiers.Count - 1;
            }

            Debug.Log(success
                ? $"添加 Modifier 成功：Index={selectedModifierIndex}, Attribute={attribute.Id}, " +
                  $"Type={modifierType}, Magnitude={modifierMagnitude}, Priority={modifierPriority}, " +
                  $"Current={GetCurrentValueText(attribute)}"
                : $"添加 Modifier 失败：Attribute={attribute.Id}, Type={modifierType}, " +
                  $"Magnitude={modifierMagnitude}, Priority={modifierPriority}");
        }

        /// <summary>按对象引用移除选中的运行时 Modifier。</summary>
        [Button("移除选中 Modifier")]
        public void RemoveModifier()
        {
            if (!TryGetSelectedModifier(out AttributeModifier modifier)) return;
            bool success = container.TryRemoveModifier(modifier);
            if (success)
            {
                appliedModifiers.RemoveAt(selectedModifierIndex);
                selectedModifierIndex = Mathf.Clamp(
                    selectedModifierIndex,
                    0,
                    Mathf.Max(0, appliedModifiers.Count - 1));
            }

            Debug.Log(success
                ? $"移除 Modifier 成功：剩余 {appliedModifiers.Count}, " +
                  $"Current={GetCurrentValueText(modifier.Attribute)}"
                : $"移除 Modifier 失败：Index={selectedModifierIndex}");
        }

        /// <summary>按 Source 一次移除当前 Tester 创建的全部 Modifier。</summary>
        [Button("移除当前 Source 的全部 Modifier")]
        public void RemoveSourceModifiers()
        {
            bool success = container.TryRemoveModifiers(this, out int removedCount);
            if (success)
            {
                appliedModifiers.Clear();
                selectedModifierIndex = 0;
            }

            Debug.Log(success
                ? $"按 Source 移除成功：Removed={removedCount}"
                : "按 Source 移除失败：当前 Source 没有已应用 Modifier。");
        }

        /// <summary>验证持续 Modifier 的 Owner 绑定、跨 Container 拒绝与移除后重新绑定。</summary>
        [Button("测试 Modifier Owner 生命周期")]
        public void TestModifierOwnerLifecycle()
        {
            if (!container.TryInitialize(sets, out string firstError) ||
                !TryGetFirstStat(container, out GameplayAttribute target))
            {
                Debug.LogError($"[AttributeTest][Owner] 主 Container 初始化或 Stat 查找失败：{firstError}");
                return;
            }

            var secondContainer = new GameplayAttributeContainer();
            if (!secondContainer.TryInitialize(sets, out string secondError))
            {
                Debug.LogError($"[AttributeTest][Owner] 第二个 Container 初始化失败：{secondError}");
                return;
            }

            var persistent = new AttributeModifier(
                this,
                target,
                AttributeModifierType.Add,
                1f);
            bool firstAdd = container.TryAddModifier(persistent);
            bool duplicateRejected = !container.TryAddModifier(persistent);
            bool crossContainerRejected = !secondContainer.TryAddModifier(persistent);
            bool firstRemove = container.TryRemoveModifier(persistent);
            bool rebound = secondContainer.TryAddModifier(persistent);
            bool secondRemove = secondContainer.TryRemoveModifier(persistent);

            var instant = new AttributeModifier(
                this,
                target,
                AttributeModifierType.Add,
                0f);
            container.ApplyInstantModifier(instant);
            bool instantRemainsUnbound = instant.Owner == null;

            LogBooleanResult("Modifier 首次绑定", firstAdd, true);
            LogBooleanResult("Modifier 重复添加被拒绝", duplicateRejected, true);
            LogBooleanResult("Modifier 跨 Container 添加被拒绝", crossContainerRejected, true);
            LogBooleanResult("Modifier 从第一个 Container 移除", firstRemove, true);
            LogBooleanResult("Modifier 解绑后可重新绑定", rebound, true);
            LogBooleanResult("Modifier 从第二个 Container 移除", secondRemove, true);
            LogBooleanResult("Instant Modifier 不绑定 Owner", instantRemainsUnbound, true);
        }

        #endregion

        #region 四属性场景按钮

        /// <summary>仅导入专用测试 Set，并验证四个 Attribute 的默认 CurrentValue。</summary>
        [Button("初始化四属性测试", ButtonSizes.Large)]
        public void InitializeFourAttributeTest()
        {
            if (!TryInitializeFourAttributeTest()) return;
            AssertCurrentValue("初始化", GameplayAttributes.Attribute_Health, 100f);
            AssertCurrentValue("初始化", GameplayAttributes.Attribute_MaxHealth, 100f);
            AssertCurrentValue("初始化", GameplayAttributes.Attribute_Armor, 10f);
            AssertCurrentValue("初始化", GameplayAttributes.Attribute_MP, 50f);
        }

        /// <summary>重新初始化后验证 Resource Clamp、触发日志以及 Stat Instant 永久结算。</summary>
        [Button("测试 Instant 与 Pre/Post", ButtonSizes.Large)]
        public void TestInstantAndPrePost()
        {
            if (!TryInitializeFourAttributeTest()) return;
            RunInstantScenario();
        }

        /// <summary>重新初始化后验证 Stat 聚合、Resource 拒绝和 MaxHealth 到 Health 的 Post FIFO。</summary>
        [Button("测试 Modifier 与 Post 联动", ButtonSizes.Large)]
        public void TestModifierAndPostLink()
        {
            if (!TryInitializeFourAttributeTest()) return;
            RunModifierScenario();
        }

        /// <summary>在相互独立的初始状态下依次执行 Instant 与 Modifier 四属性场景。</summary>
        [Button("执行完整四属性测试", ButtonSizes.Gigantic)]
        public void RunCompleteFourAttributeTest()
        {
            Debug.Log("[AttributeTest] ===== 开始完整四属性测试 =====");
            bool instantReady = TryInitializeFourAttributeTest();
            if (instantReady) RunInstantScenario();

            bool modifierReady = TryInitializeFourAttributeTest();
            if (modifierReady) RunModifierScenario();

            appliedModifiers.Clear();
            selectedModifierIndex = 0;
            Debug.Log(
                instantReady && modifierReady
                    ? "[AttributeTest] ===== 完整四属性测试结束 ====="
                    : "[AttributeTest] ===== 测试因初始化失败而中止 =====");
        }

        #endregion

        #region 四属性场景实现

        // 用专用 Set 重建 Container，保证每个场景从同一默认状态开始。
        private bool TryInitializeFourAttributeTest()
        {
            if (fourAttributeTestSet == null)
            {
                Debug.LogError("[AttributeTest] 请先指定 GameplayAttributeTestSet 资产。");
                return false;
            }

            bool success = container.TryInitialize(
                new GameplayAttributeSet[] { fourAttributeTestSet },
                out string error);
            appliedModifiers.Clear();
            selectedModifierIndex = 0;
            Debug.Log(success
                ? $"[AttributeTest] 初始化成功：Set={fourAttributeTestSet.name}, Count={container.Count}"
                : $"[AttributeTest] 初始化失败：{error}");
            return success;
        }

        // 只编排真实 Instant API 调用，并在每次提交后读取最终 CurrentValue。
        private void RunInstantScenario()
        {
            ApplyScenarioInstant("Health Add -150", GameplayAttributes.Attribute_Health,
                AttributeModifierType.Add, -150f, 0f);
            ApplyScenarioInstant("Health Override 150", GameplayAttributes.Attribute_Health,
                AttributeModifierType.Override, 150f, 100f);
            ApplyScenarioInstant("MP Add -80", GameplayAttributes.Attribute_MP,
                AttributeModifierType.Add, -80f, 0f);
            ApplyScenarioInstant("Armor Add 5", GameplayAttributes.Attribute_Armor,
                AttributeModifierType.Add, 5f, 15f);
        }

        // 通过真实 Modifier API 验证聚合、FIFO 联动、Resource 拒绝和 Source 批量删除。
        private void RunModifierScenario()
        {
            bool addArmor = TryAddScenarioModifier(
                GameplayAttributes.Attribute_Armor, AttributeModifierType.Add, 10f, 0);
            bool multiplyArmor = TryAddScenarioModifier(
                GameplayAttributes.Attribute_Armor, AttributeModifierType.Multiply, 2f, 0);
            LogBooleanResult("Armor Add/Multiply 添加", addArmor && multiplyArmor, true);
            AssertCurrentValue("Armor 同 Priority: (10 + 10) × 2",
                GameplayAttributes.Attribute_Armor, 40f);

            bool overrideMaxHealth = TryAddScenarioModifier(
                GameplayAttributes.Attribute_MaxHealth, AttributeModifierType.Override, 60f, 0);
            LogBooleanResult("MaxHealth Override 添加", overrideMaxHealth, true);
            AssertCurrentValue("MaxHealth 持续 Override",
                GameplayAttributes.Attribute_MaxHealth, 60f);
            AssertCurrentValue("MaxHealth Post FIFO Clamp Health",
                GameplayAttributes.Attribute_Health, 60f);

            var invalidHealthModifier = new AttributeModifier(
                this,
                GameplayAttributes.Attribute_Health,
                AttributeModifierType.Add,
                10f);
            bool healthModifierAccepted = container.TryAddModifier(invalidHealthModifier);
            if (healthModifierAccepted) appliedModifiers.Add(invalidHealthModifier);
            LogBooleanResult("Resource Health 持续 Modifier 应被拒绝",
                healthModifierAccepted, false);

            bool removed = container.TryRemoveModifiers(this, out int removedCount);
            appliedModifiers.Clear();
            selectedModifierIndex = 0;
            LogBooleanResult("按 Source 删除全部 Modifier", removed, true);
            LogIntegerResult("按 Source 删除数量", removedCount, 3);
            AssertCurrentValue("删除后 Armor 恢复", GameplayAttributes.Attribute_Armor, 10f);
            AssertCurrentValue("删除后 MaxHealth 恢复",
                GameplayAttributes.Attribute_MaxHealth, 100f);
            AssertCurrentValue("删除后 Health 保持已结算值",
                GameplayAttributes.Attribute_Health, 60f);
        }

        // 创建已计算 Modifier 并调用 Instant 入口，不复制 Container 的运算或 Clamp 实现。
        private void ApplyScenarioInstant(
            string step,
            GameplayAttribute target,
            AttributeModifierType type,
            float magnitude,
            float expected)
        {
            Debug.Log(
                $"[AttributeTest][Input] Step={step}, Attribute={target.Id}, " +
                $"Type={type}, Magnitude={magnitude}");
            container.ApplyInstantModifier(
                new AttributeModifier(this, target, type, magnitude));
            AssertCurrentValue(step, target, expected);
        }

        // 添加单个场景 Modifier，并保存返回的对象 Handle 供测试结束清理。
        private bool TryAddScenarioModifier(
            GameplayAttribute target,
            AttributeModifierType type,
            float magnitude,
            int priority)
        {
            var modifier = new AttributeModifier(this, target, type, magnitude, priority);
            bool success = container.TryAddModifier(modifier);
            if (success) appliedModifiers.Add(modifier);
            Debug.Log(
                $"[AttributeTest][Input] AddModifier Attribute={target.Id}, Type={type}, " +
                $"Magnitude={magnitude}, Priority={priority}, Success={success}");
            return success;
        }

        #endregion

        #region 断言与通用辅助

        // 读取真实 Container 结果并输出统一的实际值、预期值和 PASS/FAIL。
        private void AssertCurrentValue(string step, GameplayAttribute target, float expected)
        {
            bool found = container.TryGetCurrentValue(target, out float actual);
            bool passed = found && Mathf.Approximately(actual, expected);
            Debug.Log(
                $"[AttributeTest][{(passed ? "PASS" : "FAIL")}] Step={step}, " +
                $"Attribute={target.Id}, Actual={(found ? actual.ToString() : "Missing")}, " +
                $"Expected={expected}");
        }

        // 输出布尔结果的统一测试日志。
        private static void LogBooleanResult(string step, bool actual, bool expected)
        {
            bool passed = actual == expected;
            Debug.Log(
                $"[AttributeTest][{(passed ? "PASS" : "FAIL")}] Step={step}, " +
                $"Actual={actual}, Expected={expected}");
        }

        // 输出整数结果的统一测试日志。
        private static void LogIntegerResult(string step, int actual, int expected)
        {
            bool passed = actual == expected;
            Debug.Log(
                $"[AttributeTest][{(passed ? "PASS" : "FAIL")}] Step={step}, " +
                $"Actual={actual}, Expected={expected}");
        }

        // 校验 Inspector 索引并取得运行时 Modifier，避免测试按钮传入失效引用。
        private bool TryGetSelectedModifier(out AttributeModifier modifier)
        {
            if (selectedModifierIndex < 0 || selectedModifierIndex >= appliedModifiers.Count)
            {
                modifier = null;
                Debug.LogWarning(
                    $"Modifier Index 无效：{selectedModifierIndex}，Count={appliedModifiers.Count}");
                return false;
            }

            modifier = appliedModifiers[selectedModifierIndex];
            return true;
        }

        // 手动测试选取任意 Stat，避免 Resource 的持续 Modifier 禁止规则干扰 Owner 验证。
        private static bool TryGetFirstStat(
            GameplayAttributeContainer targetContainer,
            out GameplayAttribute target)
        {
            IReadOnlyList<GameplayAttributeDefinition> definitions = targetContainer.Attributes;
            for (int i = 0; i < definitions.Count; i++)
            {
                GameplayAttributeDefinition definition = definitions[i];
                if (definition == null || definition.Type != GameplayAttributeType.Stat) continue;
                target = definition.Attribute;
                return true;
            }

            target = GameplayAttribute.Empty;
            return false;
        }

        // 返回日志使用的 CurrentValue 文本，不向测试脚本复制业务计算。
        private string GetCurrentValueText(GameplayAttribute target) =>
            container.TryGetCurrentValue(target, out float value)
                ? value.ToString()
                : "Missing";

        #endregion
    }
}
#endif
