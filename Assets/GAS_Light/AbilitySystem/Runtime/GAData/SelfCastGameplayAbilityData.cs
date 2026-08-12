using UnityEngine;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>等待配置时长后对 Source 应用统一 Effects 的自身读条 Ability。</summary>
    [CreateAssetMenu(fileName = "SelfCastGameplayAbility", menuName = "WSFrame/GAS/Gameplay Ability/Self Cast")]
    public sealed class SelfCastGameplayAbilityData : AsynchronousGameplayAbilityData
    {
        #region 构造

        /// <summary>创建默认等待一秒后结算自身 Effects 的读条配置。</summary>
        public SelfCastGameplayAbilityData()
        {
            SetRootTask(new SequenceGameplayAbilityTaskConfig(
                new GameplayAbilityTaskConfig[]
                {
                    new WaitDurationGameplayAbilityTaskConfig(1f),
                    new ApplySelfEffectsGameplayAbilityTaskConfig()
                }));
        }

        #endregion

        #region 属性与校验

        /// <summary>读条期间拒绝同一 Spec 重复激活。</summary>
        public override GameplayAbilityReactivationPolicy ReactivationPolicy =>
            GameplayAbilityReactivationPolicy.RejectWhileActive;

        // 固定顺序保证 Effects 只会在等待正常完成后结算。
        internal override bool IsRuntimeConfigurationValid =>
            base.IsRuntimeConfigurationValid &&
            RootTask is SequenceGameplayAbilityTaskConfig sequence &&
            sequence.Children.Count == 2 &&
            sequence.Children[0] is WaitDurationGameplayAbilityTaskConfig &&
            sequence.Children[1] is ApplySelfEffectsGameplayAbilityTaskConfig;

        #endregion
    }
}
