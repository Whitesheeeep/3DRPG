using System.Collections.Generic;
using UnityEngine;
using WS_Modules.GAS.Generated;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>为需要基础伤害结算的 Ability 提供可选等级倍率与对应的 SetByCaller 写入。</summary>
    public abstract class DamageGameplayAbilityData : GameplayAbilityData
    {
        #region 伤害倍率配置

        [SerializeField, Tooltip("根据 Ability Level 求值的伤害倍率；为空时按常量倍率 1 处理。")]
        private GameplayScalableFloat damageMultiplier = new(1f);

        /// <summary>获取可选的等级伤害倍率配置；为空时运行时使用倍率 1。</summary>
        public GameplayScalableFloat DamageMultiplier => damageMultiplier;

        #endregion

        #region 伤害倍率求值与 SetByCaller

        /// <summary>使用 Ability Level 求出本次激活的最终伤害倍率。</summary>
        /// <param name="abilityLevel">本次 Ability 激活等级。</param>
        /// <param name="multiplier">求值成功时返回最终倍率；未配置时返回 1。</param>
        /// <returns>未配置或配置产生有限且不小于零的结果时返回 true。</returns>
        public bool TryEvaluateDamageMultiplier(int abilityLevel, out float multiplier)
        {
            // 空配置代表作者选择默认倍率，而不是无效配置，兼容旧资产未序列化该字段的状态。
            if (damageMultiplier == null)
            {
                multiplier = 1f;
                return true;
            }

            if (!damageMultiplier.TryEvaluate(abilityLevel, out multiplier) ||
                float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier < 0f)
            {
                multiplier = default;
                return false;
            }

            return true;
        }

        /// <summary>将伤害倍率写入本次结果 GE Spec 使用的具体 SetByCaller Tag。</summary>
        /// <param name="abilityLevel">本次 Ability 激活等级快照。</param>
        /// <param name="setByCallerMagnitudeByTagMap">即将传入 GE Spec 的可变动态数值。</param>
        /// <returns>倍率有效并成功写入时返回 true。</returns>
        protected override bool TryPopulateConfiguredEffectSetByCaller(
            int abilityLevel,
            IDictionary<GameplayTag, float> setByCallerMagnitudeByTagMap)
        {
            if (!TryEvaluateDamageMultiplier(abilityLevel, out float multiplier))
            {
                Debug.LogError(
                    $"[DamageGameplayAbilityData] Ability '{name}' 的伤害倍率无效，" +
                    $"AbilityLevel={abilityLevel}，无法创建结果 GE Spec。",
                    this);
                return false;
            }

            // 作者配置的倍率必须覆盖激活入口同名值，保证同一 Ability 的伤害曲线具有唯一权威来源。
            setByCallerMagnitudeByTagMap[GameplayTags.Tag_Data_Damage_Multiplier] = multiplier;
            return true;
        }

        #endregion
    }
}
