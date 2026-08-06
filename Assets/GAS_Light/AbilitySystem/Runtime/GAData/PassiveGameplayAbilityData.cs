using System.Collections.Generic;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.GameplayEffect;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>应用自身 Infinite GE 并保持 Active，直到外部结束或取消的 Passive Ability。</summary>
    [CreateAssetMenu(fileName = "PassiveGameplayAbility", menuName = "WSFrame/GAS/Gameplay Ability/Passive")]
    public sealed class PassiveGameplayAbilityData : AsynchronousGameplayAbilityData
    {
        #region 字段与属性
        [SerializeField, Tooltip("激活期间应用到 Source 自身的 Infinite GE；每项独立提交。")]
        private List<GameplayEffectData> effects = new();

        /// <summary>获取该 Passive Ability 的持续 GE 列表。</summary>
        public IReadOnlyList<GameplayEffectData> Effects => effects;
        #endregion

        #region 构造
        /// <summary>创建默认使用 Passive 保持 Task 的 Ability 数据。</summary>
        public PassiveGameplayAbilityData()
        {
            SetRootTask(new PassiveGameplayAbilityTaskConfig());
        }
        #endregion

        #region 运行时校验
        // Passive 使用专用保持 Task，并要求每个 GE 都能由本次激活独立持有。
        internal override bool IsRuntimeConfigurationValid
        {
            get
            {
                if (!base.IsRuntimeConfigurationValid || RootTask is not PassiveGameplayAbilityTaskConfig)
                    return false;

                for (int i = 0; i < effects.Count; i++)
                {
                    GameplayEffectData effect = effects[i];
                    if (effect == null ||
                        effect.DurationType != E_GameEffectDurationType.Infinite ||
                        effect.StackingType != E_GameEffectStackingType.None)
                        return false;
                }

                return true;
            }
        }
        #endregion

        #region Runtime 工厂
        // Passive Runtime 额外保存本次成功应用的 GE 句柄，结束时精确移除。
        protected override AsynchronousGameplayAbilityRuntime CreateAsynchronousRuntime(
            int activationId,
            GameplayAbilitySpec spec,
            AbilitySystemComponentBase source,
            IReadOnlyDictionary<GameplayTag, float> setByCaller) =>
            new PassiveGameplayAbilityRuntime(
                activationId,
                spec,
                source,
                setByCaller,
                this);
        #endregion
    }
}