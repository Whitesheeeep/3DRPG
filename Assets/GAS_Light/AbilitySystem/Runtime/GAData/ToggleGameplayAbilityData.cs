using UnityEngine;
using WS_Modules.GAS.GameplayEffect;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>首次激活持有 Source Infinite Effects，再次激活正常关闭的 Toggle Ability。</summary>
    [CreateAssetMenu(fileName = "ToggleGameplayAbility", menuName = "WSFrame/GAS/Gameplay Ability/Toggle")]
    public sealed class ToggleGameplayAbilityData : AsynchronousGameplayAbilityData
    {
        #region 构造

        /// <summary>创建默认使用持续自身效果 Root Task 的 Toggle 配置。</summary>
        public ToggleGameplayAbilityData()
        {
            SetRootTask(new PersistentSelfEffectsGameplayAbilityTaskConfig());
        }

        #endregion

        #region 属性与校验

        /// <summary>已有 Active Runtime 时再次激活会正常结束旧 Runtime。</summary>
        public override GameplayAbilityReactivationPolicy ReactivationPolicy =>
            GameplayAbilityReactivationPolicy.ToggleOff;

        // Toggle 必须能够通过保存的精确句柄完整撤销本次持续效果。
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
    }
}
