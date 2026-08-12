using UnityEngine;
using WS_Modules.GAS.GameplayEffect;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>应用自身 Infinite GE 并保持 Active，直到外部结束或取消的 Passive Ability。</summary>
    [CreateAssetMenu(fileName = "PassiveGameplayAbility", menuName = "WSFrame/GAS/Gameplay Ability/Passive")]
    public sealed class PassiveGameplayAbilityData : AsynchronousGameplayAbilityData
    {

        #region 构造
        /// <summary>创建默认使用 Passive 保持 Task 的 Ability 数据。</summary>
        public PassiveGameplayAbilityData()
        {
            SetRootTask(new PersistentSelfEffectsGameplayAbilityTaskConfig());
        }
        #endregion

        #region 运行时校验
        // Passive 使用专用保持 Task，并要求每个 GE 都能由本次激活独立持有。
        internal override bool IsRuntimeConfigurationValid
        {
            get
            {
                if (!base.IsRuntimeConfigurationValid ||
                    RootTask is not PersistentSelfEffectsGameplayAbilityTaskConfig ||
                    Effects == null)
                    return false;

                for (int i = 0; i < Effects.Count; i++)
                {
                    GameplayEffectData effect = Effects[i];
                    if (effect == null ||
                        effect.DurationType != E_GameEffectDurationType.Infinite ||
                        effect.StackingType != E_GameEffectStackingType.None)
                        return false;
                }

                return true;
            }
        }
        #endregion

        #region 激活策略

        /// <summary>Passive 已激活时拒绝重复创建 Runtime。</summary>
        public override GameplayAbilityReactivationPolicy ReactivationPolicy =>
            GameplayAbilityReactivationPolicy.RejectWhileActive;

        #endregion
    }
}
