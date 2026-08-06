using System.Collections.Generic;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.GameplayEffect;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>在当前调用内逐项应用 Instant GE 的同步 Ability。</summary>
    [CreateAssetMenu(fileName = "InstantGameplayAbility", menuName = "WSFrame/GAS/Gameplay Ability/Instant")]
    public sealed class InstantGameplayAbilityData : SynchronousGameplayAbilityData
    {
        #region 字段与属性
        [SerializeField, Tooltip("按顺序应用到 Source 自身的 Instant GE；单项失败不回滚其他项。")]
        private List<GameplayEffectData> effects = new();

        /// <summary>获取该 Instant Ability 的专属 GE 列表。</summary>
        public IReadOnlyList<GameplayEffectData> Effects => effects;
        #endregion

        #region 运行时校验
        // 每个专属 GE 都必须在当前调用内完成，避免同步 Ability 残留异步生命周期。
        internal override bool IsRuntimeConfigurationValid
        {
            get
            {
                if (!base.IsRuntimeConfigurationValid) return false;
                for (int i = 0; i < effects.Count; i++)
                    if (effects[i] == null || effects[i].DurationType != E_GameEffectDurationType.Instant)
                        return false;
                return true;
            }
        }
        #endregion

        #region 同步执行
        // 每个 GE 独立提交；失败项不阻止后续项，也不撤销已成功项。
        protected override void Execute(SynchronousGameplayAbilityRuntime runtime)
        {
            for (int i = 0; i < effects.Count; i++)
            {
                GameplayEffectData effect = effects[i];
                if (effect != null)
                    runtime.Source.GameEffectCtrl.TryApply(
                        effect,
                        runtime.Source,
                        runtime.Level,
                        runtime.SetByCaller,
                        out _);
            }
        }
        #endregion
    }
}