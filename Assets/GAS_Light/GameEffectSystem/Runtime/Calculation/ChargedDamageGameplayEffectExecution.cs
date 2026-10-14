using System.Collections.Generic;
using WS_Modules.GAS.Generated;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayEffect
{
    /// <summary>在基础伤害倍率上叠加必需的蓄力倍率 SetByCaller。</summary>
    [System.Serializable]
    public sealed class ChargedDamageGameplayEffectExecution : BasicDamageGameplayEffectExecution
    {
        #region 倍率读取

        /// <summary>组合 GA 基础倍率与本次释放封存的蓄力倍率。</summary>
        /// <param name="context">本次 GE 应用的实时上下文。</param>
        /// <param name="damageMultiplier">成功时返回两项倍率的乘积。</param>
        /// <returns>两项 SetByCaller 都存在时返回 true。</returns>
        protected override bool TryGetDamageMultiplier(
            GameplayEffectCalculationContext context,
            out float damageMultiplier)
        {
            if (!base.TryGetDamageMultiplier(context, out float skillMultiplier) ||
                !context.TryGetSetByCaller(
                    GameplayTags.Tag_Data_Damage_ChargeMultiplier,
                    out float chargeMultiplier))
            {
                damageMultiplier = default;
                return false;
            }

            // BasicDamage 的统一有限值检查也会拒绝乘积溢出或非有限蓄力输入。
            damageMultiplier = skillMultiplier * chargeMultiplier;
            return true;
        }

        #endregion

        #region 配置契约

        /// <summary>继承基础伤害倍率 Key，并要求蓄力倍率在 Spec 封存前已提供。</summary>
        /// <param name="keys">GE 汇总的必需 SetByCaller Tag 集合。</param>
        protected internal override void CollectRequiredSetByCallerKeys(ISet<GameplayTag> keys)
        {
            base.CollectRequiredSetByCallerKeys(keys);
            keys.Add(GameplayTags.Tag_Data_Damage_ChargeMultiplier);
        }

        #endregion
    }
}
