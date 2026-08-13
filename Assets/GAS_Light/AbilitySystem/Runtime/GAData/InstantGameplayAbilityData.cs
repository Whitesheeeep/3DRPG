using System.Collections.Generic;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.GameplayEffect;
using WS_Modules.GAS.GameplayCue;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>在当前调用内逐项应用 Instant GE 的同步 Ability。</summary>
    [CreateAssetMenu(fileName = "InstantGameplayAbility", menuName = "WSFrame/GAS/Gameplay Ability/Instant")]
    public sealed class InstantGameplayAbilityData : SynchronousGameplayAbilityData
    {

        #region 激活策略

        /// <summary>同步结算允许同一 Spec 连续或并行触发。</summary>
        public override GameplayAbilityReactivationPolicy ReactivationPolicy =>
            GameplayAbilityReactivationPolicy.AllowMultiple;

        #endregion

        #region 运行时校验
        // 每个专属 GE 都必须在当前调用内完成，避免同步 Ability 残留异步生命周期。
        internal override bool IsRuntimeConfigurationValid
        {
            get
            {
                if (!base.IsRuntimeConfigurationValid || Effects == null) return false;
                for (int i = 0; i < Effects.Count; i++)
                    if (Effects[i] == null || Effects[i].DurationType != E_GameEffectDurationType.Instant)
                        return false;
                return true;
            }
        }
        #endregion

        #region 同步执行
        // 每个 GE 独立提交；失败项不阻止后续项，也不撤销已成功项。
        // 将全部结果 GE 逐项应用到 Source；单项失败不回滚其他已成功项目。
        protected override void Execute(SynchronousGameplayAbilityRuntime runtime)
        {
            ApplyConfiguredEffects(
                runtime.SourceASC,
                runtime.SourceASC,
                runtime.Level,
                runtime.SetByCaller);
            PublishConfiguredCues(
                GameplayCueEventType.Execute,
                runtime.SourceASC,
                runtime.SourceASC,
                abilityRuntime: runtime);
        }
        #endregion
    }
}
