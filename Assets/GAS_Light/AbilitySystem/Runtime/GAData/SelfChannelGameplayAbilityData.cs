using UnityEngine;
using WS_Modules.GAS.GameplayEffect;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>通过 Root Task 周期向 Source 应用 Instant Effects 的自身引导 Ability。</summary>
    [CreateAssetMenu(fileName = "SelfChannelGameplayAbility", menuName = "WSFrame/GAS/Gameplay Ability/Self Channel")]
    public sealed class SelfChannelGameplayAbilityData : AsynchronousGameplayAbilityData
    {
        #region 构造

        /// <summary>创建默认持续一秒、每秒结算并立即执行首跳的自身引导配置。</summary>
        public SelfChannelGameplayAbilityData()
        {
            SetRootTask(new PeriodicSelfEffectsGameplayAbilityTaskConfig(
                false, 1f, 1f, true));
        }

        #endregion

        #region 属性与校验

        /// <summary>引导期间拒绝同一 Spec 重复激活。</summary>
        public override GameplayAbilityReactivationPolicy ReactivationPolicy =>
            GameplayAbilityReactivationPolicy.RejectWhileActive;

        // 周期结算只接受 Instant GE，避免每跳无边界累积 Active Runtime。
        internal override bool IsRuntimeConfigurationValid
        {
            get
            {
                if (!base.IsRuntimeConfigurationValid ||
                    RootTask is not PeriodicSelfEffectsGameplayAbilityTaskConfig ||
                    Effects == null)
                    return false;

                for (int i = 0; i < Effects.Count; i++)
                    if (Effects[i] == null ||
                        Effects[i].DurationType != E_GameEffectDurationType.Instant)
                        return false;
                return true;
            }
        }

        #endregion
    }
}
