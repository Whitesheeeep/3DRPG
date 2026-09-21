#if UNITY_EDITOR
using UnityEngine;
using Sirenix.OdinInspector;
using WS_Modules;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.GameplayAbilitySystem;
using WS_Modules.GAS.Generated;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayEffect
{
    /// <summary>通过 Odin Button 手动验证 ScalableFloat、Spec 封存和基础伤害 GE 管线。</summary>
    public sealed class GameplayEffectOdinTester : MonoBehaviour
    {
        #region 测试输入

        [SerializeField, AssetsOnly, Tooltip("用于创建 Spec 的基础伤害 GE；可留空以仅测试 ScalableFloat。")]
        private GameplayEffectData basicDamageEffect;

        [SerializeField, Required, Tooltip("提供 Source AttackPower、暴击属性和 Spec 的 ASC。")]
        private GameplayAbilitySystemComponent source;

        [SerializeField, Required, Tooltip("接收基础伤害的 Target ASC。")]
        private GameplayAbilitySystemComponent target;

        #endregion

        #region Odin 操作

        /// <summary>验证基础值、等级曲线插值和非法等级拒绝规则。</summary>
        [Button("测试 ScalableFloat", ButtonSizes.Medium)]
        public void TestScalableFloat()
        {
            // 曲线值是倍率，BaseValue 负责统一缩放作者配置；这里覆盖无曲线默认分支。
            var scalable = new GameplayScalableFloat(2f);
            if (!scalable.TryEvaluate(1, out float levelOne) ||
                !Mathf.Approximately(levelOne, 2f) ||
                scalable.TryEvaluate(0, out _))
                throw new System.InvalidOperationException("GameplayScalableFloat 基础校验失败。");

            Debug.Log("[GameplayEffectOdinTester] ScalableFloat Level=1 与非法 Level=0 校验通过。", this);
        }

        /// <summary>创建并封存基础伤害 Spec，确认伤害倍率写入 Data.Damage.Multiplier。</summary>
        [Button("测试 BasicDamage Spec", ButtonSizes.Medium)]
        public void TestBasicDamageSpec()
        {
            if (basicDamageEffect == null || source == null || target == null)
            {
                Debug.LogWarning("[GameplayEffectOdinTester] 请先配置 BasicDamage GE、Source 和 Target。", this);
                return;
            }

            if (!source.TryCreateOutgoingEffectSpec(
                    basicDamageEffect,
                    1,
                    null,
                    out GameplayEffectSpec spec))
            {
                Debug.LogError(
                    $"[GameplayEffectOdinTester] BasicDamage Spec 阶段=CreateOutgoingSpec 失败，" +
                    $"GE={basicDamageEffect.name}，Source={source.name}，Level=1。",
                    this);
                return;
            }

            GameplayTag damageMultiplierTag = GameplayTags.Tag_Data_Damage_Multiplier;
            if (!spec.TrySetSetByCaller(damageMultiplierTag, 1f))
            {
                Debug.LogError(
                    $"[GameplayEffectOdinTester] BasicDamage Spec 阶段=SetByCaller 失败，" +
                    $"Key=Data.Damage.Multiplier ({damageMultiplierTag.Id})，" +
                    $"{BuildDamageMultiplierTagDiagnostic(damageMultiplierTag)}，" +
                    $"specSealed={spec.IsSealed}。",
                    this);
                return;
            }

            if (!spec.TrySeal())
            {
                Debug.LogError(
                    $"[GameplayEffectOdinTester] BasicDamage Spec 阶段=Seal 失败，" +
                    $"GE={basicDamageEffect.name}，DurationType={basicDamageEffect.DurationType}，" +
                    $"Period={basicDamageEffect.Period}，Modifiers={basicDamageEffect.Modifiers.Count}，" +
                    $"Executions={basicDamageEffect.Executions.Count}，" +
                    $"{BuildDamageMultiplierTagDiagnostic(damageMultiplierTag)}，" +
                    $"specSealed={spec.IsSealed}。",
                    this);
                return;
            }

            if (!target.TryApplyEffect(spec, out GameplayEffectApplicationResult result))
            {
                Debug.LogError(
                    $"[GameplayEffectOdinTester] BasicDamage Spec 阶段=Apply 失败，" +
                    $"GE={basicDamageEffect.name}，Source={source.name}，Target={target.name}；" +
                    "请检查 Source/Target 所需 Attribute。",
                    this);
                return;
            }

            Debug.Log(
                $"[GameplayEffectOdinTester] BasicDamage Spec 应用成功，" +
                $"ExecutionResults={result.CalculationOutput.ExecutionResults.Count}。",
                this);
        }

        /// <summary>
        /// 构建伤害倍率 Tag 的运行时有效性、数据库和 Bake 状态诊断摘要。
        /// </summary>
        /// <param name="tag">需要诊断的 SetByCaller Tag。</param>
        /// <returns>可直接附加到测试错误日志的状态摘要。</returns>
        private static string BuildDamageMultiplierTagDiagnostic(GameplayTag tag)
        {
            GameplayTagManager tagManager = GameplayTagManager.Instance;
            GameplayTagDatabase database = tagManager.Database;
            bool containsTag = database != null && database.TryGetNode(tag, out _);
            string databaseName = database == null ? "<null>" : database.name;
            string bakeDirty = database == null ? "N/A" : database.BakeDirty.ToString();
            return
                $"tagValid={tagManager.IsValidTag(tag)}，" +
                $"managerInitialized={tagManager.IsInitialized}，" +
                $"database={databaseName}，databaseContainsTag={containsTag}，" +
                $"bakeDirty={bakeDirty}";
        }

        #endregion
    }
}
#endif
